#!/bin/bash
# round.sh B1 B2 C1 C2 -- pick cards by row (0-based), end selection, press continue, wait for hero input state
G=~/code/gloomhaven-perf/baseline/tools/g.sh; W=~/code/gloomhaven-perf/baseline/tools/waitstate.py
y(){ echo $((256+40*$1)); }
$G click 200 $(y $1) wait 0.8 click 200 $(y $2) wait 1.2 click 243 130 wait 1.5 click 200 $(y $3) wait 0.8 click 200 $(y $4) wait 1.2
$G note "selection end, click End Selection" click 800 835 wait 2.5 note "click Continue (enemy phase)" click 800 835
python3 -I $W 90
