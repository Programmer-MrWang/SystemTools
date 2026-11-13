#!/usr/bin/env python3
"""Build, package and verify the Windows M2 preview in isolated desktop data."""
from pathlib import Path, PurePosixPath
import argparse
import json
import hashlib
import os
import shutil
import stat
import subprocess
import time
import zipfile
import m1

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts/m2'
TFM = 'net10.0-windows10.0.19041.0'
PRIVATE = 'platforms/win-x64'
EXTRAS = ['black.html', 'title.png', 'version.json', 'README-1.md', 'README-2.md',
          'Lyricify Lite - README.md', 'huifu.bat', 'huifu.ps1',
          'jinyongshubiao.bat', 'jinyongshubiao.ps1']
MODULES = [m1.module_name(rid) for rid in m1.MODULES]
HOST_PREFIXES = ('classisland.', 'avalonia', 'microsoft.extensions.', 'fluentavalonia',
                 'winrt.', 'microsoft.windows.sdk.')

def sdk_shared_assemblies():
    assets = json.loads((ROOT / 'src/SystemTools.Platforms.Windows/obj/project.assets.json').read_text(encoding='utf-8'))
    target = next(iter(assets['targets'].values()))
    packages = {key.rsplit('/', 1)[0].casefold(): value for key, value in target.items()}
    pending = ['classisland.pluginsdk', 'avalonia', 'microsoft.extensions.dependencyinjection']
    visited, assemblies = set(), set()
    while pending:
        name = pending.pop().casefold()
        if name in visited:
            continue
        visited.add(name)
        package_info = packages.get(name, {})
        pending.extend(package_info.get('dependencies', {}))
        for group in ('runtime', 'runtimeTargets'):
            assemblies.update(PurePosixPath(path).name.casefold()
                              for path in package_info.get(group, {}) if path.endswith('.dll'))
    return assemblies

def safe_name(name):
    parts = PurePosixPath(name).parts
    return (bool(parts) and not name.startswith('/') and '\\' not in name and ':' not in name
            and all(part not in ('.', '..', '') for part in name.split('/')))

def verify_files(names, manifest, read):
    expected = set(manifest['files']) | {'m2-package.json'}
    if len(names) != len(set(name.casefold() for name in names)) or set(names) != expected:
        raise RuntimeError('Package contents differ from the file manifest or contain duplicate paths.')
    shared = set(manifest['hostSharedAssemblies'])
    for name in names:
        if not safe_name(name):
            raise RuntimeError('Invalid package path: ' + name)
        if name == 'm2-package.json':
            continue
        if hashlib.sha256(read(name)).hexdigest() != manifest['files'][name]:
            raise RuntimeError('Package hash mismatch: ' + name)
        basename = PurePosixPath(name).name.casefold()
        is_worker = name.startswith(PRIVATE + '/VoskWorker/')
        if basename.endswith('.dll') and not is_worker:
            if basename in shared or basename.startswith(HOST_PREFIXES):
                raise RuntimeError('Duplicate host/shared assembly: ' + name)
        if '/' not in name:
            if name not in set(m1.COMMON + MODULES + EXTRAS):
                raise RuntimeError('Unexpected root file: ' + name)
        elif not name.startswith(PRIVATE + '/'):
            raise RuntimeError('Unexpected platform asset: ' + name)
    if not set(m1.COMMON + MODULES).issubset(names):
        raise RuntimeError('Missing entry, contract or platform module.')

def validate(directory):
    directory = Path(directory)
    manifest = json.loads((directory / 'm2-package.json').read_text(encoding='utf-8'))
    files = [path for path in directory.rglob('*') if path.is_file()]
    if any(path.is_symlink() for path in directory.rglob('*')):
        raise RuntimeError('Package input cannot contain symlinks.')
    verify_files([path.relative_to(directory).as_posix() for path in files],
                 manifest, lambda name: (directory / name).read_bytes())
    return manifest

