#!/bin/bash
# pick.sh LABEL B1 B2 C1 C2 (rows) two-hero round: select cards, end, continue, wait for hero turn
T=~/code/gloomhaven-perf/mod/test; L=$T/$(cat $T/current_run).log
y(){ echo $((256+40*$1)); }
python3 -c "import os;p='$L';open(p+'.off','w').write(str(os.path.getsize(p)))"
$T/tools/g.sh click 200 $(y $2) wait 0.8 click 200 $(y $3) wait 1.2 click 243 130 wait 1.5 click 200 $(y $4) wait 0.8 click 200 $(y $5) wait 1.2
$T/tools/g.sh note "$1" click 800 835 wait 2.5 click 800 835
STALL=${STALL:-12} python3 -I $T/tools/waitstate.py 90 | cut -c1-110
grep -E "'s Turn$| Moves \[| deals |Round [0-9]+$| takes |applies" $L | grep -v -E 'DLL|COMBAT|Phase' | tail -${TAILN:-12} | cut -c1-105
