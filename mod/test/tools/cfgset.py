#!/usr/bin/env python3
# cfgset.py key=value ... (game must be closed)
import os,re,subprocess,sys
p=os.path.expanduser('~/.local/share/Steam/steamapps/common/Gloomhaven/BepInEx/config/com.kez.gloomspeed.cfg')
if subprocess.run("ps -eo args | grep -q '[S]:.steamapps.common.Gloomhaven.GH.exe'",shell=True).returncode==0: sys.exit('game running')
s=open(p,newline='').read()
for kv in sys.argv[1:]:
    k,v=kv.split('='); s,n=re.subn(rf'(?m)^{k} = .*?(\r?)$',lambda m:f'{k} = {v}{m.group(1)}',s)
    assert n==1,k
open(p,'w',newline='').write(s)
print(' '.join(l.strip() for l in s.splitlines() if re.match(r'(Enabled|DisableTweenDebug|QuietUnityLog|QuietRuleLog) =',l)))
