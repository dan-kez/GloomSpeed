#!/bin/bash
# GloomSpeed installer for Linux (Proton). Players run "GloomSpeed install - Linux.sh", which runs this.
# Usage: bash linux.sh install | uninstall
# GLOOMSPEED_GAME_DIR overrides the game folder, GLOOMSPEED_BEPINEX_ZIP uses a local BepInEx zip.
set -euo pipefail

BEPINEX_URL=https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip
BEPINEX_SHA=6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755
RECORD=GloomSpeed-installed-files.txt
OVERRIDE_RECORD=GloomSpeed-added-wine-override.txt
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
STAMP=$(date +%Y%m%d-%H%M%S)

die() { echo "ERROR: $*" >&2; exit 1; }

find_game() {
	local steam root
	for steam in "$HOME/.local/share/Steam" "$HOME/.steam/steam" "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"; do
		if [ -f "$steam/steamapps/common/Gloomhaven/GH.exe" ]; then
			echo "$steam/steamapps/common/Gloomhaven"
			return
		fi
		[ -f "$steam/steamapps/libraryfolders.vdf" ] || continue
		while IFS= read -r root; do
			if [ -f "$root/steamapps/common/Gloomhaven/GH.exe" ]; then
				echo "$root/steamapps/common/Gloomhaven"
				return
			fi
		done < <(sed -n 's/.*"path"[[:space:]]*"\(.*\)".*/\1/p' "$steam/steamapps/libraryfolders.vdf")
	done
}

check() {
	local log="$GAME/BepInEx/LogOutput.log"
	if [ ! -f "$log" ]; then
		echo "The mod has not run yet. Start the game from Steam and look for Game Speed in Options > General."
	elif grep -q 'GloomSpeed.*patches applied' "$log"; then
		echo "Result of the last game launch:"
		grep 'GloomSpeed.*patches applied' "$log" | sed 's/^/  /'
	else
		echo "BepInEx ran, but GloomSpeed did not report in. Please open an issue with this file:"
		echo "  $log"
	fi
}

# Wine only loads the mod loader's winhttp.dll if the game's Proton prefix says to.
set_override() {
	python3 -I - "$PFX/user.reg" "$1" <<'PY'
import os, sys
path, action = sys.argv[1], sys.argv[2]
header = '[Software\\\\Wine\\\\DllOverrides]'
entry = '"winhttp"="native,builtin"\n'
with open(path, encoding='utf-8', errors='surrogateescape', newline='') as f:
    lines = f.readlines()
start = next((i for i, l in enumerate(lines) if l.startswith(header)), None)
if start is None:
    sys.exit('The DllOverrides section was not found in user.reg')
end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith('[')), len(lines))
present = [i for i in range(start, end) if lines[i].startswith('"winhttp"=')]
if action == 'add':
    if present:
        print('existing')
        sys.exit(0)
    at = start + 1
    if at < len(lines) and lines[at].startswith('#time='):
        at += 1
    lines.insert(at, entry)
else:
    for i in reversed(present):
        if lines[i] == entry:
            del lines[i]
tmp = path + '.gloomspeed.tmp'
with open(tmp, 'w', encoding='utf-8', errors='surrogateescape', newline='') as f:
    f.writelines(lines)
os.chmod(tmp, os.stat(path).st_mode)
os.replace(tmp, path)
PY
}

ACTION=${1:-install}
case "$ACTION" in install | uninstall) ;; *) die "usage: bash linux.sh install|uninstall" ;; esac

GAME=${GLOOMSPEED_GAME_DIR:-$(find_game)}
if [ -z "$GAME" ]; then
	echo "Could not find Gloomhaven automatically."
	echo "In Steam: right-click Gloomhaven > Manage > Browse local files, then paste that folder's path here."
	read -r -p "Folder: " GAME
fi
[ -f "$GAME/GH.exe" ] || die "GH.exe was not found in: $GAME"
echo "Game folder: $GAME"

# The prefix sits beside the game, in the same Steam library.
PFX=$(cd "$GAME/../.." && pwd)/compatdata/780290/pfx
DATA=$PFX/drive_c/users/steamuser/AppData/LocalLow/FlamingFowlStudios/Gloomhaven

