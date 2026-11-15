#!/usr/bin/env python3
"""M7 candidate builds, provenance/architecture audit and fail-closed formal release gates."""
from pathlib import Path
import argparse
import json
import os
import re
import struct
import subprocess
import sys
import tempfile
import zipfile
import audit_dependencies
import m1
import m2
import m4
import m6
import release_inventory

ROOT = m1.ROOT
ARTIFACTS = ROOT / 'artifacts/m7'
POLICY = release_inventory.POLICY


def check():
    m6.check()
    results = list(json.loads((ROOT / 'artifacts/m6/checks.json').read_text(encoding='utf-8'))['checks'])
    commands = [('m7-contracts', ['dotnet', 'run', '--project', 'tests/SystemTools.M7.Tests', '-c', 'Release']),
                ('m7-lifecycle', [sys.executable, '-B', 'eng/test_release_lifecycle.py']),
                ('m7-migration', [sys.executable, '-B', 'eng/test_migration_cli.py']),
                ('m7-mac-release', [sys.executable, '-B', 'eng/test_mac_release.py']),
                ('m7-package', [sys.executable, '-B', 'eng/test_m7_packaging.py']),
                ('m7-dependency-audit', [sys.executable, '-B', 'eng/test_dependency_audit.py'])]
    for name, arguments in commands:
        completed = subprocess.run(arguments, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding='utf-8', errors='replace')
        (ARTIFACTS / (name + '.log')).write_text(completed.stdout, encoding='utf-8')
        count = len(re.findall(r'^PASS \d+:', completed.stdout, re.M))
        if match := re.search(r'Ran (\d+) tests', completed.stdout): count = int(match[1])
        results.append(dict(name=name, passed=completed.returncode == 0, checks=count))
        report = dict(m1.identity(), milestone='M7', passed=all(x['passed'] for x in results), executionOS=sys.platform, checks=results, fullPlatformAcceptance=False)
        m1.write_json(ARTIFACTS / 'checks.json', report)
        print(name, 'PASS' if not completed.returncode else 'FAIL', count, flush=True)
        if completed.returncode: raise RuntimeError(completed.stdout)


def stage(target=None):
    if target and target != m1.native_rid(): raise RuntimeError('M7 候选阶段只接收对应系统原生构建。')
    import build_npoi
    build_npoi.build()
    target = target or m1.native_rid(); m4.ARTIFACTS = ARTIFACTS
    destination = m4.stage(target, milestone='M7')
    metadata = m4.verify_stage(destination)
    release_inventory.generate(destination, target)
    audit_dependencies.audit(destination / ('release/dependency-audit-' + target + '.json'))
    if target == 'win-x64': audit_dependencies.audit(destination / 'release/worker-dependency-audit-win-x64.json', 'VoskWorker/SystemTools.VoskWorker.csproj')
    if target == 'osx-arm64':
        import mac_release
        for name in mac_release.TARGETS: mac_release.command(['codesign', '--force', '--sign', '-', '--timestamp=none', destination / name])
        mac_release.verify_tree(destination, candidate=True)
    metadata['files'] = {path.relative_to(destination).as_posix(): m1.sha(path) for path in destination.rglob('*') if path.is_file() and path.name != 'm4-stage.json'}
    m1.write_json(destination / 'm4-stage.json', metadata); m4.verify_stage(destination)
    return destination


def native_architecture(data, expected):
    if expected == 'win-x64':
        if data[:2] != b'MZ' or len(data) < 64: return False
        offset = struct.unpack_from('<I', data, 60)[0]
        return len(data) > offset + 6 and data[offset:offset+4] == b'PE\0\0' and struct.unpack_from('<H', data, offset + 4)[0] == 0x8664
    if expected == 'linux-x64': return data[:5] == b'\x7fELF\x02' and len(data) >= 20 and struct.unpack_from('<H', data, 18)[0] == 62
    if expected == 'osx-arm64':
        if len(data) < 8: return False
        if data[:4] == b'\xcf\xfa\xed\xfe': return struct.unpack_from('<I', data, 4)[0] == 0x100000c
        if data[:4] == b'\xca\xfe\xba\xbe':
            count = struct.unpack_from('>I', data, 4)[0]
            return count <= 16 and len(data) >= 8 + count * 20 and any(struct.unpack_from('>I', data, 8+i*20)[0] == 0x100000c for i in range(count))
    return False


