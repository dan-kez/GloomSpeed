#!/bin/bash
T=~/code/gloomhaven-perf/baseline/tools
while :; do
  $T/g.sh wait 2.2
  python3 -I $T/heroskip.py
  r=$(STALL=8 python3 -I $T/waitstate.py 90); echo "$r" | cut -c1-120
  case "$r" in hero*) continue;; *) break;; esac
done
