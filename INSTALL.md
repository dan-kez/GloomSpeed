# Install GloomSpeed

GloomSpeed makes Gloomhaven's animations and enemy turns faster. It changes pacing only, not the rules, and it doesn't change your saves. Removing it is just deleting the files it added.

You choose your speed in the game, in **Options > General > Game Speed**:

| Option | What it does |
|---|---|
| **Normal** | The game at normal speed |
| **Fast Forward** | The game's own Fast Forward, as it ships |
| **Faster** | The same speed, without enemy banners and other fixed delays |
| **Fastest** | The default: faster animations too, especially on enemy turns |

Your choice applies immediately. This row replaces the game's Auto Fast Forward Animations row, which comes back if you remove the mod.

The same tab also has **Skip Intro Logos** and **Skip Epic Login**, both off by default and applied on your next launch. Skipping the Epic login means Epic friends and invites stay unavailable. Steam multiplayer doesn't need it.

During a session, the in-game **Multiplayer** menu has a **GloomSpeed** section listing each player's mod version and Game Speed. A warning appears if something doesn't match. Players show as **Can't determine** if they aren't your Steam friend or Steam hasn't shared their status yet.

**Everyone in your group should install it.** The host waits for the slowest player, so one player without it can hold up the group.

> **This is an early test version.** It has been played solo on Linux and on one real Mac, and one Mac test ended in a system crash whose cause isn't known. Nothing has been run on a real Windows PC or in a session with another player. Expect rough edges, and tell Kez what happens.

**The manual steps below don't run any scripts or download anything in the background.** The zip contains everything: the mod and [BepInEx](https://github.com/BepInEx/BepInEx), a standard mod loader. All you do is unzip it into your game folder.

## Before you start

1. Close Gloomhaven.
2. Let Steam finish any Gloomhaven updates. Everyone must be on the same game version.
3. **Optional but sensible:** copy your saves somewhere safe.
   - **Windows:** `%USERPROFILE%\AppData\LocalLow\FlamingFowlStudios\Gloomhaven\GloomSaves`
   - **macOS:** `~/Library/Application Support/unity.FlamingFowlStudios.Gloomhaven/GloomSaves`
