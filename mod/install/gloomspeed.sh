#!/bin/bash
# Reversible install of BepInEx + GloomSpeed for Gloomhaven under Proton.
#   gloomspeed.sh install    snapshot saves, hash the game, add the mod files and DLL override
#   gloomspeed.sh uninstall  remove exactly those things, then verify the game against the hashes
#   gloomspeed.sh status     show what is currently installed
#   gloomspeed.sh update     replace only the plugin DLL with the current build
# The game must be closed. Saves are never modified or restored by this script.
set -euo pipefail

ROOT=$HOME/code/gloomhaven-perf/mod
GAME=$HOME/.local/share/Steam/steamapps/common/Gloomhaven
PFX=$HOME/.local/share/Steam/steamapps/compatdata/780290/pfx
DATA=$PFX/drive_c/users/steamuser/AppData/LocalLow/FlamingFowlStudios/Gloomhaven
ZIP=$ROOT/downloads/bepinex/BepInEx_win_x64_5.4.23.6.zip
ZIP_SHA=6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755
PLUGIN=$ROOT/GloomSpeed/bin/Release/GloomSpeed.dll
STATE=$ROOT/install/state
HASHES=$STATE/game-files.sha256
FILELIST=$STATE/game-files.list

die() { echo "ERROR: $*" >&2; exit 1; }

require_closed() {
	if pgrep -f 'GH\.exe' >/dev/null; then die "Gloomhaven is running; close it first"; fi
}

# Files the game itself rewrites, so they can't be part of the integrity check
list_game_files() {
	(cd "$GAME" && find . -type f ! -name log.txt ! -name GH.gc.log | LC_ALL=C sort)
}

set_override() {
	python3 -I - "$PFX/user.reg" "$1" <<'EOF'
import os, sys
path, action = sys.argv[1], sys.argv[2]
header = '[Software\\\\Wine\\\\DllOverrides]'
entry = '"winhttp"="native,builtin"\n'
with open(path, encoding='utf-8', errors='surrogateescape', newline='') as f:
    lines = f.readlines()
start = next((i for i, l in enumerate(lines) if l.startswith(header)), None)
if start is None:
    sys.exit('DllOverrides section not found in user.reg')
end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith('[')), len(lines))
present = [i for i in range(start, end) if lines[i].startswith('"winhttp"=')]
if action == 'add':
    if present:
        sys.exit('winhttp override already exists in user.reg')
    at = start + 1
    if lines[at].startswith('#time='):
        at += 1
    lines.insert(at, entry)
else:
    for i in reversed(present):
        if lines[i] != entry:
            sys.exit('winhttp override has an unexpected value: ' + lines[i].strip())
        del lines[i]
tmp = path + '.gloomspeed.tmp'
with open(tmp, 'w', encoding='utf-8', errors='surrogateescape', newline='') as f:
    f.writelines(lines)
os.chmod(tmp, os.stat(path).st_mode)
os.replace(tmp, path)
EOF
}

install_mod() {
	require_closed
	[ ! -e "$GAME/winhttp.dll" ] && [ ! -e "$GAME/BepInEx" ] || die "already installed"
	[ -f "$PLUGIN" ] || die "plugin not built: $PLUGIN"
	echo "$ZIP_SHA  $ZIP" | sha256sum -c --quiet || die "BepInEx zip checksum mismatch"
	while read -r f; do
		[ ! -e "$GAME/$f" ] || die "zip file would overwrite game file: $f"
	done < <(unzip -Z1 "$ZIP" | grep -v '/$')

	mkdir -p "$STATE"
	local snap
	snap=$ROOT/install/saves-before-install-$(date +%Y%m%d-%H%M%S)
	mkdir -p "$snap"
	cp -a "$DATA/GloomSaves" "$snap/"
	[ ! -d "$DATA/GloomSavesOpenBeta" ] || cp -a "$DATA/GloomSavesOpenBeta" "$snap/"
	cp -a "$PFX/user.reg" "$snap/user.reg"
	diff -r "$DATA/GloomSaves" "$snap/GloomSaves" >/dev/null || die "save snapshot does not match"
	echo "saves snapshot: $snap"

	echo "hashing game files..."
	list_game_files > "$FILELIST"
	(cd "$GAME" && xargs -d '\n' sha256sum < "$FILELIST") > "$HASHES"
	unzip -Z1 "$ZIP" | grep -v '/$' > "$STATE/zip-files.list"

	unzip -q -n "$ZIP" -d "$GAME"
	mkdir -p "$GAME/BepInEx/plugins"
	cp "$PLUGIN" "$GAME/BepInEx/plugins/GloomSpeed.dll"
	set_override add
	echo "installed; winhttp override added to the prefix registry"
}

uninstall_mod() {
	require_closed
	[ -f "$STATE/zip-files.list" ] || die "no install record in $STATE"
	if [ -f "$GAME/BepInEx/LogOutput.log" ]; then
		cp "$GAME/BepInEx/LogOutput.log" "$STATE/LogOutput-$(date +%Y%m%d-%H%M%S).log"
	fi
	# Timing logs live under BepInEx/, which is deleted below
	if [ -d "$GAME/BepInEx/GloomSpeed" ]; then
		cp -a "$GAME/BepInEx/GloomSpeed" "$STATE/GloomSpeed-logs-$(date +%Y%m%d-%H%M%S)"
	fi
	set_override remove
	while read -r f; do
		case "$f" in BepInEx/*) ;; *) rm -f "$GAME/$f" ;; esac
	done < "$STATE/zip-files.list"
	rm -rf "${GAME:?}/BepInEx"

	echo "verifying game files..."
	(cd "$GAME" && sha256sum -c --quiet "$HASHES") || die "game files differ from the pre-install hashes"
	if ! diff "$FILELIST" <(list_game_files) >"$STATE/leftover.diff"; then
		cat "$STATE/leftover.diff"
		die "file list differs from pre-install"
	fi
	if grep -q '^"winhttp"=' "$PFX/user.reg"; then die "winhttp override still in user.reg"; fi
	rm -f "$STATE/zip-files.list"
	echo "uninstalled; all game files match their pre-install hashes and no extra files remain"
}

update_plugin() {
	require_closed
	[ -d "$GAME/BepInEx/plugins" ] || die "not installed"
	[ -f "$PLUGIN" ] || die "plugin not built: $PLUGIN"
	cp "$GAME/BepInEx/plugins/GloomSpeed.dll" "$STATE/GloomSpeed-previous.dll"
	cp "$PLUGIN" "$GAME/BepInEx/plugins/GloomSpeed.dll"
	echo "plugin updated; previous build kept at $STATE/GloomSpeed-previous.dll"
}

status_mod() {
	for f in winhttp.dll doorstop_config.ini BepInEx/plugins/GloomSpeed.dll BepInEx/LogOutput.log BepInEx/config/com.kez.gloomspeed.cfg; do
		if [ -e "$GAME/$f" ]; then echo "present  $f"; else echo "absent   $f"; fi
	done
	grep -n '^"winhttp"=' "$PFX/user.reg" || echo "no winhttp override in user.reg"
}

case "${1:-}" in
	install) install_mod ;;
	uninstall) uninstall_mod ;;
	status) status_mod ;;
	update) update_plugin ;;
	*) echo "usage: $0 install|uninstall|status|update" >&2; exit 2 ;;
esac
