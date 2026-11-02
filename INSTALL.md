# Install GloomSpeed

GloomSpeed makes Gloomhaven's animations and enemy turns faster. It changes pacing only, not the rules, and it never changes your saves. It can be completely removed. The installer backs up your saves first anyway.

**Everyone in your group should install it.** The host waits for the slowest player, so one player without it can hold up the group.

> **This is an early test version.** Earlier builds have only been run on Linux, in solo play. Nothing has been run on a real Windows PC or Mac, or in multiplayer. On Mac, some features may be missing, and the mod might not load at all. Expect rough edges, and tell Kez what happens.

## Before you start

1. Close Gloomhaven.
2. Make sure everyone is on the same game version: let Steam finish any Gloomhaven updates.
3. Download **GloomSpeed-0.2.0.zip** from the [Releases page](https://github.com/dan-kez/gloomspeed/releases), or get it directly from Kez.
4. Unzip it. Don't run anything from inside the zip.

Then follow the steps for your computer.

---

## Windows

**Install**

1. Open the unzipped `GloomSpeed-0.2.0` folder.
2. Double-click **`Windows - Install.bat`**.
   - If you see "Windows protected your PC", click **More info**, then **Run anyway**.
3. When it says **Installed**, press any key to close the window.
4. Start Gloomhaven from Steam as normal.

**Check it worked**

1. Reach the main menu, then quit the game.
2. Double-click **`Windows - Install.bat`** again. It should show:
   ```
   GloomSpeed patches applied: 12, failed: 0.
   ```

**Remove it**

Close the game and double-click **`Windows - Uninstall.bat`**.

---

## macOS

**Install**

1. Open **Terminal** (press Cmd+Space, type `Terminal`, press Return).
2. Type `bash` and then a **space**. Don't press Return yet.
3. In Finder, open the unzipped `GloomSpeed-0.2.0` folder and drag **`macos.sh`** into the Terminal window.
4. Type a **space** and then `install`, so it looks something like this, then press Return:
   ```
   bash /Users/you/Downloads/GloomSpeed-0.2.0/macos.sh install
   ```
5. The installer prints a line starting with `"/Users/` and copies it to your clipboard. **You need this for the next step.**
6. In Steam, right-click **Gloomhaven** > **Properties** > **General**, click in **Launch Options**, and paste.
7. Start Gloomhaven from Steam as normal.

**Check it worked**

1. Reach the main menu, then quit the game.
2. Run the same install command in Terminal again. It should show:
   ```
   GloomSpeed patches applied: 12, failed: 0.
   ```

**Remove it**

1. Close the game and run the same command with `uninstall` instead of `install`.
2. In Steam, clear **Launch Options** for Gloomhaven. The game won't start properly until you do.

---

## If something goes wrong

| Problem | What to do |
|---|---|
| The game won't start or behaves oddly | Run the uninstaller. On Mac, also clear the Launch Options. |
| "Could not find Gloomhaven automatically" | In Steam, right-click Gloomhaven > **Manage** > **Browse local files**, then give the installer that folder. |
| "Gloomhaven is running" | Close the game, then try again. |
| The check shows `failed:` above 0 | The mod still works, with that feature turned off. Tell Kez. |
| Mac: "no .app found" | Your copy may not be the macOS version. Tell Kez. |
| A desync in multiplayer | Tell Kez when it happened and send `BepInEx/LogOutput.log` from your game folder. |

To undo a save problem, your backup is in your Documents folder, named `Gloomhaven-save-backup-` followed by the date.

## After a game update

A Gloomhaven update can turn off parts of the mod. Everyone should update the game and then run the installer's check again.

## What it does on your computer

- Adds [BepInEx](https://github.com/BepInEx/BepInEx), a standard mod loader, to your game folder. The installer downloads it from BepInEx's official release and verifies its checksum.
- Adds `GloomSpeed.dll`, the mod itself.
- On Mac, also needs the Steam launch option above to start the mod loader.
- While playing online, shares your mod version and speed settings with Steam friends so the group can see who has it.
- If you host, writes a local log of when each player finished each step, including player names. It isn't uploaded anywhere.
