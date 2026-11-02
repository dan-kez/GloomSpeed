#!/usr/bin/env python3
# Wait for a player-input state in the timestamped log. Prints reason. usage: waitstate.py [timeout_s]
import os,re,sys,time,subprocess
G=os.path.expanduser('~/code/gloomhaven-perf/mod/test/tools/g.sh')
log=os.path.expanduser('~/code/gloomhaven-perf/mod/test/'+open(os.path.expanduser('~/code/gloomhaven-perf/mod/test/current_run')).read().strip()+'.log')
off_f=log+'.off'
off=int(open(off_f).read()) if os.path.exists(off_f) else os.path.getsize(log)
t0=time.time(); limit=float(sys.argv[1]) if len(sys.argv)>1 else 90
pat=re.compile(r"(?P<hero>(Brute|Cragheart)'s Turn$)|(?P<round>Hidden banner phase START_ROUND)", re.I)
last=''
quiet=time.time()
with open(log,errors='replace') as f:
    f.seek(off)
    while time.time()-t0<limit:
        l=f.readline()
        if not l:
            if time.time()-quiet>float(os.environ.get('STALL','10')):
                print('stall'); break
            time.sleep(0.05); continue
        if 'tween' in l or 'LogEOS' in l or ' at ' in l[12:18]: continue
        quiet=time.time()
        if 'Finish  ShowAttackModifDamage coroutine' in l:
            time.sleep(0.3); subprocess.run([G,'key',os.environ.get('DMGKEY','c')])
        m=pat.search(l)
        if m:
            print(m.lastgroup,l.strip()[:150]); break
    else:
        print('timeout')
    open(off_f,'w').write(str(f.tell()))
