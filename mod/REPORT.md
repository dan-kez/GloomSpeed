# Gloomhaven Fast Forward and multiplayer pacing

Game: Steam 780290, Unity 2021.3.5f1, Mono. The assemblies are not obfuscated.

Labels:
- **[confirmed]** read from decompiled code, serialized game assets, or the bundled session log.
- **[inferred]** my reasoning or arithmetic from those.

File references are relative to `decompiled/`, which is local only. Line numbers are from ilspycmd 8.2 output.

The serialized values were dumped from the game's asset files with `tools/assetdump`. The session log is `Player-prev.log` from 29 Oct: Kez hosting 3 clients, 12 start-of-round banners, and roughly 74 minutes if its first timestamp and the file time share a timezone.

## Summary

1. **Fast Forward is local to each player and is a 3x multiplier.** It is a game-clock setting, not `Time.timeScale`, so it only reaches actor animators and coroutines on that clock. Banners, camera moves, attack modifier reveals and choreographer timers run in real time. [confirmed]
2. **The host waits with no timeout for every client after each ability, turn and round.** A client's ack waits on its own animations, so one player with Fast Forward off slows all four. Have everyone check this setting first; it costs nothing. [confirmed]
3. **The `timeout on :null` lines are false alarms.** They are a logging bug and do not mean a 10-second stall. [confirmed]
4. **A mod is viable and can be purely presentational.** The prototype builds and all targets verify, but it has never been run. [confirmed]
5. **Expect a useful but modest gain from the fixed delays.** They total about 3 to 6 minutes of the 74-minute session (table below). The log has no timestamps, so I can't tell how much time went on animation versus player decisions; the baseline run or a timed comparison has to answer that. [inferred]

## 1. How Fast Forward works

**What toggles it** [confirmed]

| Source | Reference | Notes |
|---|---|---|
| Options switch | `Gloomhaven/GeneralSettings.cs:77`, `Gloomhaven/ControlsSettings.cs:275` | Saved locally in `GlobalData.m_SpeedUpToggle` |
| `SKIP_ATTACK` key | `Choreographer.cs:2352` | Toggles it; hold behaviour in `AnimationScenarioState.cs:48-58` |
| `TOGGLE_SPEED` key | `SpeedUpButton.cs:11-26` | **Host only when online** |

**Where the value goes** [confirmed]

1. `GlobalData.SpeedUpToggle` only arms it (`GlobalData.cs:265`). The game then calls `StartSpeedUp` and `StopSpeedUp` by state, mostly every frame, from `Choreographer.Update` (`Choreographer.cs:2410-2960`).
2. `StartSpeedUp` sets `TimeManager.DefaultTimeScale` to `SceneController.GameSpeedIncreaseAmount` (`GlobalData.cs:630-641`).
3. That sets `Timekeeper.instance.m_GlobalClock.localTimeScale`, the Chronos clock (`TimeManager.cs:23-38`).
4. `GameSpeedChanged` fires, and `Choreographer.SetAnimSpeed` sets `animator.speed` to the clock's scale for each actor (`Choreographer.cs:3026-3047`). About 20 animator state behaviours do the same on state entry. `IdleSMB` uses `1/amount` for non-moving idle (`IdleSMB.cs:33-41`).

**The multiplier** [confirmed]
- `GameSpeedIncreaseAmount = 3f`, set in `SceneController.Awake` (`SceneController.cs:224`) and verified in IL.
- It has a public setter and **no cap or clamp**. `AutoLogPlayback.cs:41,80` uses 4 for automated tests.
- `c_MinimumAnimSpeed` and `c_AnimSpeedRampScalar` are locomotion blend constants, not caps.
- `NeedSkipAnimation` (tile highlight), `SkipAnim` and `m_skipAnimationState` (ability data) are unrelated to Fast Forward.

**States that force 1x** [confirmed]
- Player turns: `StartTurn` calls `StopSpeedUp` for player actors (`Choreographer.cs:3796`).
- `WaitingForModifierDrawAnim` (`:2588`).
- `WaitingForEndTurnSync`, every frame (`:2768`). This includes the wait for damage labels to fade.
- Waits for player input.

**What it does NOT speed up** [confirmed]

`Time.timeScale` stays 1 in play. It is only written by debug and capture code. So anything on Unity's own time runs at wall-clock speed:

