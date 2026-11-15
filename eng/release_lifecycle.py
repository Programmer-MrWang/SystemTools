#!/usr/bin/env python3
"""Offline, recoverable installation on an explicit ClassIsland package root."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import stat
import subprocess
import time
import uuid
import m2

POLICY = json.loads((Path(__file__).parent / 'release-policy.json').read_text(encoding='utf-8'))
OWNED = ['data/Plugins/SystemTools'] + ['data/Plugins/' + name for name in POLICY['legacyPluginIds']]
PATHS = OWNED + ['data/Config']
EMPTY_HASH = hashlib.sha256(b'').hexdigest()


def no_links(path):
    path = Path(os.path.abspath(path))
    for parent in [path] + list(path.parents):
        if parent.is_symlink() or (getattr(parent, 'is_junction', lambda: False)()): raise RuntimeError('链接路径不能用于安装或恢复：' + str(parent))
    return path


def tree(path):
    path = no_links(path)
    if not path.exists(): return None
    if not path.is_dir(): raise RuntimeError('需要目录：' + str(path))
    result = {}
    for folder, directories, files in os.walk(path, followlinks=False):
        for name in directories + files: no_links(Path(folder) / name)
        for name in files:
            file = Path(folder) / name
            if not stat.S_ISREG(file.stat().st_mode): raise RuntimeError('拒绝非常规文件：' + str(file))
            with file.open('rb') as stream:
                digest = hashlib.sha256()
                for chunk in iter(lambda: stream.read(1024 * 1024), b''): digest.update(chunk)
                result[file.relative_to(path).as_posix()] = digest.hexdigest()
    return result


def private(path):
    if os.name != 'nt':
        for folder, directories, files in os.walk(path):
            os.chmod(folder, 0o700)
            for name in files:
                file = Path(folder) / name
                os.chmod(file, 0o700 if file.stat().st_mode & 0o111 else 0o600)


def write(path, data):
    no_links(path)
    temporary = no_links(path.with_name(path.name + '.writing'))
    with temporary.open('w', encoding='utf-8') as output:
        json.dump(data, output, ensure_ascii=False, indent=2)
        output.flush(); os.fsync(output.fileno())
    if os.name != 'nt': os.chmod(temporary, 0o600)
    os.replace(temporary, path)


def stopped():
    if os.name == 'nt': running = subprocess.check_output(['tasklist', '/fo', 'csv', '/nh'], text=True, errors='replace')
    else: running = subprocess.check_output(['ps', '-axo', 'comm='], text=True)
    if any('classisland' in line.casefold() or 'systemtoolscompanion' in line.casefold() for line in running.splitlines()):
        raise RuntimeError('请先退出所有 ClassIsland 和 SystemTools 采集助手，再执行离线安装/恢复。')


def root_path(path):
    root = no_links(path)
    if not root.is_dir() or not no_links(root / 'PackageType').is_file():
        raise RuntimeError('需要明确的 ClassIsland 包根目录（包含 PackageType）。')
    return root


class RootLock:
    def __init__(self, root): self.path = root / '.systemtools-maintenance.lock'
    def __enter__(self):
        no_links(self.path)
        self.file = self.path.open('a+b')
        try:
            if os.name == 'nt':
                import msvcrt
                self.file.seek(0); self.file.write(b'0'); self.file.flush(); self.file.seek(0)
                msvcrt.locking(self.file.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.file.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except Exception:
            self.file.close(); raise RuntimeError('另一个安装/恢复操作仍在进行。')
        return self
    def __exit__(self, *args): self.file.close()


def journal_path(root, journal):
    journal = no_links(journal)
    if journal.parent.parent != root / 'data/SystemToolsRecovery' or journal.name != 'journal.json':
        raise RuntimeError('恢复日志不属于指定包根目录。')
    data = json.loads(journal.read_text(encoding='utf-8'))
    if (data.get('schemaVersion') != 1 or data.get('root') != str(root) or data.get('paths') != PATHS
            or set(data.get('before', {})) != set(PATHS) or set(data.get('after', {})) != set(PATHS)):
        raise RuntimeError('恢复日志结构或路径不符。')
    return journal, data


def plan(root, package):
    root = root_path(root)
    with __import__('tempfile').TemporaryDirectory(prefix='systemtools-plan-') as temporary:
        candidate = Path(temporary) / 'candidate'
        metadata = m2.extract(package, candidate)
        manifest = (candidate / 'manifest.yml').read_text(encoding='utf-8')
        if not __import__('re').search(r'^id:\s*SystemTools\s*$', manifest, __import__('re').M):
            raise RuntimeError('安装包不是正式 SystemTools 身份。')
    active = [name for name in POLICY['legacyPluginIds'] if no_links(root / 'data/Plugins' / name).is_dir()
              and not no_links(root / 'data/Plugins' / name / '.disabled').exists()]
    return dict(root=str(root), packageSha256=m2.m1.sha(package), packageCommit=metadata['commit'],
                activeLegacy=active, before={name: tree(root / name) for name in PATHS})


def install(root, package, *, disable_legacy=False):
    root = root_path(root); stopped()
    with RootLock(root):
        if list(no_links(root / 'data/Cache/PluginPackages').glob('*.cipx')):
            raise RuntimeError('宿主有待安装包，请先处理明确的待安装包，防止下次启动覆盖。')
        preview = plan(root, package)
        if preview['activeLegacy'] and not disable_legacy:
            raise RuntimeError('旧跨平台插件仍启用；先预览迁移，再显式选择 --disable-legacy。')
        recovery = no_links(root / 'data/SystemToolsRecovery' / (time.strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:8]))
        recovery.mkdir(parents=True); private(recovery)
        candidate = recovery / 'candidate'; metadata = m2.extract(package, candidate)
        before = preview['before']
        for name, hashes in before.items():
            if hashes is not None: shutil.copytree(root / name, recovery / 'before' / name)
        private(recovery / 'before')
        if any(tree(recovery / 'before' / name) != value for name, value in before.items()): raise RuntimeError('备份校验失败。')
        if {name: tree(root / name) for name in PATHS} != before: raise RuntimeError('备份时文件被修改，安装已停止。')
        if before[OWNED[0]] is not None and '.disabled' in before[OWNED[0]]: (candidate / '.disabled').write_bytes(b'')
        after = dict(before); after[OWNED[0]] = tree(candidate)
        for name in preview['activeLegacy']:
            key = 'data/Plugins/' + name; after[key] = dict(before[key], **{'.disabled': EMPTY_HASH})
        data = dict(schemaVersion=1, root=str(root), paths=PATHS, before=before, after=after, status='Prepared',
                    packageSha256=preview['packageSha256'], packageCommit=metadata['commit'])
        journal = recovery / 'journal.json'; write(journal, data)
        destination = no_links(root / OWNED[0]); destination.parent.mkdir(parents=True, exist_ok=True)
        # Both directory moves are atomic. The durable expected hashes allow recovery between moves.
        if destination.exists(): os.replace(destination, recovery / 'parked-plugin')
        os.replace(candidate, destination)
        for name in preview['activeLegacy']: no_links(root / 'data/Plugins' / name / '.disabled').write_bytes(b'')
        data['status'] = 'Installed'; write(journal, data)
        return journal


def seal(root, journal):
    root = root_path(root); stopped()
    with RootLock(root):
        journal, data = journal_path(root, journal)
        if data['status'] not in ('Installed', 'Sealed'): raise RuntimeError('仅可为已安装事务记录验收后的状态。')
        if any(tree(root / name) != data['after'][name] for name in OWNED): raise RuntimeError('安装文件已变化，不能确认旧事务。')
        data['after'] = {name: tree(root / name) for name in PATHS}; data['status'] = 'Sealed'; write(journal, data)


def rollback(root, journal):
    root = root_path(root); stopped()
    with RootLock(root):
        journal, data = journal_path(root, journal)
        if data['status'] == 'RolledBack': return
        if data['status'] not in ('Prepared', 'Installed', 'Sealed', 'RollingBack'): raise RuntimeError('事务状态不支持恢复。')
        before, after = data['before'], data['after']
        for name in PATHS:
            current = tree(root / name)
            moved = current is None and ((name == OWNED[0] and tree(journal.parent / 'parked-plugin') == before[name])
                or (data['status'] == 'RollingBack' and tree(journal.parent / 'displaced' / name) == after[name]))
            if current != before[name] and current != after[name] and not moved:
                raise RuntimeError('数据在验收记录后发生变化，拒绝覆盖：' + name)
            if before[name] is not None and tree(journal.parent / 'before' / name) != before[name]:
                raise RuntimeError('恢复备份损坏：' + name)
        data['status'] = 'RollingBack'; write(journal, data)
        for name in PATHS:
            destination = root / name
            if tree(destination) == before[name]: continue
            prepared = journal.parent / 'restore' / name
            if before[name] is not None:
                if prepared.exists(): shutil.rmtree(prepared)
                prepared.parent.mkdir(parents=True, exist_ok=True)
                shutil.copytree(journal.parent / 'before' / name, prepared)
                if tree(prepared) != before[name]: raise RuntimeError('回退副本校验失败。')
            if destination.exists():
                displaced = no_links(journal.parent / 'displaced' / name)
                if displaced.exists(): raise RuntimeError('回退暂存目录冲突，请保留现场检查。')
                displaced.parent.mkdir(parents=True, exist_ok=True); os.replace(destination, displaced)
            if before[name] is not None:
                destination.parent.mkdir(parents=True, exist_ok=True); os.replace(prepared, destination)
        data['status'] = 'RolledBack'; write(journal, data)


def mark(root, operation):
    root = root_path(root); stopped()
    with RootLock(root):
        folder = no_links(root / OWNED[0])
        if not folder.is_dir(): raise RuntimeError('SystemTools 未安装。')
        marker = no_links(folder / ('.uninstall' if operation == 'uninstall' else '.disabled'))
        if operation == 'enable': marker.unlink(missing_ok=True)
        else: marker.write_bytes(b'')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['plan', 'install', 'seal', 'rollback', 'disable', 'enable', 'uninstall'])
    parser.add_argument('--root', required=True); parser.add_argument('--package'); parser.add_argument('--journal'); parser.add_argument('--disable-legacy', action='store_true')
    args = parser.parse_args()
    if args.operation in ('plan', 'install') and not args.package: parser.error('--package is required')
    if args.operation in ('seal', 'rollback') and not args.journal: parser.error('--journal is required')
    if args.operation == 'plan': print(json.dumps(plan(args.root, args.package), ensure_ascii=False, indent=2))
    elif args.operation == 'install': print(install(args.root, args.package, disable_legacy=args.disable_legacy))
    elif args.operation == 'seal': seal(args.root, args.journal)
    elif args.operation == 'rollback': rollback(args.root, args.journal)
    else: mark(args.root, args.operation)


if __name__ == '__main__': main()
