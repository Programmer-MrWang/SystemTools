#!/usr/bin/env python3
"""Prepare an isolated Mac interaction lab using our probe and an unchanged installed host."""
from pathlib import Path
import argparse,json,os,plistlib,shutil,subprocess,time,uuid,zipfile
import m1,m2,m4,m7,m8,m7_macos

ROOT=m1.ROOT
ARTIFACTS=ROOT/'artifacts/m8/mac'

def fixture_alive(root):
    state=root/'fixture.json'
    if not state.exists():return False
    pid=json.loads(state.read_text(encoding='utf-8'))['pid']
    command=subprocess.run(['/bin/ps','-p',str(pid),'-o','command='],capture_output=True,text=True)
    return command.returncode==0 and str(root/'SystemToolsM8Fixture.app/Contents/MacOS/fixture') in command.stdout

def preview(base,output):
    m7.ARTIFACTS=ROOT/'artifacts/m8'
    stage=m7.stage('osx-arm64',milestone='M8');current=m4.verify_stage(stage)
    folder=ROOT/'artifacts/m8'/('preview-'+uuid.uuid4().hex[:8]);metadata=m2.extract(base,folder)
    for name in list(metadata['files']):
        if name.startswith('platforms/shared/') or name.startswith('release/'):(folder/name).unlink()
    for name in current['files']:
        target=folder/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(stage/name,target)
    metadata.update(m1.identity(),milestone='M8',kind='local-mac-development-preview',fullPlatformAcceptance=False)
    metadata['files']={p.relative_to(folder).as_posix():m1.sha(p) for p in folder.rglob('*') if p.is_file() and p.name!='m2-package.json'}
    m1.write_json(folder/'m2-package.json',metadata);return m2.package(folder,output)

def prepare(package,existing=None):
    m7_macos.lifecycle.stopped()
    m7_macos.require_unlocked_desktop()
    root=Path(existing).absolute() if existing else ARTIFACTS/(time.strftime('%Y%m%d-%H%M%S')+'-'+uuid.uuid4().hex[:6])
    sandbox=root/'sandbox'
    if existing and not sandbox.is_dir():raise RuntimeError('Existing lab sandbox does not exist.')
    if not existing:
        m7_macos.seed(sandbox)
        m2.extract(package,sandbox/'data/Plugins/SystemTools')
        config=sandbox/'data/Config/Plugins/SystemTools/Main.json'
        data=json.loads(config.read_text(encoding='utf-8'));data['enableFloatingWindowFeature']=True
        config.write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
    with zipfile.ZipFile(package) as archive:metadata=json.loads(archive.read('m2-package.json'))
    for name,digest in metadata['files'].items():
        if not m2.safe_name(name) or m1.sha(sandbox/'data/Plugins/SystemTools'/name)!=digest:raise RuntimeError('Lab package differs: '+name)
    if fixture_alive(root):
        (root/'fixture-stop').touch()
        deadline=time.monotonic()+10
        while fixture_alive(root) and time.monotonic()<deadline:time.sleep(.1)
        if fixture_alive(root):raise RuntimeError('Owned fixture did not stop before rebuild.')
    m1.write_json(root/'lab-package.json',dict(package=str(Path(package).absolute()),sha256=m1.sha(package),sourceHash=metadata.get('sourceHash'),milestone=metadata['milestone']))
    m1.command('dotnet','build','tests/SystemTools.M8.MacHostProbe','-c','Release','--nologo','-v','minimal')
    probe=sandbox/'data/Plugins/SystemTools.M8.MacHostProbe';probe.mkdir(parents=True,exist_ok=True)
    source=ROOT/'tests/SystemTools.M8.MacHostProbe/bin/Release/net10.0'
    for name in ('SystemTools.M8.MacHostProbe.dll','manifest.yml'):shutil.copy2(source/name,probe/name)
    app=root/'SystemToolsM8Fixture.app/Contents';(app/'MacOS').mkdir(parents=True,exist_ok=True)
    (app/'Info.plist').write_bytes(plistlib.dumps(dict(CFBundleIdentifier='dev.systemtools.m8.fixture',CFBundleName='SystemTools M8 输入夹具',CFBundleExecutable='fixture',CFBundlePackageType='APPL',NSPrincipalClass='NSApplication')))
    m1.command('xcrun','swiftc','-swift-version','5','-O','tests/SystemTools.M8.WindowFixture/main.swift','-o',app/'MacOS/fixture')
    return root

def launch(root,host_app,observe_exit=False):
    m7_macos.lifecycle.stopped()
    m7_macos.require_unlocked_desktop()
    root=Path(root).absolute();host=Path(host_app)/'Contents/MacOS/ClassIsland.Desktop'
    environment=dict(os.environ)
    for name in list(environment):
        if name.startswith('SystemTools_M') or name in ('ClassIsland_DebugForceMonoPluginLoadBehavior','ClassIsland_DebugSuppressMacOSPluginLoadBehavior','ClassIsland_WaitDebuggers'):environment.pop(name)
    environment.update(ClassIsland_PackageRoot=str(root/'sandbox'),SystemTools_M8_ProbeRoot=str(root),SystemTools_M8_CleanupReferences=str(ROOT/'artifacts/m8/synthetic-keychain-cleanup.json'))
    global_path=m1.global_storage_path()
    before=m1.sha(global_path) if global_path.exists() else None
    fixture_state=root/'fixture.json'
    if not fixture_alive(root):
        fixture_state.unlink(missing_ok=True)
        (root/'fixture-stop').unlink(missing_ok=True)
        subprocess.run(['/usr/bin/open','-n','-a',str(root/'SystemToolsM8Fixture.app'),'--args',str(root)],check=True)
    deadline=time.monotonic()+10
    while not (root/'fixture.json').exists() and time.monotonic()<deadline:time.sleep(.1)
    if not (root/'fixture.json').exists():raise RuntimeError('Fixture did not initialize')
    fixture_pid=json.loads((root/'fixture.json').read_text(encoding='utf-8'))['pid']

    m7_macos.lifecycle.stopped()
    log=(root/'host.log').open('a');process=subprocess.Popen([str(host),'--skip-oobe','--quiet','--verbose'],cwd=host.parent,env=environment,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
    m1.write_json(root/'processes.json',dict(hostPid=process.pid,fixturePid=fixture_pid,globalBefore=before,hostSha256=m1.sha(host)))
    m1.write_json(ROOT/'artifacts/m8/current-lab.json',dict(root=str(root),hostPid=process.pid,fixturePid=fixture_pid))
    print(json.dumps(dict(root=str(root),hostPid=process.pid,fixturePid=fixture_pid)),flush=True)
    if observe_exit:
        code=process.wait()
        m1.write_json(root/'host-exit.json',dict(exitCode=code,utc=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())))
        print('Host exit code',code,flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='operation',required=True)
    command=sub.add_parser('preview');command.add_argument('--base',required=True);command.add_argument('--output',required=True)
    command=sub.add_parser('launch');command.add_argument('--package',required=True);command.add_argument('--existing');command.add_argument('--host-app',default='/Applications/ClassIsland.Desktop.app');command.add_argument('--observe-exit',action='store_true')
    args=parser.parse_args()
    if args.operation=='preview':print(preview(args.base,args.output))
    else:launch(prepare(args.package,args.existing),args.host_app,args.observe_exit)
