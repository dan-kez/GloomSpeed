#!/bin/bash
# rec.sh NAME SECONDS : record game window at 15fps 1600px in background then return immediately
export DISPLAY=:1 XAUTHORITY=$(ls /run/user/1000/xauth_* | head -1)
T=~/code/gloomhaven-perf/mod/test; mkdir -p $T/video
nohup ffmpeg -v error -y -f x11grab -framerate 15 -video_size 3840x2160 -t $2 -i $DISPLAY+0,0 -vf scale=1280:-2 -c:v libx264 -preset ultrafast -crf 30 $T/video/$1.mp4 >/dev/null 2>&1 &
