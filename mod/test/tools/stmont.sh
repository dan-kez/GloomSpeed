#!/bin/bash
cd ~/code/gloomhaven-perf/mod/test/v030/$1
montage $(ls t*.jpg | sort -V | awk -v n="${2:-3}" 'NR%n==1') -tile 6x -geometry 300x169+1+1 -pointsize 18 -set label '%f' ../$1-m.jpg
