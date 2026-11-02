# GloomSpeed

A BepInEx mod that shortens presentation delays in Gloomhaven (Steam, Unity 2021.3.5 Mono), plus the tools used to measure them.

## Just want to install it?

**[Follow the install guide for Windows and macOS.](INSTALL.md)** You don't need anything else in this repository.

| | Windows | macOS |
|---|---|---|
| Install | Double-click `Windows - Install.bat` | Run `macos.sh install` in Terminal, then paste one Steam launch option |
| Remove | Double-click `Windows - Uninstall.bat` | Run `macos.sh uninstall`, then clear the launch option |
| Tested on a real computer | Not yet | Not yet |

## What's in this repository

- `mod/GloomSpeed/`: the plugin. Its README covers building, installing and config.
- `mod/install/windows/` and `mod/install/macos/`: the player installers.
- `mod/install/package.sh`: builds the release zip.
- `mod/install/gloomspeed.sh`: reversible install, uninstall and update for Proton on Linux.
- `mod/tools/`: patch target verifier and asset dumper.
- `mod/REPORT.md`: how Fast Forward and multiplayer acknowledgements work.
- `baseline/`: solo enemy-turn measurements and analysis tools.
- `mod/test/tools/`: scripts that drive the game for in-game tests.

Decompiled game code, game assemblies, BepInEx, saves and session logs are deliberately not in this repository. See `.gitignore`.
