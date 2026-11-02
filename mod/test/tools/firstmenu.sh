#!/bin/bash
# prints sample times and mean brightness of the menu-column region, to locate the first frame with the main menu
cd ~/code/gloomhaven-perf/mod/test/v030/$1
for f in $(ls t*.jpg | sort -V | head -${2:-22}); do echo "$f $(convert $f -crop 90x120+0+40 +repage -colorspace gray -format '%[fx:int(mean*1000)]' info:)"; done | tr '\n' ' '; echo
