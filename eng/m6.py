#!/usr/bin/env python3
"""M6 regression, per-platform stages and strict same-source native aggregation."""
from pathlib import Path
import argparse
import json
import re
import subprocess
import sys
import m1
import m4
import m5

ROOT = m1.ROOT
ARTIFACTS = ROOT / 'artifacts/m6'

def check():
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    m5.check()
    results = list(json.loads((ROOT / 'artifacts/m5/checks.json').read_text())['checks'])
    commands = [('m6-contracts', ['dotnet', 'run', '--project', 'tests/SystemTools.M6.Tests', '-c', 'Release'])]
    if m1.native_rid() == 'osx-arm64':
        native = ARTIFACTS / 'native-tests'
        commands.extend([
            ('m6-native-compile', ['xcrun', 'swiftc', '-swift-version', '5', '-O', 'native/macos/SystemToolsNative.swift', 'native/macos/SystemToolsAuthentication.swift', 'tests/SystemTools.M6.NativeTests/main.swift', '-o', str(native)]),
            ('m6-native', [str(native)])])
    for name, arguments in commands:
        result = subprocess.run(arguments, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding='utf-8', errors='replace')
        (ARTIFACTS / (name + '.log')).write_text(result.stdout, encoding='utf-8')
        print(result.stdout, end='')
        results.append(dict(name=name, passed=result.returncode == 0, checks=len(re.findall(r'^PASS \d+:', result.stdout, re.MULTILINE))))
        report = dict(m1.identity(), milestone='M6', passed=all(row['passed'] for row in results), executionOS=sys.platform, checks=results, fullPlatformAcceptance=False)
        m1.write_json(ARTIFACTS / 'checks.json', report)
        if result.returncode: raise RuntimeError(name + ' failed.')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='operation', required=True)
    commands.add_parser('check')
    stage = commands.add_parser('stage')
    stage.add_argument('--target', choices=list(m1.MODULES))
    aggregate = commands.add_parser('aggregate')
    aggregate.add_argument('--input', nargs=3, required=True)
    aggregate.add_argument('--output', required=True)
    aggregate.add_argument('--preview', action='store_true')
    args = parser.parse_args()
    m4.ARTIFACTS = ARTIFACTS
    if args.operation == 'check': check()
    elif args.operation == 'stage': m4.stage(args.target, milestone='M6')
    else: m4.aggregate(args.input, args.output, args.preview)

if __name__ == '__main__': main()