| Not sped up | Why | Reference |
|---|---|---|
| **All LeanTween tweens**, not only `useEstimatedTime:True` ones | `dtActual = Time.deltaTime`, `dtEstimated = Time.unscaledDeltaTime`, and both are real time here | `GH.Runtime.FirstPass/LeanTween.cs:176-177` |
| Phase banners | LeanTween, and they gate the enemy turn | `PhaseBannerHandler.cs`, `Choreographer.cs:3853` |
| Attack modifier reveal | `WaitForSecondsFrozen` uses `realtimeSinceStartup` | `WaitForSecondsFrozen.cs`, `WorldspaceUI/AttackModBar.cs:400-495` |
| Camera moves, which also **pause the game clock** | `unscaledDeltaTime`; `SmartFocus(..., pauseDuringTransition: true)` | `CameraController.cs:1422`, `CameraTargetFocalFollowController.cs:55-73` |
| Choreographer timers | `Environment.TickCount` | `Choreographer.cs:2286` |
| Network action queue, end-of-round compare | MEC `Timing.WaitForSeconds` | `FFSNet/ActionProcessor.cs:256-260` |
| Locomotion blend ramp | `DoTransform(Time.deltaTime)`; the stride itself does scale through the animator | `ActorBehaviour.cs:464` |
| `WaitForSecondsRealtime` users | Card hand animations, etc. | `CardsHandUI.cs:1047-1101`, `MiniAbilityCard.cs:323` |
| SRL thread sleeps | `Thread.Sleep` | Section 3 |

What it does speed up: actor animators and `Timekeeper.WaitForSeconds` coroutines (VFX, projectile timing, damage label fade, host ack polling).

**Not traced:** particle systems outside the Chronos and state-behaviour paths.

## 2. Multiplayer

**Local or synced?** Local. There is no speed `GameActionType` and no network code touches the toggle. Each client saves its own value. [confirmed]

**Does the host block on acks? Yes, with no timeout.** [confirmed]
- `WaitForEndOfTurnComplete`, `WaitForEndOfRoundComplete` and `WaitForEndOfAbilityComplete` (`Choreographer.cs:14211`, `:14281`, `:14413`) loop until every `PlayerRegistry.Participants` entry has acked. The only other exit is the session going offline.
- Then the host sends `...AllPlayersReady`, and everyone proceeds.
- The 30-second check at `:2767` and `:2878` only logs "Stuck at...", and only for a machine's own local wait.
- Polling is `Timekeeper.WaitForSeconds(0.1f)`: on the scaled clock, and stopped while the clock is paused or frozen.

**What triggers a client's ack** [confirmed]

| Ack | Condition (local to that client) | Reference |
|---|---|---|
| Ability | No death banner, SRL and message queues empty, and the actor is gone, idle, or 500ms has passed outside an animator transition | `Choreographer.cs:2741` |
| Turn | **All actors idle, no worldspace flow active**, no buff VFX, doors or gold drops pending, queues empty. Runs at 1x | `:2768-2815` |
| Round | Doors, drops and queues clear; no actor idle check | `:2878-2893` |

Every client also runs the choreographer steps for that ability first, each waiting on its own animation. So **a client without Fast Forward does hold everyone up**, and the group runs at the slowest client's pace. [confirmed from code; the size of the effect isn't measured]

Session counts: 277 ability syncs, 140 turn syncs, 11 round syncs, each with 3 clients. [confirmed from log]

Each sync adds Bolt send batching (60 fps, send rate 3, so a packet about every 50ms), the host's 33 to 100ms poll, and network latency. That is about 0.1 to 0.25s per sync. [settings confirmed; total inferred]

## 3. Timeouts and sleeps

**Durations** [confirmed]
- `WaitingForGeneralAnim`: 10000ms if an animation should play, 400ms if not, and 0 at four call sites (`Choreographer.cs:10474`, `:10517`, `:10556`, `:10595`).
- `WaitingForProgressChoreographer`: 10000ms at all 7 rule-library call sites.
- Both use `Environment.TickCount`, so **they do not scale with game speed**. The animation's `ProgressChoreographer` event normally ends the wait early.

**What causes `:null`** [confirmed]

It is a logging bug. At `Choreographer.cs:2664-2669` and `:2701-2706`, the game sets its wait state to `Play` with no actor, and only then checks the wait state to decide whether to log a timeout.

By then `m_WaitState` is the new `Play` state, whose actor is always null. The warning fires whenever the tick count advanced between those lines, so it does not indicate a real timeout. Real timeouts are not logged here at all. The session had 9 + 7 of these lines.

