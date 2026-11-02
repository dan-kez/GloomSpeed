#!/bin/bash
# sel.sh B1 B2 [C1 C2]: pick rows; End Selection; report the guard card chosen
T=~/code/gloomhaven-perf/mod/test; L=$T/$(cat $T/current_run).log
y(){ echo $((256+40*$1)); }
$T/tools/g.sh click 200 $(y $1) wait 0.8 click 200 $(y $2) wait 1.2
if [ -n "$3" ]; then $T/tools/g.sh click 243 130 wait 1.5 click 200 $(y $3) wait 0.8 click 200 $(y $4) wait 1.2; fi
O=$(wc -l < $L); $T/tools/g.sh click 800 835 wait 2.2
tail -n +$O $L | grep -E 'selected card' | grep -v DLL | cut -c14-70 | sort -u
