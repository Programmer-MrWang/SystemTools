#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""M1 build/package/isolated host verification. Python 3.9+, .NET SDK 10."""
from pathlib import Path
import argparse, hashlib, json, os, platform, shutil, subprocess, sys, tempfile, time, zipfile

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts/m1'
MODULES = {'win-x64': 'Windows', 'linux-x64': 'Linux', 'osx-arm64': 'MacOS'}
COMMON = ['SystemTools.dll', 'SystemTools.Contracts.dll', 'SystemTools.deps.json', 'manifest.yml', 'README.md', 'icon.png']

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def command(*arguments):
    subprocess.run([str(a) for a in arguments], cwd=ROOT, check=True)

def native_rid(require_session=False):
    system, arch = platform.system(), platform.machine().lower()
    if system == 'Darwin' and arch in ('arm64', 'aarch64'):
        return 'osx-arm64'
    if system == 'Windows' and arch in ('amd64', 'x86_64'):
        return 'win-x64'
    if system == 'Linux' and arch in ('amd64', 'x86_64'):
        if require_session and (os.environ.get('XDG_SESSION_TYPE', '').lower() != 'x11' or not os.environ.get('DISPLAY') or os.environ.get('WAYLAND_DISPLAY')):
            raise RuntimeError('Runtime validation requires native X11: XDG_SESSION_TYPE=x11, DISPLAY set, WAYLAND_DISPLAY absent. Do not spoof these in a Wayland session.')
        return 'linux-x64'
    raise RuntimeError('Unsupported OS/architecture: ' + system + '/' + arch)

def identity():
    # Content identity includes uncommitted new source too. Ignore bin/obj outputs.
    paths = [ROOT / 'global.json', ROOT / 'SystemTools.slnx']
    paths += [p for p in (ROOT / 'src').rglob('*') if p.is_file() and not {'bin', 'obj'}.intersection(p.relative_to(ROOT).parts)]
    digest = hashlib.sha256()
    for path in sorted(paths):
        digest.update(path.relative_to(ROOT).as_posix().encode())
        digest.update(path.read_bytes())
    return {'commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT).decode().strip(), 'sourceHash': digest.hexdigest()}

def module_name(rid):
    return 'SystemTools.Platforms.' + MODULES[rid] + '.dll'

def build(clean=False):
    rid = native_rid()
    if clean:
        command('dotnet', 'clean', 'SystemTools.slnx', '-c', 'Release', '--nologo', '-v', 'minimal')
    command('dotnet', 'restore', 'SystemTools.slnx', '--nologo')
    command('dotnet', 'build', 'SystemTools.slnx', '-c', 'Release', '--no-restore', '--nologo', '-v', 'minimal')
    output = ROOT / 'src/SystemTools/bin/Release/net10.0'
    stage = ARTIFACTS / rid
    # Only our generated stage is replaced, never any host plugin directory.
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True)
    for name in COMMON + [module_name(rid)]:
        shutil.copy2(output / name, stage / name)
    write_json(stage / 'm1-build.json', dict(identity(), runtimeId=rid, buildMachine=platform.platform(), kind='native', files={n: sha(stage / n) for n in COMMON + [module_name(rid)]}))
    print('LOCAL STAGE:', stage, flush=True)
    return stage

