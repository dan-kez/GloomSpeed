#!/bin/bash
G=~/code/gloomhaven-perf/mod/test/tools/g.sh
$G click 200 256 wait 0.8 click 200 296 wait 1 click 800 835 wait 2.5 click 800 835
for i in 1 2 3 4 5 6 7 8 9 10 11 12; do $G key c wait 0.7; done
$G click 188 190 wait 1.6 click 590 835 wait 1.6 click 188 740 wait 1.6 click 590 835 wait 2 click 800 835 wait 3
for i in 1 2 3 4; do $G key c wait 0.7; done