# Wine shows the game's Windows path as its command line.
if ps -eo args | grep -Eq '^[A-Za-z]:\\.*\\GH\.exe'; then die "Gloomhaven is running. Close it and try again."; fi

if [ "$ACTION" = uninstall ]; then
	[ -f "$GAME/$RECORD" ] || die "this installer didn't install GloomSpeed here. If you installed it by hand, follow the Remove steps in the install guide."
	if [ -d "$GAME/BepInEx/GloomSpeed" ]; then
		cp -R "$GAME/BepInEx/GloomSpeed" "$HOME/Documents/GloomSpeed-logs-$STAMP"
		echo "Timing logs kept at: $HOME/Documents/GloomSpeed-logs-$STAMP"
	fi
	if [ -f "$GAME/$OVERRIDE_RECORD" ]; then
		set_override remove
	fi
	while IFS= read -r file; do
		case "$file" in
			BepInEx/* | "") ;;
			*) rm -f "${GAME:?}/${file:?}" ;;
		esac
	done <"$GAME/$RECORD"
	rm -rf "${GAME:?}/BepInEx"
	rm -f "${GAME:?}/${RECORD:?}" "${GAME:?}/${OVERRIDE_RECORD:?}"
	echo "GloomSpeed is removed. Your game and saves are as they were."
	exit 0
fi

[ -f "$HERE/GloomSpeed.dll" ] || die "GloomSpeed.dll is missing. Unzip the whole package and run this from that folder."

if [ -f "$GAME/BepInEx/plugins/GloomSpeed.dll" ]; then
	cp "$HERE/GloomSpeed.dll" "$GAME/BepInEx/plugins/GloomSpeed.dll"
	echo "GloomSpeed was already installed, so it has been updated. Your settings are kept."
	check
	exit 0
fi

command -v python3 >/dev/null || die "python3 is needed but wasn't found. Install it, or follow Install by hand in the install guide."
[ -f "$PFX/user.reg" ] || die "Gloomhaven's Proton files weren't found. Start the game from Steam once, close it, and try again."

if [ -d "$DATA/GloomSaves" ]; then
	mkdir -p "$HOME/Documents"
	cp -R "$DATA/GloomSaves" "$HOME/Documents/Gloomhaven-save-backup-$STAMP"
	cp "$PFX/user.reg" "$HOME/Documents/Gloomhaven-save-backup-$STAMP/user.reg"
	echo "Saves backed up to: $HOME/Documents/Gloomhaven-save-backup-$STAMP"
else
	echo "No saves found to back up. Continuing."
fi

TEMP=$(mktemp -d)
trap 'rm -rf "${TEMP:?}"' EXIT
ZIP=${GLOOMSPEED_BEPINEX_ZIP:-}
if [ -z "$ZIP" ]; then
	ZIP=$TEMP/BepInEx.zip
	echo "Downloading BepInEx from its official GitHub release..."
	if command -v curl >/dev/null; then curl -fsSL -o "$ZIP" "$BEPINEX_URL"; else wget -q -O "$ZIP" "$BEPINEX_URL"; fi
fi
[ "$(sha256sum "$ZIP" | cut -d' ' -f1)" = "$BEPINEX_SHA" ] || die "BepInEx download failed its checksum. Nothing was installed."

python3 -I -c 'import sys, zipfile; print("\n".join(n for n in zipfile.ZipFile(sys.argv[1]).namelist() if not n.endswith("/")))' "$ZIP" >"$TEMP/files"
while IFS= read -r file; do
	[ ! -e "$GAME/$file" ] || die "refusing to overwrite an existing file: $file"
done <"$TEMP/files"

cp "$TEMP/files" "$GAME/$RECORD"
python3 -I -m zipfile -e "$ZIP" "$GAME"
mkdir -p "$GAME/BepInEx/plugins"
cp "$HERE/GloomSpeed.dll" "$GAME/BepInEx/plugins/GloomSpeed.dll"
result=$(set_override add) || die "couldn't update Gloomhaven's Proton settings. Run the uninstaller, then follow Install by hand in the install guide."
if [ "$result" != existing ]; then
	echo "winhttp" >"$GAME/$OVERRIDE_RECORD"
fi

echo
echo "Installed. Start Gloomhaven from Steam as usual, no launch options needed."
echo "To check it worked, open Options > General in the game and look for Game Speed."
