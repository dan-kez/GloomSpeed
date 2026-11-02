#!/bin/bash
for p in $(ps -eo pid,args | grep -E '[t]ools/capture.sh ' | awk '{print $1}'); do kill $p; done
sleep 1
for p in $(ps -eo pid,args | grep -E '[t]ail -n0 -F|[n]vidia-smi --query-gpu=timestamp|[t]op -H -b -d 1|[p]ython3 -uc' | awk '{print $1}'); do kill $p; done
