#!/bin/bash
# vs.sh name [g.sh ops...] ; run ops then shot into v030/
T=~/code/gloomhaven-perf/mod/test; n=$1; shift; $T/tools/g.sh "$@" shot _v; cp $T/shots/_v.jpg $T/v030/$n.jpg
