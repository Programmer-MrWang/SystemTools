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
ARTIFACTS = ROOT / 'artifacts/m5/mac-companion'


def preview(base, output):
    if m1.native_rid() != 'osx-arm64':
        raise RuntimeError('Build this preview on a native ARM64 Mac.')
    m4.ARTIFACTS = ROOT / 'artifacts/m5'
    native = m4.stage('osx-arm64', milestone='M5')
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
    metadata.update(m1.identity(), schemaVersion=1, milestone='M5', kind='mac-development-preview',
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


def smoke(host_app, package, case='normal', timeout=70, manual_authentication=False):
    host_app = Path(host_app).resolve()
    host = host_app / 'Contents/MacOS/ClassIsland.Desktop'
    if not host.is_file():
        raise FileNotFoundError(host)
    probe = ROOT / 'tests/SystemTools.M5.MacHostProbe/bin/Release/net10.0'
    if not (probe / 'SystemTools.M5.MacHostProbe.dll').is_file():
        raise RuntimeError('Build tests/SystemTools.M5.MacHostProbe first.')
    run = ARTIFACTS / ('host-' + time.strftime('%Y%m%d-%H%M%S') + '-' + case)
    sandbox = run / 'sandbox'
    sandbox.mkdir(parents=True)
    (sandbox / 'PackageType').write_text('folder')
    m1.write_json(sandbox / 'data/Settings.json', dict(IsWelcomeWindowShowed=True, IsSplashEnabled=False, HasEditModeTutorialShown=True))
    config = sandbox / 'data/Config/Plugins/SystemTools'
    m1.write_json(config / 'Main.json', dict(enableAiService=False, enableVoiceWakeAi=False, futurePlatform=dict(keep=7)))
    validated = run / 'validated'
    metadata = m2.extract(package, validated)
    if case == 'normal':
        cache = sandbox / 'data/Cache/PluginPackages'
        cache.mkdir(parents=True)
        shutil.copy2(package, cache / 'SystemTools.M5.cipx')
    else:
        destination = sandbox / 'data/Plugins/SystemTools'
        shutil.copytree(validated, destination)
        if case == 'foreign-corrupt':
            for rid in ('win-x64', 'linux-x64'):
                (destination / m1.module_name(rid)).write_bytes(b'INVALID FOREIGN MODULE')
        else:
            (destination / m1.module_name('osx-arm64')).unlink()
    installed_probe = sandbox / 'data/Plugins/SystemTools.M5.MacHostProbe'
    installed_probe.mkdir(parents=True)
    for name in ('SystemTools.M5.MacHostProbe.dll', 'manifest.yml'):
        shutil.copy2(probe / name, installed_probe / name)
    global_path = m1.global_storage_path()
    before = m1.sha(global_path) if global_path.exists() else None
    environment = dict(os.environ, ClassIsland_PackageRoot=str(sandbox), SystemTools_M5_ProbeRoot=str(run), SystemTools_M5_ProbeCase=case)
    model = ARTIFACTS / 'vosk-model-small-en-us-0.15'
    if model.is_dir(): environment['SystemTools_M5_TestModel'] = str(model)
    if manual_authentication: environment['SystemTools_M5_ManualAuthentication'] = '1'
    else: environment.pop('SystemTools_M5_ManualAuthentication', None)
    for name in ('ClassIsland_DebugForceMonoPluginLoadBehavior', 'ClassIsland_DebugSuppressMacOSPluginLoadBehavior', 'ClassIsland_WaitDebuggers',
                 'SystemTools_M1_SmokeTest', 'SystemTools_M2_Acceptance', 'SystemTools_M3_ProbeRoot', 'SystemTools_M4_Acceptance'):
        environment.pop(name, None)
    timed_out = False
    with (run / 'host.log').open('w') as stream:
        process = subprocess.Popen([str(host), '--skip-oobe', '--quiet', '--verbose'], cwd=host.parent, env=environment,
                                   stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        print('HOST:', process.pid, 'RUN:', run, flush=True)
        try:
            process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            process.terminate()
            try: process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
    report_path = run / 'acceptance.json'
    acceptance = json.loads(report_path.read_text()) if report_path.exists() else None
    after = m1.sha(global_path) if global_path.exists() else None
    helper_stopped = True
    if acceptance and acceptance.get('identity'):
        pid = acceptance['identity']['pid']
        deadline = time.monotonic() + 10
        helper_stopped = False
        while time.monotonic() < deadline:
            try: os.kill(pid, 0)
            except ProcessLookupError:
                helper_stopped = True
                break
            time.sleep(0.1)
    result = dict(m1.identity(), case=case, packageSha256=m1.sha(package), kind=metadata['kind'],
        host=str(host_app), hostExecutableSha256=m1.sha(host), hostSourceModified=False, manualAuthentication=manual_authentication,
        timedOut=timed_out, exitCode=process.returncode, helperStopped=helper_stopped,
        globalStorageUnchanged=before == after, acceptance=acceptance,
        passed=not timed_out and process.returncode == 0 and before == after and helper_stopped
               and acceptance is not None and acceptance['passed'])
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
    test.add_argument('--timeout', type=int, default=120)
    test.add_argument('--manual-authentication', action='store_true', help='Require owner success followed by manual cancellation in the real host.')
    args = parser.parse_args()
    if args.operation == 'preview': preview(args.base_package, args.output)
    else: raise SystemExit(0 if smoke(args.host_app, args.package, args.case, args.timeout, args.manual_authentication)['passed'] else 1)
