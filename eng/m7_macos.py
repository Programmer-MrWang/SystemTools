#!/usr/bin/env python3
"""Run candidate lifecycle checks against an unchanged installed Mac host in isolated data."""
from pathlib import Path
import argparse
import json
import os
import shutil
import subprocess
import time
import uuid
import m1
import m2
import m7
import release_lifecycle as lifecycle

ARTIFACTS = m1.ROOT / 'artifacts/m7/mac'
PROBE_ID = 'SystemTools.M7.MacHostProbe'


def seed(root):
    root.mkdir(parents=True, exist_ok=True); (root / 'PackageType').write_text('folder')
    m1.write_json(root / 'data/Settings.json', dict(IsWelcomeWindowShowed=True, IsSplashEnabled=False, HasEditModeTutorialShown=True))
    config = root / 'data/Config/Plugins/SystemTools'; config.mkdir(parents=True, exist_ok=True)
    m1.write_json(config / 'Main.json', dict(enableAiService=False, enableFloatingWindowFeature=False, autoSwitchClassIslandTheme=False,
        autoHideMainWindowWhenOccluded=False, aiApiKey='dpapi:opaque-test', m7Unknown=dict(keep=17)))
    (config / 'Attachment.bin').write_bytes(bytes([0, 255, 1, 17]))


