#!/usr/bin/env python3
"""M3 ordered regression, packaging, and isolated Windows desktop acceptance."""
from pathlib import Path
import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import m1
import m2

ARTIFACTS = m1.ROOT / 'artifacts/m3'
PACKAGE = ARTIFACTS / 'SystemTools.M3.cipx'


def check():
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    environment = os.environ.copy()
    if sys.platform == 'darwin':
        # macOS exposes its temporary directory through /var -> /private/var.
        # Keep migration's link rejection intact by using the real test root.
        environment['TMPDIR'] = str(Path(tempfile.gettempdir()).resolve())
    commands = [
        ('core', ['dotnet', 'run', '--project', 'tests/SystemTools.M3.Tests', '-c', 'Release']),
        ('shared-linux-contract', ['dotnet', 'run', '--project', 'tests/SystemTools.M3.Host.Tests', '-c', 'Release', '--', 'linux-x64']),
        ('shared-macos-contract', ['dotnet', 'run', '--project', 'tests/SystemTools.M3.Host.Tests', '-c', 'Release', '--', 'osx-arm64']),
        ('host-accessibility', ['dotnet', 'run', '--project', 'tests/SystemTools.HostCompatibility.Tests', '-c', 'Release']),
        ('m1-package', [sys.executable, '-X', 'utf8', 'eng/test_m1_packaging.py']),
        ('m2-package', [sys.executable, '-X', 'utf8', 'eng/test_m2_packaging.py']),
    ]
    results = []
    for name, arguments in commands:
        completed = subprocess.run(arguments, cwd=m1.ROOT, stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, encoding='utf-8', errors='replace',
                                   env=environment)
        (ARTIFACTS / (name + '.log')).write_text(completed.stdout, encoding='utf-8')
        count = len(re.findall(r'^PASS(?: |:)', completed.stdout, re.MULTILINE))
        if match := re.search(r'Ran (\d+) tests', completed.stdout):
            count = int(match[1])
        results.append(dict(name=name, passed=completed.returncode == 0, checks=count))
        print(name, 'PASS' if completed.returncode == 0 else 'FAIL', count, flush=True)
        if completed.returncode:
            print(completed.stdout)
            break
    report = dict(m1.identity(), passed=len(results) == len(commands) and all(x['passed'] for x in results),
                  executionOS=sys.platform, targetPlatformRuntimeAcceptance=False, checks=results)
    m1.write_json(ARTIFACTS / 'checks.json', report)
    if not report['passed']:
        raise RuntimeError('M3 regression failed; inspect artifacts/m3 logs.')


def package(host):
    m1.command('dotnet', 'build', 'SystemTools.slnx', '-c', 'Release', '--nologo', '-v', 'minimal')
    m1.command('dotnet', 'build', 'VoskWorker/SystemTools.VoskWorker.csproj', '-c', 'Release', '--nologo', '-v', 'minimal')
    stage = m2.stage(host, m2.ARTIFACTS / 'm3-stage')
    result = m2.package(stage, PACKAGE)
    print('M3 PACKAGE:', result, 'SHA256:', m1.sha(result), flush=True)


def smoke(host):
    if not host:
        raise ValueError('M3 desktop acceptance requires --host-bin.')
    results = []
    for case in ('normal', 'foreign-corrupt', 'missing-backend'):
        results.append(m2.smoke(host, PACKAGE, missing_dependencies=True, experimental=True, case=case, graceful_stop=True))
    m1.write_json(ARTIFACTS / 'windows-desktop.json', dict(m1.identity(), passed=all(x['passed'] for x in results),
                  packageSha256=m1.sha(PACKAGE), scenarios=results))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['check', 'package', 'smoke'])
    parser.add_argument('--host-bin')
    arguments = parser.parse_args()
    if arguments.operation == 'check':
        check()
    elif arguments.operation == 'package':
        package(arguments.host_bin)
    else:
        smoke(arguments.host_bin)
