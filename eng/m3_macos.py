#!/usr/bin/env python3
"""Verify M3 packages in a real, isolated native macOS host."""
from pathlib import Path
import argparse
import json
import os
import plistlib
import shutil
import subprocess
import time
import zipfile
import m1
import m2

ARTIFACTS = m1.ROOT / 'artifacts/m3/mac'
PROBE = m1.ROOT / 'tests/SystemTools.M3.DesktopProbe/bin/Release/net10.0'


def build_host(source, target_version, minimum_version, skip_xcode_version_check):
    source = Path(source).resolve()
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    plist = plistlib.loads((source / 'ClassIsland.Desktop/Info.plist').read_bytes())
    plist['LSMinimumSystemVersion'] = minimum_version
    plist_path = ARTIFACTS / 'host-Info.plist'
    plist_path.write_bytes(plistlib.dumps(plist))
    targets = ARTIFACTS / 'host-local.targets'
    from xml.sax.saxutils import escape
    targets.write_text('''<Project>
  <PropertyGroup Condition="$(TargetFramework.Contains('-macos'))">
    <TargetFramework>net10.0-macos%s</TargetFramework>
    <TargetPlatformVersion>%s</TargetPlatformVersion>
    <SupportedOSPlatformVersion>%s</SupportedOSPlatformVersion>
  </PropertyGroup>
  <PropertyGroup Condition="'$(MSBuildProjectName)' == 'ClassIsland.Desktop'">
    <AppBundleManifest>%s</AppBundleManifest>
  </PropertyGroup>
</Project>
''' % tuple(escape(value) for value in (target_version, target_version, minimum_version, str(plist_path))), encoding='utf-8')
    command = ['dotnet', 'build', 'ClassIsland.Desktop/ClassIsland.Desktop.csproj', '-c', 'Debug',
        '--nologo', '-v', 'minimal', '-p:DirectoryBuildTargetsPath=' + str(targets)]
    if skip_xcode_version_check:
        command.append('-p:ValidateXcodeVersion=false')
    log = ARTIFACTS / 'host-verification-build.log'
    with log.open('w') as stream:
        completed = subprocess.run(command, cwd=source, stdout=stream, stderr=subprocess.STDOUT)
    m1.write_json(ARTIFACTS / 'host-build.json', dict(command=command, source=str(source),
        commit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=source, text=True).strip(),
        targetVersion=target_version, minimumVersion=minimum_version,
        xcodeVersionCheckSkipped=skip_xcode_version_check, passed=completed.returncode == 0,
        scope='Local acceptance override only; no host source edits or distribution compatibility claim.'))
    print('HOST BUILD:', completed.returncode, 'LOG:', log, flush=True)
    if completed.returncode:
        raise RuntimeError('Host verification build failed; inspect the complete build log.')


def extract(package, destination):
    with zipfile.ZipFile(package) as archive:
        manifest = json.loads(archive.read('m2-package.json'))
        m2.verify_files(archive.namelist(), manifest, archive.read)
        archive.extractall(destination)
    return manifest


def native_package(base, output):
    if m1.native_rid() != 'osx-arm64':
        raise RuntimeError('Build the native preview on an ARM64 Mac.')
    stage = ARTIFACTS / ('native-stage-' + str(time.time_ns()))
    stage.mkdir(parents=True)
    manifest = extract(base, stage)
    built = m1.ROOT / 'src/SystemTools/bin/Release/net10.0'
    for name in m1.COMMON + [m1.module_name('osx-arm64')]:
        shutil.copy2(built / name, stage / name)
    for path in (built / 'platforms/shared').glob('*.dll'):
        if path.name.casefold() not in manifest['hostSharedAssemblies']:
            shutil.copy2(path, stage / 'platforms/shared' / path.name)
    manifest['nativeMacPreview'] = dict(m1.identity(), basePackageSha256=m1.sha(base),
        executionOS='darwin', runtimeId='osx-arm64',
        scope='Native Mac shared/entry/backend outputs with unchanged foreign assets; not a new three-OS release acceptance.')
    manifest['files'] = {path.relative_to(stage).as_posix(): m1.sha(path)
                         for path in sorted(stage.rglob('*')) if path.is_file() and path.name != 'm2-package.json'}
    m1.write_json(stage / 'm2-package.json', manifest)
    result = m2.package(stage, output)
    print('NATIVE MAC PREVIEW:', result, 'SHA256:', m1.sha(result), flush=True)


