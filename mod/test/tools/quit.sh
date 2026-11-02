#!/bin/bash
# quit.sh: Exit Game from pause menu, confirm, wait for exit, keep logs, stop capture
T=~/code/gloomhaven-perf/mod/test; R=$(cat $T/current_run); G=~/.local/share/Steam/steamapps/common/Gloomhaven
DATA=~/.local/share/Steam/steamapps/compatdata/780290/pfx/drive_c/users/steamuser/AppData/LocalLow/FlamingFowlStudios/Gloomhaven
$T/tools/g.sh wait 2 key Escape wait 2.5 note "exit" click 73 619 wait 2.5 click 728 496
for i in $(seq 1 40); do ps -eo args | grep -q '[S]:.steamapps.common.Gloomhaven.GH.exe' || break; sleep 1; done
ps -eo args | grep -q '[S]:.steamapps.common.Gloomhaven.GH.exe' && echo STILL-RUNNING || echo "exited after ${i}s"
cp -a "$DATA/Player.log" $T/$R.Player.raw.log; cp -a $G/BepInEx/LogOutput.log $T/$R.LogOutput.log; $T/tools/stopcap.sh; rm -f $T/$R.log.off
