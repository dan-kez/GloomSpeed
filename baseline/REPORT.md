# Gloomhaven solo enemy-turn baseline (1 Nov 2026)

Saved from the baseline agent's final report. Labels: [M] measured from timestamped logs or samplers, [I] inferred.

## Result
In solo play, enemy-turn slowness is not a performance problem. Nearly all the time goes to fixed-length presentation, and Fast Forward only shortens part of it. This run can't speak to network waits: `ClientReady` appeared 0 times in all three solo logs, against 2,574 and 4,182 in the two backed-up multiplayer logs.

## Backup
- Path: `~/code/gloomhaven-perf/backup-20261101-214809/`
- Contents: 45 files covering GloomSaves, GloomSavesOpenBeta, both logs, `user.reg` and the Steam userdata folder for 780290.
- Verification: `diff -r`/`cmp` matched the live files before launch, SHA-256 matched, and the backup re-verified at the end.

## Enemy timings
"Fast Forward" here is **Options > General > Auto Fast Forward Animations**. "On" is the user's normal setting (Enabled). All values are seconds and exclude the time the script took to answer damage prompts. 8 display-off turns are excluded. [M]

| Component | Off: median [range] (n) | On: median [range] (n) | Effect |
|---|---|---|---|
| Whole enemy turn, mixed content | 5.31 [0.50-6.65] (18) | 3.04 [0.22-6.47] (25) | Content differs, see matched rounds |
| Turn banner, when shown | 1.29 [1.29-1.30] (12) | 1.29 [1.29-1.29] (17) | None |
| Move, 1 hex | 0.441 [0.433-0.474] (14) | 0.158 [0.150-0.159] (10) | 2.8x faster |
| Move, 2 hexes (0.58 / 0.29 per hex) | 1.166 [1.165-1.166] (3) | 0.583 (3) | 2.0x faster |
| Move, 3 hexes | no sample | 0.923 (2), display off only | |
| Attack with damage, whole sequence | 4.08 [3.77-4.40] (10) | 2.26 [1.90-2.65] (14) | 1.8x faster |
| Modifier display, any attack | 1.73 [1.09-1.94] (11) | 1.10 [1.09-1.43] (17) | Single draw: 1.09 in both |
| Damage label | 2.30 [2.28-2.31] (10) | 0.767 [0.766-0.775] (14) | 3.0x faster |
| Strengthen self-buff, extra time | 4.22 [4.22-4.23] (3) | 1.47 [1.46-1.49] (3) | 2.9x faster |
| Shield self-buff, extra time | no sample | 1.44 [1.44-1.81] (3) | |
| Hero death, extra time | no sample | 2.36 [2.35-2.37] (3) | |
| Gap, one enemy's end to next start (plain turns) | 0.142 [0.134-0.150] (9) | 0.109 [0.108-0.275] (9) | |
| AI pathing | 0.026 [0.002-0.107] (18) | 0.017 [0.002-0.048] (19) | Negligible |
| Whole phase, 3 enemies | 16.99 [4.46-17.62] (6) | 9.59 [7.78-13.02] (8) | Content differs |

The third enemy each round showed no banner in every sample. These rounds can't show whether that is because it shares a class with the previous enemy or because it acts last.

### Share of enemy-turn time [M]
| Bucket | Off | On |
|---|---|---|
| Attack sequences | 51.7% | 43.4% |
| Banners | 18.8% | 27.7% |
| Self-buffs, deaths, other | 17.9% | 24.7% |
| Move animation | 11.7% | 4.2% |

### Matched rounds, 3 enemies [M]
| Comparison | Off | On | Ratio | How well matched |
|---|---|---|---|---|
| Round 5, via Restart Round | 12.23 | 8.16 | 1.50x | Same actions, draws and targets |
| Strengthen plus 1-hex moves | 16.88 | 7.78 | 2.17x | Same action types, different attempts |
| Move plus attack | 17.50, 17.62 | 11.55 | About 1.5x | Same action types, draws differ |

## Idle versus animation
- No unexplained idle time: logged events account for nearly all of each turn, and plain gaps between enemies are about 0.11-0.15 s [M].
- The large blocks look deliberate [I]: the banner (tweens with a 1 s delay and 0.3 s duration), modifier cards (with a logged time-scale freeze) and the damage label.
- Longer gaps of 0.2-2.4 s followed turns with self-buffs, up to 2.37 s after Shield and Retaliate. These look like the rest of that ability [I].

