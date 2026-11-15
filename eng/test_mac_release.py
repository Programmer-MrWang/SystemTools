#!/usr/bin/env python3
from pathlib import Path
from unittest.mock import patch
import plistlib
import tempfile
import unittest
import mac_release as release
import m1
import m2


class MacReleaseTests(unittest.TestCase):
    def test_development_and_ad_hoc_cannot_pass_formal_identity(self):
        for identity in (None, '-', 'Apple Development: Example'):
            with self.assertRaises(RuntimeError): release.identity_options(identity, '0123456789')

    def test_signs_only_own_assets_and_companion_has_capture_entitlements(self):
        commands = release.sign_commands(Path('/payload'), 'Developer ID Application: Example (0123456789)', '0123456789')
        self.assertEqual(len(commands), 3)
        self.assertTrue(all('ClassIsland' not in str(row) for row in commands))
        self.assertTrue(all('--timestamp' in row and 'runtime' in row for row in commands))
        self.assertIn('--entitlements', commands[-1]); self.assertNotIn('--deep', commands[-1])
        entitlements = plistlib.loads(release.ENTITLEMENTS.read_bytes())
        self.assertEqual(set(entitlements), {'com.apple.security.device.camera', 'com.apple.security.device.audio-input', 'com.apple.security.automation.apple-events'})

    def test_signature_team_runtime_and_notarization_are_independent(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); info = root / m2.MAC_COMPANION / 'Contents/Info.plist'; info.parent.mkdir(parents=True)
            info.write_bytes(plistlib.dumps(dict(CFBundleIdentifier=release.POLICY['macSigning']['bundleId'])))
            with patch.object(release, 'command', return_value='Signature=adhoc'):
                with self.assertRaises(RuntimeError): release.verify_tree(root, '0123456789')
                self.assertEqual(len(release.verify_tree(root, candidate=True)), 3)
                with self.assertRaises(RuntimeError): release.verify_tree(root, candidate=True, notarized=True)
            with patch.object(release, 'command', return_value='Authority=Developer ID Application: Example\nTeamIdentifier=0123456789\nflags=runtime') as command:
                self.assertEqual(len(release.verify_tree(root, '0123456789', notarized=True)), 3)
                self.assertTrue(any('stapler' in call.args[0] for call in command.call_args_list))
                self.assertTrue(any('spctl' in call.args[0] for call in command.call_args_list))

    def test_sign_refuses_overwriting_input(self):
        with patch.object(release.platform, 'system', return_value='Darwin'):
            with self.assertRaises(RuntimeError):
                release.process('sign', __file__, __file__, 'Developer ID Application: Example', '0123456789')


if __name__ == '__main__': unittest.main()
