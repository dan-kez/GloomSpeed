#!/bin/bash
# GloomSpeed installer for macOS. Usage: bash macos.sh install | uninstall
# GLOOMSPEED_GAME_DIR overrides the game folder, GLOOMSPEED_BEPINEX_ZIP uses a local BepInEx zip.
set -euo pipefail

BEPINEX_URL=https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_macos_universal_5.4.23.6.zip
BEPINEX_SHA=2e711e21f6570da964fce4654b1976500dbe043f61778bce0008500078564638
RECORD=GloomSpeed-installed-files.txt
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
STEAM="$HOME/Library/Application Support/Steam"
SAVES="$HOME/Library/Application Support/unity.FlamingFowlStudios.Gloomhaven/GloomSaves"
STAMP=$(date +%Y%m%d-%H%M%S)

die() { echo "ERROR: $*" >&2; exit 1; }

find_game() {
	local vdf="$STEAM/steamapps/libraryfolders.vdf" root
	if [ -d "$STEAM/steamapps/common/Gloomhaven" ]; then
		echo "$STEAM/steamapps/common/Gloomhaven"
		return
	fi
	if [ -f "$vdf" ]; then
		while IFS= read -r root; do
			if [ -d "$root/steamapps/common/Gloomhaven" ]; then
				echo "$root/steamapps/common/Gloomhaven"
				return
			fi
		done < <(sed -n 's/.*"path"[[:space:]]*"\(.*\)".*/\1/p' "$vdf")
	fi
}

sha256() {
	if command -v shasum >/dev/null; then shasum -a 256 "$1" | cut -d' ' -f1; else sha256sum "$1" | cut -d' ' -f1; fi
}

check() {
	local log="$GAME/BepInEx/LogOutput.log"
	if [ ! -f "$log" ]; then
		echo "The mod has not run yet. Set the launch option, start the game from Steam,"
		echo "reach the main menu, quit, then run this installer again to check it."
	elif grep -q 'GloomSpeed.*patches applied' "$log"; then
		echo "Result of the last game launch:"
		grep 'GloomSpeed.*patches applied' "$log" | sed 's/^/  /'
	else
		echo "BepInEx ran, but GloomSpeed did not report in. Please open an issue with this file:"
		echo "  $log"
	fi
}

launch_help() {
	local option="\"$GAME/run_bepinex.sh\" %command%"
	if command -v pbcopy >/dev/null; then
		printf '%s' "$option" | pbcopy
		echo "The line below is already copied to your clipboard."
	fi
	echo
	echo "ONE MORE STEP: in Steam, right-click Gloomhaven > Properties > General,"
	echo "and paste this into Launch Options:"
	echo
	echo "  $option"
	echo
}

ACTION=${1:-install}
case "$ACTION" in install | uninstall) ;; *) die "usage: bash macos.sh install|uninstall" ;; esac

GAME=${GLOOMSPEED_GAME_DIR:-$(find_game)}
if [ -z "$GAME" ]; then
	echo "Could not find Gloomhaven automatically."
	echo "In Steam: right-click Gloomhaven > Manage > Browse local files, then drag that folder here."
	read -r -p "Folder: " GAME
	GAME=${GAME//\\ / }
	GAME=${GAME%% }
fi
[ -n "$GAME" ] && [ -d "$GAME" ] || die "folder not found: $GAME"
echo "Game folder: $GAME"

if pgrep -f 'Gloomhaven/.*\.app/Contents/MacOS' >/dev/null; then die "Gloomhaven is running. Close it and try again."; fi

if [ "$ACTION" = uninstall ]; then
	[ -f "$GAME/$RECORD" ] || die "GloomSpeed is not installed in this folder."
	if [ -d "$GAME/BepInEx/GloomSpeed" ]; then
		cp -R "$GAME/BepInEx/GloomSpeed" "$HOME/Documents/GloomSpeed-logs-$STAMP"
		echo "Timing logs kept at: $HOME/Documents/GloomSpeed-logs-$STAMP"
	fi
	while IFS= read -r file; do
		case "$file" in
			BepInEx/* | "") ;;
			*) rm -f "${GAME:?}/${file:?}" ;;
		esac
	done <"$GAME/$RECORD"
	rm -rf "${GAME:?}/BepInEx"
	rm -f "${GAME:?}/${RECORD:?}"
	echo "Files removed. LAST STEP: in Steam, right-click Gloomhaven > Properties > General,"
	echo "and clear Launch Options. The game won't start properly until you do."
	exit 0
fi

[ -f "$HERE/GloomSpeed.dll" ] || die "GloomSpeed.dll is missing. Unzip the whole package and run this from that folder."

if [ -f "$GAME/$RECORD" ]; then
	cp "$HERE/GloomSpeed.dll" "$GAME/BepInEx/plugins/GloomSpeed.dll"
	echo "GloomSpeed was already installed, so its DLL has been updated."
	check
	launch_help
	exit 0
fi

APP=$(cd "$GAME" && ls -d ./*.app 2>/dev/null | head -1 | sed 's|^\./||' || true)
[ -n "$APP" ] || die "no .app found in $GAME, so this doesn't look like the macOS version of the game."

if [ -d "$SAVES" ]; then
	mkdir -p "$HOME/Documents"
	cp -R "$SAVES" "$HOME/Documents/Gloomhaven-save-backup-$STAMP"
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
	curl -fsSL -o "$ZIP" "$BEPINEX_URL"
fi
[ "$(sha256 "$ZIP")" = "$BEPINEX_SHA" ] || die "BepInEx download failed its checksum. Nothing was installed."

unzip -Z1 "$ZIP" | grep -v '/$' >"$TEMP/files"
while IFS= read -r file; do
	[ ! -e "$GAME/$file" ] || die "refusing to overwrite an existing file: $file"
done <"$TEMP/files"

cp "$TEMP/files" "$GAME/$RECORD"
unzip -q -n "$ZIP" -d "$GAME"
mkdir -p "$GAME/BepInEx/plugins"
cp "$HERE/GloomSpeed.dll" "$GAME/BepInEx/plugins/GloomSpeed.dll"
sed "s|^executable_name=\"\"|executable_name=\"$APP\"|" "$GAME/run_bepinex.sh" >"$TEMP/run"
cat "$TEMP/run" >"$GAME/run_bepinex.sh"
chmod u+x "$GAME/run_bepinex.sh"
if command -v xattr >/dev/null; then
	xattr -dr com.apple.quarantine "$GAME/libdoorstop.dylib" "$GAME/BepInEx" 2>/dev/null || true
fi

echo
echo "Installed."
launch_help
echo "Then start the game from Steam. To check it worked: reach the main menu, quit,"
echo "and run this installer again."
