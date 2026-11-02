#!/bin/bash
# adv.sh: Continue into enemy phase, handle hero turns until next round; screenshot
T=~/code/gloomhaven-perf/mod/test; L=$T/$(cat $T/current_run).log
python3 -c "import os;p='$L';open(p+'.off','w').write(str(os.path.getsize(p)))"
$T/tools/g.sh click 800 835
r=$(STALL=9 python3 -I $T/tools/waitstate.py 90 | cut -c1-70); echo "$r"
case "$r" in hero*) $T/tools/heroturns.sh | cut -c1-70;; esac
$T/tools/g.sh wait 1.5 shot "${1:-adv}"
grep -E "takes |Round [0-9]+$" $L | grep -v DLL | tail -4 | cut -c1-70