The move timeout does log, and it fired 0 times against 344 normal arrivals. [confirmed from log]

**`Sleeping SRL Thread`** [confirmed]

| Site | Sleep | Waiting for |
|---|---|---|
| `GameState.cs:1198`, `:1264` | 100ms polls | A **player's** answer: burn a card or avoid damage |
| `CCharacterClass.cs:1062` | 5ms polls | Another thread to finish checking active bonuses |

It logs once per wait, so the total duration is however long the wait lasts. All sampled stacks were the damage-response path, which means it was waiting on a person and is not a game delay. 11 occurrences in the session. The `CScenario` 50ms and `GameState.cs:2493` 10ms sleeps are threading-error retries.

## 4. Fixed delays in the enemy turn

Values are [confirmed]. "Scaled?" means whether Fast Forward shortens it.

| Step | Value | Scaled? | Reference |
|---|---|---|---|
| Enemy banner, **holds the turn**, skipped if same enemy type as last | 0.3s fade in; fade out 0.3s after 1.0s delay; 1.3s total | No | `Choreographer.cs:3853`, level6 animator 6796, session log; window transition is off |
| Camera to off-screen target, clock paused | `m_FocusSpeed` 0.5 or 0.75 by level; about 0.3 to 1s [inferred] | No | `CameraController.cs:1422-1435` |
| Move | Animation; 10000ms then teleport; 400ms if not animated | Stride yes, timeout no | `Choreographer.cs:2485`, `:4399-4449` |
| Attack animation | Animation event; 10000ms; 400ms | Animation yes, timer no | `:5332` |
| Slow-mo | `SlowMoDuration = 0`, so off | n/a | `GlobalSettings` asset, `ActorProgressModifiers.cs:65-77` |
| Modifier reveal start | `AttackModiersRevealTimeout`, per animation; safety 5s | No | `SetProgressModifierCallbackSMB.cs`, `AttackModBar.cs:331`. Value not readable: see below |
| Modifier reveal, per card | 0.15 + 0.1 + 0.2 + 0.3 = 0.75s | No | `AttackModBar` asset config, `AttackModBar.cs:400-409` |
| Modifier replaced | 0.4s plus 0.1s or more | Partly | `:414-420` |
| Rolling modifier | 0.3 + 0.2 + 0.1s; animation may freeze `RollingModDuration` = 1s per extra card | No | `:470-473`, `SetProgressModifierCallbackSMB.cs:26-38` |
| Reveal finish | 0.3s | No | `:489` |
| Damage label fade | 1.3 + 1.0s | Yes, **but 1x once end-of-turn sync starts** | `:521-531`, `:2768` |
| Step with no animation | 400ms | No | About 30 call sites, `SetChoreographerState(..., ? 10000 : 400, ...)` |
| End of ability | 500ms unless already idle | No | `:8309` |
| Initiative track reorder | 0.35s | Yes, cosmetic | `InitiativeTrack.cs:286` |
| Autosave at round start | Until done, 6000ms maximum | No | `:3540`, `:2995` |
| Consecutive queued network actions | **1.0s gap** | No | `FFSNet/ActionProcessor.cs:256`; otherwise 0.05s poll |
| Ready-up ack poll | 0.2s | No | `UIReadyToggle.cs:66` |

**Could not be answered:** `AttackModiersRevealTimeout` is on animator state behaviours that aren't in the asset files I parsed, probably in the Addressables bundles under `StreamingAssets/aa`.

**Rough session budget** [inferred from log counts and the values above]

| Delay | Calculation | Total |
|---|---|---|
| Enemy banners | 29 x 1.3s | ~38s |
| Modifier reveals | 72 cards x 0.75s + 67 x 0.3s | ~75s |
| Camera pauses | 62 x 0.3 to 1s | ~20 to 60s |
| Sync overhead | 428 x 0.1 to 0.25s | ~45 to 105s |
| Queued network action gaps | Up to 91 x 1s | ≤ ~90s |

Not counted: end-of-turn label fades at 1x, 400ms steps (not logged), and whatever animation time clients without Fast Forward add. The log can't show those.

## 5. Logging cost

**Gate** [confirmed]
- `ENABLE_LOGS` is a compile-time `[Conditional]`, and the retail assemblies were built with it on. There is no shipped runtime flag.
- The only runtime off-switch is `UnityEngine.Debug.unityLogger`, which only `CheatPanel.cs:257` touches.
- That filter does not save the string building, because the message is built before the call.