def stage(host, destination):
    if m1.native_rid() != 'win-x64':
        raise RuntimeError('The M2 Windows stage requires a native Windows x64 build.')
    host = Path(host).resolve() if host else None
    destination = Path(destination).resolve()
    if host and not host.is_file():
        raise FileNotFoundError(host)
    if destination == ARTIFACTS.resolve() or not destination.is_relative_to(ARTIFACTS.resolve()):
        raise RuntimeError('M2 stage must be an artifacts/m2 child.')
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    entry = ROOT / 'src/SystemTools/bin/Release/net10.0'
    windows = ROOT / 'src/SystemTools.Platforms.Windows/bin/Release' / TFM
    shared = ({path.name.casefold() for path in host.parent.rglob('*.dll')}
              if host else sdk_shared_assemblies())
    for name in m1.COMMON:
        shutil.copy2(entry / name, destination / name)
    for path in windows.glob('*.dll'):
        if path.name.casefold() in shared or path.name.casefold().startswith(HOST_PREFIXES):
            continue
        if path.name in {'SystemTools.dll', 'SystemTools.Contracts.dll'}:
            continue
        target = destination / path.name if path.name in MODULES else destination / PRIVATE / path.name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
    for name in ('Linux', 'MacOS'):
        project = ROOT / f'src/SystemTools.Platforms.{name}/SystemTools.Platforms.{name}.csproj'
        m1.command('dotnet', 'build', project, '-c', 'Release', '--nologo', '-v', 'minimal')
        shutil.copy2(project.parent / f'bin/Release/net10.0/SystemTools.Platforms.{name}.dll',
                     destination / f'SystemTools.Platforms.{name}.dll')
    for name in EXTRAS:
        shutil.copy2(ROOT / name, destination / name)
    for path in (windows / 'runtimes/win-x64').rglob('*.dll'):
        if path.name.casefold() not in shared:
            target = destination / PRIVATE / path.relative_to(windows)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
    worker = ROOT / 'VoskWorker/bin/Release/net10.0/win-x64'
    if not worker.is_dir():
        raise RuntimeError('Build VoskWorker/SystemTools.VoskWorker.csproj before packaging Windows M2.')
    shutil.copytree(worker, destination / PRIVATE / 'VoskWorker')
    files = {path.relative_to(destination).as_posix(): m1.sha(path)
             for path in sorted(destination.rglob('*')) if path.is_file()}
    m1.write_json(destination / 'm2-package.json',
                  dict(m1.identity(), schemaVersion=1, milestone='M2-Windows-preview',
                       kind='native-windows-with-managed-platform-descriptors',
                       sdk=subprocess.check_output(['dotnet', '--version'], cwd=ROOT, text=True).strip(),
                       host=str(host) if host else None, hostSha256=m1.sha(host) if host else None,
                       hostContract='actual-host-files' if host else 'sdk-contract-only-not-runtime-validated',
                       hostSharedAssemblies=sorted(shared), files=files,
                       linuxRuntimeValidated=False, macBusinessImplemented=False))
    validate(destination)
    return destination

def package(source, output):
    source, output = Path(source).resolve(), Path(output).resolve()
    manifest = validate(source)
    if output == source or source in output.parents:
        raise RuntimeError('Write the CIPX outside its input stage.')
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(list(manifest['files']) + ['m2-package.json']):
            info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, (source / name).read_bytes())
    output.with_suffix(output.suffix + '.sha256').write_text(
        m1.sha(output) + '  ' + output.name + '\n', encoding='utf-8')
    return output

def extract(package_path, destination):
    destination = Path(destination).resolve()
    with zipfile.ZipFile(package_path) as archive:
        names = archive.namelist()
        if 'm2-package.json' not in names:
            raise RuntimeError('Missing M2 package manifest.')
        manifest = json.loads(archive.read('m2-package.json'))
        verify_files(names, manifest, archive.read)
        if any(stat.S_ISLNK(item.external_attr >> 16) for item in archive.infolist()):
            raise RuntimeError('Package cannot contain symlinks.')
        archive.extractall(destination)
    return manifest

