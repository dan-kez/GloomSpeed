#!/bin/bash
# door.sh LABEL: R1 from narrator through door opening and the enemy phase, stopping at Brute's turn (before R2 autosave)
T=~/code/gloomhaven-perf/mod/test; L=$T/$(cat $T/current_run).log; R=$(cat $T/current_run)
for i in 1 2 3 4 5; do $T/tools/g.sh click 800 780 wait 2.5; done; $T/tools/g.sh wait 1
TAILN=1 $T/tools/pick.sh "$1 R1 select" 8 9 4 10
$T/tools/g.sh wait 1.5 click 188 325 wait 2 click 508 560 wait 1.5 shot $R-door0 note "$1 click Open Door" click 800 835 wait 6 shot $R-door1 click 188 575 wait 1.8 click 590 835 wait 2 shot $R-door2
python3 -c "import os;p='$L';open(p+'.off','w').write(str(os.path.getsize(p)))"
$T/tools/g.sh note "$1 end Crag R1" click 800 835
STALL=10 python3 -I $T/tools/waitstate.py 90 | cut -c1-100
grep -E "'s Turn$| Moves \[| deals |takes " $L | grep -v -E 'DLL|COMBAT' | tail -${TAILN:-14} | cut -c1-110
$T/tools/g.sh wait 1 shot $R-door3