def pack(stages, output, prototype=False):
    stages = [Path(p).resolve() for p in stages]
    builds = [json.loads((p / 'm1-build.json').read_text(encoding='utf-8')) for p in stages]
    pairs = sorted(zip(stages, builds), key=lambda pair: pair[1]['runtimeId'])
    stages, builds = [p for p, _ in pairs], [b for _, b in pairs]
    if not prototype:
        if {b['runtimeId'] for b in builds} != set(MODULES) or len(builds) != 3:
            raise RuntimeError('A native aggregate requires exactly one stage per target OS.')
        if len({b['sourceHash'] for b in builds}) != 1 or len({b['commit'] for b in builds}) != 1:
            raise RuntimeError('Stages are from different source trees/commits.')
        if any(b['kind'] != 'native' for b in builds):
            raise RuntimeError('Native aggregate cannot consume prototype stages.')
    files = {}
    for stage, build_info in zip(stages, builds):
        for name, expected in build_info['files'].items():
            if Path(name).name != name or sha(stage / name) != expected:
                raise RuntimeError('Invalid stage manifest or modified file: ' + name)
        for name in COMMON:
            candidate = stage / name
            if name in files and sha(files[name]) != sha(candidate):
                raise RuntimeError('Shared assembly/metadata conflict: ' + name)
            files[name] = candidate
        name = module_name(build_info['runtimeId'])
        files[name] = stage / name
    if prototype:
        # M1 backends are pure managed descriptors, with no OS SDK/native code.
        # Compile the two other modules for the universal loading prototype only.
        for rid, name in MODULES.items():
            command('dotnet', 'build', ROOT / ('src/SystemTools.Platforms.' + name) / ('SystemTools.Platforms.' + name + '.csproj'), '-c', 'Release', '--nologo', '-v', 'minimal')
            files[module_name(rid)] = ROOT / ('src/SystemTools.Platforms.' + name) / 'bin/Release/net10.0' / module_name(rid)
    output = Path(output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    metadata = {'schemaVersion': 1, 'milestone': 'M1-preview', 'kind': 'local-managed-prototype' if prototype else 'native-aggregate', 'source': builds[0]['sourceHash'], 'builds': builds, 'files': {n: sha(p) for n, p in files.items()}, 'runtimeValidation': 'Separate per-OS smoke evidence required; packaging is not runtime validation.'}
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(files):
            item = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            item.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(item, files[name].read_bytes())
        archive.writestr(zipfile.ZipInfo('m1-package.json', (2026, 1, 1, 0, 0, 0)), json.dumps(metadata, ensure_ascii=False, indent=2))
    output.with_suffix(output.suffix + '.sha256').write_text(sha(output) + '  ' + output.name + '\n', encoding='utf-8')
    print('PACKAGE:', output, '\nSHA256:', sha(output), flush=True)
    return output

def extract(package, destination):
    # Our package format has a strict allowlist: no embedded host/native DLLs.
    allowed = set(COMMON + [module_name(r) for r in MODULES] + ['m1-package.json'])
    with zipfile.ZipFile(package) as archive:
        if set(archive.namelist()) != allowed or len(archive.namelist()) != len(allowed):
            raise RuntimeError('Unexpected/incomplete M1 package contents.')
        manifest = json.loads(archive.read('m1-package.json'))
        for name in allowed - {'m1-package.json'}:
            if hashlib.sha256(archive.read(name)).hexdigest() != manifest['files'].get(name):
                raise RuntimeError('Package hash mismatch: ' + name)
        archive.extractall(destination)

def smoke(host, package, case, timeout):
    rid = native_rid(require_session=True)
    host, package = Path(host).resolve(), Path(package).resolve()
    if not host.is_file() or not package.is_file():
        raise FileNotFoundError('Provide existing --host-bin and --package paths.')
    run = ARTIFACTS / 'runs' / (time.strftime('%Y%m%d-%H%M%S') + '-' + case)
    run.mkdir(parents=True, exist_ok=False)
    sandbox = run / 'sandbox'
    sandbox.mkdir()
    (sandbox / 'PackageType').write_text('folder', encoding='utf-8')
    # The host shows its welcome wizard before AppStarted even with --skip-oobe.
    # Seed only synthetic settings in this freshly created sandbox.
    write_json(sandbox / 'data/Settings.json', {'IsWelcomeWindowShowed': True, 'IsSplashEnabled': False, 'HasEditModeTutorialShown': True})
    config = sandbox / 'data/Config/Plugins/SystemTools'
    config.mkdir(parents=True)
    before = {}
    for fixture in (ROOT / 'tests/fixtures/legacy-windows').glob('*.json'):
        shutil.copy2(fixture, config / fixture.name)
        before[fixture.name] = sha(config / fixture.name)
    # Normal case exercises the host's actual CIPX installation path.
    plugin = sandbox / 'data/Plugins/SystemTools'
    if case == 'normal':
        extract(package, run / 'validated-package')
        packages = sandbox / 'data/Cache/PluginPackages'
        packages.mkdir(parents=True)
        shutil.copy2(package, packages / 'SystemTools.M1.cipx')
    else:
        plugin.mkdir(parents=True)
        extract(package, plugin)
        if case == 'foreign-corrupt':
            for other in MODULES:
                if other != rid:
                    (plugin / module_name(other)).write_bytes(b'INVALID FOREIGN MODULE: must never be loaded')
        elif case == 'missing-backend':
            (plugin / module_name(rid)).unlink()
    env = os.environ.copy()
    env['ClassIsland_PackageRoot'] = str(sandbox)
    env['SystemTools_M1_SmokeTest'] = '1'
    # Exclude inherited plugin/debug-loader injection from the test environment.
    for key in ('ClassIsland_DebugForceMonoPluginLoadBehavior', 'ClassIsland_DebugSuppressMacOSPluginLoadBehavior', 'ClassIsland_WaitDebuggers'):
        env.pop(key, None)
    logfile = run / 'host.log'
    failure = None
    proof = None
    with logfile.open('wb') as log:
        process = subprocess.Popen([str(host), '--skip-oobe', '--quiet', '--verbose'], cwd=host.parent, env=env, stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + timeout
            report_name = 'M1.LoadFailure.json' if case == 'missing-backend' else 'M1.HostSmoke.json'
            while time.monotonic() < deadline:
                path = config / report_name
                if path.is_file():
                    try:
                        proof = json.loads(path.read_text(encoding='utf-8'))
                        break
                    except json.JSONDecodeError:
                        pass
                if process.poll() is not None:
                    raise RuntimeError('Host exited before evidence; inspect ' + str(logfile))
                time.sleep(0.2)
            if proof is None:
                raise TimeoutError('No host evidence; inspect ' + str(logfile))
            if case == 'missing-backend':
                if 'FileNotFoundException' not in proof.get('error', '') or module_name(rid)[:-4] not in proof['error']:
                    raise RuntimeError('Expected explicit missing local backend failure.')
            elif not proof.get('passed') or proof.get('runtimeId') != rid:
                raise RuntimeError('Host smoke failed: ' + json.dumps(proof, ensure_ascii=False))
        except Exception as ex:
            failure = str(ex)
        finally:
            # Only stop the process this invocation created. No global kill/name matching.
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
    after = {name: sha(config / name) for name in before}
    if before != after:
        failure = 'Legacy fixture bytes changed during host startup/shutdown.'
    evidence = dict(identity(), case=case, passed=failure is None, runtimeId=rid, machine=platform.platform(), host=str(host), hostSha256=sha(host), packageSha256=sha(package), fixtureHashesUnchanged=before == after, evidence=proof, error=failure)
    write_json(run / 'result.json', evidence)
    print('EVIDENCE:', run / 'result.json', flush=True)
    if failure:
        raise RuntimeError(failure)
    print('PASS host smoke:', case, rid, flush=True)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='operation', required=True)
    b = sub.add_parser('build'); b.add_argument('--clean', action='store_true')
    p = sub.add_parser('prototype'); p.add_argument('--output', default=str(ARTIFACTS / 'SystemTools.M1.cipx'))
    a = sub.add_parser('package'); a.add_argument('--input', nargs=3, required=True); a.add_argument('--output', default=str(ARTIFACTS / 'SystemTools.M1.cipx'))
    t = sub.add_parser('test'); t.add_argument('--stage')
    s = sub.add_parser('smoke'); s.add_argument('--host-bin', required=True); s.add_argument('--package', required=True); s.add_argument('--case', choices=['normal', 'foreign-corrupt', 'missing-backend'], default='normal'); s.add_argument('--timeout', type=int, default=60)
    args = parser.parse_args()
    if args.operation == 'build': build(args.clean)
    elif args.operation == 'prototype': pack([build()], args.output, prototype=True)
    elif args.operation == 'package': pack(args.input, args.output)
    elif args.operation == 'test': command('dotnet', 'run', '--project', 'tests/SystemTools.M1.Tests', '-c', 'Release', '--', args.stage or ARTIFACTS / native_rid())
    elif args.operation == 'smoke': smoke(args.host_bin, args.package, args.case, args.timeout)

if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, OSError, subprocess.CalledProcessError) as error:
        print('ERROR:', error, file=sys.stderr)
        sys.exit(1)
