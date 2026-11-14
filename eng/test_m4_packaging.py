"""Exercise M4 provenance, integrity and mixed-platform package boundaries."""
from pathlib import Path
import json
import tempfile
import unittest
import m1
import m2
import m4


class M4PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.previous = m4.ARTIFACTS
        m4.ARTIFACTS = self.root / 'artifacts'
        self.stages = []
        for rid in m1.MODULES:
            stage = self.root / rid
            stage.mkdir()
            names = m1.COMMON + [m1.module_name(rid)] + ([m2.MAC_NATIVE] if rid == 'osx-arm64' else [])
            if rid == 'win-x64': names += m4.WINDOWS_REQUIRED
            for name in names:
                path = stage / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(name)
            self.stages.append(stage)
            self.manifest(stage, rid)

    def tearDown(self):
        m4.ARTIFACTS = self.previous
        self.temporary.cleanup()

    def manifest(self, stage, rid, **extra):
        data = dict(schemaVersion=1, milestone='M4', runtimeId=rid, kind='native', buildRuntime=rid,
                    commit='a' * 40, sourceHash='b' * 64, sdk='10.0.403', hostSharedAssemblies=[],
                    files={path.relative_to(stage).as_posix(): m1.sha(path) for path in stage.rglob('*')
                           if path.is_file() and path.name != 'm4-stage.json'})
        data.update(extra)
        m1.write_json(stage / 'm4-stage.json', data)

    def aggregate(self, stages=None, preview=False, name='result.cipx'):
        return m4.aggregate(stages or self.stages, self.root / name, preview)

    def test_native_aggregate_and_input_order_are_deterministic(self):
        first = m1.sha(self.aggregate())
        second = m1.sha(self.aggregate(list(reversed(self.stages)), name='second.cipx'))
        self.assertEqual(first, second)

    def test_cross_build_requires_explicit_preview(self):
        self.manifest(self.stages[0], 'win-x64', kind='cross-compiled', buildRuntime='osx-arm64')
        with self.assertRaisesRegex(RuntimeError, 'Native aggregate'):
            self.aggregate()
        self.assertTrue(self.aggregate(preview=True).is_file())
        metadata = json.loads((m4.ARTIFACTS / 'aggregate-preview/m2-package.json').read_text())
        self.assertEqual(metadata['kind'], 'cross-compiled-development-preview')

    def test_exactly_three_distinct_targets(self):
        for stages in (self.stages[:2], [self.stages[0]] * 3):
            with self.assertRaisesRegex(RuntimeError, 'Exactly one'):
                self.aggregate(stages)

    def test_source_commit_and_sdk_conflicts(self):
        for key in ('commit', 'sourceHash', 'sdk'):
            with self.subTest(key=key):
                self.manifest(self.stages[0], 'win-x64', **{key: 'different'})
                with self.assertRaisesRegex(RuntimeError, 'different commits'):
                    self.aggregate()
                self.manifest(self.stages[0], 'win-x64')

    def test_modified_and_unlisted_files_rejected(self):
        target = self.stages[0] / m1.module_name('win-x64')
        target.write_text('tampered')
        with self.assertRaisesRegex(RuntimeError, 'modified stage'):
            self.aggregate()
        self.manifest(self.stages[0], 'win-x64')
        (self.stages[0] / 'unlisted').write_text('extra')
        with self.assertRaisesRegex(RuntimeError, 'contents differ'):
            self.aggregate()

    def test_missing_native_bridge_rejected(self):
        (self.stages[2] / m2.MAC_NATIVE).unlink()
        self.manifest(self.stages[2], 'osx-arm64')
        with self.assertRaisesRegex(RuntimeError, 'Missing required'):
            self.aggregate()

    def test_missing_windows_worker_rejected(self):
        (self.stages[0] / m4.WINDOWS_REQUIRED[-1]).unlink()
        self.manifest(self.stages[0], 'win-x64')
        with self.assertRaisesRegex(RuntimeError, 'Missing required'):
            self.aggregate()

    def test_conflicting_common_outputs_rejected(self):
        (self.stages[0] / m1.COMMON[0]).write_text('different assembly')
        self.manifest(self.stages[0], 'win-x64')
        with self.assertRaisesRegex(RuntimeError, 'Conflicting shared'):
            self.aggregate()

    def test_different_host_contract_rejected(self):
        self.manifest(self.stages[0], 'win-x64', hostSharedAssemblies=['host.dll'])
        with self.assertRaisesRegex(RuntimeError, 'contracts differ'):
            self.aggregate()

    def test_invalid_provenance_rejected(self):
        self.manifest(self.stages[0], 'win-x64', buildRuntime='osx-arm64')
        with self.assertRaisesRegex(RuntimeError, 'Invalid M4'):
            self.aggregate()

    def test_host_assembly_cannot_be_packaged(self):
        path = self.stages[0] / 'platforms/shared/ClassIsland.Core.dll'
        path.parent.mkdir(parents=True)
        path.write_text('duplicate host')
        self.manifest(self.stages[0], 'win-x64')
        with self.assertRaisesRegex(RuntimeError, 'Duplicate host'):
            self.aggregate()

    def test_unsafe_manifest_path_rejected(self):
        path = self.stages[0] / 'm4-stage.json'
        data = json.loads(path.read_text())
        data['files']['../outside.dll'] = '0' * 64
        m1.write_json(path, data)
        with self.assertRaises(RuntimeError):
            self.aggregate()


if __name__ == '__main__':
    unittest.main()