def audit(package, output=None):
    with tempfile.TemporaryDirectory(prefix='systemtools-m7-audit-') as temporary:
        root = Path(temporary); metadata = m2.extract(package, root)
        if metadata.get('milestone') != 'M7' or metadata.get('kind') != 'native-build-aggregate': raise RuntimeError('M7 发行只接收同源原生三端聚合。')
        if metadata.get('sdk') != POLICY['sdk']: raise RuntimeError('SDK 锁不匹配。')
        if (len(metadata.get('nativeStages', [])) != 3 or any(item.get('kind') != 'native' or item.get('buildRuntime') != item.get('runtimeId') for item in metadata.get('nativeStages', []))
                or {item.get('runtimeId') for item in metadata.get('nativeStages', [])} != set(POLICY['runtimeIds'])): raise RuntimeError('缺少原生阶段。')
        manifest = (root / 'manifest.yml').read_text(encoding='utf-8')
        for key, expected in [('id', POLICY['pluginId']), ('version', POLICY['version']), ('apiVersion', POLICY['hostApiVersion'])]:
            if not re.search(r'^' + key + r':\s*' + re.escape(expected) + r'\s*$', manifest, re.M): raise RuntimeError('插件身份/版本/API 不符：' + key)
        policy = json.loads((root / 'release/policy.json').read_text(encoding='utf-8'))
        if policy != POLICY or policy['channel'] != 'rc': raise RuntimeError('候选策略不符。')
        capabilities = json.loads((root / 'release/capabilities.json').read_text(encoding='utf-8'))
        if capabilities != release_inventory.capabilities(): raise RuntimeError('能力文档与源码台账不同。')
        if (root / 'release/platform-support.md').read_text(encoding='utf-8') != release_inventory.support_markdown(capabilities): raise RuntimeError('平台表未由同一台账生成。')
        for rid in POLICY['runtimeIds']:
            dependency = json.loads((root / ('release/dependency-audit-' + rid + '.json')).read_text(encoding='utf-8'))
            if (not dependency.get('passed') or dependency.get('vulnerabilities') or dependency.get('commit') != metadata['commit']
                    or dependency.get('sourceHash') != metadata['sourceHash'] or dependency.get('runtimeId') != rid):
                raise RuntimeError('依赖审计缺失、失败或未绑定当前来源：' + rid)
            legal = json.loads((root / ('release/licenses-' + rid + '.json')).read_text(encoding='utf-8'))
            if not legal.get('packages'): raise RuntimeError('缺少依赖许可证清单。')
            for item in legal['packages']:
                for notice in item['notices']:
                    if not m2.safe_name(notice['file']) or m1.sha(root / notice['file']) != notice['sha256']: raise RuntimeError('许可证内容损坏。')
        worker = json.loads((root / 'release/worker-dependency-audit-win-x64.json').read_text(encoding='utf-8'))
        if not worker.get('passed') or worker.get('vulnerabilities') or worker.get('commit') != metadata['commit'] or worker.get('sourceHash') != metadata['sourceHash']:
            raise RuntimeError('Windows Worker 依赖审计不符。')
        binaries = {m2.MAC_NATIVE: 'osx-arm64', m2.MAC_COMPANION_EXECUTABLE: 'osx-arm64',
                    m2.MEDIA_NATIVE['osx-arm64']: 'osx-arm64', m2.MEDIA_NATIVE['linux-x64']: 'linux-x64'}
        binaries.update({name: 'win-x64' for name in m4.WINDOWS_REQUIRED if '/native/' in name})
        for name, rid in binaries.items():
            if not native_architecture((root / name).read_bytes(), rid): raise RuntimeError('原生架构不符：' + name)
        if any('sixlabors.imagesharp' in name.casefold() for name in metadata['files']): raise RuntimeError('旧 ImageSharp 仍在包内。')
        if not any('npoi.core.dll' in name.casefold() for name in metadata['files']): raise RuntimeError('缺少源码 Excel 依赖。')
        report = dict(schemaVersion=1, passed=True, packageSha256=m1.sha(package), commit=metadata['commit'], sourceHash=metadata['sourceHash'],
                      sdk=metadata['sdk'], channel='rc', files=len(metadata['files']), featureRows=len(capabilities['features']), runtimeIds=POLICY['runtimeIds'],
                      nativeArchitectureVerified=True, hostAssembliesExcluded=True, dependencyAudit=True, fullPlatformAcceptance=False)
    m1.write_json(output or ARTIFACTS / 'package-audit.json', report)
    return report


