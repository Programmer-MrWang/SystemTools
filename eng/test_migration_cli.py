#!/usr/bin/env python3
from pathlib import Path
import json
import subprocess
import tempfile
import unittest
import m1


class MigrationCliTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        subprocess.run(['dotnet', 'build', 'eng/SystemTools.Migrate', '-c', 'Release', '--nologo', '-v', 'quiet'], cwd=m1.ROOT, check=True)
        cls.dll = m1.ROOT / 'eng/SystemTools.Migrate/bin/Release/net10.0/SystemTools.Migrate.dll'

    def setUp(self):
        base = m1.ROOT / 'artifacts/m7/migration-tests'; base.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=base); self.root = Path(self.temp.name)
        self.source = self.root / 'source.json'; self.source.write_text('{"pluginId":"SystemTools.CrossPlatform","unknown":17,"key":"opaque"}')
        self.target = self.root / 'target.json'; self.target.write_text('{"original":true}')
        self.plan = self.root / 'plan.json'; self.reviewed = self.root / 'plan.json.reviewed.json'
        self.plan.write_text(json.dumps(dict(TargetRoot=str(self.root), Files=[dict(Source=str(self.source), Target=str(self.target), Choice='ImportLegacy')])))

    def tearDown(self): self.temp.cleanup()
    def run_cli(self, operation, file):
        return subprocess.run(['dotnet', str(self.dll), operation, str(file)], cwd=m1.ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8', errors='replace')

    def test_preview_apply_and_rollback(self):
        self.assertEqual(self.run_cli('preview', self.plan).returncode, 0)
        self.assertEqual(self.target.read_text(), '{"original":true}')
        self.assertEqual(self.run_cli('apply', self.reviewed).returncode, 0)
        migrated = json.loads(self.target.read_text()); self.assertEqual(migrated['unknown'], 17); self.assertEqual(migrated['key'], 'opaque')
        journal = next((self.root / '.migrations').glob('*/journal.json'))
        self.assertEqual(self.run_cli('rollback', journal).returncode, 0)
        self.assertEqual(self.target.read_text(), '{"original":true}')

    def test_changed_source_and_target_are_rejected(self):
        self.assertEqual(self.run_cli('preview', self.plan).returncode, 0)
        self.source.write_text('{"changed":true}')
        self.assertNotEqual(self.run_cli('apply', self.reviewed).returncode, 0)
        self.assertEqual(self.target.read_text(), '{"original":true}')

    def test_unreviewed_plan_and_replacing_preview_are_rejected(self):
        self.assertNotEqual(self.run_cli('apply', self.plan).returncode, 0)
        self.assertEqual(self.run_cli('preview', self.plan).returncode, 0)
        self.assertNotEqual(self.run_cli('preview', self.plan).returncode, 0)


if __name__ == '__main__': unittest.main()
