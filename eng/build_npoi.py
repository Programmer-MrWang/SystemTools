#!/usr/bin/env python3
"""Build pinned Apache NPOI source; never consume or acknowledge the upstream binary EULA."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PIN = json.loads((ROOT / 'eng/npoi-source.json').read_text(encoding='utf-8'))
PROVENANCE = dict(PIN, buildScriptSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), sdk='10.0.403',
    patches=['System.Security.Cryptography.Xml 10.0.10 -> 10.0.15', 'Explicit ordinal compile input order across operating systems'], targetFramework='net10.0')
WORK = ROOT / 'artifacts/dependencies/npoi'
PACKAGE = WORK / 'feed' / (PIN['packageId'] + '.' + PIN['packageVersion'] + '.nupkg')


def build():
    if subprocess.check_output(['dotnet', '--version'], cwd=ROOT, text=True).strip() != PROVENANCE['sdk']:
        raise RuntimeError('NPOI source build requires the locked SDK.')
    WORK.mkdir(parents=True, exist_ok=True)
    archive = WORK / 'source.tar.gz'
    if not archive.exists():
        with urllib.request.urlopen(PIN['archiveUrl'], timeout=60) as reply: data = reply.read()
        archive.write_bytes(data)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != PIN['archiveSha256']: raise RuntimeError('NPOI source archive differs from pinned hash.')
    if PACKAGE.exists():
        with zipfile.ZipFile(PACKAGE) as package:
            if json.loads(package.read('source-provenance.json')) == PROVENANCE: return PACKAGE
        raise RuntimeError('Cached NPOI package has different provenance.')
    PACKAGE.parent.mkdir(parents=True, exist_ok=True)
    source = WORK / 'source'
    if source.exists():
        if source.is_symlink(): raise RuntimeError('Unsafe dependency workspace.')
        shutil.rmtree(source)
    source.mkdir()
    with tarfile.open(archive, 'r:gz') as tar:
        for member in tar.getmembers():
            parts = Path(member.name).parts[1:]
            if not parts: continue
            if member.issym() or member.islnk() or any(p in ('.', '..') for p in parts): raise RuntimeError('Unsafe source archive.')
            target = source.joinpath(*parts)
            if member.isdir(): target.mkdir(parents=True, exist_ok=True)
            elif member.isfile():
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(tar.extractfile(member).read())
    # Only central dependency patch: the application pins the same patched XML package.
    central = source / 'Directory.Packages.props'
    original = central.read_text(encoding='utf-8-sig')
    old = 'Include="System.Security.Cryptography.Xml" Version="10.0.10"'
    if original.count(old) != 1: raise RuntimeError('Pinned NPOI dependency patch no longer applies.')
    central.write_text(original.replace(old, 'Include="System.Security.Cryptography.Xml" Version="10.0.15"'), encoding='utf-8')
    # MSBuild glob order differs by OS case rules. Freeze the compiler input order from the same archive.
    for project in ['main/NPOI.Core.csproj', 'ooxml/NPOI.OOXML.Core.csproj',
                    'openxml4Net/NPOI.OpenXml4Net.Core.csproj', 'OpenXmlFormats/NPOI.OpenXmlFormats.Core.csproj']:
        path = source / project
        document = ET.parse(path); element = document.getroot()
        properties = ET.SubElement(element, 'PropertyGroup')
        ET.SubElement(properties, 'EnableDefaultCompileItems').text = 'false'
        excluded = {item.attrib['Remove'].replace('\\', '/') for item in element.iter('Compile') if 'Remove' in item.attrib}
        items = ET.SubElement(element, 'ItemGroup')
        for file in sorted(path.parent.rglob('*.cs'), key=lambda file: file.relative_to(path.parent).as_posix()):
            if (not {'bin', 'obj'}.intersection(file.relative_to(path.parent).parts)
                    and file.relative_to(path.parent).as_posix() not in excluded):
                ET.SubElement(items, 'Compile', {'Include': file.relative_to(path.parent).as_posix()})
        document.write(path, encoding='utf-8', xml_declaration=True)
    command = ['dotnet', 'build', str(source / 'ooxml/NPOI.OOXML.Core.csproj'), '-c', 'Release', '-m:1',
        '-p:TargetFrameworks=net10.0', '-p:ContinuousIntegrationBuild=true', '-p:Deterministic=true',
        '-p:DebugType=none', '-p:DebugSymbols=false', '-p:EnableSourceLink=false', '-p:EnableSourceControlManagerQueries=false',
        '-p:GenerateDocumentationFile=false', '-p:EnableNETAnalyzers=false', '-p:PathMap=' + str(source) + '=/_/npoi', '--nologo', '-v', 'minimal']
    subprocess.run(command, cwd=ROOT, check=True)
    binaries = source / 'solution/Release/net10.0'
    names = ['NPOI.Core.dll', 'NPOI.OOXML.dll', 'NPOI.OpenXml4Net.dll', 'NPOI.OpenXmlFormats.dll']
    if not all((binaries / name).is_file() for name in names): raise RuntimeError('NPOI build outputs missing.')
    metadata = ET.Element('package'); values = ET.SubElement(metadata, 'metadata')
    for key, value in [('id', PIN['packageId']), ('version', PIN['packageVersion']), ('authors', 'NPOI contributors; SystemTools source build'),
        ('description', 'Pinned Apache-2.0 NPOI source build; see source-provenance.json for source and build patches.')]: ET.SubElement(values, key).text = value
    ET.SubElement(values, 'license', {'type': 'expression'}).text = 'Apache-2.0'
    ET.SubElement(values, 'repository', {'type': 'git', 'url': PIN['repository'], 'commit': PIN['commit']})
    dependencies = ET.SubElement(values, 'dependencies'); group = ET.SubElement(dependencies, 'group', {'targetFramework': 'net10.0'})
    for name, version in [('BouncyCastle.Cryptography', '2.6.2'), ('Enums.NET', '5.0.0'), ('ExtendedNumerics.BigDecimal', '2025.1001.2.129'),
        ('MathNet.Numerics.Signed', '5.0.0'), ('Microsoft.IO.RecyclableMemoryStream', '3.0.1'), ('SharpZipLib', '1.4.2'),
        ('SkiaSharp', '3.119.4'), ('System.Security.Cryptography.Xml', '10.0.15'), ('ZString', '2.6.0')]:
        ET.SubElement(group, 'dependency', {'id': name, 'version': version})
    files = {PIN['packageId'] + '.nuspec': ET.tostring(metadata, encoding='utf-8'),
        'LICENSE': (source / 'LICENSE').read_bytes(), 'source-provenance.json': (json.dumps(PROVENANCE, sort_keys=True) + '\n').encode()}
    for legal in source.rglob('*'):
        if legal.is_file() and legal.name.upper().startswith(('LICENSE', 'NOTICE')):
            files['source-licenses/' + legal.relative_to(source).as_posix()] = legal.read_bytes()
    files.update({'lib/net10.0/' + name: (binaries / name).read_bytes() for name in names})
    PACKAGE.parent.mkdir(exist_ok=True)
    temporary = PACKAGE.with_suffix('.writing')
    with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED) as output:
        for name, data in sorted(files.items()):
            entry = zipfile.ZipInfo(name, (2020, 1, 1, 0, 0, 0)); entry.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(entry, data)
    os.replace(temporary, PACKAGE)
    print('SOURCE NPOI:', PACKAGE, flush=True)
    return PACKAGE


if __name__ == '__main__': build()
