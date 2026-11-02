# GloomSpeed 0.1.0 solo test against vanilla (2 Nov 2026)

Saved from the test agent's final report. Labels: [M] measured, [I] inferred.

GloomSpeed works in solo with no softlocks, and enemy phases ran about 1.9x faster than vanilla with Fast Forward on. This can't show anything about multiplayer, acknowledgements or desyncs: it was all solo, with no clients and no network.

## How it was measured
- Logging.* false in measured runs; real play would have them on. Auto Fast Forward Enabled in both conditions. Control was `General.Enabled=false`; the mod used defaults.
- Each pair quits and reloads the same round checkpoint, except the two R5 reps, which used Restart Round.
- All 13 pairs had the same turn order, moves, draws, damage, targets and buffs, compared line by line. [M]
- Times are the enemy phase only, first enemy turn to end of the last, excluding the script's prompt answers (about 0.4s each). The exception is R7: the killing blow's prompt couldn't be excluded in either condition.

## Whole enemy phases [M]
Section A has 3 enemies (room 1). Section B has 6 enemies, after the door.

| Round | Off (s) | On (s) | Speedup | Content |
|---|---|---|---|---|
| A attempt 2, R5 rep 1 | 8.42 | 6.24 | 1.35x | move, attack |
| A attempt 2, R5 rep 2 | 8.23 | 6.27 | 1.31x | same |
| A attempt 3, R1 | 7.32 | 2.63 | 2.79x | Shield only |
| A R2 | 11.59 | 9.68 | 1.20x | 3 move + attack |
| A R3 | 11.68 | 3.22 | 3.62x | Shield + Retaliate |
| A R4 | 11.66 | 3.25 | 3.59x | Shield + Retaliate |
| B R1 (door) | 11.97 | 9.29 | 1.29x | 3 hits, 2 moves, archer move |
| B R2 | 14.64 | 10.04 | 1.46x | hits, hero death |
| B R3 | 8.64 | 4.50 | 1.92x | moves, 1 hit |
| B R4 | 12.78 | 5.34 | 2.39x | buffs, moves |
| B R5 | 23.69 | 6.69 | 3.54x | Shield + Retaliate on 5 enemies |
| B R6 | 16.54 | 9.94 | 1.66x | moves, melee hit, ranged hit |
| B R7 (3 turns, ends in defeat) | 14.96 | 9.05 | 1.65x | ranged hit, 2 melee |

Totals: A 58.9s to 31.3s (1.88x). B 103.2s to 54.9s (1.88x). Buff rounds gain most (2.4x to 3.6x); attack rounds gain least (1.2x to 1.7x).

## Components, 13 pairs (57 turns per side) [M]
| Component | Off: median [range] (n) | On: median [range] (n) |
|---|---|---|
| Enemy banner | 1.293 [..1.298] (35 shown) | 0: no hold in any of 57 turns |
| Modifier reveal | 1.10 [1.09-1.94] (18) | 0.575 [0.567-1.168] (18) |
| Reveal end to damage | 0.025 [0.016-0.066] (18) | 0.515 [0.016-0.658] (18) |
| Damage label | 0.766 (16) | 0.191 (16) |
| Idle gap, with prompt | 0.096 [0.083-0.224] (14) | 1.175 [1.15-1.20] (14) |
| Attack with damage, whole | 2.03 [1.98-2.82] (14) | 2.50 [2.28-2.62] (14) |
| Move, 1 hex | 0.158 (16) | 0.083 (16) |
| Move, 2 hexes | 0.583 (9) | 0.411 (9) |
| Move, 3 hexes | 0.924 (2) | 0.658 (2) |
| Gap between enemies, all | 0.152 (56) | 0.190 (56) |
| Gap after Shield + Retaliate | 2.37 (7) | 0.475 (7) |
| Start-of-round banner | 1.29 (1 sample) | 0.65 (1 sample) |

The modifier reveal halved, but the attack then waits about 0.5s for the hit, so reveal plus wait is about the same (1.125s vs 1.09s).

## Door and enemy types
- 6 enemies act per round: Bandit Guard Elite, 4 Bandit Guards, Bandit Archer Elite. The archer is the only new type.
- The reveal worked normally with the mod on. [M]
- Door timing, n=1 pair: Open Door click to control returning took 1.98s off and 2.26s on. [M]

