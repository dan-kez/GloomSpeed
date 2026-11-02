#!/bin/bash
# File managers can run this without a window, so open one to keep the result on screen.
if [ ! -t 0 ]; then
	for terminal in konsole gnome-terminal xfce4-terminal xterm; do
		command -v "$terminal" >/dev/null || continue
		case "$terminal" in
			gnome-terminal) exec gnome-terminal -- bash "$0" ;;
			xfce4-terminal) exec xfce4-terminal -x bash "$0" ;;
			*) exec "$terminal" -e bash "$0" ;;
		esac
	done
fi
bash "$(dirname "$0")/linux.sh" install
echo
read -r -p "Press Return to close this window. " _
