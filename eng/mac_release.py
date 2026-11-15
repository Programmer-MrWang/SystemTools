#!/usr/bin/env python3
"""Prepare, sign, notarize and verify only SystemTools-owned Mac assets."""
from pathlib import Path
import argparse
import json
import os
import platform
import plistlib
import re
import subprocess
import tempfile
import m1
import m2

POLICY = json.loads((m1.ROOT / 'eng/release-policy.json').read_text(encoding='utf-8'))
ENTITLEMENTS = m1.ROOT / 'eng/mac-companion.entitlements.plist'
TARGETS = [m2.MEDIA_NATIVE['osx-arm64'], m2.MAC_NATIVE, m2.MAC_COMPANION]


def command(arguments):
    return subprocess.run([str(a) for a in arguments], check=True, stdout=subprocess.PIPE,
                          stderr=subprocess.STDOUT, text=True).stdout


def identity_options(identity, team):
    if not identity or not identity.startswith('Developer ID Application:') or not re.fullmatch(r'[A-Z0-9]{10}', team or ''):
        raise RuntimeError('正式签名需要 Developer ID Application 身份及明确的十位 Team ID。')
    return ['--sign', identity, '--options', 'runtime', '--timestamp']


def sign_commands(root, identity, team):
    options = identity_options(identity, team)
    return [['codesign', '--force'] + options + (['--entitlements', str(ENTITLEMENTS)] if target == m2.MAC_COMPANION else [])
            + [str(root / target)] for target in TARGETS]


def verify_tree(root, team=None, candidate=False, notarized=False):
    info = plistlib.loads((root / m2.MAC_COMPANION / 'Contents/Info.plist').read_bytes())
    if info.get('CFBundleIdentifier') != POLICY['macSigning']['bundleId']: raise RuntimeError('采集助手 Bundle ID 不符。')
    reports = []
    for target in TARGETS:
        command(['codesign', '--verify', '--strict', '--verbose=2', root / target])
        detail = command(['codesign', '-d', '--verbose=4', root / target])
        if not candidate:
            if ('Authority=Developer ID Application:' not in detail or 'TeamIdentifier=' + str(team) not in detail
                    or 'runtime' not in detail):
                raise RuntimeError('缺少匹配团队的 Developer ID 加固签名：' + target)
        reports.append(dict(path=target, signature=detail, candidateOnly=candidate))
    if notarized:
        if candidate: raise RuntimeError('候选自签不能作为公证证明。')
        command(['xcrun', 'stapler', 'validate', root / m2.MAC_COMPANION])
        command(['spctl', '--assess', '--type', 'execute', '--verbose=2', root / m2.MAC_COMPANION])
    return reports


def preflight(package, identity=None, team=None, profile=None):
    blockers = []
    if platform.system() != 'Darwin': blockers.append('需要 macOS 原生工具链')
    else:
        available = command(['security', 'find-identity', '-v', '-p', 'codesigning'])
        if not identity or identity not in available or 'Developer ID Application:' not in identity: blockers.append('未配置选定的 Developer ID Application 身份')
    try: identity_options(identity, team)
    except RuntimeError as error: blockers.append(str(error))
    if not profile: blockers.append('未配置 notarytool Keychain profile')
    with tempfile.TemporaryDirectory(prefix='systemtools-mac-preflight-') as temporary:
        root = Path(temporary); metadata = m2.extract(package, root)
        if not all((root / target).exists() for target in TARGETS): raise RuntimeError('包缺少 Mac 原生资产。')
        info = plistlib.loads((root / m2.MAC_COMPANION / 'Contents/Info.plist').read_bytes())
        if info.get('CFBundleIdentifier') != POLICY['macSigning']['bundleId']: raise RuntimeError('Bundle ID 不符。')
    return dict(schemaVersion=1, packageSha256=m1.sha(package), packageCommit=metadata['commit'],
                passed=not blockers, blockers=blockers, changesOriginalPackage=False,
                operations=['verify source package', 'sign Vosk and native dylibs', 'sign companion with hardened runtime',
                            'submit ZIP using Keychain profile', 'require Accepted', 'staple companion', 'verify signatures and Gatekeeper', 'repack new CIPX'])


def repack(root, metadata, output, state, proof, team=None):
    metadata['macDistribution'] = dict(state=state, teamId=team, proof=proof, affectsOnly='SystemTools-owned assets')
    metadata['files'] = {p.relative_to(root).as_posix(): m1.sha(p) for p in root.rglob('*') if p.is_file() and p.name != 'm2-package.json'}
    m1.write_json(root / 'm2-package.json', metadata)
    return m2.package(root, output)


def process(operation, package, output=None, identity=None, team=None, profile=None, candidate=False, notarized=False):
    if platform.system() != 'Darwin': raise RuntimeError('Mac 发行操作需要 macOS。')
    if operation in ('sign', 'notarize', 'sign-candidate'):
        if operation != 'sign-candidate': identity_options(identity, team)
        if not output or Path(output).exists() or Path(output).resolve() == Path(package).resolve(): raise RuntimeError('输出必须是一个不存在的新 CIPX。')
    with tempfile.TemporaryDirectory(prefix='systemtools-mac-distribution-') as temporary:
        root = Path(temporary) / 'payload'; metadata = m2.extract(package, root)
        if operation == 'verify': return verify_tree(root, team, candidate, notarized)
        if operation == 'sign-candidate':
            for target in TARGETS: command(['codesign', '--force', '--sign', '-', '--timestamp=none', root / target])
            proof = verify_tree(root, candidate=True)
            return repack(root, metadata, output, 'ad-hoc-not-notarized', proof)
        if operation == 'sign':
            for arguments in sign_commands(root, identity, team): command(arguments)
            proof = verify_tree(root, team)
            return repack(root, metadata, output, 'developer-id-not-notarized', proof, team)
        if not profile: raise RuntimeError('公证需要事先在本机配置的 Keychain profile；不要在聊天或参数中放密码。')
        verify_tree(root, team)
        submission = Path(temporary) / 'SystemTools.zip'
        command(['ditto', '-c', '-k', '--keepParent', root, submission])
        proof = json.loads(command(['xcrun', 'notarytool', 'submit', submission, '--keychain-profile', profile, '--wait', '--output-format', 'json']))
        if proof.get('status') != 'Accepted': raise RuntimeError('Apple 公证未接受：' + json.dumps(proof))
        command(['xcrun', 'stapler', 'staple', root / m2.MAC_COMPANION])
        verify_tree(root, team, notarized=True)
        return repack(root, metadata, output, 'developer-id-notarized', proof, team)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['dry-run', 'preflight', 'sign-candidate', 'sign', 'notarize', 'verify'])
    parser.add_argument('--package', required=True); parser.add_argument('--output'); parser.add_argument('--identity'); parser.add_argument('--team-id'); parser.add_argument('--keychain-profile')
    parser.add_argument('--candidate', action='store_true'); parser.add_argument('--notarized', action='store_true')
    args = parser.parse_args()
    if args.operation in ('dry-run', 'preflight'):
        report = preflight(args.package, args.identity, args.team_id, args.keychain_profile)
        print(json.dumps(report, ensure_ascii=False, indent=2))
        if args.operation == 'preflight' and not report['passed']: raise SystemExit(1)
    else:
        result = process(args.operation, args.package, args.output, args.identity, args.team_id, args.keychain_profile, args.candidate, args.notarized)
        print(json.dumps(result, ensure_ascii=False, indent=2) if isinstance(result, list) else result)


if __name__ == '__main__': main()
