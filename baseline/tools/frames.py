#!/usr/bin/env python3
"""Frame deltas during banner delay tweens only, where one tween prints every frame, so a gap between lines means a slow frame and not idle log time."""
import re,os,json,csv
from datetime import datetime as D
B=os.path.expanduser('~/code/gloomhaven-perf/baseline')
sec=lambda s:(D.strptime(s,'%H:%M:%S.%f')-D(1900,1,1)).total_seconds()
pat=re.compile(r'^(\S+) tween:name:Banner toggle:True passed:0 time:0\.3 delay:([-\d.E]+)')
turns=list(csv.DictReader(open(B+'/turns.csv')))
out={}
hitches=[]
def cond(run,t):
    for r in turns:
        a=sec(r['t']); b=a+float(r['total_excl_input'])+float(r['script_input_wait'] or 0)
        if r['run']==run and a<=t<=b: return r['condition']
    return 'not-enemy-turn'
for run in ('run-1','run-2','run-3'):
    prev=None
    for l in open(f'{B}/{run}.log',errors='replace'):
        m=pat.match(l)
        if not m:
            continue
        t=sec(m[1]); d=float(m[2])
        if prev and d<prev[1] and t-prev[0]<2:
            arr=(t-prev[0])*1000; uni=(prev[1]-d)*1000
            c=cond(run,t); out.setdefault(c,[]).append((arr,uni))
            if max(arr,uni)>25: hitches.append((run,m[1],round(arr,1),round(uni,1),c))
        prev=(t,d)
res={}
for c,v in out.items():
    a=sorted(x[0] for x in v); u=sorted(x[1] for x in v); q=lambda s,p:round(s[min(len(s)-1,int(p*len(s)))],2)
    res[c]=dict(n=len(v),wall_median=q(a,.5),wall_p95=q(a,.95),wall_p99=q(a,.99),wall_max=q(a,1),unity_median=q(u,.5),unity_p99=q(u,.99),unity_max=q(u,1),over16_7=sum(1 for x in a if x>16.7),over33=sum(1 for x in a if x>33.4))
print(json.dumps(res,indent=0)); print('hitches>25ms',hitches)
json.dump(dict(res=res,hitches=hitches),open(B+'/frames.json','w'),indent=1)
