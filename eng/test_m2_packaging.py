#!/usr/bin/env python3
"""M2 package boundaries use synthetic data and never invoke the desktop or hardware."""
from pathlib import Path
from unittest.mock import patch
import hashlib
import json
import tempfile
import unittest
import zipfile
import m1
import m2

class M2PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='systemtools-m2-package-')
        self.root = Path(self.temporary.name)
        self.stage = self.root / 'stage'
        self.stage.mkdir()
        self.names = m1.COMMON + m2.MODULES + [m2.PRIVATE + '/NPOI.Core.dll',
            m2.PRIVATE + '/VoskWorker/coreclr.dll']
        for name in self.names:
            path = self.stage / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(('synthetic:' + name).encode())
        self.manifest = {'files': {name: m1.sha(self.stage / name) for name in self.names},
                         'hostSharedAssemblies': ['avalonia.base.dll', 'classisland.core.dll']}
        self.write_manifest()

    def tearDown(self):
        self.temporary.cleanup()

    def write_manifest(self):
        (self.stage / 'm2-package.json').write_text(json.dumps(self.manifest), encoding='utf-8')

    def add(self, name):
        path = self.stage / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b'unexpected bytes')
        self.manifest['files'][name] = m1.sha(path)
        self.write_manifest()

    def test_valid_package_is_deterministic_and_extractable(self):
        first = m2.package(self.stage, self.root / 'first.cipx')
        second = m2.package(self.stage, self.root / 'second.cipx')
        self.assertEqual(m1.sha(first), m1.sha(second))
        m2.extract(first, self.root / 'extracted')
        self.assertEqual(m2.validate(self.root / 'extracted'), self.manifest)

    def test_modified_stage_is_rejected(self):
        (self.stage / 'SystemTools.dll').write_bytes(b'tampered')
        with self.assertRaisesRegex(RuntimeError, 'hash mismatch'):
            m2.package(self.stage, self.root / 'bad.cipx')

    def test_host_copy_is_rejected_in_private_directory(self):
        self.add(m2.PRIVATE + '/ClassIsland.Core.dll')
        with self.assertRaisesRegex(RuntimeError, 'host/shared'):
            m2.validate(self.stage)

    def test_foreign_native_directory_is_rejected(self):
        self.add('platforms/linux-x64/libexample.so')
        with self.assertRaisesRegex(RuntimeError, 'platform asset'):
            m2.validate(self.stage)

    def test_unexpected_root_dll_is_rejected(self):
        self.add('NPOI.Core.dll')
        with self.assertRaisesRegex(RuntimeError, 'root file'):
            m2.validate(self.stage)

    def test_case_duplicate_archive_is_rejected(self):
        valid = m2.package(self.stage, self.root / 'valid.cipx')
        bad = self.root / 'bad.cipx'
        with zipfile.ZipFile(valid) as source, zipfile.ZipFile(bad, 'w') as target:
            for name in source.namelist():
                target.writestr(name, source.read(name))
            target.writestr('systemtools.dll', b'duplicate')
        with self.assertRaisesRegex(RuntimeError, 'contents'):
            m2.extract(bad, self.root / 'out')

    def test_archive_traversal_is_rejected_before_writing(self):
        valid = m2.package(self.stage, self.root / 'valid.cipx')
        bad = self.root / 'bad.cipx'
        with zipfile.ZipFile(valid) as source, zipfile.ZipFile(bad, 'w') as target:
            for name in source.namelist():
                target.writestr(name, source.read(name))
            target.writestr('../escaped', b'invalid')
        with self.assertRaises(RuntimeError):
            m2.extract(bad, self.root / 'out')
        self.assertFalse((self.root / 'escaped').exists())

    def test_linked_windows_source_affects_identity(self):
        repo = self.root / 'repository'
        (repo / 'src').mkdir(parents=True)
        (repo / 'global.json').write_text('sdk')
        (repo / 'SystemTools.slnx').write_text('solution')
        (repo / 'Actions').mkdir()
        path = repo / 'Actions/CopyAction.cs'
        path.write_text('first')
        with patch.object(m1, 'ROOT', repo), patch.object(m1.subprocess, 'check_output', return_value=b'commit'):
            before = m1.identity()['sourceHash']
            path.write_text('second')
            self.assertNotEqual(before, m1.identity()['sourceHash'])

if __name__ == '__main__':
    unittest.main()
