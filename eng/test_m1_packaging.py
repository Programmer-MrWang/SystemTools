#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Boundary tests for package provenance, collisions and archive extraction."""
from pathlib import Path
import hashlib, json, tempfile, unittest, zipfile
from unittest.mock import patch
import m1

class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='systemtools-m1-package-')
        self.root = Path(self.temporary.name)
        self.stages = []
        for rid in m1.MODULES:
            stage = self.root / rid
            stage.mkdir()
            files = m1.COMMON + [m1.module_name(rid)]
            for name in files:
                (stage / name).write_bytes(('synthetic:' + name).encode())
            m1.write_json(stage / 'm1-build.json', {'runtimeId': rid, 'commit': 'same-commit', 'sourceHash': 'same-source', 'kind': 'native', 'files': {n: m1.sha(stage / n) for n in files}})
            self.stages.append(stage)

    def tearDown(self):
        self.temporary.cleanup()

    def update(self, index, **changes):
        path = self.stages[index] / 'm1-build.json'
        data = json.loads(path.read_text(encoding='utf-8'))
        data.update(changes)
        m1.write_json(path, data)

    def test_valid_aggregate_is_order_independent(self):
        first = m1.pack(self.stages, self.root / 'a.cipx')
        second = m1.pack(list(reversed(self.stages)), self.root / 'b.cipx')
        self.assertEqual(m1.sha(first), m1.sha(second))
        m1.extract(first, self.root / 'extracted')
        self.assertEqual(len(list((self.root / 'extracted').glob('SystemTools.Platforms.*.dll'))), 3)

    def test_shared_collision_rejected(self):
        stage = self.stages[1]
        (stage / 'SystemTools.Contracts.dll').write_bytes(b'incompatible contract')
        data = json.loads((stage / 'm1-build.json').read_text(encoding='utf-8'))
        data['files']['SystemTools.Contracts.dll'] = m1.sha(stage / 'SystemTools.Contracts.dll')
        m1.write_json(stage / 'm1-build.json', data)
        with self.assertRaisesRegex(RuntimeError, 'conflict'):
            m1.pack(self.stages, self.root / 'bad.cipx')

    def test_mixed_source_rejected(self):
        self.update(1, sourceHash='different')
        with self.assertRaisesRegex(RuntimeError, 'different source'):
            m1.pack(self.stages, self.root / 'bad.cipx')

    def test_missing_target_rejected(self):
        with self.assertRaisesRegex(RuntimeError, 'exactly one'):
            m1.pack([self.stages[0]] * 3, self.root / 'bad.cipx')

    def test_modified_stage_rejected(self):
        (self.stages[0] / m1.module_name('win-x64')).write_bytes(b'modified after manifest')
        with self.assertRaisesRegex(RuntimeError, 'modified file'):
            m1.pack(self.stages, self.root / 'bad.cipx')

    def test_prototype_cannot_be_native_aggregate(self):
        self.update(0, kind='local-managed-prototype')
        with self.assertRaisesRegex(RuntimeError, 'prototype'):
            m1.pack(self.stages, self.root / 'bad.cipx')

    def test_source_identity_preserves_posix_order_on_every_os(self):
        files = {
            'SystemTools.slnx': b'solution', 'global.json': b'sdk',
            'src/Zeta/value.cs': b'upper', 'src/alpha/value.cs': b'lower',
            'src/alpha.extra/value.cs': b'dotted'
        }
        for name, content in files.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        expected = hashlib.sha256(
            b'SystemTools.slnxsolutionglobal.jsonsdk'
            b'src/Zeta/value.csuppersrc/alpha/value.cslower'
            b'src/alpha.extra/value.csdotted').hexdigest()
        with patch.object(m1, 'ROOT', self.root), patch.object(m1.subprocess, 'check_output', return_value=b'same-commit\n'):
            self.assertEqual(m1.identity(), {'commit': 'same-commit', 'sourceHash': expected})

    def altered_archive(self, replacement=None, extra=None):
        package = m1.pack(self.stages, self.root / 'ok.cipx')
        result = self.root / 'modified.cipx'
        with zipfile.ZipFile(package) as source, zipfile.ZipFile(result, 'w') as target:
            for name in source.namelist():
                target.writestr(name, replacement[1] if replacement and name == replacement[0] else source.read(name))
            if extra:
                target.writestr(extra, b'forbidden')
        return result

    def test_corrupt_package_rejected(self):
        archive = self.altered_archive(replacement=('SystemTools.dll', b'tampered'))
        with self.assertRaisesRegex(RuntimeError, 'hash mismatch'):
            m1.extract(archive, self.root / 'out')

    def test_duplicate_host_assembly_rejected(self):
        archive = self.altered_archive(extra='ClassIsland.Core.dll')
        with self.assertRaisesRegex(RuntimeError, 'contents'):
            m1.extract(archive, self.root / 'out')

    def test_zip_path_traversal_rejected(self):
        archive = self.altered_archive(extra='../escaped')
        with self.assertRaisesRegex(RuntimeError, 'contents'):
            m1.extract(archive, self.root / 'out')
        self.assertFalse((self.root / 'escaped').exists())

if __name__ == '__main__':
    unittest.main()
