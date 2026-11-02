#!/bin/bash
# toload.sh RUN: launch, reach the Load list, verify first entry is Baselinetest, load it.
T=~/code/gloomhaven-perf/mod/test
$T/tools/launch.sh "$1"; L=$T/run-$1.log
for i in $(seq 1 60); do sleep 3; grep -q 'Mono path' "$L" 2>/dev/null && [ "$(grep -c . $L)" -gt 100 ] && break; done
sleep 40
for i in $(seq 1 30); do $T/tools/g.sh click 800 730; sleep 4; grep -q 'WindowsVideoMedia error' "$L" && break; done
sleep 6
$T/tools/g.sh click 70 210 wait 3 click 400 208 wait 4 shot load-$1
convert $T/shots/load-$1.jpg -crop 300x26+690+118 +repage $T/shots/load-$1.crop.png
if [ ! -f $T/shots/ref-title.png ]; then echo NOREF; exit 3; fi
d=$(compare -metric RMSE $T/shots/ref-title.png $T/shots/load-$1.crop.png null: 2>&1 | sed 's/.*(\(.*\))/\1/')
echo "title diff $d"
python3 -c "import sys;sys.exit(0 if float('$d')<0.08 else 1)" || { echo MISMATCH; exit 4; }
$T/tools/g.sh note "load Baselinetest ($1)" click 1353 234 wait 22 shot loaded-$1
grep -m1 -oE 'Campaign_[A-Za-z_]+_[0-9]+' $L | head -1
