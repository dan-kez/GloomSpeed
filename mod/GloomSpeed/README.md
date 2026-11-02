# GloomSpeed (prototype)

BepInEx 5 plugin that shortens presentation delays in Gloomhaven (Steam, Windows build, Unity 2021.3.5 Mono).

**Prototype.** 0.1.0 loaded and ran in solo testing under Proton. The 0.2.0 diagnostics compile and their targets are verified, but have not been run in-game. Nothing has been tested in multiplayer, on Windows or on macOS. The macOS build's assemblies have not been inspected, and presence status is expected to be unavailable there because this DLL references the Windows Steamworks assembly. Try it on a throwaway scenario first.

It changes pacing on the machine it runs on. It sends no game network messages, skips no acknowledgements, and does not touch rule code or random numbers. The host still waits for the slowest client, so everyone needs it for the full benefit. A mixed group should work, but that is untested too.

## Build

Needs the .NET 8 SDK and the BepInEx zip extracted at `../downloads/bepinex/extracted`.

    dotnet build -c Release
    dotnet ../tools/verify-targets/bin/Debug/net8.0/verify-targets.dll bin/Release/GloomSpeed.dll "$HOME/.local/share/Steam/steamapps/common/Gloomhaven/GH_Data/Managed"

Set `-p:GameManaged=...` or `-p:BepInExCore=...` if your paths differ. The build references game assemblies and copies none of them. Share only `GloomSpeed.dll`.

## Install

Players should use the [install guide](../../INSTALL.md). These are the manual steps for Proton on Linux; `../install/gloomspeed.sh` automates them. Each player does this, with the game closed.

1. Download `BepInEx_win_x64_5.4.23.6.zip` from https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6 (SHA-256 `6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755`).
2. Extract it into the game folder, so `winhttp.dll` sits next to `GH.exe`. On Linux that is `~/.local/share/Steam/steamapps/common/Gloomhaven`.
3. **Proton only:** in Steam, open Gloomhaven > Properties > Launch Options and set:

       WINEDLLOVERRIDES="winhttp=n,b" %command%

   Without this, Wine uses its own `winhttp` and BepInEx never loads. Windows players skip this step.
4. Start the game once and quit. `BepInEx/plugins` and `BepInEx/config` should now exist. If not, check step 3.
5. Copy `GloomSpeed.dll` to `BepInEx/plugins/`.
6. Start the game and check `BepInEx/LogOutput.log` for `GloomSpeed patches applied: 12, failed: 0` and `GloomSpeed diagnostic patches applied: 6, failed: 0`.

## Config

`BepInEx/config/com.kez.gloomspeed.cfg` appears after the first run with the plugin. Restart the game after changing it.

| Setting | Default | Vanilla equivalent |
|---|---|---|
| `General.Enabled` | true | false |
| `Speed.Multiplier` | 5 | 3 |
| `Speed.EnemyTurnMultiplier` | 6 | 3 |
| `Speed.AlwaysFastForward` | true | false |
| `Speed.KeepSpeedAtEndOfTurn` | true | false |
| `Speed.AllowClientHotkey` | true | false |
| `Pacing.SkipEnemyBanner` | true | false |
| `Pacing.OtherBannerTimeScale` | 0.5 | 1 |
| `Pacing.NoAnimWaitMs` | 200 | 400 |
| `Pacing.AttackModifierTimeScale` | 0.5 | 1 |
| `Pacing.NoPauseDuringEnemyCamera` | true | false |
| `Pacing.CameraFocusSpeedScale` | 1.5 | 1 |
| `Logging.DisableTweenDebug` | true | false |
| `Logging.QuietUnityLog` | true | false |
| `Logging.QuietRuleLog` | true | false |
| `Diagnostics.AckTimingLog` | true | n/a |
| `Diagnostics.PresenceStatus` | true | n/a |
| `Diagnostics.PresenceOverlay` | true | n/a |

If something misbehaves, set `General.Enabled` to false first. Then turn features on one at a time. Set the three `Logging` values to false before reporting a bug, because they remove most of `Player.log`.

## Diagnostics

Both work with `General.Enabled` off, so an unmodified session can be measured.

**Acknowledgement timing (host only).** Other players don't need the mod. The host records when each player reaches the end of each ability, turn and round, and writes `BepInEx/GloomSpeed/acks-<time>.csv` and `acks-<time>-summary.txt`. The summary updates every 25 syncs and on exit. It lists each player's median and worst lag behind the first player to arrive, and the total time they alone made everyone wait. Arrival times are taken on the host, so they include network delay. These files contain player names. Only players controlling a hero are waited on, so anyone without one won't appear as holding the group up.

**Presence status (Steam).** Each modded player publishes `version;on|off;multiplier;enemyMultiplier` through Steam Rich Presence, outside the game's network traffic. During multiplayer the mod reads other players' values, shows them in a small on-screen box, and logs changes to `BepInEx/LogOutput.log`. Steam exposes these values only for friends, so "nothing reported" doesn't prove someone lacks the mod. It can't see the game's own Fast Forward setting.

## Uninstall

- **Plugin only:** delete `BepInEx/plugins/GloomSpeed.dll`.
- **Everything:** with the game closed, delete `BepInEx/`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt` from the game folder, then clear the Steam launch option.
- With `AlwaysFastForward` on, the game's Fast Forward option can be saved as on. Turn it off in the game's settings if you want it back off.

## Game updates and official mods

- A game update replaces `GH_Data/Managed` but should leave the BepInEx files alone. Each feature is patched separately, so a renamed method turns off that feature and logs an error. Rerun `verify-targets` after an update.
- All players must be on the same game version, as always (`GHNetworkCallbacks.CheckVersions`).
- The official mod system (`SteamMods`) loads YML rule data into a separate modded ruleset and contains no code loading, so it can't do what this plugin does. This plugin doesn't use it, so it should not set the game's `IsModded` flag. Neither fact was tested in-game.