def run_host(host_app, root, label, expected='Loaded', kind='M7', timeout=55):
    lifecycle.stopped()
    run = root.parent / ('run-' + label + '-' + uuid.uuid4().hex[:8]); run.mkdir()
    probe = root / 'data/Plugins' / PROBE_ID; probe.mkdir(parents=True, exist_ok=True)
    source = m1.ROOT / 'tests' / PROBE_ID / 'bin/Release/net10.0'
    for name in (PROBE_ID + '.dll', 'manifest.yml'): shutil.copy2(source / name, probe / name)
    host = host_app / 'Contents/MacOS/ClassIsland.Desktop'
    environment = dict(os.environ)
    for name in list(environment):
        if name.startswith('SystemTools_M') or name in ('ClassIsland_DebugForceMonoPluginLoadBehavior','ClassIsland_DebugSuppressMacOSPluginLoadBehavior','ClassIsland_WaitDebuggers'):
            environment.pop(name)
    environment.update(ClassIsland_PackageRoot=str(root), SystemTools_M7_ProbeRoot=str(run), SystemTools_M7_Expected=expected, SystemTools_M7_PackageKind=kind)
    global_path = m1.global_storage_path(); before = m1.sha(global_path) if global_path.exists() else None
    timed_out = False; forced = False
    with (run / 'host.log').open('w') as output:
        process = subprocess.Popen([str(host), '--skip-oobe', '--quiet', '--verbose'], cwd=host.parent, env=environment,
                                   stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        print('HOST', label, process.pid, 'RUN', run, flush=True)
        try: process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = forced = True; process.terminate()
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
    after = m1.sha(global_path) if global_path.exists() else None
    acceptance = json.loads((run / 'acceptance.json').read_text(encoding='utf-8')) if (run / 'acceptance.json').exists() else None
    result = dict(label=label, expected=expected, packageKind=kind, hostSha256=m1.sha(host), host=str(host_app), run=str(run),
                  exitCode=process.returncode, timedOut=timed_out, forcedStop=forced, globalUnchanged=before == after, acceptance=acceptance)
    result['passed'] = bool(acceptance and acceptance['passed'] and process.returncode == 0 and not timed_out and not forced and before == after)
    m1.write_json(run / 'result.json', result)
    if not result['passed']: raise RuntimeError('M7 host lifecycle failed: ' + str(run))
    return result


def verify_boundary(baseline):
    data = json.loads(Path(baseline).read_text(encoding='utf-8')); checked = 0
    for name, info in data['repos'].items():
        root = Path(name)
        if subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip() != info['head']: raise RuntimeError('宿主 HEAD 变化。')
        if subprocess.check_output(['git','status','--porcelain'],cwd=root,text=True) != info['status']: raise RuntimeError('宿主原 dirty 变化。')
        for relative, digest in info['files'].items():
            if digest is not None and m1.sha(root / relative) != digest: raise RuntimeError('宿主源码变化：' + relative)
            checked += 1
    for name, info in data['apps'].items():
        files = info.get('files', info)
        for relative, digest in files.items():
            if m1.sha(Path(name) / relative) != digest: raise RuntimeError('已安装宿主变化：' + relative)
            checked += 1
    return dict(passed=True, files=checked, baselineSha256=m1.sha(baseline))


def smoke(host_app, package, previous, baseline=None, audit_package=True):
    if m1.native_rid() != 'osx-arm64': raise RuntimeError('需要原生 ARM64 Mac。')
    host_app = Path(host_app).absolute(); package = Path(package).absolute(); previous = Path(previous).absolute()
    lifecycle.stopped()
    if audit_package: m7.audit(package)
    if baseline: verify_boundary(baseline)
    m1.command('dotnet','build','tests/SystemTools.M7.MacHostProbe','-c','Release','--nologo','-v','minimal')
    session = ARTIFACTS / (time.strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:6]); session.mkdir(parents=True)
    cases = []; assertions = []
    fresh = session / 'fresh'; seed(fresh)
    fresh_journal = lifecycle.install(fresh, package)
    cases.append(run_host(host_app, fresh, 'fresh-install'))
    snapshot = fresh / 'data/Config/Plugins/SystemTools/.upgrade-snapshots/3.0.0.0/snapshot.json'
    snapshot_hash = m1.sha(snapshot)
    cases.append(run_host(host_app, fresh, 'restart'))
    if m1.sha(snapshot) != snapshot_hash: raise RuntimeError('重启覆盖了升级前快照。')
    assertions.append('同版本重启保留原快照')
    lifecycle.mark(fresh, 'disable'); config_before = lifecycle.tree(fresh / 'data/Config')
    cases.append(run_host(host_app, fresh, 'disabled', 'Disabled'))
    if lifecycle.tree(fresh / 'data/Config') != config_before: raise RuntimeError('停用状态改写配置。')
    lifecycle.mark(fresh, 'enable'); cases.append(run_host(host_app, fresh, 're-enabled'))
    lifecycle.mark(fresh, 'uninstall'); config_before = lifecycle.tree(fresh / 'data/Config')
    cases.append(run_host(host_app, fresh, 'uninstalled', 'Absent'))
    if (fresh / lifecycle.OWNED[0]).exists() or lifecycle.tree(fresh / 'data/Config') != config_before: raise RuntimeError('卸载未移除插件或损坏配置。')
    if not fresh_journal.exists(): raise RuntimeError('卸载删除了恢复资料。')
    assertions.append('宿主真实卸载保留配置、附件和恢复资料')
    upgrade = session / 'upgrade'; seed(upgrade)
    lifecycle.install(upgrade, previous)
    cases.append(run_host(host_app, upgrade, 'previous-m6', kind='M6'))
    old_plugin = lifecycle.tree(upgrade / lifecycle.OWNED[0]); old_config = lifecycle.tree(upgrade / 'data/Config')
    journal = lifecycle.install(upgrade, package)
    cases.append(run_host(host_app, upgrade, 'upgraded-m7'))
    cases.append(run_host(host_app, upgrade, 'upgrade-restart'))
    lifecycle.seal(upgrade, journal); lifecycle.rollback(upgrade, journal)
    if lifecycle.tree(upgrade / lifecycle.OWNED[0]) != old_plugin or lifecycle.tree(upgrade / 'data/Config') != old_config:
        raise RuntimeError('回退未逐字节恢复实际旧包与旧配置。')
    cases.append(run_host(host_app, upgrade, 'rolled-back-m6', kind='M6'))
    assertions.append('真实 M6 包到 M7 同数字版本升级、回退逐字节恢复且旧包可重新加载')
    m1.command('dotnet','build','tests/SystemTools.M7.LegacyStub','-c','Release','--nologo','-v','minimal')
    conflict = session / 'conflict'; seed(conflict); lifecycle.install(conflict, package)
    legacy = conflict / 'data/Plugins/SystemTools-Cross-platform'; legacy.mkdir()
    shutil.copy2(m1.ROOT / 'tests/SystemTools.M7.LegacyStub/bin/Release/net10.0/SystemTools.M7.LegacyStub.dll', legacy)
    (legacy / 'manifest.yml').write_text('id: SystemTools-Cross-platform\nname: M7 synthetic legacy identity fixture\nentranceAssembly: SystemTools.M7.LegacyStub.dll\nversion: 0.0.0.1\napiVersion: 2.2.0.0\nsupportedOSPlatforms: [macOS]\n', encoding='utf-8')
    cases.append(run_host(host_app, conflict, 'synthetic-legacy-conflict', 'Error'))
    lifecycle.install(conflict, package, disable_legacy=True)
    cases.append(run_host(host_app, conflict, 'legacy-explicitly-disabled'))
    assertions.append('合成旧身份冲突在真实宿主阻止重复注册；显式停用旧包后加载恢复')
    boundary = verify_boundary(baseline) if baseline else None
    result = dict(m1.identity(), schemaVersion=1, passed=all(x['passed'] for x in cases), packageSha256=m1.sha(package), previousPackageSha256=m1.sha(previous),
                  runtimeIds=['osx-arm64'], finalCandidate=audit_package, fullPlatformAcceptance=False, scenarios=cases, assertions=assertions, hostBoundary=boundary,
                  scope='Isolated Mac package lifecycle; M6 3.0.0.0 -> M7 3.0.0.0. Not Windows 2.x, formal signing, device/TCC or Linux acceptance.')
    m1.write_json(session / 'result.json', result); m1.write_json(ARTIFACTS.parent / 'mac-lifecycle.json', result)
    return result


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--host-app',default='/Applications/ClassIsland.Desktop.app');parser.add_argument('--package',required=True)
    parser.add_argument('--previous',required=True);parser.add_argument('--host-baseline')
    parser.add_argument('--preview',action='store_true',help='Explicit local Mac preview only; never final candidate evidence')
    args=parser.parse_args();print(json.dumps(smoke(args.host_app,args.package,args.previous,args.host_baseline,not args.preview),ensure_ascii=False,indent=2))
