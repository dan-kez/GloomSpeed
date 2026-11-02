#!/bin/bash
# launch.sh RUNLABEL: start capture and game
T=~/code/gloomhaven-perf/mod/test
echo "run-$1" > $T/current_run
nohup setsid $T/tools/capture.sh "$1" >/dev/null 2>&1 &
sleep 2
$T/tools/g.sh note "launch $1"
nohup steam -applaunch 780290 >/dev/null 2>&1 &
