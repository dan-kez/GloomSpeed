#!/bin/bash
# End Session + Yes (menu on Multiplayer tab), then Exit Game + confirm in scenario pause menu; preserves logs under run label.
T=~/code/gloomhaven-perf/mod/test; R=$(cat $T/current_run); G=~/.local/share/Steam/steamapps/common/Gloomhaven; D=~/.local/share/Steam/steamapps/compatdata/780290/pfx/drive_c/users/steamuser/AppData/LocalLow/FlamingFowlStudios/Gloomhaven
$T/tools/g.sh note "end session" click 445 78 wait 2.5 click 729 480 wait 8 note "exit scenario" click 73 619 wait 2.5 click 728 496
for i in $(seq 1 40); do ps -eo args | grep -q '[S]:.steamapps.common.Gloomhaven.GH.exe' || break; sleep 1; done; echo exit-after-$i procs=$(ps -eo args | grep -c '[S]:.steamapps.common.Gloomhaven.GH.exe')
cp -a "$D/Player.log" $T/$R.Player.raw.log; cp -a $G/BepInEx/LogOutput.log $T/$R.LogOutput.log; $T/tools/stopcap.sh
