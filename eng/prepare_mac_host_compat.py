#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build a reviewable host-only workaround into an isolated .app copy.

Original host sources/binaries and the plugin CIPX are never modified.
This is a test adapter, not a complete host source build or a signed release.
"""
from pathlib import Path
import argparse, difflib, json, platform, plistlib, shutil, subprocess, sys
from xml.sax.saxutils import escape
import m1

def prepare(host_source, host_app, output_directory=None):
    if platform.system() != 'Darwin' or m1.native_rid() != 'osx-arm64':
        raise RuntimeError('Apple Silicon macOS is required.')
    source, original = Path(host_source).resolve(), Path(host_app).resolve()
    host_commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=source).decode().strip()
    if host_commit != '3afce2714b6ec50d4bd2a3baac95a71d96ce3b9b':
        raise RuntimeError('Host commit changed; review the workaround before preparing another test adapter.')
    work = Path(output_directory).resolve() if output_directory else m1.ARTIFACTS / 'host-compat'
    if work.exists():
        raise RuntimeError('Isolated host-compat directory already exists; inspect it before creating another copy.')
    work.mkdir(parents=True)
    snapshot = work / 'source'
    snapshot.mkdir()
    program_path = source / 'ClassIsland.Desktop/Program.cs'
    raw = program_path.read_text(encoding='utf-8-sig')
    target = '        var stopTokenSource = new CancellationTokenSource();'
    if raw.count(target) != 1:
        raise RuntimeError('Host Program.cs changed; review insertion point.')
    patched = raw.replace(target, '#if Platforms_MacOs\n        ClassIsland.Desktop.Helpers.NavigationRepeaterAutomationFix.Initialize();\n#endif\n' + target)
    # The source patch contains only the host fix. This test adapter additionally
    # has a safe default data root in case UI tooling launches it without env.
    fallback = work / 'automatic-sandbox'
    fallback.mkdir()
    (fallback / 'PackageType').write_text('folder', encoding='utf-8')
    m1.write_json(fallback / 'data/Settings.json', {'IsWelcomeWindowShowed': True, 'IsSplashEnabled': False})
    fallback_literal = '@"' + str(fallback).replace('"', '""') + '"'
    test_entry = patched.replace(target, '        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ClassIsland_PackageRoot")))\n            Environment.SetEnvironmentVariable("ClassIsland_PackageRoot", ' + fallback_literal + ');\n' + target)
    (snapshot / 'Program.cs').write_text(test_entry, encoding='utf-8')
    (snapshot / 'MacOSAssembly.cs').write_text('using System.Runtime.Versioning;\n[assembly: SupportedOSPlatform("macos12.0")]\n', encoding='utf-8')
    helper = m1.ROOT / 'eng/host-compat/NavigationRepeaterAutomationFix.cs'
    shutil.copy2(helper, snapshot / helper.name)
    for name in ('DesktopAppLifetimeService.cs', 'LauncherService.cs'):
        shutil.copy2(source / 'ClassIsland.Desktop/Services/Platform' / name, snapshot / name)
    # Recompile just the existing three-source desktop entrance against the
    # app's own exact assemblies. This does not rebuild the entire host tree.
    bundle = original / 'Contents/MonoBundle'
    references = []
    for path in sorted(bundle.glob('*.dll')):
        if path.name.startswith(('System.', 'Microsoft.VisualBasic')) or path.name in ('mscorlib.dll', 'netstandard.dll', 'System.dll', 'Microsoft.CSharp.dll', 'WindowsBase.dll', 'ClassIsland.Desktop.dll'):
            continue
        references.append('<Reference Include="' + escape(path.stem) + '"><HintPath>' + escape(str(path)) + '</HintPath><Private>false</Private></Reference>')
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><UseAppHost>false</UseAppHost><AssemblyName>ClassIsland.Desktop</AssemblyName><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><DefineConstants>TRACE;DEBUG;Platforms_MacOs</DefineConstants><DebugType>portable</DebugType></PropertyGroup><ItemGroup>' + ''.join(references) + '</ItemGroup></Project>'
    (snapshot / 'HostCompat.csproj').write_text(project, encoding='utf-8')
    patch = ''.join(difflib.unified_diff(raw.splitlines(True), patched.splitlines(True), fromfile='a/ClassIsland.Desktop/Program.cs', tofile='b/ClassIsland.Desktop/Program.cs'))
    patch += ''.join(difflib.unified_diff([], helper.read_text(encoding='utf-8').splitlines(True), fromfile='/dev/null', tofile='b/ClassIsland.Desktop/Helpers/' + helper.name))
    patch_path = m1.ROOT / 'eng/host-patches/classisland-macos-navigation-accessibility.patch'
    patch_path.parent.mkdir(parents=True, exist_ok=True)
    patch_path.write_text(patch, encoding='utf-8')
    originals = {str(p.relative_to(original)): m1.sha(p) for p in (bundle / 'ClassIsland.Desktop.dll', bundle / 'FluentAvalonia.dll', original / 'Contents/MacOS/ClassIsland.Desktop')}
    subprocess.run(['dotnet', 'build', str(snapshot / 'HostCompat.csproj'), '-c', 'Debug', '--nologo', '-v', 'minimal'], cwd=m1.ROOT, check=True)
    output = work / 'ClassIsland.Desktop.M1Compat.app'
    shutil.copytree(original, output)
    rebuilt = snapshot / 'bin/Debug/net10.0'
    for name in ('ClassIsland.Desktop.dll', 'ClassIsland.Desktop.pdb'):
        shutil.copy2(rebuilt / name, output / 'Contents/MonoBundle' / name)
    # A distinct test bundle identity prevents UI tooling from reopening the
    # user's installed ClassIsland after an unexpected test-process exit.
    plist_path = output / 'Contents/Info.plist'
    with plist_path.open('rb') as stream:
        plist = plistlib.load(stream)
    plist['CFBundleIdentifier'] = 'cn.classisland.app.m1compat'
    plist['CFBundleName'] = 'ClassIsland M1 Compat'
    plist['CFBundleDisplayName'] = 'ClassIsland M1 Compat'
    with plist_path.open('wb') as stream:
        plistlib.dump(plist, stream)
    subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(output)], check=True)
    for name, digest in originals.items():
        if m1.sha(original / name) != digest:
            raise RuntimeError('Original host changed unexpectedly: ' + name)
    report = {'kind': 'isolated-host-entry-workaround', 'hostSource': str(source), 'hostSourceCommit': host_commit, 'originalApp': str(original), 'originalFilesUnchanged': originals, 'compatApp': str(output), 'testBundleId': 'cn.classisland.app.m1compat', 'fallbackSandbox': str(fallback), 'hostPatch': str(patch_path.relative_to(m1.ROOT)), 'patchedDesktopSha256': m1.sha(output / 'Contents/MonoBundle/ClassIsland.Desktop.dll'), 'scope': 'Same host native launcher/deps and assemblies, only desktop entrance rebuilt with the existing Android navigation-peer workaround enabled for Mac and a fallback sandbox for this test adapter. No plugin modifications.'}
    m1.write_json(work / 'preparation.json', report)
    print('ISOLATED HOST:', output / 'Contents/MacOS/ClassIsland.Desktop')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--host-source', required=True)
    parser.add_argument('--host-app', required=True)
    parser.add_argument('--output', help='New directory for the isolated adapter; existing paths are never replaced.')
    args = parser.parse_args()
    prepare(args.host_source, args.host_app, args.output)
