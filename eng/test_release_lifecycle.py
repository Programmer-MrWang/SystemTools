#!/usr/bin/env python3
"""Exercise offline rollback, durable crash gaps and preservation using synthetic packages."""
from pathlib import Path
from unittest.mock import patch
import json
import os
import tempfile
import subprocess
import sys
import time
import unittest
import m1
import m2
import release_lifecycle as lifecycle


class LifecycleTests(unittest.TestCase):
    def setUp(self):
        base = m1.ROOT / 'artifacts/m7/lifecycle-tests'; base.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=base)
        self.folder = Path(self.temp.name); self.root = self.folder / 'host'; self.root.mkdir()
        (self.root / 'PackageType').write_text('folder')
        self.config = self.root / 'data/Config/Plugins/SystemTools'; self.config.mkdir(parents=True)
        (self.config / 'Main.json').write_bytes(b'{"unknown":17,"key":"dpapi:opaque"}')
        (self.config / 'attachment.bin').write_bytes(bytes([0, 255, 1]))
        old = self.root / lifecycle.OWNED[0]; old.mkdir(parents=True); (old / 'old.dll').write_bytes(b'old-version')
        self.before = {name: lifecycle.tree(self.root / name) for name in lifecycle.PATHS}
        stage = self.folder / 'stage'; stage.mkdir()
        names = m1.COMMON + m2.MODULES + m2.MAC_COMPANION_FILES
        for name in names:
            file = stage / name; file.parent.mkdir(parents=True, exist_ok=True); file.write_bytes(('fixture:' + name).encode())
        (stage / 'manifest.yml').write_text('id: SystemTools\nversion: 3.0.0.0\n')
        m1.write_json(stage / 'm2-package.json', dict(commit='synthetic-test', files={name: m1.sha(stage / name) for name in names}, hostSharedAssemblies=[]))
        self.package = m2.package(stage, self.folder / 'candidate.cipx')
        self.stop = patch.object(lifecycle, 'stopped'); self.stop.start(); self.addCleanup(self.stop.stop)

    def tearDown(self): self.temp.cleanup()
    def install(self, **kwargs): return lifecycle.install(self.root, self.package, **kwargs)
    def journal(self): return next((self.root / 'data/SystemToolsRecovery').glob('*/journal.json'))
    def restored(self): self.assertEqual({name: lifecycle.tree(self.root / name) for name in lifecycle.PATHS}, self.before)

    def test_plan_is_read_only_and_install_rollback_is_idempotent(self):
        preview = lifecycle.plan(self.root, self.package); self.assertEqual(preview['before'], self.before)
        self.assertFalse((self.root / 'data/SystemToolsRecovery').exists())
        journal = self.install(); self.assertTrue((self.root / lifecycle.OWNED[0] / 'SystemTools.dll').exists())
        lifecycle.rollback(self.root, journal); lifecycle.rollback(self.root, journal); self.restored()

    def test_new_install_rolls_back_to_absence(self):
        import shutil
        shutil.rmtree(self.root / lifecycle.OWNED[0]); self.before[lifecycle.OWNED[0]] = None
        journal = self.install(); lifecycle.rollback(self.root, journal); self.restored()

    def test_upgrade_preserves_unknown_fields_ciphertext_and_attachments(self):
        journal = self.install()
        self.assertEqual(lifecycle.tree(self.root / 'data/Config'), self.before['data/Config'])
        self.assertEqual(lifecycle.tree(journal.parent / 'before/data/Config'), self.before['data/Config'])

    def test_legacy_requires_explicit_disable_and_rollback_restores_it(self):
        legacy = self.root / lifecycle.OWNED[1]; legacy.mkdir(); (legacy / 'old.dll').write_bytes(b'legacy')
        self.before[lifecycle.OWNED[1]] = lifecycle.tree(legacy)
        with self.assertRaisesRegex(RuntimeError, 'disable-legacy'): self.install()
        journal = self.install(disable_legacy=True); self.assertTrue((legacy / '.disabled').exists())
        lifecycle.rollback(self.root, journal); self.restored()

    def test_pending_host_package_is_rejected(self):
        cache = self.root / 'data/Cache/PluginPackages'; cache.mkdir(parents=True); (cache / 'pending.cipx').write_bytes(b'pending')
        with self.assertRaisesRegex(RuntimeError, '待安装包'): self.install()
        self.restored()

    def test_changes_after_install_require_explicit_seal(self):
        journal = self.install(); (self.config / 'Main.json').write_text('{"new":true}')
        with self.assertRaisesRegex(RuntimeError, '拒绝覆盖'): lifecycle.rollback(self.root, journal)
        lifecycle.seal(self.root, journal); lifecycle.rollback(self.root, journal); self.restored()

    def test_new_data_after_seal_is_never_overwritten(self):
        journal = self.install(); lifecycle.seal(self.root, journal); (self.config / 'extra.json').write_text('new')
        with self.assertRaisesRegex(RuntimeError, '拒绝覆盖'): lifecycle.rollback(self.root, journal)
        self.assertEqual((self.config / 'extra.json').read_text(encoding='utf-8'), 'new')

    def test_corrupt_backup_prevents_all_rollback_writes(self):
        journal = self.install(); current = lifecycle.tree(self.root / lifecycle.OWNED[0])
        (journal.parent / 'before/data/Config/Plugins/SystemTools/Main.json').write_bytes(b'corrupt')
        with self.assertRaisesRegex(RuntimeError, '备份损坏'): lifecycle.rollback(self.root, journal)
        self.assertEqual(lifecycle.tree(self.root / lifecycle.OWNED[0]), current)

    def test_crash_between_install_directory_moves_is_recoverable(self):
        replace = os.replace
        def crash(source, target):
            if Path(source).name == 'candidate': raise SystemExit('simulated process loss')
            return replace(source, target)
        with patch.object(lifecycle.os, 'replace', side_effect=crash):
            with self.assertRaises(SystemExit): self.install()
        lifecycle.rollback(self.root, self.journal()); self.restored()

    def test_crash_after_install_before_journal_is_recoverable(self):
        write = lifecycle.write
        def crash(path, data):
            if data['status'] == 'Installed': raise SystemExit('simulated process loss')
            return write(path, data)
        with patch.object(lifecycle, 'write', side_effect=crash):
            with self.assertRaises(SystemExit): self.install()
        lifecycle.rollback(self.root, self.journal()); self.restored()

    def test_crash_between_rollback_directory_moves_is_recoverable(self):
        journal = self.install(); replace = os.replace
        def crash(source, target):
            if 'restore' in Path(source).parts: raise SystemExit('simulated process loss')
            return replace(source, target)
        with patch.object(lifecycle.os, 'replace', side_effect=crash):
            with self.assertRaises(SystemExit): lifecycle.rollback(self.root, journal)
        lifecycle.rollback(self.root, journal); self.restored()

    def test_killed_installer_process_recovers_from_durable_journal(self):
        marker = self.folder / 'installer-paused'
        script = """import sys,time,os
from pathlib import Path
import release_lifecycle as module
original=os.replace
def replace(source,target):
 if Path(source).name=='candidate':
  Path(sys.argv[3]).write_text('ready')
  while True: time.sleep(.1)
 return original(source,target)
module.os.replace=replace
module.install(sys.argv[1],sys.argv[2])
"""
        child = subprocess.Popen([sys.executable, '-c', script, str(self.root), str(self.package), str(marker)], cwd=m1.ROOT / 'eng', stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 15
            while not marker.exists() and child.poll() is None and time.monotonic() < deadline: time.sleep(.05)
            self.assertTrue(marker.exists(), 'child installer never reached durable crash gap')
        finally:
            if child.poll() is None: child.kill()
            child.communicate(timeout=5)
        lifecycle.rollback(self.root, self.journal()); self.restored()

    def test_disable_enable_uninstall_only_changes_host_markers(self):
        self.install(); config = lifecycle.tree(self.config)
        lifecycle.mark(self.root, 'disable'); self.assertTrue((self.root / lifecycle.OWNED[0] / '.disabled').exists())
        lifecycle.mark(self.root, 'enable'); self.assertFalse((self.root / lifecycle.OWNED[0] / '.disabled').exists())
        lifecycle.mark(self.root, 'uninstall'); self.assertTrue((self.root / lifecycle.OWNED[0] / '.uninstall').exists())
        self.assertEqual(lifecycle.tree(self.config), config)

    @unittest.skipIf(os.name == 'nt', 'Creating symlinks requires machine-specific Windows privileges')
    def test_linked_data_path_is_rejected(self):
        outside = self.folder / 'outside'; outside.mkdir(); (self.config / 'link').symlink_to(outside, target_is_directory=True)
        with self.assertRaisesRegex(RuntimeError, '链接路径'): self.install()
        self.assertEqual(list(outside.iterdir()), [])

    @unittest.skipIf(os.name == 'nt', 'POSIX execute permissions')
    def test_backup_and_restore_preserve_executable_permission(self):
        executable = self.root / lifecycle.OWNED[0] / 'helper'; executable.write_bytes(b'helper'); executable.chmod(0o755)
        journal = self.install(); lifecycle.rollback(self.root, journal)
        self.assertTrue(executable.stat().st_mode & 0o100)

    def test_running_host_is_rejected(self):
        self.stop.stop()
        with patch.object(lifecycle.subprocess, 'check_output', return_value='ClassIsland.Desktop\n'):
            with self.assertRaisesRegex(RuntimeError, '退出所有'): lifecycle.stopped()


if __name__ == '__main__': unittest.main()
