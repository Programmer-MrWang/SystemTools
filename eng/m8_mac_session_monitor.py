#!/usr/bin/env python3
"""Read-only session evidence for an explicitly prepared Mac closeout lab."""
from pathlib import Path
import argparse,json,plistlib,subprocess,time
from datetime import datetime,timezone

def monitor(root,maximum_seconds):
    root=Path(root).absolute()
    if not (root/'lab-package.json').exists():raise RuntimeError('Requires an owned Mac lab.')
    started=time.monotonic();previous=None;events=[];last=time.monotonic();fixture_event_count=0;wake_pending=False
    while time.monotonic()-started<maximum_seconds:
        state=plistlib.loads(subprocess.check_output(['/usr/sbin/ioreg','-a','-d','1','-n','Root']))
        if isinstance(state,list):state=state[0]
        locked=bool(state.get('IOConsoleLocked'))
        now=time.monotonic();utc=datetime.now(timezone.utc).isoformat()
        if locked!=previous:
            events.append(dict(utc=utc,kind='locked' if locked else 'unlocked',observedAfterPrevious=previous))
            if previous is True and not locked:
                (root/'closeout-recovery-request').write_text('sleep-wake' if wake_pending else 'lock-unlock')
                wake_pending=False
            previous=locked
        if now-last>20:events.append(dict(utc=utc,kind='monitor-gap',seconds=now-last))
        last=now
        fixture=root/'fixture.json'
        if fixture.exists():
            notes=json.loads(fixture.read_text()).get('workspaceEvents',[])
            for note in notes[fixture_event_count:]:
                events.append(dict(kind='workspace',**note))
                if note['name']=='NSWorkspaceDidWakeNotification':
                    wake_pending=True
                    if not locked:
                        (root/'closeout-recovery-request').write_text('sleep-wake')
                        wake_pending=False
            fixture_event_count=len(notes)
        (root/'session-evidence.json').write_text(json.dumps(dict(updatedUtc=utc,locked=locked,events=events),indent=2))
        if (root/'session-monitor-stop').exists():break
        time.sleep(3)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--root',required=True);parser.add_argument('--maximum-seconds',type=int,default=14400)
    args=parser.parse_args();monitor(args.root,args.maximum_seconds)
