# GloomSpeed

A BepInEx mod that shortens presentation delays in Gloomhaven (Steam, Unity 2021.3.5 Mono), plus the tools used to measure them.

![Side-by-side clip of the same enemy phase. Fast Forward alone takes 10.83 seconds; GloomSpeed takes 4.80 seconds.](docs/media/speedup-side-by-side.gif)

**The same enemy phase, side by side:** five enemies each apply Shield. Fast Forward alone takes 10.8s, and GloomSpeed takes 4.8s (2.27x). The footage isn't retimed; the GloomSpeed side only freezes once it has finished. ([MP4 version](docs/media/speedup-side-by-side.mp4))

**This is close to the best case, not the typical result.** Rounds with self-buffs gain the most, up to 3.5x. Attack-heavy rounds gain 1.2x to 1.7x. Across 13 matched solo enemy phases the mod was 1.88x faster overall. Multiplayer hasn't been measured. Details are in [the test report](mod/test/REPORT.md).

## Install

**[Follow the install guide for Windows and macOS.](INSTALL.md)** You don't need anything else in this repository. Downloads are on the [Releases page](https://github.com/dan-kez/gloomspeed/releases).

| | Windows | macOS |
|---|---|---|
| Install | Unzip into the game folder | Unzip into the game folder, run a few Terminal commands, and paste one Steam launch option |
| Update | Unzip the update into the game folder | Replace one file |
| Remove | Delete the files it added | Delete the files it added and clear the launch option |

Nothing downloads in the background on this path, and it doesn't change your saves or mark them as modded. A one-step scripted installer is also available.

**Everyone in your group should install it.** The host waits for the slowest player, so one player without it can hold up the group.

## What it does

In the game, under **Options > General**:

| Setting | What it does |
|---|---|
| **Game Speed** | **Normal**, **Fast Forward** (the game's own), **Faster** (the same speed, without enemy banners and other fixed delays), or **Fastest** (the default: 5x, 6x on enemy turns, plus shorter reveals). Applies immediately, and replaces the game's Auto Fast Forward row. |
| **Skip Intro Logos** | Skips the startup video and logos. Off by default. |
| **Skip Epic Login** | Stops the Epic sign-in window opening at startup. Off by default. Epic friends and invites stay unavailable, but Steam multiplayer doesn't need them. |

During multiplayer:

- A **GloomSpeed** section in the in-game **Multiplayer** menu lists each player's mod version and Game Speed.
- A warning appears on screen if someone's version or speed differs, or their status can't be read. Steam only shares this between friends.
- The host's game can write a log of how long each player takes to finish each step, in `BepInEx/GloomSpeed`. It's stored locally, includes player names, and isn't uploaded.

The game's Skip Attack key no longer changes speed.

## Status

**This is an early test version.**

| | Status |
|---|---|
| Linux (Proton) | Played solo |
| macOS | Played solo on one Mac. One run on an earlier build ended in a system crash, cause unknown ([details below](#known-issues)) |
| Windows | Not yet run on a real PC |
| Multiplayer | Hosted solo only, so not yet tested with another player. The main risk is desyncs |

It changes pacing only. It doesn't touch rule code, skip acknowledgements or send game network messages, but that hasn't been proven in a session with other players yet. Consider trying a throwaway campaign first.

### Known issues

- **macOS crash:** a Mac froze after opening the General tab, then restarted when its display service (WindowServer) stopped responding. It hasn't been retested on a Mac since, and the cause is unknown, so the mod can't be ruled out.
- **Faster and Fastest on attacks:** attack sequences gain little, and can take slightly longer when damage prompts are answered quickly.
- **Scrollbar:** the multiplayer menu gains a scrollbar when the GloomSpeed section appears, which narrows its rows slightly.

Please [open an issue](https://github.com/dan-kez/gloomspeed/issues) if you hit something, with `BepInEx/LogOutput.log` from your game folder.

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

Game code, game assemblies, BepInEx, saves and session logs are deliberately not in this repository. See `.gitignore`. Release zips bundle BepInEx, with its licences included.

## Disclaimer

This is an unofficial fan project, not affiliated with or endorsed by Flaming Fowl Studios, Twin Sails Interactive, Asmodee or Cephalofair Games. You need your own copy of Gloomhaven. Use it at your own risk.
