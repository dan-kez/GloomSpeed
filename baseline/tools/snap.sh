#!/bin/bash
S=~/code/gloomhaven-perf/baseline/shots; export DISPLAY=:1 XAUTHORITY=$(ls /run/user/1000/xauth_* | head -1)
W=$(xwininfo -root -tree | grep '"Gloomhaven": ("steam_app_780290"' | awk '{print $1}' | head -1)
timeout 15 import -silent -window "$W" -resize 1600x -quality 80 "$S/$1.jpg" 2>&1 | head -2
