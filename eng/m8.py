#!/usr/bin/env python3
"""M8 native stages, regression and same-source candidate."""
import argparse,json,re,subprocess,sys
from pathlib import Path
import m1,m4,m7

ROOT=m1.ROOT
ARTIFACTS=ROOT/'artifacts/m8'

def check():
    ARTIFACTS.mkdir(parents=True,exist_ok=True);m7.check()
    result=subprocess.run(['dotnet','run','--project','tests/SystemTools.M8.Tests','-c','Release'],cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,encoding='utf-8',errors='replace')
    (ARTIFACTS/'contracts.log').write_text(result.stdout,encoding='utf-8')
    report=json.loads((ROOT/'artifacts/m7/checks.json').read_text(encoding='utf-8'))
    report['milestone']='M8';report['passed']=report['passed'] and result.returncode==0
    report['checks'].append(dict(name='m8-contracts',passed=result.returncode==0,checks=len(re.findall(r'^PASS \d+:',result.stdout,re.M))))
    m1.write_json(ARTIFACTS/'checks.json',report)
    print(result.stdout,end='')
    if not report['passed']:raise RuntimeError('M8 checks failed')

def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='operation',required=True);sub.add_parser('check')
    command=sub.add_parser('stage');command.add_argument('--target',choices=list(m1.MODULES))
    command=sub.add_parser('aggregate');command.add_argument('--input',nargs=3,required=True);command.add_argument('--output',required=True)
    args=parser.parse_args();ARTIFACTS.mkdir(parents=True,exist_ok=True)
    if args.operation=='check':check()
    elif args.operation=='stage':
        m7.ARTIFACTS=ARTIFACTS;print(m7.stage(args.target,milestone='M8'))
    else:
        m4.ARTIFACTS=ARTIFACTS
        for path in args.input:
            if m4.verify_stage(path)['milestone']!='M8':raise RuntimeError('M8 requires M8 stages')
        package=m4.aggregate(args.input,args.output)
        print(json.dumps(m7.audit(package,ARTIFACTS/'package-audit.json'),ensure_ascii=False,indent=2))

if __name__=='__main__':main()
