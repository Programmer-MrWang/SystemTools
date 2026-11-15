"""Verify that failed desktop evidence cannot leave the M5 command successful."""
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import m5


class DesktopExitTests(unittest.TestCase):
    def invoke(self, outcomes):
        with tempfile.TemporaryDirectory() as temporary:
            package = Path(temporary) / 'fixture.cipx'
            with zipfile.ZipFile(package, 'w') as archive:
                archive.writestr('m2-package.json', json.dumps({'milestone': 'M5'}))
            with (patch.dict(os.environ), patch.object(m5.m1, 'native_rid', return_value='win-x64'),
                  patch.object(m5.m2, 'verify_files'), patch.object(m5.m1, 'command'),
                  patch.object(m5.m1, 'identity', return_value={}), patch.object(m5.m1, 'sha', return_value='fixture'),
                  patch.object(m5.m1, 'write_json') as report,
                  patch.object(m5.m2, 'smoke', side_effect=[{'passed': value} for value in outcomes]) as smoke):
                error = None
                try:
                    m5.smoke_windows('isolated-host', package, {}, 'fixture.wav', 'fixture-camera')
                except RuntimeError as failure:
                    error = failure
                return error, report.call_args.args[1], smoke.call_count

    def test_failure_is_reported_and_raises(self):
        error, report, count = self.invoke([True, True, False])
        self.assertIsNotNone(error)
        self.assertFalse(report['passed'])
        self.assertEqual(count, 3)

    def test_all_scenarios_required_for_success(self):
        error, report, count = self.invoke([True, True, True])
        self.assertIsNone(error)
        self.assertTrue(report['passed'])
        self.assertEqual(count, 3)


if __name__ == '__main__':
    unittest.main()
