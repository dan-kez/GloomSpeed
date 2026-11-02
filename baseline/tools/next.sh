#!/bin/bash
# next.sh B1 B2 C1 C2: one full round with reset watcher, then hero turns until next round
B=~/code/gloomhaven-perf/baseline
$B/tools/g.sh wait 1.5 note "run3 next round cards=$1,$2/$3,$4"
python3 -c "
import os;l=os.path.expanduser('~/code/gloomhaven-perf/baseline/run-3.log');open(l+'.off','w').write(str(os.path.getsize(l)))"
r=$($B/tools/round.sh "$1" "$2" "$3" "$4" | cut -c1-70); echo "$r"
case "$r" in hero*) $B/tools/heroturns.sh | cut -c1-70;; esac
