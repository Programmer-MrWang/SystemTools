#!/usr/bin/env python3
from pathlib import Path
from unittest.mock import patch
import json
import subprocess
import tempfile
import unittest
import audit_dependencies as audit
import m1


class DependencyAuditTests(unittest.TestCase):
    def test_nested_query_problems_are_never_treated_as_success(self):
        for query in [dict(projects=[dict(path='project', problems=['feed unavailable'])]),
                      dict(projects=[], errors=['query unavailable'])]:
            completed = subprocess.CompletedProcess([], 0, json.dumps(query), '')
            with patch.object(audit.subprocess,'run',return_value=completed):
                with self.assertRaisesRegex(RuntimeError,'incomplete'): audit.audit('unused-output')

    def test_reported_vulnerability_blocks_release_and_is_saved(self):
        query = dict(projects=[dict(frameworks=[dict(transitivePackages=[dict(id='fixture',vulnerabilities=[dict(severity='High')])])])])
        completed = subprocess.CompletedProcess([], 0, json.dumps(query), '')
        with tempfile.TemporaryDirectory() as temporary, patch.object(audit.subprocess,'run',return_value=completed), patch.object(m1,'identity',return_value=dict(commit='test',sourceHash='test')):
            output=Path(temporary)/'audit.json'
            with self.assertRaisesRegex(RuntimeError,'Vulnerable'): audit.audit(output)
            self.assertFalse(json.loads(output.read_text(encoding='utf-8'))['passed'])

    def test_nonzero_or_stderr_blocks_release(self):
        for completed in [subprocess.CompletedProcess([],1,'','failure'), subprocess.CompletedProcess([],0,'{}','feed warning')]:
            with patch.object(audit.subprocess,'run',return_value=completed):
                with self.assertRaisesRegex(RuntimeError,'unavailable'): audit.audit('unused-output')


if __name__=='__main__': unittest.main()
