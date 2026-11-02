#!/bin/bash
# recround.sh RUN: load the R6 checkpoint, pick the same cards, record game window with wallclock pts, press Continue, stop after hero turn starts.
T=~/code/gloomhaven-perf/mod/test; R=$1; L=$T/run-$R.log
export DISPLAY=:1 XAUTHORITY=$(ls /run/user/1000/xauth_* | head -1)
$T/tools/toload.sh $R || exit 1
$T/tools/g.sh move 1000 100 wait 1 shot $R-sel
$T/tools/sel.sh 3 7
$T/tools/g.sh move 800 835 wait 1.5 shot $R-cont
W=$(xwininfo -root -tree | grep '"Gloomhaven": ("steam_app_780290"' | awk '{print $1}' | head -1)
python3 -c "import os;p='$L';open(p+'.off','w').write(str(os.path.getsize(p)))"
nohup ffmpeg -v error -y -f x11grab -draw_mouse 0 -framerate 30 -window_id $W -t 30 -i :1 -copyts -fps_mode passthrough -c:v libx264 -preset ultrafast -crf 18 -f matroska $T/clip/$R.raw.mkv > $T/clip/$R.ffmpeg.log 2>&1 &
sleep 3
$T/tools/g.sh note "$R continue" click 800 835
STALL=8 python3 -I $T/tools/waitstate.py 60 | cut -c1-70
sleep 4; pkill -INT -x ffmpeg; for i in 1 2 3 4 5 6 7 8 9 10; do pgrep -x ffmpeg >/dev/null || break; sleep 1; done
grep -E "'s Turn$| applies | Moves \[| deals |Round [0-9]+$" $L | grep -v -E 'DLL|COMBAT' | tail -11 | cut -c1-80
ffprobe -v error -show_entries format=duration,start_time:stream=width,height,nb_read_frames -count_frames -of default=nw=1 $T/clip/$R.raw.mkv | tr '\n' ' '; echo