## Autosave and timeouts
- Autosave runs at round start, not during enemy turns. It blocks for a median of 17 ms [9-24], n=14, with the checkpoint log line at 97 ms [75-157] [M]. The test save is under 1 MB, so real campaigns may take longer [I].
- `WaitingForGeneralAnim` occurred 0 times. `WaitingForProgressChoreographer timeout on :null` occurred once in 51 enemy turns, and never in the 43 turns with live rendering [M].
- That one timeout fell in the turn that overlapped the display switching off, and added about 4.67 s (6.11 s against 1.44 s for an equivalent turn) [M].
- The backed-up multiplayer logs have 9 + 7 and 24 + 15 of these timeouts (general + progress) [M]. At about 5 s each they would be a real but bounded cost [I, n=1]. A client that isn't rendering might trigger them, but that is a weak hypothesis from one event [I].

## CPU, GPU and frame time
Sampled at 1 Hz during enemy turns with the display on (off n=110, on n=99) [M].

| Metric | Off | On |
|---|---|---|
| Main thread, % of one core, median [range] | 57 [41-73] | 56 [49-66] |
| All threads, % of one core | 225 | 216 |
| Unity graphics worker, % of one core | 29 | 27 |
| GPU utilisation | 24% | 25% |
| GPU power | 156 W | 177 W |
| VRAM | 5.8 GB | 6.0 GB |

- Frame time: median 8.0 ms, p99 9 ms, max 12 ms, none over 16.7 ms in 3,808 enemy-turn frames [M]. That is the 120 fps cap.
- Limit: the sample comes from banner tweens only, so moves and attacks weren't sampled this way.
- MangoHud was not used. It isn't installed and sudo needs a password.
- Rendering doesn't set the pace: Round 9 with identical actions took 9.915 s with the display off and 9.848 s with it on [M].

## Log timestamps
Unity writes the log line by line, with no bursts. Against the game's own EOS timestamps (n=200), lag was median 1 ms, p99 16 ms, max 18 ms [M]. Stamps are good to about one or two frames.

## Other findings
- Display sleep killed rendering twice. KDE's PowerDevil turns displays off after 300 s idle, and the synthetic input evidently didn't count as activity.
  - Run 2: displays off, frozen frame and GPU at 0% from about 22:19:52. Waking them logged NVIDIA Xid 32 against `GH.exe` at 22:22:46, and rendering never recovered [M].
  - Run 1: same freeze from about 22:07:11, Xid 13/32 at 22:07:21 [M]; display sleep as the cause there is [I].
  - Game logic kept running blind. Both times the process hung after quitting and needed TERM. With the display held on, run 3 exited cleanly in about 2 s [M].
  - A host idle for 5 minutes could hit this [I]. The journal's last 60 days show no earlier `GH.exe` Xid [M].
- Power profile: `power-saver` is active, set in KDE's AC settings [M]. It caused no CPU limit here [I].
- Determinism: enemy actions replay identically after a reload or Restart Round [M], which is useful for later A/B tests.

## Caveats
- Solo only: no network, acks or multiplayer behaviour.
- Limited content: 2 heroes and only Bandit Guards in room 1, on Friendly difficulty. No ranged enemies, doors, summons or bosses.
- Sample: 18 off, 25 on, 8 display-off turns. Off and on were not randomised, though repeated rounds were stable to about 0.1 s.
- Prompts: the script answered damage prompts about 0.3-0.4 s after the label finished; two waited longer (25 s, 17 s). All excluded, so real players add thinking time per prompt.
- Hero turns were skipped, so nothing here measures player actions.
- The process ran under Proton Experimental.

## Test campaign
`~/.local/share/Steam/steamapps/compatdata/780290/pfx/drive_c/users/steamuser/AppData/LocalLow/FlamingFowlStudios/Gloomhaven/GloomSaves/Campaign/Campaign_Baselinetest_<steam-id>/`

4.8 MB. Guild "Baselinetest", mercs TestA (Brute) and TestB (Cragheart), mid-way through a retry of Black Barrow.

## What's left changed
- Auto Fast Forward was toggled during testing and restored to Enabled, confirmed on screen.
- Bwehpies, Bob's Boigers, Bwehbians and Redeemers are byte-identical to the backup.
- `GlobalData.dat` differs from the backup because the game rewrites it. The main menu's Continue may now open Baselinetest [I].
- `user.reg`: in the game's section, only the session counter and session ID differ.
- `Player.log` and `Player-prev.log` now hold runs 3 and 2. The originals, including the 29 Oct log, exist only in the backup.
- Steam Cloud: `remotecache.vdf` changed and now lists Baselinetest, so it may sync.
- Game, capture processes and the temporary idle inhibitor are stopped. The existing `nosleep-toggle` inhibitor was left alone.
- Not changed: game files, Steam launch options, power settings. No packages were installed.

## Files
`turns.csv`, `phases.csv`, `stats.json`, `frames.json`, `notes.txt`, `run-{1,2,3}.log`, `.Player.raw.log`, `.top.log`, `.gpu.csv`, `.size.log`, `tools/analyze.py`, `tools/frames.py`, `shots/`
