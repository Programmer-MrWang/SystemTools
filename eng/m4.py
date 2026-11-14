#!/usr/bin/env python3
"""M4 per-platform build stages and strict same-source aggregation."""
from pathlib import Path
import argparse
import json
import os
import plistlib
import re
import shutil
import subprocess
import sys
import m1
import m2

ROOT = m1.ROOT
ARTIFACTS = ROOT / 'artifacts/m4'
WINDOWS_REQUIRED = [m2.PRIVATE + '/runtimes/win-x64/native/' + name for name in
                    ('DlibDotNetNative.dll', 'DlibDotNetNativeDnn.dll', 'OpenCvSharpExtern.dll', 'opencv_videoio_ffmpeg490_64.dll')]
WINDOWS_REQUIRED += [m2.PRIVATE + '/VoskWorker/' + name for name in
                     ('SystemTools.VoskWorker.exe', 'SystemTools.VoskWorker.dll', 'SystemTools.VoskWorker.runtimeconfig.json')]


def verify_stage(stage):
    stage = Path(stage).resolve()
    manifest = json.loads((stage / 'm4-stage.json').read_text(encoding='utf-8'))
    target = manifest.get('runtimeId')
    if (manifest.get('schemaVersion') != 1 or manifest.get('milestone') != 'M4'
            or target not in m1.MODULES or manifest.get('buildRuntime') not in m1.MODULES
            or manifest.get('kind') not in ('native', 'cross-compiled')
            or (manifest['kind'] == 'native') != (target == manifest['buildRuntime'])
            or not isinstance(manifest.get('files'), dict)
            or not isinstance(manifest.get('hostSharedAssemblies'), list)):
        raise RuntimeError('Invalid M4 stage metadata.')
    required = set(m1.COMMON + [m1.module_name(target)])
    if target == 'osx-arm64': required.add(m2.MAC_NATIVE)
    if target == 'win-x64': required.update(WINDOWS_REQUIRED)
    if not required.issubset(manifest['files']):
        raise RuntimeError('Missing required stage assets.')
    names = [path.relative_to(stage).as_posix() for path in stage.rglob('*') if path.is_file()]
    if any(path.is_symlink() for path in stage.rglob('*')):
        raise RuntimeError('Stage may not contain symlinks.')
    if set(names) != set(manifest['files']) | {'m4-stage.json'} or len(names) != len({name.casefold() for name in names}):
        raise RuntimeError('Stage contents differ from manifest or contain case duplicates.')
    for name, digest in manifest['files'].items():
        if not m2.safe_name(name) or m1.sha(stage / name) != digest:
            raise RuntimeError('Unsafe or modified stage file: ' + name)
    return manifest