**Per-frame tween logging** [confirmed]
- `LTDescr.updateInternal` at `GH.Runtime.FirstPass/LTDescr.cs:1355` and `:1381` logs once per frame per tween when `debug` is set.
- The only setter is `LeanTweenGUIAnimator.Animate` (`LeanTweenGUIAnimator.cs:50`), copied from the serialized `GUIAnimator.enableDebug`.
- 12 of 955 animators in the scene files ship with it on, including the phase banner in level6 (animator 6796).
- The logged tween names are only `Banner`, `TextMeshPro Text`, `Party Display UI` and `Container`.

**Other routine logging** [confirmed]
- `SimpleLog.AddToSimpleLog` runs a regex and queues a second copy per call.
- `DLLDebug.SendMessage` captures `Environment.StackTrace` on the rule thread for Info messages, then does not use it.
- `FFSNet.Console` logs every action. Bolt logs `FRAME STALL` and `FRAME RESET`.

**Volume, from the session log** [confirmed]

| Lines | Count | Share of 22.5 MB |
|---|---|---|
| Tween lines | 39,418 | 11 MB, 49% |
| Bolt connection lines | 36,830 | ~2 MB |
| `[SIMPLE]` lines | 20,763 | |
| Other `[DLL Log]` lines | 2,119 | |

**Cost** [inferred]: tween logging only runs while a banner or party panel animates, so it won't make the game feel slow overall. It may cause hitches on those frames. Turn it off because it's free and safe, but don't expect it to be the main gain. The actual frame cost is unmeasured.

## Timing, simulation order and random numbers

- Each machine runs the rule library on its own thread, advanced by `StepComplete` and `NextPhase` messages (`ScenarioRuleClient.Work`). Rule state doesn't read game time. [confirmed by searching the rule libraries for time sources; the hits are timestamps and diagnostics]
- Gameplay randomness comes from seeded generators in `ScenarioState` (`ScenarioRNG`, `EnemyAbilityCardRNG`, etc.). Timing changes when a draw happens but not the order of draws within the rule thread. [inferred]
- Two main-thread `ScenarioRNG` draws exist: `LevelEventsController.cs:1221` (scripted spawn) and `CardsHandUI.cs:752`, `:886` (short rest). I found nothing tying them to animation speed, but I did not prove their order relative to rule thread draws under all timings. This is the first thing to look at if a desync appears. [confirmed they exist; safety inferred]
- `SharedClient.GlobalRNG` is drawn by a cosmetic state behaviour (`AnimationOffsetSMB.cs:20`), so its sequence already depends on animation timing. It also supplies new-party and scenario seeds, and `CActor.cs:1836` and `CAbilityCreate.cs:231` use it for rotations. I did not check whether those are compared between machines. [confirmed usage; effect unknown]
- **Scaled-clock timeouts:** `UIReadyToggle.cs:683`, `:875` measure 30s and 20s on the scaled clock, then call `HandleDesync`. At 6x that would be 5s and 3.3s if the clock were still scaled. The code suggests it's 1x by then, because card selection calls `StopSpeedUp`. **Test this first.** [confirmed code; runtime state inferred]
- **Phase timeout:** a client that receives a player action for a phase it hasn't reached retries for 15s, then calls `HandleDesync` (`ActionProcessor.cs:376-392`). Ability acks limit how far apart clients can drift, so a mixed group should stay under this, but it's the second thing to watch. [inferred]

## Mod options, ranked