def smoke(host, package, case, label, observe_seconds, timeout):
    if m1.native_rid() != 'osx-arm64':
        raise RuntimeError('Runtime acceptance requires a native ARM64 Mac.')
    host, package = Path(host).resolve(), Path(package).resolve()
    if not host.is_file() or not package.is_file() or not (PROBE / 'SystemTools.M3.DesktopProbe.dll').is_file():
        raise FileNotFoundError('Provide a built desktop host, package and DesktopProbe.')
    run = ARTIFACTS / 'runs' / (time.strftime('%Y%m%d-%H%M%S') + '-' + label + '-' + case)
    run.mkdir(parents=True)
    sandbox = run / 'sandbox'
    sandbox.mkdir()
    (sandbox / 'PackageType').write_text('folder', encoding='utf-8')
    m1.write_json(sandbox / 'data/Settings.json', dict(IsWelcomeWindowShowed=True, IsSplashEnabled=False, HasEditModeTutorialShown=True))
    config = sandbox / 'data/Config/Plugins/SystemTools'
    config.mkdir(parents=True)
    m1.write_json(config / 'Main.json', dict(aiApiKey='dpapi-current-user:v1:REDACTED-INACCESSIBLE',
        enableWindowsHello=True, futurePlatform=dict(keep=7)))
    validated = run / 'validated-package'
    manifest = extract(package, validated)
    if case == 'normal':
        cache = sandbox / 'data/Cache/PluginPackages'
        cache.mkdir(parents=True)
        shutil.copy2(package, cache / 'SystemTools.M3.cipx')
    else:
        plugin = sandbox / 'data/Plugins/SystemTools'
        shutil.copytree(validated, plugin)
        if case == 'foreign-corrupt':
            for rid in ('win-x64', 'linux-x64'):
                (plugin / m1.module_name(rid)).write_bytes(b'INVALID FOREIGN MODULE: must never load')
        else:
            (plugin / m1.module_name('osx-arm64')).unlink()
    probe = sandbox / 'data/Plugins/SystemTools.M3.DesktopProbe'
    probe.mkdir(parents=True)
    for name in ('SystemTools.M3.DesktopProbe.dll', 'manifest.yml'):
        shutil.copy2(PROBE / name, probe / name)
    global_path = m1.global_storage_path()
    before = m1.sha(global_path) if global_path.is_file() else None
    environment = os.environ.copy()
    for key in ('ClassIsland_DebugForceMonoPluginLoadBehavior', 'ClassIsland_DebugSuppressMacOSPluginLoadBehavior',
                'ClassIsland_WaitDebuggers', 'SystemTools_M1_SmokeTest', 'SystemTools_M2_Acceptance'):
        environment.pop(key, None)
    environment.update(ClassIsland_PackageRoot=str(sandbox), SystemTools_M3_ProbeRoot=str(run),
        SystemTools_M3_ProbeCase=case, SystemTools_M3_ObserveSeconds=str(observe_seconds),
        TMPDIR=str(Path(os.environ.get('TMPDIR', '/tmp')).resolve()))
    timed_out = False
    with (run / 'host.log').open('w') as stream:
        process = subprocess.Popen([str(host), '--skip-oobe', '--quiet', '--verbose'], cwd=host.parent,
            env=environment, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        print('HOST PID:', process.pid, 'RUN:', run, flush=True)
        try:
            process.wait(timeout=timeout + observe_seconds)
        except subprocess.TimeoutExpired:
            timed_out = True
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
    after = m1.sha(global_path) if global_path.is_file() else None
    reports = {}
    for name in ('acceptance', 'shutdown'):
        path = run / (name + '.json')
        reports[name] = json.loads(path.read_text()) if path.is_file() else None
    plugins = sorted(p.name for p in (sandbox / 'data/Plugins').iterdir() if p.is_dir())
    result = dict(m1.identity(), package=str(package), packageSha256=m1.sha(package),
        packageSource=manifest.get('nativeMacPreview', manifest.get('sourceHash')),
        host=str(host), hostSha256=m1.sha(host), case=case, label=label,
        processId=process.pid, exitCode=process.returncode, timedOut=timed_out,
        executionOS='darwin', nativeRuntime='osx-arm64', plugins=plugins,
        globalStorageBefore=before, globalStorageAfter=after, reports=reports,
        passed=not timed_out and process.returncode == 0 and before == after
            and plugins == ['SystemTools', 'SystemTools.M3.DesktopProbe']
            and all(report is not None and report['passed'] for report in reports.values()))
    m1.write_json(run / 'result.json', result)
    print(json.dumps(dict(passed=result['passed'], case=case, exitCode=process.returncode,
        globalStorageUnchanged=before == after,
        reports={name: None if report is None else dict(passed=report['passed'], checks=len(report['checks']),
            failures=[error[:1800] for error in report['failures']]) for name, report in reports.items()}),
        ensure_ascii=False, indent=2), flush=True)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='operation', required=True)
    host = sub.add_parser('host-build')
    host.add_argument('--host-source', required=True)
    host.add_argument('--target-version', required=True)
    host.add_argument('--minimum-version', required=True)
    host.add_argument('--skip-xcode-version-check', action='store_true')
    pack = sub.add_parser('native-package')
    pack.add_argument('--base-package', required=True)
    pack.add_argument('--output', required=True)
    test = sub.add_parser('smoke')
    test.add_argument('--host-bin', required=True)
    test.add_argument('--package', required=True)
    test.add_argument('--label', choices=['transferred', 'native'], required=True)
    test.add_argument('--case', choices=['all', 'normal', 'foreign-corrupt', 'missing-backend'], default='all')
    test.add_argument('--observe-seconds', type=int, default=0)
    test.add_argument('--timeout', type=int, default=90)
    args = parser.parse_args()
    if args.operation == 'host-build':
        build_host(args.host_source, args.target_version, args.minimum_version, args.skip_xcode_version_check)
    elif args.operation == 'native-package':
        native_package(args.base_package, args.output)
    else:
        cases = ['normal', 'foreign-corrupt', 'missing-backend'] if args.case == 'all' else [args.case]
        results = [smoke(args.host_bin, args.package, case, args.label,
            args.observe_seconds if case == 'normal' else 0, args.timeout) for case in cases]
        m1.write_json(ARTIFACTS / (args.label + '-desktop.json'), dict(passed=all(x['passed'] for x in results),
            executionOS='darwin', cases=results))
        raise SystemExit(0 if all(x['passed'] for x in results) else 1)