def stage(target=None):
    actual = m1.native_rid()
    target = target or actual
    platform = m1.MODULES[target]
    if target == 'win-x64':
        m1.command('dotnet', 'build', 'VoskWorker/SystemTools.VoskWorker.csproj', '-c', 'Release', '-m:1', '--nologo', '-v', 'minimal')
    m1.command('dotnet', 'build', 'SystemTools.slnx', '-c', 'Release', '-m:1',
               '-p:SystemToolsPlatform=' + platform, '--nologo', '-v', 'minimal')
    destination = ARTIFACTS / 'stages' / target
    if destination.exists():
        if destination.is_symlink() or destination.resolve().parent != (ARTIFACTS / 'stages').resolve():
            raise RuntimeError('Unsafe stage output directory.')
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    entry = ROOT / 'src/SystemTools/bin/Release/net10.0'
    shared = m2.sdk_shared_assemblies(ROOT / 'src/SystemTools.SharedHost/obj/project.assets.json')
    for name in m1.COMMON + [m1.module_name(target)]:
        shutil.copy2(entry / name, destination / name)
    shared_output = ROOT / 'src/SystemTools.SharedHost/bin/Release/net10.0'
    private = {path.name for path in shared_output.glob('*.dll')
               if path.name not in m1.COMMON and path.name.casefold() not in shared
               and not path.name.casefold().startswith(m2.HOST_PREFIXES)}
    for name in private:
        output = destination / m2.SHARED_PRIVATE / name
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(shared_output / name, output)
    if target == 'osx-arm64':
        native = entry / m2.MAC_NATIVE
        if not native.is_file():
            raise RuntimeError('Mac stage requires the native Swift bridge, compiled on a Mac.')
        (destination / m2.MAC_NATIVE).parent.mkdir(parents=True)
        shutil.copy2(native, destination / m2.MAC_NATIVE)
    if target == 'win-x64':
        windows = ROOT / 'src/SystemTools.Platforms.Windows/bin/Release' / m2.TFM
        for path in windows.glob('*.dll'):
            if path.name in m1.COMMON or path.name in private or path.name == m1.module_name(target):
                continue
            if path.name.casefold() in shared or path.name.casefold().startswith(m2.HOST_PREFIXES):
                continue
            output = destination / m2.PRIVATE / path.name
            output.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, output)
        native = windows / 'runtimes/win-x64'
        if native.is_dir():
            shutil.copytree(native, destination / m2.PRIVATE / 'runtimes/win-x64')
        shutil.copytree(ROOT / 'VoskWorker/bin/Release/net10.0/win-x64', destination / m2.PRIVATE / 'VoskWorker')
        for name in m2.EXTRAS:
            shutil.copy2(ROOT / name, destination / name)
    files = {path.relative_to(destination).as_posix(): m1.sha(path) for path in sorted(destination.rglob('*')) if path.is_file()}
    manifest = dict(m1.identity(), schemaVersion=1, milestone='M4', runtimeId=target,
                    kind='native' if actual == target else 'cross-compiled', buildRuntime=actual,
                    sdk=subprocess.check_output(['dotnet', '--version'], cwd=ROOT, text=True).strip(),
                    hostSharedAssemblies=sorted(shared), files=files, runtimeVerified=False)
    m1.write_json(destination / 'm4-stage.json', manifest)
    verify_stage(destination)
    print('M4 STAGE:', destination, 'KIND:', manifest['kind'], flush=True)
    return destination


def aggregate(stages, output, preview=False):
    pairs = sorted(((Path(path).resolve(), verify_stage(path)) for path in stages), key=lambda item: item[1]['runtimeId'])
    if len(pairs) != 3 or {manifest['runtimeId'] for _, manifest in pairs} != set(m1.MODULES):
        raise RuntimeError('Exactly one stage per supported runtime is required.')
    if len({(manifest['commit'], manifest['sourceHash'], manifest['sdk']) for _, manifest in pairs}) != 1:
        raise RuntimeError('Stages have different commits, source hashes or SDK versions.')
    if not preview and any(manifest['kind'] != 'native' for _, manifest in pairs):
        raise RuntimeError('Native aggregate cannot consume cross-compiled stages. Use --preview for an explicitly labelled development package.')
    shared_sets = {tuple(manifest['hostSharedAssemblies']) for _, manifest in pairs}
    if len(shared_sets) != 1:
        raise RuntimeError('Host dependency contracts differ across stages.')
    merged = {}
    for source, manifest in pairs:
        for name, digest in manifest['files'].items():
            if name in merged and m1.sha(merged[name]) != digest:
                raise RuntimeError('Conflicting shared output across platform stages: ' + name)
            merged[name] = source / name
    destination = ARTIFACTS / ('aggregate-preview' if preview else 'aggregate-native')
    if destination.exists():
        if destination.is_symlink(): raise RuntimeError('Unsafe aggregate output.')
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    for name, path in merged.items():
        target = destination / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
    build = pairs[0][1]
    metadata = dict(commit=build['commit'], sourceHash=build['sourceHash'], sdk=build['sdk'],
                    schemaVersion=1, milestone='M4', kind='cross-compiled-development-preview' if preview else 'native-build-aggregate',
                    hostSharedAssemblies=build['hostSharedAssemblies'],
                    files={name: m1.sha(path) for name, path in sorted(merged.items())},
                    nativeStages=[dict(runtimeId=manifest['runtimeId'], kind=manifest['kind'], buildRuntime=manifest['buildRuntime']) for _, manifest in pairs],
                    runtimeValidation='Build provenance only; per-OS desktop and device validation is recorded separately.')
    m1.write_json(destination / 'm2-package.json', metadata)
    result = m2.package(destination, output)
    print('M4 PACKAGE:', result, 'SHA256:', m1.sha(result), flush=True)
    return result


