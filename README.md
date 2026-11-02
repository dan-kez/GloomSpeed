# GloomSpeed

A BepInEx mod that shortens presentation delays in Gloomhaven (Steam, Windows build, Unity 2021.3.5 Mono), plus the tools used to measure them.

- `mod/GloomSpeed/`: the plugin. Its README covers building, installing and config.
- `mod/install/gloomspeed.sh`: reversible install, uninstall and update for Proton on Linux.
- `mod/tools/`: patch target verifier and asset dumper.
- `mod/REPORT.md`: how Fast Forward and multiplayer acknowledgements work.
- `baseline/`: solo enemy-turn measurements and analysis tools.
- `mod/test/tools/`: scripts that drive the game for in-game tests.

Decompiled game code, game assemblies, BepInEx, saves and session logs are deliberately not in this repository. See `.gitignore`.