Section B per-type figures, turn totals excluding prompts [M]:

| Type and action | Off (s) | On (s) | n |
|---|---|---|---|
| Archer Elite, move only | 2.03 | 0.54 | 5 |
| Archer Elite, ranged attack, stationary (R6) | 3.39 | 2.44 | 1 |
| Archer Elite, 1-hex move + ranged attack (R7) | 3.66 | 2.59 | 1 |
| Guard Elite, attack | 3.40 | 2.60 | 3 |
| Guard Elite, buff | 2.76 | 0.78 | 2 |
| Guard, attack, banner shown | 4.64 | 2.97 | 2 |
| Guard, attack, no banner off | 2.23 | 2.47 | 2 |
| Guard, move + attack | 6.91 | 3.64 | 1 |
| Guard, buff, banner shown | 2.74 | 0.75 | 2 |
| Guard, buff, no banner off | 1.44 | 0.76 | 6 |
| Guard, move only, no banner off | 0.23 | 0.15 | 5 |

## The post-attack idle gap
- With the mod on and a prompt answered at about 0.4s it was 1.15 to 1.20s (n=19 attacks). Off it was 0.08 to 0.22s (n=14). With no prompt it is 0.007s in both. [M]
- It waits on the attacker, not the hero: `m_AttackingActors[0]` (`CAbilityAttack.cs:1710`) and the idle-state check (`Choreographer.cs:2720`). [M, from code]
- On screen, n=1: the attacking guard stays nearly frozen in its follow-through for about 1s, then straightens up. `shots/gap-melee2.jpg` [M]
- Likely cause: `IdleSMB` sets `animator.speed = 1 / GameSpeedIncreaseAmount` on entering idle, so the transition back to idle crawls at 1/6 speed. Vanilla has the same effect at 1/3, hidden by its longer damage label. [I, animator not instrumented]

Config-only diagnostics [M]:

| Run | Idle gap | Attack, excl. prompt |
|---|---|---|
| `EnemyTurnMultiplier=3` | 0.49 to 0.50 (n=2) | 2.02 to 2.06 |
| Default 6x, prompt answered at 2.6s | 0.01 (n=2) | 1.22 to 1.38 |

The gap overlaps the prompt, so slower answers absorb it. With 2.6s prompts, Archer and Guard Elite turns took 1.56s and 1.43s, against 2.59s and 2.60s at 0.4s and 3.66s and 4.17s for control at 0.4s. So the tables overstate the gap for human players, who likely see more saving than these attack rounds show. [I] Control wasn't run with slow prompts.

## Behaviour with all defaults, Logging on (2 runs, 5 rounds) [M]
- No softlocks in any of the 11 mod-on scenario runs.
- Worked: hero attacks, a kill and gold drop, retaliate, heal, element infusion, damage prompts including burn, rests, redraw prompts, ready-up, end of round, round banners, hero death, Defeat then Retry then Return To Map. A win wasn't reached.
- Autosave blocks 18ms on [9-19] (n=6) vs 19ms off [18-33] (n=5).
- `HandleDesync`: 0 in all 19 runs. Every non-Hydra exception came within 6s of quitting, in control runs too. One `timeout on :null` line appeared in each of m2, c3 and d1.
- Visuals are only lightly checked: 7 of 8 videos were black from a capture mistake. The check rests on screenshots and `video/m7-gap2.mp4`. Nothing looked broken apart from the freeze above. Feel at 5x/6x can't be judged this way.

## CPU and GPU [M]
Main thread median over a whole run: 57% on (m7), 59% off (c6). GPU 29% in m7. No Xid errors with displays held on. No frame-time capture.

## Caveats
- 2 heroes, Friendly difficulty, Black Barrow only. Most hero turns were skipped. Ranged attacks are n=2.
- c6 R6 control ran after a Restart Round, following a misjudged card pick. m7 R6 is the first pass. R1's mod side came after a Retry, as did its control.
- Not exercised: the `UIReadyToggle` 30s/20s scaled-clock timeouts, the 15s phase timeout, mixed modded and unmodded groups.
- The game ran under Proton Experimental.
