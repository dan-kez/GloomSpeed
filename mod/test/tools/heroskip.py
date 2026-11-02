#!/usr/bin/env python3
# Skip one hero's turn, driven by log state.
import os,subprocess,time,re,sys
B=os.path.expanduser('~/code/gloomhaven-perf/mod/test'); G=B+'/tools/g.sh'; log=B+'/'+open(B+'/current_run').read().strip()+'.log'
def g(*a): subprocess.run([G,*map(str,a)])
def tail_from(o):
    with open(log,errors='replace') as f:
        f.seek(o); return f.read()
start=os.path.getsize(log)
def state():
    t=tail_from(start)
    return dict(end=('EREADYBUTTONENDTURN' in t and 'READY BUTTON - ON CLICK' not in t.split('EREADYBUTTONENDTURN')[-1]),
                skipped=len(re.findall(r"^\S+ (Brute|Cragheart) skipped ",t,re.M)),
                acted=len(re.findall(r"^\S+ (Brute|Cragheart) uses ",t,re.M)))
cards=[(188,190),(188,740)]
order=sys.argv[1:] or []
for idx,(x,y) in enumerate(cards):
    if state()['end']: break
    before=state()['skipped']
    g('click',x,y,'wait',1.5)
    for i in range(5):
        s=state()
        if s['end'] or s['skipped']>before: break
        g('click',590,835,'wait',1.4)
for i in range(8):
    if state()['end']: break
    g('wait',0.5)
print('hero',state())
g('click',800,835,'wait',1.5)
