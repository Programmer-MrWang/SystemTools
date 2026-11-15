#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Compare this OS's outputs from two checkout paths; not a cross-OS claim."""
from pathlib import Path
import argparse, json, shutil, subprocess, tempfile
import m1
import m2

def main(working_tree=False):
    dirty = subprocess.check_output(['git', 'status', '--porcelain', '--', 'src', 'global.json',
        'SystemTools.slnx', 'icon.png', 'Plugin.cs', 'DlibExtensions.cs', 'DriveUtils.cs',
        'NativeMethods.txt', 'eng/shared-host-sources.props', 'eng/shared-host-sources.json', 'Actions', 'Config', 'ConfigHandlers', 'Controls', 'Converters',
        'Helpers', 'Models', 'Properties', 'Rules', 'Services', 'Settings', 'SettingsPage',
        'Shared', 'Themes', 'ThirdParty', 'Triggers', 'Version', 'Views', 'VoskWorker', 'native'], cwd=m1.ROOT).decode()
    if dirty and not working_tree:
        raise RuntimeError('Commit build inputs before reproducibility verification:\n' + dirty)
    m1.build()
    with tempfile.TemporaryDirectory(prefix='systemtools-m1-repro-') as temporary:
        clone = Path(temporary) / 'repository'
        subprocess.run(['git', 'clone', '--local', '--no-hardlinks', '--quiet', str(m1.ROOT), str(clone)], check=True)
        remote = subprocess.check_output(['git', 'remote', 'get-url', 'origin'], cwd=m1.ROOT).decode().strip()
        subprocess.run(['git', 'remote', 'set-url', 'origin', remote], cwd=clone, check=True)
        if working_tree:
            names = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=m1.ROOT).decode().split('\0')
            for name in sorted(set(names) - {''}):
                source, destination = m1.ROOT / name, clone / name
                if destination.is_file() or destination.is_symlink(): destination.unlink()
                if source.is_file() or source.is_symlink():
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(source, destination, follow_symlinks=False)
            copied_identity = json.loads(subprocess.check_output(['python3', '-B', '-c',
                'import json,sys; sys.path.insert(0,"eng"); import m1; print(json.dumps(m1.identity()))'], cwd=clone))
            if copied_identity != m1.identity():
                raise RuntimeError('Working-tree snapshot differs from current build inputs.')
        subprocess.run(['python3' if m1.native_rid() != 'win-x64' else 'python', '-B', 'eng/build_npoi.py'], cwd=clone, check=True)
        subprocess.run(['dotnet', 'build', 'SystemTools.slnx', '-c', 'Release', '--nologo', '-v', 'minimal'], cwd=clone, check=True)
        names = m1.COMMON + [m1.module_name(m1.native_rid())]
        if m1.native_rid() == 'osx-arm64':
            names.append('platforms/osx-arm64/libSystemToolsNative.dylib')
            names.extend(m2.MAC_COMPANION_FILES)
        hashes = {}
        for name in names:
            first = m1.ROOT / 'src/SystemTools/bin/Release/net10.0' / name
            second = clone / 'src/SystemTools/bin/Release/net10.0' / name
            if m1.sha(first) != m1.sha(second):
                diagnostic = m1.ARTIFACTS / 'reproducibility-failure'
                diagnostic.mkdir(parents=True, exist_ok=True)
                shutil.copy2(first, diagnostic / ('first-' + first.name))
                shutil.copy2(second, diagnostic / ('second-' + second.name))
                m1.write_json(diagnostic / 'comparison.json', dict(file=name,
                    firstSha256=m1.sha(first), secondSha256=m1.sha(second),
                    firstBytes=first.stat().st_size, secondBytes=second.stat().st_size))
                raise RuntimeError('Non-reproducible output: ' + name)
            hashes[name] = m1.sha(first)
    report = dict(m1.identity(), passed=True, nativeRuntime=m1.native_rid(), workingTreeSnapshot=working_tree,
        differentCheckoutPaths=True, sameOrigin=True, files=hashes, scope='Same OS only. Cross-OS common hash comparison remains enforced by package aggregation.')
    m1.write_json(m1.ARTIFACTS / 'reproducible-build.json', report)
    print('PASS: common and platform output files identical across two checkout paths on ' + m1.native_rid())

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--working-tree', action='store_true', help='Compare an exact current-tree snapshot without staging or committing existing changes.')
    main(parser.parse_args().working_tree)