def smoke(host, source, timeout=40, missing_dependencies=False, case='normal', experimental=False):
    if m1.native_rid() != 'win-x64':
        raise RuntimeError('Windows M2 desktop acceptance must run on Windows x64.')
    host, source = Path(host).resolve(), Path(source).resolve()
    run = ARTIFACTS / 'runs' / (time.strftime('%Y%m%d-%H%M%S') + '-' + case)
    run.mkdir(parents=True, exist_ok=False)
    sandbox = run / 'sandbox'
    plugin = sandbox / 'data/Plugins/SystemTools'
    if source.is_file():
        extract(source, run / 'validated-package')
        if case == 'normal':
            cache = sandbox / 'data/Cache/PluginPackages'
            cache.mkdir(parents=True)
            shutil.copy2(source, cache / source.name)
        else:
            shutil.copytree(run / 'validated-package', plugin)
    else:
        validate(source)
        shutil.copytree(source, plugin)
    if case == 'foreign-corrupt':
        for rid in ('linux-x64', 'osx-arm64'):
            (plugin / m1.module_name(rid)).write_bytes(b'Foreign module must not load on Windows')
    elif case == 'missing-backend':
        (plugin / m1.module_name('win-x64')).unlink()
    sandbox.mkdir(parents=True, exist_ok=True)
    (sandbox / 'PackageType').write_text('folder', encoding='utf-8')
    m1.write_json(sandbox / 'data/Settings.json',
                  {'IsWelcomeWindowShowed': True, 'IsSplashEnabled': False,
                   'HasEditModeTutorialShown': True})
    config = sandbox / 'data/Config/Plugins/SystemTools'
    config.mkdir(parents=True)
    original = {'enableAiService': True, 'enableFloatingWindowFeature': True,
                'enableExperimentalFeatures': experimental,
                'aiApiKey': 'dpapi-current-user:v1:not-a-real-user-secret',
                'm2UnknownField': {'preserved': True, 'windowsOnly': [1, 2, 3]}}
    if missing_dependencies:
        original.update(enableFfmpegFeatures=True, enableFaceRecognition=True)
    m1.write_json(config / 'Main.json', original)
    environment = os.environ.copy()
    environment['ClassIsland_PackageRoot'] = str(sandbox)
    environment['SystemTools_M2_Acceptance'] = '1'
    for name in ('SystemTools_M1_SmokeTest', 'ClassIsland_WaitDebuggers',
                 'ClassIsland_DebugForceMonoPluginLoadBehavior',
                 'ClassIsland_DebugSuppressMacOSPluginLoadBehavior'):
        environment.pop(name, None)
    if case == 'missing-backend':
        environment['SystemTools_M1_SmokeTest'] = '1'
    global_path = m1.global_storage_path()
    before = m1.sha(global_path) if global_path.exists() else None
    proof = None
    with (run / 'host.log').open('wb') as log:
        process = subprocess.Popen([str(host), '--quiet', '--skip-oobe', '--verbose'],
                                   cwd=host.parent, env=environment, stdout=log,
                                   stderr=subprocess.STDOUT,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            deadline = time.monotonic() + timeout
            evidence = 'M1.LoadFailure.json' if case == 'missing-backend' else 'M2.Acceptance.json'
            while time.monotonic() < deadline:
                path = config / evidence
                if path.is_file():
                    try:
                        proof = json.loads(path.read_text(encoding='utf-8'))
                        break
                    except json.JSONDecodeError:
                        pass
                if process.poll() is not None:
                    break
                time.sleep(.2)
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=10)
    after = m1.sha(global_path) if global_path.exists() else None
    saved = json.loads((config / 'Main.json').read_text(encoding='utf-8'))
    keys = ['aiApiKey', 'm2UnknownField']
    if missing_dependencies:
        keys += ['enableFfmpegFeatures', 'enableFaceRecognition']
    preserved = all(saved.get(key) == original[key] for key in keys)
    if case == 'missing-backend':
        expected = bool(proof and 'FileNotFoundException' in proof.get('error', '')
                        and 'SystemTools.Platforms.Windows' in proof.get('error', ''))
    else:
        expected = bool(proof and proof.get('passed'))
    result = dict(m1.identity(), passed=expected and preserved and before == after,
                  case=case, proof=proof, host=str(host), hostSha256=m1.sha(host),
                  source=str(source), packageSha256=m1.sha(source) if source.is_file() else None,
                  globalUnchanged=before == after, configContractPreserved=preserved,
                  run=str(run), hostExitCode=process.returncode,
                  missingDependenciesRequested=missing_dependencies, experimental=experimental)
    m1.write_json(run / 'result.json', result)
    print(json.dumps(result, ensure_ascii=False, indent=2), flush=True)
    if not result['passed']:
        raise RuntimeError('M2 desktop acceptance failed; inspect ' + str(run / 'host.log'))
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['stage', 'package', 'smoke'])
    parser.add_argument('--host-bin')
    parser.add_argument('--stage', default=str(ARTIFACTS / 'stage'))
    parser.add_argument('--package')
    parser.add_argument('--output', default=str(ARTIFACTS / 'SystemTools.M2.cipx'))
    parser.add_argument('--missing-dependencies', action='store_true')
    parser.add_argument('--experimental', action='store_true')
    parser.add_argument('--case', choices=['normal', 'foreign-corrupt', 'missing-backend'], default='normal')
    args = parser.parse_args()
    if args.operation == 'package':
        print(package(args.stage, args.output))
    else:
        if args.operation == 'stage':
            print(stage(args.host_bin, args.stage))
        else:
            if not args.host_bin:
                parser.error('--host-bin is required for smoke.')
            smoke(args.host_bin, args.package or args.stage, missing_dependencies=args.missing_dependencies,
                  case=args.case, experimental=args.experimental)

if __name__ == '__main__':
    main()
