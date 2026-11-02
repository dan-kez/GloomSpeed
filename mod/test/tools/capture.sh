#!/bin/bash
# usage: capture.sh N  -- timestamped Player.log, per-thread CPU and GPU sampling. Writes only under baseline/.
N=$1; OUT=~/code/gloomhaven-perf/mod/test
DATA=~/.local/share/Steam/steamapps/compatdata/780290/pfx/drive_c/users/steamuser/AppData/LocalLow/FlamingFowlStudios/Gloomhaven
trap 'kill $(jobs -p) 2>/dev/null' EXIT TERM INT
tail -n0 -F "$DATA/Player.log" 2>"$OUT/run-$N.tail.err" | python3 -uc 'import sys,datetime as d
for l in sys.stdin: print(d.datetime.now().strftime("%H:%M:%S.%f")[:-3], l, end="", flush=True)' > "$OUT/run-$N.log" &
# file growth sampled at 50ms, to judge how the log gets flushed independently of tail
python3 -uc '
import os,time,sys,datetime as d
p=sys.argv[1]; last=-1
while True:
    try: s=os.stat(p).st_size
    except OSError: s=-2
    if s!=last:
        print(d.datetime.now().strftime("%H:%M:%S.%f")[:-3], s, flush=True); last=s
    time.sleep(0.05)' "$DATA/Player.log" > "$OUT/run-$N.size.log" &
nvidia-smi --query-gpu=timestamp,utilization.gpu,utilization.memory,memory.used,power.draw,clocks.gr,temperature.gpu --format=csv,noheader -l 1 > "$OUT/run-$N.gpu.csv" &
# per-thread top for GH.exe at 1s, once it exists
( P=""; while [ -z "$P" ]; do P=$(ps -eo pid,args | grep '[S]:.steamapps.common.Gloomhaven.GH.exe' | awk '{print $1;exit}'); sleep 1; done
  echo "# pid $P" > "$OUT/run-$N.top.log"
  top -H -b -d 1 -w 200 -p "$P" | awk '/^top -/{print; fflush(); next} /^ *[0-9]+ /{ if ($9+0>=1.0) {print; fflush()} }' >> "$OUT/run-$N.top.log" ) &
wait
