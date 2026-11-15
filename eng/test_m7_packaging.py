#!/usr/bin/env python3
from pathlib import Path
from unittest.mock import patch
import json
import struct
import tempfile
import unittest
import m1
import m2
import m7
import release_inventory as inventory


class M7PackagingTests(unittest.TestCase):
    def test_generated_matrix_covers_exactly_the_runtime_ledger(self):
        actual = inventory.capabilities(); expected = json.loads((m1.ROOT / 'src/SystemTools.Contracts/Features.json').read_text())
        self.assertEqual([x['Id'] for x in actual['features']], [x['Id'] for x in expected])
        self.assertEqual(len({x['Id'] for x in actual['features']}), len(expected))
        self.assertIn('SystemTools.CameraCapture', inventory.support_markdown(actual))
        self.assertTrue(all(not x['runtimeVerified'] for x in actual['features']))

    def test_unsupported_features_do_not_become_available_in_docs(self):
        rows = {x['Id']:x for x in inventory.capabilities()['features']}
        self.assertIn('不支持', rows['SystemTools.AutoSwitchClassIslandTheme']['platforms']['linux-x64'])
        self.assertIn('不提供', rows['SystemTools.InternalDisplay']['platforms']['osx-arm64'])
        self.assertIn('未提供', rows['SystemTools.LyricsDisplay']['platforms']['osx-arm64'])
        self.assertIn('产品移除', rows['SystemTools.FullscreenClock']['platforms']['win-x64'])
        self.assertIn('原未注册', rows['SystemTools.ClickSimulation']['platforms']['win-x64'])

    def test_workflow_keeps_supported_architectures_and_locked_sdk(self):
        workflow = (m1.ROOT / '.github/workflows/m1-cross-platform.yml').read_text()
        for rid in m7.POLICY['runtimeIds']: self.assertIn(rid, workflow)
        self.assertNotIn('arm74', workflow)
        self.assertIn(m7.POLICY['sdk'], workflow)
        self.assertIn('python eng/m7.py aggregate', workflow)

    def test_metadata_allowlist_never_allows_extra_executable(self):
        self.assertTrue(m2.release_asset('release/capabilities.json'))
        self.assertTrue(m2.release_asset('release/licenses/osx-arm64/NPOI/LICENSE.txt'))
        for name in ('release/extra.dll','release/../extra.json','release/licenses/extra.dylib','release/nested/extra.json'):
            self.assertFalse(m2.release_asset(name))

    def test_architecture_headers_reject_wrong_platform(self):
        elf = b'\x7fELF\x02' + b'\0'*13 + struct.pack('<H',62)
        arm = b'\xcf\xfa\xed\xfe' + struct.pack('<I',0x100000c)
        pe = bytearray(128); pe[:2]=b'MZ'; struct.pack_into('<I',pe,60,80); pe[80:84]=b'PE\0\0'; struct.pack_into('<H',pe,84,0x8664)
        self.assertTrue(m7.native_architecture(elf,'linux-x64')); self.assertFalse(m7.native_architecture(elf,'osx-arm64'))
        self.assertTrue(m7.native_architecture(arm,'osx-arm64')); self.assertFalse(m7.native_architecture(arm,'win-x64'))
        self.assertTrue(m7.native_architecture(pe,'win-x64')); self.assertFalse(m7.native_architecture(pe,'linux-x64'))
        self.assertFalse(m7.native_architecture(b'bad','win-x64'))

    def test_formal_gate_requires_bound_platform_and_real_signing(self):
        with tempfile.TemporaryDirectory() as temporary:
            report = dict(packageSha256='actual-package',passed=True)
            proof = Path(temporary)/'proof.json'; proof.write_text(json.dumps(dict(passed=True, packageSha256='another-package', fullPlatformAcceptance=True, runtimeIds=m7.POLICY['runtimeIds'])))
            with patch.object(m7,'audit',return_value=report), patch.object(m7.sys,'platform','linux'):
                actual=m7.gate('unused',proof,Path(temporary)/'gate.json')
                self.assertFalse(actual['stableAllowed']);self.assertFalse(actual['localLifecycleValidated'])
                self.assertIn('platformLifecycleEvidence',actual['blockedBy']);self.assertIn('developerIdSignature',actual['blockedBy'])
                self.assertIn('explicitPublicationApproval',actual['blockedBy'])


if __name__=='__main__': unittest.main()
