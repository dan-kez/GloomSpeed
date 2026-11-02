#!/bin/bash
# startup.sh LABEL SECONDS: launch and sample the game window every ~1.5s (no clicks).
T=~/code/gloomhaven-perf/mod/test; S=$T/v030/$1; mkdir -p $S; export DISPLAY=:1 XAUTHORITY=$(ls /run/user/1000/xauth_* | head -1)
$T/tools/launch.sh "$1"; t0=$(date +%s.%N); echo "$t0" > $S/t0
while :; do now=$(date +%s.%N); e=$(python3 -c "print(round($now-$t0,1))"); python3 -c "import sys;sys.exit(0 if $e<$2 else 1)" || break
 W=$(xwininfo -root -tree 2>/dev/null | grep '"Gloomhaven": ("steam_app_780290"' | awk '{print $1}' | head -1)
 if [ -n "$W" ]; then timeout 5 import -silent -window "$W" -resize 480x -quality 60 $S/t$e.jpg 2>/dev/null; fi
 sleep 1
done
ls $S | wc -l; ps -eo pid,args | grep -E '[w]inebrowser|[x]dg-open' | cut -c1-100; grep -n 'Skipped the\|patches applied\|Settings rows\|Presence' ~/.local/share/Steam/steamapps/common/Gloomhaven/BepInEx/LogOutput.log | cut -c1-110
grep -v -E '^\[(Info|Message)' ~/.local/share/Steam/steamapps/common/Gloomhaven/BepInEx/LogOutput.log | head -5 | cut -c1-140
