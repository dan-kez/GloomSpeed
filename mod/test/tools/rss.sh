#!/bin/bash
P=$(ps -eo pid,args | grep '[S]:.steamapps.common.Gloomhaven.GH.exe' | awk '{print $1;exit}'); echo "$(date +%T) pid=$P rss_kb=$(ps -o rss= -p $P)"
