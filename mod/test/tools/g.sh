#!/bin/bash
# g.sh click X Y | move X Y | key NAME | str TEXT | shot NAME | wait SEC  (X,Y in 1600x900 view coords; several ops chained with --)
export DISPLAY=:1 XAUTHORITY=$(ls /run/user/1000/xauth_* | head -1)
T=~/code/gloomhaven-perf/mod/test/tools; NOTES=~/code/gloomhaven-perf/mod/test/notes.txt
sc(){ python3 -c "print(round($1*2.4))"; }
while [ $# -gt 0 ]; do case "$1" in
 click) X=$(sc $2);Y=$(sc $3); xte "mousemove $X $Y"; sleep 0.25; xte "mousedown 1"; sleep 0.08; xte "mouseup 1"; shift 3;;
 move) X=$(sc $2);Y=$(sc $3); xte "mousemove $X $Y"; shift 3;;
 key) xte "keydown $2"; sleep 0.12; xte "keyup $2"; shift 2;;
 scroll) for i in $(seq 1 $2); do xte "mouseclick 5"; sleep 0.05; done; shift 2;;
 str) xte "str $2"; shift 2;;
 shot) $T/snap.sh "$2"; shift 2;;
 wait) sleep "$2"; shift 2;;
 note) echo "$(date +%H:%M:%S.%3N) $2" >> $NOTES; shift 2;;
 --) shift;;
 *) echo "bad op $1"; exit 2;;
esac; done
