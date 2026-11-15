#!/usr/bin/env python3
"""M5 regressions, strict native packages and isolated Windows acceptance."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import zipfile
import m1
import m2
import m4

ROOT = m1.ROOT
ARTIFACTS = ROOT / 'artifacts/m5'


def check():
    m4.ARTIFACTS = ARTIFACTS
    m4.check()
    result = subprocess.run(['dotnet', 'run', '--project', 'tests/SystemTools.M5.Tests', '-c', 'Release'],
        cwd=ROOT, encoding='utf-8', errors='replace', stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (ARTIFACTS / 'm5-tests.log').write_text(result.stdout, encoding='utf-8')
    print(result.stdout, end='')
    if result.returncode:
        raise RuntimeError('M5 tests failed.')


def smoke_windows(host, package, preferences, wave, device):
    if m1.native_rid() != 'win-x64':
        raise RuntimeError('Windows M5 acceptance must run on Windows x64.')
    package = Path(package).resolve()
    with zipfile.ZipFile(package) as archive:
        manifest = json.loads(archive.read('m2-package.json'))
        m2.verify_files(archive.namelist(), manifest, archive.read)
        if manifest.get('milestone') != 'M5':
            raise RuntimeError('Provide an explicit M5 package.')
    m1.command('dotnet', 'build', 'tests/SystemTools.M4.WindowsProbe', '-c', 'Release', '--nologo', '-v', 'minimal')
    os.environ['SystemTools_M5_Camera'] = device
    os.environ['SystemTools_M5_TestWave'] = str(Path(wave).resolve())
    m2.ARTIFACTS = ARTIFACTS / 'windows'
    probe = ROOT / 'tests/SystemTools.M4.WindowsProbe/bin/Release/net10.0'
    results = [m2.smoke(host, package, timeout=120, missing_dependencies=True, experimental=True,
        graceful_stop=True, case=case, rejection_probe=probe if case == 'missing-backend' else None,
        media_preferences=preferences) for case in ('normal', 'foreign-corrupt', 'missing-backend')]
    m1.write_json(ARTIFACTS / 'windows/desktop.json', dict(m1.identity(), passed=all(item['passed'] for item in results),
        packageSha256=m1.sha(package), executionOS=sys.platform, scenarios=results))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='operation', required=True)
    commands.add_parser('check')
    build = commands.add_parser('stage')
    build.add_argument('--target', choices=list(m1.MODULES))
    pack = commands.add_parser('aggregate')
    pack.add_argument('--input', nargs=3, required=True)
    pack.add_argument('--output', required=True)
    pack.add_argument('--preview', action='store_true')
    smoke = commands.add_parser('smoke-windows')
    for name in ('host-bin', 'package', 'model', 'wave', 'device'):
        smoke.add_argument('--' + name, required=True)
    smoke.add_argument('--ffmpeg', default='')
    args = parser.parse_args()
    m4.ARTIFACTS = ARTIFACTS
    if args.operation == 'check':
        check()
    elif args.operation == 'stage':
        m4.stage(args.target, milestone='M5')
    elif args.operation == 'aggregate':
        m4.aggregate(args.input, args.output, args.preview)
    else:
        smoke_windows(args.host_bin, args.package, dict(SpeechModelPath=str(Path(args.model).resolve()),
            FfmpegPath=args.ffmpeg, MicrophoneDevice='default'), args.wave, args.device)


if __name__ == '__main__':
    main()