def gate(package, lifecycle=None, output=None, publication_approved=False):
    report = audit(package)
    proof = json.loads(Path(lifecycle).read_text(encoding='utf-8')) if lifecycle else {}
    lifecycle_passed = bool(proof.get('passed') and proof.get('packageSha256') == report['packageSha256'])
    platform_passed = lifecycle_passed and set(proof.get('runtimeIds', [])) == set(POLICY['runtimeIds']) and proof.get('fullPlatformAcceptance') is True
    signature = False; notarization = False
    if sys.platform == 'darwin':
        import mac_release
        with zipfile.ZipFile(package) as archive:
            distribution = json.loads(archive.read('m2-package.json')).get('macDistribution', {})
        team = distribution.get('teamId')
        if team:
            try:
                mac_release.process('verify', package, team=team, notarized=True)
                signature = notarization = True
            except (RuntimeError, subprocess.CalledProcessError): pass
    conditions = dict(nativeSameSourceAggregate=True, packageAudit=True, dependencyAudit=True, platformLifecycleEvidence=platform_passed,
                      developerIdSignature=signature, appleNotarization=notarization, explicitPublicationApproval=publication_approved)
    result = dict(report, candidatePackageReady=True, localLifecycleValidated=lifecycle_passed, stableAllowed=all(conditions.values()),
                  conditions=conditions, blockedBy=[key for key in POLICY['stableRequires'] if not conditions[key]])
    m1.write_json(output or ARTIFACTS / 'release-gate.json', result); return result


def main():
    parser = argparse.ArgumentParser(description=__doc__); sub = parser.add_subparsers(dest='operation', required=True)
    sub.add_parser('check')
    command = sub.add_parser('stage'); command.add_argument('--target', choices=POLICY['runtimeIds'])
    command = sub.add_parser('aggregate'); command.add_argument('--input', nargs=3, required=True); command.add_argument('--output', required=True)
    for name in ('audit', 'gate'):
        command = sub.add_parser(name); command.add_argument('--package', required=True); command.add_argument('--output')
        if name == 'gate':
            command.add_argument('--lifecycle'); command.add_argument('--channel', choices=['rc', 'stable'], default='rc')
            command.add_argument('--publication-approved', action='store_true')
    args = parser.parse_args(); ARTIFACTS.mkdir(parents=True, exist_ok=True)
    if args.operation == 'check': check()
    elif args.operation == 'stage': print(stage(args.target))
    elif args.operation == 'aggregate':
        m4.ARTIFACTS = ARTIFACTS
        for path in args.input:
            if m4.verify_stage(path)['milestone'] != 'M7': raise RuntimeError('M7 聚合不能消费旧阶段。')
        package = m4.aggregate(args.input, args.output); print(json.dumps(audit(package), ensure_ascii=False, indent=2))
    elif args.operation == 'audit': print(json.dumps(audit(args.package, args.output), ensure_ascii=False, indent=2))
    else:
        result = gate(args.package, args.lifecycle, args.output, args.publication_approved); print(json.dumps(result, ensure_ascii=False, indent=2))
        if args.channel == 'stable' and not result['stableAllowed']: raise SystemExit(1)


if __name__ == '__main__': main()
