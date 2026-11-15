#!/usr/bin/env python3
"""Record resolved NuGet versions and fail closed on a live vulnerability query."""
from pathlib import Path
import argparse
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
import m1


def audit(output, project='src/SystemTools/SystemTools.csproj'):
    output = Path(output)
    assets = json.loads((m1.ROOT / Path(project).parent / 'obj/project.assets.json').read_text(encoding='utf-8'))
    completed = subprocess.run(['dotnet', 'list', project, 'package', '--vulnerable', '--include-transitive', '--format', 'json'],
        cwd=m1.ROOT, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8', errors='replace')
    if completed.returncode or completed.stderr.strip():
        raise RuntimeError('Dependency audit unavailable: ' + completed.stderr + completed.stdout)
    query = json.loads(completed.stdout)
    if query.get('errors') or not query.get('projects'):
        raise RuntimeError('Dependency audit returned incomplete results.')
    vulnerabilities = []
    for item in query['projects']:
        for framework in item.get('frameworks', []):
            for group in ('topLevelPackages', 'transitivePackages'):
                for package in framework.get(group, []):
                    if package.get('vulnerabilities'): vulnerabilities.append(package)
    inventory = []
    for key, library in sorted(assets['libraries'].items()):
        if library['type'] != 'package': continue
        package = next((Path(folder) / library['path'] for folder in assets['packageFolders'] if (Path(folder) / library['path']).is_dir()), None)
        if package is None: raise RuntimeError('Resolved package is missing: ' + key)
        nuspec = next(package.glob('*.nuspec'))
        root = ET.parse(nuspec).getroot()
        def value(name):
            node = next((e for e in root.iter() if e.tag.rsplit('}', 1)[-1] == name), None)
            return node.text if node is not None else None
        inventory.append(dict(id=key.rsplit('/', 1)[0], version=key.rsplit('/', 1)[1], sha512=library.get('sha512'),
            license=value('license'), licenseUrl=value('licenseUrl'), projectUrl=value('projectUrl')))
    report = dict(m1.identity(), schemaVersion=1, runtimeId=m1.native_rid(), passed=not vulnerabilities,
        packages=inventory, vulnerabilities=vulnerabilities, query=query,
        scope='Resolved application dependency graph; host-owned packages are included for transparency, not redistributed by this report.')
    m1.write_json(output, report)
    if vulnerabilities: raise RuntimeError('Vulnerable dependencies block release: ' + ', '.join(p['id'] for p in vulnerabilities))
    print('PASS dependency audit:', len(inventory), 'resolved packages; 0 reported vulnerabilities', flush=True)
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', default='artifacts/m7/dependency-audit.json')
    parser.add_argument('--project', default='src/SystemTools/SystemTools.csproj')
    args = parser.parse_args()
    audit(args.output, args.project)
