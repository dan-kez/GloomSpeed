#!/bin/bash
# enemy.sh LABEL Y1 [Y2]: click card rows, End Selection, Continue, wait (auto-answers damage) until hero turn/round. Reports outcome.
T=~/code/gloomhaven-perf/mod/test; L=$T/$(cat $T/current_run).log; lab=$1; shift
python3 -c "import os;p='$L';open(p+'.off','w').write(str(os.path.getsize(p)))"
for y in "$@"; do $T/tools/g.sh click 200 $y wait 1; done
$T/tools/g.sh wait 0.5 note "$lab" click 800 835 wait 2.5 click 800 835
STALL=${STALL:-12} python3 -I $T/tools/waitstate.py 90 | cut -c1-110
grep -E "'s Turn$| Moves \[| deals |Round [0-9]+$| takes " $L | grep -v -E 'DLL|COMBAT|Phase' | tail -${TAILN:-9} | cut -c1-105