4. Find your game folder: in Steam, right-click **Gloomhaven** > **Manage** > **Browse local files**.
5. Download the zip for your computer from the [Releases page](https://github.com/dan-kez/gloomspeed/releases), or get it from Kez.

---

## Windows

### Install

1. Download **`GloomSpeed-0.3.0-Windows.zip`**.
2. Right-click the zip and choose **Extract All...**
3. Set the destination to your **game folder**, the one containing `GH.exe`, and click **Extract**. To get the path, click the address bar of the game folder window and copy it.
4. Check that `winhttp.dll` and a `BepInEx` folder are now right next to `GH.exe`. If they ended up inside a new `GloomSpeed` folder, move them out.
5. Start Gloomhaven from Steam as normal. No launch options are needed.

### Check it worked

Reach the main menu and quit. Open `BepInEx\LogOutput.log` from your game folder in Notepad. It should contain:

```
GloomSpeed patches applied: 17, failed: 0.
```

If there's no `LogOutput.log`, the mod loader didn't start. Check step 4.

### Update

1. Close the game and download **`GloomSpeed-<version>-Update.zip`**.
2. Extract it into your **game folder** in the same way, and choose **Replace** if asked.

Your settings are kept. The update only contains `BepInEx\plugins\GloomSpeed.dll`.

### Remove

Close the game and delete these from your game folder:

- the `BepInEx` folder
- `winhttp.dll`
- `doorstop_config.ini`
- `.doorstop_version`
- `changelog.txt`

---

## macOS

This takes a few Terminal commands, because macOS blocks downloaded mod files until you allow them.

### Install

1. Download **`GloomSpeed-0.3.0-macOS.zip`** and double-click it. This creates a folder of files.
2. Open that folder and your **game folder** side by side in Finder.
3. Move everything from the unzipped folder into the game folder, so that `run_bepinex.sh` and the `BepInEx` folder sit next to `GH_OSX.app`.
4. Open **Terminal** (press Cmd+Space, type `Terminal`, press Return). Type `cd` and a **space**, drag the game folder into the Terminal window, and press Return.
5. Allow the mod files to run. Paste each line and press Return:
   ```
   chmod u+x run_bepinex.sh
   xattr -dr com.apple.quarantine run_bepinex.sh libdoorstop.dylib BepInEx
   ```
6. Print and copy your Steam launch option:
   ```
   printf '"%s/run_bepinex.sh" %%command%%' "$PWD" | tee /dev/tty | pbcopy
   ```
7. In Steam, right-click **Gloomhaven** > **Properties** > **General**, click in **Launch Options**, and paste.
8. Start Gloomhaven from Steam as normal.

### Check it worked

Reach the main menu and quit. Open `BepInEx/LogOutput.log` from your game folder in TextEdit. It should contain:

```
GloomSpeed patches applied: 17, failed: 0.
```

If there's no `LogOutput.log`, check the Launch Options from step 7.

### Update

1. Close the game, download **`GloomSpeed-<version>-Update.zip`**, and double-click it.
2. In the unzipped folder, open `BepInEx/plugins` and find `GloomSpeed.dll`.
3. Drag that one file into `BepInEx/plugins` in your game folder and choose **Replace**.

Your settings are kept. Move only that file: dragging the whole `BepInEx` folder in Finder can replace your existing folder and lose your settings.

### Remove

1. Close the game and delete these from your game folder: the `BepInEx` folder, `run_bepinex.sh`, `libdoorstop.dylib` and `changelog.txt`. There's also a hidden `.doorstop_version` file if you moved it; it does nothing on its own.
2. In Steam, clear **Launch Options** for Gloomhaven. **The game won't start properly until you do.**

---

## If something goes wrong

| Problem | What to do |
|---|---|
| The game won't start or behaves oddly | Follow the **Remove** steps. Your game is then back to normal. |
| The Game Speed row is missing | The original Auto Fast Forward row stays in its place. Tell Kez and send `BepInEx/LogOutput.log`. |
| The check shows `failed:` above 0 | The mod still works, with that feature turned off. Tell Kez. |
| No `.app` in the Mac game folder | Your copy may not be the macOS version. Tell Kez. |
| A desync in multiplayer | Tell Kez when it happened and send `BepInEx/LogOutput.log`. |
| A game file seems damaged | In Steam: right-click Gloomhaven > **Properties** > **Installed Files** > **Verify integrity**. This doesn't remove the mod's files. |

## After a game update

A Gloomhaven update can turn off parts of the mod. Everyone should update the game and then repeat the check above.

## Prefer a script?

**`GloomSpeed-0.3.0-Installer.zip`** does the same job with one step, and it also backs up your saves. Unzip it first. Don't mix it with a manual install: if you've already installed by hand, remove that first.

It downloads BepInEx from its official GitHub release and verifies the checksum.

- **Windows:** double-click `Windows - Install.bat`. If you see "Windows protected your PC", click **More info**, then **Run anyway**. Run it again after launching the game to check it worked. Remove with `Windows - Uninstall.bat`.
- **macOS:** in Terminal, type `bash` and a space, drag in `macos.sh`, type a space and `install`, and press Return. Then paste the Steam launch option it copies for you. Remove by running it with `uninstall`, then clear the launch option.

## What it adds to your computer

- BepInEx, in your game folder. Its licences and those of the libraries it includes are in `BepInEx/GloomSpeed-THIRD-PARTY-NOTICES.txt`.
- `GloomSpeed.dll`, the mod itself, in `BepInEx/plugins`.
- On Mac, the Steam launch option, which starts the mod loader.
- While playing online, it shares your mod version and Game Speed choice with Steam friends, so the group can see who has it.
- If you host, it writes a local log in `BepInEx/GloomSpeed` of when each player finished each step, including player names. It isn't uploaded anywhere.
