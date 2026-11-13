#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Compare this OS's outputs from two checkout paths; not a cross-OS claim."""
from pathlib import Path
import json, subprocess, tempfile
import m1

def main():
    dirty = subprocess.check_output(['git', 'status', '--porcelain', '--', 'src', 'global.json',
        'SystemTools.slnx', 'icon.png', 'Plugin.cs', 'DlibExtensions.cs', 'DriveUtils.cs',
        'NativeMethods.txt', 'eng/shared-host-sources.props', 'eng/shared-host-sources.json', 'Actions', 'Config', 'ConfigHandlers', 'Controls', 'Converters',
        'Helpers', 'Models', 'Properties', 'Rules', 'Services', 'Settings', 'SettingsPage',
        'Shared', 'Themes', 'ThirdParty', 'Triggers', 'Version', 'Views', 'VoskWorker'], cwd=m1.ROOT).decode()
    if dirty:
        raise RuntimeError('Commit build inputs before reproducibility verification:\n' + dirty)
    m1.build()
    with tempfile.TemporaryDirectory(prefix='systemtools-m1-repro-') as temporary:
        clone = Path(temporary) / 'repository'
        subprocess.run(['git', 'clone', '--local', '--no-hardlinks', '--quiet', str(m1.ROOT), str(clone)], check=True)
        remote = subprocess.check_output(['git', 'remote', 'get-url', 'origin'], cwd=m1.ROOT).decode().strip()
        subprocess.run(['git', 'remote', 'set-url', 'origin', remote], cwd=clone, check=True)
        subprocess.run(['dotnet', 'build', 'SystemTools.slnx', '-c', 'Release', '--nologo', '-v', 'minimal'], cwd=clone, check=True)
        names = m1.COMMON + [m1.module_name(m1.native_rid())]
        hashes = {}
        for name in names:
            first = m1.ROOT / 'src/SystemTools/bin/Release/net10.0' / name
            second = clone / 'src/SystemTools/bin/Release/net10.0' / name
            if m1.sha(first) != m1.sha(second):
                raise RuntimeError('Non-reproducible output: ' + name)
            hashes[name] = m1.sha(first)
    report = dict(m1.identity(), passed=True, nativeRuntime=m1.native_rid(), differentCheckoutPaths=True, sameOrigin=True, files=hashes, scope='Same OS only. Cross-OS common hash comparison remains enforced by package aggregation.')
    m1.write_json(m1.ARTIFACTS / 'reproducible-build.json', report)
    print('PASS: common and platform output files identical across two checkout paths on ' + m1.native_rid())

if __name__ == '__main__':
    main()
