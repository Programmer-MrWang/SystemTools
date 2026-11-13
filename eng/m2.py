#!/usr/bin/env python3
"""M2 Windows staging and isolated desktop acceptance; never uses daily host data."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import time
import zipfile
import m1

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts/m2'
TFM = 'net10.0-windows10.0.19041.0'

def stage(host, destination):
    host = Path(host).resolve()
    destination = Path(destination).resolve()
    if destination == ROOT or not destination.is_relative_to(ARTIFACTS.resolve()):
        raise RuntimeError('M2 stage must be an artifacts/m2 child.')
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    entry = ROOT / 'src/SystemTools/bin/Release/net10.0'
    windows = ROOT / 'src/SystemTools.Platforms.Windows/bin/Release' / TFM
    shared = {path.name.casefold() for path in host.parent.glob('*.dll')}
    for name in m1.COMMON:
        shutil.copy2(entry / name, destination / name)
    for path in windows.glob('*.dll'):
        if path.name.casefold() in shared or path.name.startswith('ClassIsland.'):
            continue
        if path.name in {'SystemTools.dll', 'SystemTools.Contracts.dll'}:
            continue
        shutil.copy2(path, destination / path.name)
    for platform in ('Linux', 'MacOS'):
        path = ROOT / f'src/SystemTools.Platforms.{platform}/bin/Release/net10.0/SystemTools.Platforms.{platform}.dll'
        if not path.exists():
            m1.command('dotnet', 'build', path.parents[3] / f'SystemTools.Platforms.{platform}.csproj',
                       '-c', 'Release', '--nologo', '-v', 'minimal')
        shutil.copy2(path, destination / path.name)
    for name in ('black.html', 'title.png', 'version.json', 'README-1.md', 'README-2.md',
                 'Lyricify Lite - README.md', 'huifu.bat', 'huifu.ps1',
                 'jinyongshubiao.bat', 'jinyongshubiao.ps1'):
        shutil.copy2(ROOT / name, destination / name)
    for path in (windows / 'runtimes/win-x64').rglob('*'):
        if path.is_file() and not any(p.name.casefold() == path.name.casefold() for p in host.parent.rglob(path.name)):
            target = destination / path.relative_to(windows)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
    files = {path.relative_to(destination).as_posix(): m1.sha(path)
             for path in sorted(destination.rglob('*')) if path.is_file()}
    m1.write_json(destination / 'm2-package.json',
                  dict(m1.identity(), milestone='M2-Windows-preview', host=str(host),
                       hostSharedAssemblies=sorted(shared), files=files))
    return destination

def smoke(host, source, timeout=40):
    host, source = Path(host).resolve(), Path(source).resolve()
    run = ARTIFACTS / 'runs' / (time.strftime('%Y%m%d-%H%M%S') + '-windows')
    sandbox = run / 'sandbox'
    plugin = sandbox / 'data/Plugins/SystemTools'
    plugin.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source, plugin)
    (sandbox / 'PackageType').write_text('folder', encoding='utf-8')
    m1.write_json(sandbox / 'data/Settings.json',
                  {'IsWelcomeWindowShowed': True, 'IsSplashEnabled': False,
                   'HasEditModeTutorialShown': True})
    config = sandbox / 'data/Config/Plugins/SystemTools'
    config.mkdir(parents=True)
    original = {'enableAiService': True, 'enableFloatingWindowFeature': True,
                'aiApiKey': 'dpapi-current-user:v1:not-a-real-user-secret',
                'm2UnknownField': {'preserved': True, 'windowsOnly': [1, 2, 3]}}
    m1.write_json(config / 'Main.json', original)
    environment = os.environ.copy()
    environment['ClassIsland_PackageRoot'] = str(sandbox)
    environment['SystemTools_M2_Acceptance'] = '1'
    for name in ('SystemTools_M1_SmokeTest', 'ClassIsland_WaitDebuggers',
                 'ClassIsland_DebugForceMonoPluginLoadBehavior',
                 'ClassIsland_DebugSuppressMacOSPluginLoadBehavior'):
        environment.pop(name, None)
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
            path = config / 'M2.Acceptance.json'
            while time.monotonic() < deadline:
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
    preserved = all(saved.get(key) == original[key] for key in ('aiApiKey', 'm2UnknownField'))
    result = dict(m1.identity(), passed=bool(proof and proof.get('passed') and preserved and before == after),
                  proof=proof, host=str(host), hostSha256=m1.sha(host),
                  source=str(source), globalUnchanged=before == after,
                  configContractPreserved=preserved, run=str(run),
                  hostExitCode=process.returncode)
    m1.write_json(run / 'result.json', result)
    print(json.dumps(result, ensure_ascii=False, indent=2), flush=True)
    if not result['passed']:
        raise RuntimeError('M2 desktop acceptance failed; inspect ' + str(run / 'host.log'))
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['stage', 'smoke'])
    parser.add_argument('--host-bin', required=True)
    parser.add_argument('--stage', default=str(ARTIFACTS / 'stage'))
    args = parser.parse_args()
    if args.operation == 'stage':
        print(stage(args.host_bin, args.stage))
    else:
        smoke(args.host_bin, args.stage)

if __name__ == '__main__':
    main()