def check(desktop_read=False):
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    commands = [('shared-regression', [sys.executable, 'eng/m3.py', 'check']),
                ('m4-contracts', ['dotnet', 'run', '--project', 'tests/SystemTools.M4.Tests', '-c', 'Release']),
                ('m4-package', [sys.executable, 'eng/test_m4_packaging.py'])]
    if m1.native_rid() != 'linux-x64' or desktop_read:
        commands.insert(1, ('loader', [sys.executable, 'eng/m1.py', 'test']))
    if m1.native_rid() == 'osx-arm64':
        executable = ARTIFACTS / 'native-tests'
        commands += [('native-compile', ['xcrun', 'swiftc', '-swift-version', '5', '-O', 'native/macos/SystemToolsNative.swift',
                     'tests/SystemTools.M4.NativeTests/main.swift', '-o', str(executable)]),
                     ('native-regression', [str(executable)] + ([] if desktop_read else ['--headless']))]
    results = []
    for name, arguments in commands:
        completed = subprocess.run(arguments, cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (ARTIFACTS / (name + '.log')).write_text(completed.stdout, encoding='utf-8')
        count = len(re.findall(r'^PASS \d+:', completed.stdout, re.MULTILINE))
        if name == 'shared-regression' and completed.returncode == 0:
            count = sum(item['checks'] for item in json.loads((ROOT / 'artifacts/m3/checks.json').read_text())['checks'])
        if match := re.search(r'Ran (\d+) tests', completed.stdout): count = int(match[1])
        results.append(dict(name=name, passed=completed.returncode == 0, checks=count))
        print(name, 'PASS' if completed.returncode == 0 else 'FAIL', count, flush=True)
        if completed.returncode:
            print(completed.stdout)
            break
    report = dict(m1.identity(), passed=len(results) == len(commands) and all(item['passed'] for item in results),
                  executionOS=sys.platform, desktopRead=desktop_read, checks=results,
                  note='Native permissions, USB hardware and other OS desktop behavior require separate acceptance.')
    m1.write_json(ARTIFACTS / 'checks.json', report)
    if not report['passed']: raise RuntimeError('M4 regression failed; inspect artifacts/m4 logs.')


def smoke(host, package):
    import m3_macos
    if m1.native_rid() != 'osx-arm64': raise RuntimeError('This smoke command requires a native ARM64 Mac.')
    m3_macos.ARTIFACTS = ARTIFACTS / 'mac'
    os.environ['SystemTools_M4_Acceptance'] = '1'
    fixture_root = ARTIFACTS / 'SystemToolsM4Fixture.app/Contents'
    fixture_root.joinpath('MacOS').mkdir(parents=True, exist_ok=True)
    fixture_root.joinpath('Info.plist').write_bytes(plistlib.dumps(dict(
        CFBundleIdentifier='dev.systemtools.m4.fixture', CFBundleExecutable='window-fixture',
        CFBundleName='SystemTools M4 Fixture', CFBundlePackageType='APPL', LSUIElement=False,
        NSPrincipalClass='NSApplication')))
    fixture = fixture_root / 'MacOS/window-fixture'
    m1.command('xcrun', 'swiftc', '-swift-version', '5', '-O', 'tests/SystemTools.M4.WindowFixture/main.swift', '-o', fixture)
    os.environ['SystemTools_M4_WindowFixture'] = str(fixture)
    results = [m3_macos.smoke(host, package, case, 'm4', 0, 90)
               for case in ('normal', 'foreign-corrupt', 'missing-backend')]
    report = dict(m1.identity(), passed=all(item['passed'] for item in results), executionOS=sys.platform, cases=results)
    m1.write_json(ARTIFACTS / 'desktop.json', report)
    if not report['passed']: raise RuntimeError('M4 isolated desktop acceptance failed.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    build = commands.add_parser('stage')
    build.add_argument('--target', choices=list(m1.MODULES))
    pack = commands.add_parser('aggregate')
    pack.add_argument('--input', nargs=3, required=True)
    pack.add_argument('--output', required=True)
    pack.add_argument('--preview', action='store_true')
    checks = commands.add_parser('check')
    checks.add_argument('--desktop-read', action='store_true')
    desktop = commands.add_parser('smoke-mac')
    desktop.add_argument('--host-bin', required=True)
    desktop.add_argument('--package', required=True)
    args = parser.parse_args()
    if args.command == 'stage': stage(args.target)
    elif args.command == 'aggregate': aggregate(args.input, args.output, args.preview)
    elif args.command == 'check': check(args.desktop_read)
    else: smoke(args.host_bin, args.package)
