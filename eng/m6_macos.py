#!/usr/bin/env python3
"""Build an explicitly local Mac preview and verify it in an unchanged installed host."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import time
import zipfile
import m1
import m2
import m4

ROOT = m1.ROOT
ARTIFACTS = ROOT / 'artifacts/m6/mac'


def preview(base, output):
    if m1.native_rid() != 'osx-arm64':
        raise RuntimeError('Build this preview on a native ARM64 Mac.')
    m4.ARTIFACTS = ROOT / 'artifacts/m6'
    native = m4.stage('osx-arm64', milestone='M6')
    stage = ARTIFACTS / ('preview-' + str(time.time_ns()))
    stage.mkdir(parents=True)
    with zipfile.ZipFile(base) as archive:
        metadata = json.loads(archive.read('m2-package.json'))
        expected = set(metadata['files']) | {'m2-package.json'}
        if set(archive.namelist()) != expected or len(expected) != len(archive.namelist()):
            raise RuntimeError('Historical base file manifest differs from its archive.')
        for item in archive.infolist():
            if not m2.safe_name(item.filename) or stat.S_ISLNK(item.external_attr >> 16):
                raise RuntimeError('Unsafe historical base asset.')
            if item.filename == 'm2-package.json':
                continue
            data = archive.read(item)
            if hashlib.sha256(data).hexdigest() != metadata['files'][item.filename]:
                raise RuntimeError('Historical base hash mismatch: ' + item.filename)
            path = stage / item.filename
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    current = m4.verify_stage(native)
    for name in current['files']:
        destination = stage / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(native / name, destination)
    metadata.update(m1.identity(), schemaVersion=1, milestone='M6', kind='mac-development-preview',
        nativeStages=[{key: value for key, value in current.items() if key != 'files'}],
        foreignAssetsFromHistoricalBase=dict(path=str(Path(base).resolve()), sha256=m1.sha(base)),
        fullPlatformAcceptance=False, hostSourceModified=False,
        runtimeValidation='Local Mac candidate only. Foreign binaries are historical assets; this is not a new three-native aggregate.')
    metadata['files'] = {path.relative_to(stage).as_posix(): m1.sha(path)
                         for path in stage.rglob('*') if path.is_file() and path.name != 'm2-package.json'}
    m1.write_json(stage / 'm2-package.json', metadata)
    result = m2.package(stage, output)
    print('LOCAL MAC PREVIEW:', result, 'SHA256:', m1.sha(result), flush=True)
    return result


def smoke(host_app, package, case='normal', click=False, timeout=100):
    host_app = Path(host_app).resolve()
    host = host_app / 'Contents/MacOS/ClassIsland.Desktop'
    if not host.is_file(): raise FileNotFoundError(host)
    package = Path(package).resolve()
    run = ARTIFACTS / ('host-' + time.strftime('%Y%m%d-%H%M%S') + '-' + case)
    sandbox = run / 'sandbox'
    sandbox.mkdir(parents=True)
    (sandbox / 'PackageType').write_text('folder')
    m1.write_json(sandbox / 'data/Settings.json', dict(IsWelcomeWindowShowed=True, IsSplashEnabled=False, HasEditModeTutorialShown=True))
    m1.write_json(sandbox / 'data/Config/Plugins/SystemTools/Main.json', dict(enableAiService=False, autoSwitchClassIslandTheme=False, autoHideMainWindowWhenOccluded=False, futurePlatform=dict(keep=7)))
    validated = run / 'validated'
    metadata = m2.extract(package, validated)
    if metadata.get('milestone') not in ('M6', 'M7'): raise RuntimeError('Provide an explicit M6 or M7 package.')
    if case == 'normal':
        cache = sandbox / 'data/Cache/PluginPackages'
        cache.mkdir(parents=True)
        shutil.copy2(package, cache / 'SystemTools.M6.cipx')
    else:
        destination = sandbox / 'data/Plugins/SystemTools'
        shutil.copytree(validated, destination)
        if case == 'foreign-corrupt':
            for rid in ('win-x64', 'linux-x64'): (destination / m1.module_name(rid)).write_bytes(b'INVALID FOREIGN MODULE')
        else: (destination / m1.module_name('osx-arm64')).unlink()
    probe = ROOT / 'tests/SystemTools.M6.MacHostProbe/bin/Release/net10.0'
    installed_probe = sandbox / 'data/Plugins/SystemTools.M6.MacHostProbe'
    installed_probe.mkdir(parents=True)
    for name in ('SystemTools.M6.MacHostProbe.dll', 'manifest.yml'): shutil.copy2(probe / name, installed_probe / name)
    fixture_app = run / 'SystemToolsM6Fixture.app/Contents'
    (fixture_app / 'MacOS').mkdir(parents=True)
    import plistlib
    (fixture_app / 'Info.plist').write_bytes(plistlib.dumps(dict(CFBundleIdentifier='dev.systemtools.m6.fixture', CFBundleExecutable='fixture', CFBundlePackageType='APPL', NSPrincipalClass='NSApplication')))
    fixture = fixture_app / 'MacOS/fixture'
    m1.command('xcrun', 'swiftc', '-swift-version', '5', '-O', 'tests/SystemTools.M6.WindowFixture/main.swift', '-o', fixture)
    fixture_process = subprocess.Popen([str(fixture), str(run)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    global_path = m1.global_storage_path()
    before = m1.sha(global_path) if global_path.exists() else None
    environment = dict(os.environ, ClassIsland_PackageRoot=str(sandbox), SystemTools_M6_ProbeRoot=str(run), SystemTools_M6_ProbeCase=case)
    for name in list(environment):
        if name.startswith('SystemTools_M') and name not in ('SystemTools_M6_ProbeRoot', 'SystemTools_M6_ProbeCase'): environment.pop(name)
    if click: environment['SystemTools_M6_ClickAcceptance'] = '1'
    timed_out = False
    try:
        deadline = time.monotonic() + 10
        while not (run / 'fixture.json').is_file():
            if fixture_process.poll() is not None or time.monotonic() > deadline: raise RuntimeError('Window fixture failed.')
            time.sleep(.1)
        with (run / 'host.log').open('w') as stream:
            process = subprocess.Popen([str(host), '--skip-oobe', '--quiet', '--verbose'], cwd=host.parent, env=environment, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            print('HOST:', process.pid, 'RUN:', run, flush=True)
            try: process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                timed_out = True; process.terminate()
                try: process.wait(timeout=10)
                except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
    finally:
        fixture_process.terminate()
        try: fixture_process.wait(timeout=5)
        except subprocess.TimeoutExpired: fixture_process.kill(); fixture_process.wait(timeout=5)
    acceptance = json.loads((run / 'acceptance.json').read_text()) if (run / 'acceptance.json').exists() else None
    after = m1.sha(global_path) if global_path.exists() else None
    result = dict(m1.identity(), case=case, packageSha256=m1.sha(package), kind=metadata['kind'], host=str(host_app), hostExecutableSha256=m1.sha(host),
        timedOut=timed_out, exitCode=process.returncode, globalStorageUnchanged=before == after, acceptance=acceptance,
        fullPlatformAcceptance=False, passed=not timed_out and process.returncode == 0 and before == after and acceptance is not None and acceptance['passed'])
    m1.write_json(run / 'result.json', result)
    print(json.dumps(result, ensure_ascii=False, indent=2), flush=True)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='operation', required=True)
    pack = commands.add_parser('preview')
    pack.add_argument('--base-package', required=True)
    pack.add_argument('--output', required=True)
    test = commands.add_parser('smoke')
    test.add_argument('--host-app', default='/Applications/ClassIsland.Desktop.app')
    test.add_argument('--package', required=True)
    test.add_argument('--case', choices=['normal', 'foreign-corrupt', 'missing-backend'], default='normal')
    test.add_argument('--click', action='store_true')
    args = parser.parse_args()
    if args.operation == 'preview': preview(args.base_package, args.output)
    else: raise SystemExit(0 if smoke(args.host_app, args.package, args.case, args.click)['passed'] else 1)