| # | Option | Benefit | Risk | Who needs it | In prototype |
|---|---|---|---|---|---|
| 0 | **All players turn on Fast Forward. No mod.** | Possibly the largest | None | Everyone | n/a |
| 1 | Skip enemy banner, shorten other banners | ~38s+; also removes hold on each enemy type | Very low: reuses the game's own same-enemy path | Per client | Yes |
| 2 | Scale modifier reveal to 0.5 | ~40s | Low: cosmetic timing; may look abrupt | Per client | Yes |
| 3 | Raise multiplier to 5, enemy turns 6 | Largest mod lever, size unmeasured | Low for sync, medium for visuals at high values. Locomotion ends on overshoot (`ActorBehaviour.cs:580-596`), and a 10s teleport is the backstop | Per client; full gain needs everyone | Yes |
| 4 | Keep speed during end-of-turn sync | Label fades, faster host polling | Low; speed is restored on leaving the state | Per client | Yes |
| 5 | Force Fast Forward on; let clients use the hotkey | Prevents option 0 slipping | Very low | Everyone | Yes |
| 6 | No clock pause on enemy camera moves, faster focus | ~20 to 60s | Low: action may start before the camera arrives | Per client | Yes |
| 7 | No-animation wait 400 to 200ms | Unmeasured | Low to medium: steps may overlap visually | Per client | Yes |
| 8 | Logging off | Small | None for play; loses diagnostics | Per client | Yes |
| 9 | Reduce 1.0s gap between queued network actions | ≤ ~90s | **Medium, desync risk unknown.** The reason for the gap isn't documented | Every client's own queue | No |
| 10 | Shorten 10000ms safety timeouts | None found: real timeouts didn't fire | Medium: cuts off slow animations | | No |
| 11 | Host-controlled speed | Same as 5 | **All players need it.** A new message type on an unmodded client is likely to hit `HandleDesync` [inferred]. Use option 5 | Everyone | No |
| 12 | **Skip or time out acks. DESYNC RISK: not recommended** | ~45 to 105s | **High.** Clients would fall behind the host, and the 15s phase timeout would then raise a desync | Everyone | No |

Tuning Bolt dejitter and send rates is also out: it changes network behaviour for everyone and I didn't trace its effect on event delivery.

Options 1 to 8 only change local pacing, send nothing, and keep every ack. A mixed group should therefore sync, but runs at the slowest client's pace. [inferred; untested]

## Prototype

Path: `GloomSpeed/`. Source is in `src/Plugin.cs` and `src/Patches.cs`. Install, config and uninstall steps are in `GloomSpeed/README.md`.

**Build:** `dotnet build -c Release` succeeds with 0 warnings and 0 errors, producing `GloomSpeed/bin/Release/GloomSpeed.dll`. It references the game's assemblies and BepInEx 5.4.23.6 with `Private=false`, so nothing is copied. [confirmed]

**Target verification:** `tools/verify-targets` reads the built plugin's `[HarmonyPatch]` attributes and checks them against the game's assemblies. Result: all targets verified. [confirmed]
- All 12 patch classes have one unique matching method.
- Hook parameters match by name and type.
- All 9 fields reached by name or used directly exist with the right type.
- All targets are over Mono's 20-byte inline limit. This check caught one, `TurnEndSyncedAndReadyToProceed` at 18 bytes, and I moved that patch to `SetChoreographerState`. The limit is from my knowledge of Mono, not from this build.

**Downloads:** BepInEx came from the official GitHub release, and its SHA-256 matched the digest GitHub lists for the asset. [confirmed]

**Updates and official mods** (details in the README)
- Each patch class applies separately, and private field access is checked first, so an update disables one feature and logs it. [confirmed in code; never exercised]
- The official `SteamMods` system loads YML rule data (`GHMod.cs:40-71`), with no code loading in `GH.Runtime`, and marks saves `IsModded`. It can't change these delays, and BepInEx doesn't interact with it. [confirmed for the system; no interaction is inferred]

## Untested

**Nothing has been run in-game.** Not installed, not loaded, and BepInEx itself hasn't been tried on this game under Proton. In particular:

1. BepInEx loading under Proton, and all patches applying (expect `patches applied: 12, failed: 0`).
2. Any multiplayer session, modded or mixed. Check for desync errors over several rounds.
3. Ready-up after enemy turns, because of the scaled-clock timeout above.
4. Visuals at 5x and 6x: projectiles, VFX, movement, damage labels.
5. Modifier reveal at 0.5 alongside the unreadable `AttackModiersRevealTimeout`, including rolling modifiers.
6. Whether skipping the enemy banner leaves any UI waiting on it. The same-enemy path ran 34 times in the session, which is reassuring.
7. Real time saved. Compare a timed enemy round against the baseline with the same party and scenario.

Suggested order: solo scenario with all defaults, then one friend, then all four.

## Tools installed

- .NET SDK 8.0.427 in `~/.dotnet`.
- ilspycmd 8.2 in `~/.dotnet/tools`. It targets .NET 6, so run it with `DOTNET_ROLL_FORWARD=Major`.
- NuGet packages in `~/.nuget`.
- Local helpers: `tools/assetdump` and `tools/verify-targets`.

I only read the game folder and Proton prefix. `decompiled/` and `downloads/` are excluded by `.gitignore`.
