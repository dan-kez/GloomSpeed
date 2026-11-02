# GloomSpeed

A BepInEx mod that shortens presentation delays in Gloomhaven (Steam, Unity 2021.3.5 Mono), plus the tools used to measure them.

![Side-by-side clip of the same enemy phase. Fast Forward alone takes 10.83 seconds; GloomSpeed takes 4.80 seconds.](docs/media/speedup-side-by-side.gif)

**The same enemy phase, side by side:** five enemies each apply Shield. Fast Forward alone takes 10.8s, and GloomSpeed takes 4.8s (2.27x). The footage isn't retimed; the GloomSpeed side only freezes once it has finished. ([MP4 version](docs/media/speedup-side-by-side.mp4))

**This is close to the best case, not the typical result.** Rounds with self-buffs gain the most, up to 3.5x. Attack-heavy rounds gain 1.2x to 1.7x. Across 13 matched solo enemy phases the mod was 1.88x faster overall. Multiplayer hasn't been measured. Details are in [the test report](mod/test/REPORT.md).

## Just want to install it?

**[Follow the install guide for Windows and macOS.](INSTALL.md)** You don't need anything else in this repository.

| | Windows | macOS |
|---|---|---|
| Install | Unzip into the game folder | Unzip into the game folder, run two Terminal commands, and paste one Steam launch option |
| Update | Unzip the update into the game folder | Replace one file |
| Remove | Delete the files it added | Delete the files it added and clear the launch option |
| Tested on a real computer | Not yet | Yes, solo |

Nothing downloads in the background on this path. A one-step scripted installer is also available.

## What's in this repository

- `mod/GloomSpeed/`: the plugin. Its README covers building, installing and config.
- `mod/install/windows/` and `mod/install/macos/`: the player installers.
- `mod/install/package.py`: builds the release zips.
- `mod/install/gloomspeed.sh`: reversible install, uninstall and update for Proton on Linux.
- `mod/tools/`: patch target verifier and asset dumper.
- `mod/REPORT.md`: how Fast Forward and multiplayer acknowledgements work.
- `baseline/`: solo enemy-turn measurements and analysis tools.
- `mod/test/REPORT.md`: solo before-and-after results.
- `mod/test/tools/`: scripts that drive the game for in-game tests.

Decompiled game code, game assemblies, BepInEx, saves and session logs are deliberately not in this repository. See `.gitignore`.
