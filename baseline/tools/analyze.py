#!/usr/bin/env python3
"""Parse timestamped Player.log captures into per-enemy-turn timings. Writes turns.csv, phases.csv, stats.json."""
import csv, json, os, re, statistics as st
from datetime import datetime as D

B = os.path.expanduser('~/code/gloomhaven-perf/baseline')
RUNS = ['run-1', 'run-2', 'run-3']


def sec(ts):
    return (D.strptime(ts, '%H:%M:%S.%f') - D(1900, 1, 1)).total_seconds()


def hms(s):
    return '%02d:%02d:%06.3f' % (s // 3600, (s % 3600) // 60, s % 60)


# Condition windows from notes.txt / the SaveGlobalData log lines written by each toggle.
# (run, start, end, condition)
COND = [
    ('run-1', '21:58:00', '22:07:11', 'off'),       # toggled off 22:00:34, before round 1 selection ended
    ('run-2', '22:14:00', '22:17:14.870', 'off'),
    ('run-2', '22:17:14.870', '22:19:50', 'on'),
    ('run-2', '22:19:50', '22:21:40', 'on-frozen'),  # display went off 22:19:51-52 (GPU 0 %, frame frozen); turn starting 22:19:50.001 overlaps it
    ('run-3', '22:25:00', '22:33:54.899', 'on'),
    ('run-3', '22:33:54.899', '22:34:36.303', 'off'),
]
TURN = re.compile(r"^(\S+) (.+?)'s Turn$")
HERO = {'Brute', 'Cragheart'}


def cond_of(run, t):
    for r, a, b, c in COND:
        if r == run and sec(a + ('' if '.' in a else '.000')) <= t < sec(b + ('' if '.' in b else '.000')):
            return c
    return 'unknown'


rows, phases, frames = [], [], {}
saves = []
timeouts = []
embedded_lag = []
for run in RUNS:
    p = f'{B}/{run}.log'
    lines = []
    for l in open(p, errors='replace'):
        l = l.rstrip('\n')
        if len(l) < 13 or l[2] != ':' or '[DLL Log]' in l:
            continue
        lines.append(l)
    # pipeline latency: game-written timestamp in LogEOS lines vs our arrival stamp
    for l in lines:
        m = re.match(r"(\S+) \d{4}-\d\d-\d\dT(\d\d:\d\d:\d\d\.\d{3})\d*-\d\d:\d\d LogEOS", l)
        if m:
            embedded_lag.append((sec(m.group(1)) - sec(m.group(2))) * 1000)
    # frame deltas from distinct arrival stamps of per-frame tween lines; unity delta from "delay:" decrement of one tween
    fts = []
    last = None
    for l in lines:
        if ' tween:name:' in l[12:30]:
            t = sec(l[:12])
            if last is None or t != last:
                if last is not None and t - last < 1.0:
                    fts.append((t, (t - last) * 1000))
                last = t
        elif last is not None and ' Round ' not in l and l[13:17] == '[Mes':
            pass
    frames[run] = fts
    # segments
    idx = [i for i, l in enumerate(lines) if TURN.match(l)]
    rnd = 0
    for ii, i in enumerate(idx):
        m = TURN.match(lines[i])
        t0 = sec(m.group(1))
        actor = m.group(2)
        j = idx[ii + 1] if ii + 1 < len(idx) else len(lines)
        seg = lines[i:j]
        # stop segment at a "Round N" line
        for k, l in enumerate(seg):
            if re.match(r'^\S+ Round \d+$', l):
                seg = seg[:k]
                break
        if actor in HERO:
            continue
        rows_round = 0
        for l in lines[:i][::-1]:
            mr = re.match(r'^\S+ Round (\d+)$', l)
            if mr:
                rows_round = int(mr.group(1))
                break
        ev = {}
        def first(pat, after=None):
            for l in seg:
                t = sec(l[:12])
                if after is not None and t < after:
                    continue
                if pat in l[13:]:
                    return t
            return None
        banner_end = first('Finished phase banner') or t0
        moves = []
        hexes = None
        for k, l in enumerate(seg):
            if 'ActorHasMoved starting' in l:
                a = sec(l[:12])
                b = None
                for l2 in seg[k + 1:]:
                    if 'WaitingForMoveAnim Choreographer state moved' in l2:
                        b = sec(l2[:12])
                        break
                moves.append((a, b))
            mh = re.search(r'moves from .*hexes=(\d+), length=(\d+)', l)
            if mh:
                hexes = int(mh.group(2))
        move = sum((b - a) for a, b in moves if b) if moves else 0.0
        ai_ms = [float(x) for l in seg for x in re.findall(r'elapsed movement time=(\d+)ms', l)]
        atk_start = first('[MessageHandler] ActorIsAttacking')
        mod_end = first('[AttackModifiers]: ShowModifiers - Finish')
        label_s = first('ShowAttackModifDamage show label')
        label_e = first('ShowAttackModifDamage finish')
        sel_e = first('[MessageHandler] PlayerSelectedToNotAvoidDamage')
        # the first hit also counts: PlayerSelecting... -> Selected includes the label wait
        atk_done = first('[MessageHandler] AttackDone')
        end_turn = first('[MessageHandler] EndTurn')
        modifiers = (mod_end - atk_start) if atk_start and mod_end else 0.0
        label = (label_e - label_s) if label_s and label_e else 0.0
        inp = (sel_e - label_e) if sel_e and label_e else 0.0
        attack_total = ((atk_done - atk_start) - inp) if atk_start and atk_done else 0.0
        applies = [l[13:] for l in seg if ' applies ' in l and 'Spawned' not in l]
        retaliate_to = [l for l in seg if 'timeout on' in l]
        for l in seg:
            if 'timeout on' in l:
                timeouts.append((run, l[:12], actor, l[13:]))
        move_lines = [l for l in seg if re.search(r' Moves \[Move\]', l)]
        moved_reported = int(re.search(r'Moves \[Move\] (\d+)', move_lines[0]).group(1)) if move_lines else 0
        hexes_final = moved_reported
        tend = end_turn if end_turn else (sec(seg[-1][:12]))
        total = tend - t0 - inp
        # gap to next turn's start
        next_start = sec(lines[idx[ii + 1]][:12]) if ii + 1 < len(idx) else None
        next_is_enemy = bool(ii + 1 < len(idx) and TURN.match(lines[idx[ii + 1]]).group(2) not in HERO and j - i < len(lines))
        no_round_between = len(seg) == j - i
        gap = (next_start - tend) if (next_start and end_turn and next_is_enemy and no_round_between) else None
        cond = cond_of(run, t0)
        kind = ('move+attack' if moves and atk_start else 'attack' if atk_start else 'move' if moves else 'other')
        rows.append(dict(run=run, round=rows_round, t=hms(t0), condition=cond, actor=actor, kind=kind,
                         hexes=hexes_final, total_excl_input=round(total, 3), banner=round(banner_end - t0, 3),
                         move_anim=round(move, 3), per_hex=round(move / hexes_final, 3) if hexes_final and move else '',
                         ai_ms=sum(ai_ms) if ai_ms else 0, attack_total=round(attack_total, 3),
                         modifiers=round(modifiers, 3), dmg_label=round(label, 3), script_input_wait=round(inp, 3),
                         n_applies=len(applies), gap_to_next_turn=round(gap, 3) if gap is not None else '',
                         other=round(total - (banner_end - t0) - move - attack_total, 3), timeout=len(retaliate_to),
                         applies=';'.join(a[a.index('applies') + 8:].split(' to ')[0] for a in applies)))
    # autosaves
    for i, l in enumerate(lines):
        if '[MessageHandler] ProcessAutosaves' in l:
            t = sec(l[:12])
            res = cp = None
            for l2 in lines[i:i + 500]:
                if res is None and 'Will try now to resume' in l2:
                    res = sec(l2[:12])
                if cp is None and 'Create scenario checkpoint' in l2:
                    cp = sec(l2[:12])
                if res and cp:
                    break
            saves.append(dict(run=run, t=hms(t), block_ms=round((res - t) * 1000) if res else None,
                              to_checkpoint_log_ms=round((cp - t) * 1000) if cp else None))

# frames restricted to live enemy turns
enemy_frames = {}
hitches = []
for run, fts in frames.items():
    wins = [(sec(r['t']), sec(r['t']) + r['total_excl_input'] + (r['script_input_wait'] or 0), r['condition']) for r in rows if r['run'] == run and r['condition'] in ('on', 'off')]
    for t, d in fts:
        for a, b, c in wins:
            if a <= t <= b:
                enemy_frames.setdefault(c, []).append(d)
                if d > 25:
                    hitches.append((run, hms(t), round(d), c))
                break
with open(f'{B}/turns.csv', 'w', newline='') as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
    w.writeheader()
    w.writerows(rows)


def tsec(x):
    return sec(x)


# Enemy phases: consecutive enemy turns inside one round with same condition
ph = {}
for r in rows:
    ph.setdefault((r['run'], r['round'], r['t'][:5]), []).append(r)
pl = {}
for r in rows:
    k = (r['run'], r['round'], r['condition'])
    pl.setdefault(k, []).append(r)
for k, v in pl.items():
    phases.append(dict(run=k[0], round=k[1], condition=k[2], turns=len(v), t=v[0]['t'],
                       sum_turns_excl_input=round(sum(x['total_excl_input'] for x in v), 3),
                       sum_gaps=round(sum(x['gap_to_next_turn'] for x in v[:-1] if x['gap_to_next_turn'] != ''), 3)))
with open(f'{B}/phases.csv', 'w', newline='') as f:
    w = csv.DictWriter(f, fieldnames=list(phases[0].keys()))
    w.writeheader()
    w.writerows(phases)


def s(vals):
    vals = [v for v in vals if v != '' and v is not None]
    if not vals:
        return None
    return dict(n=len(vals), median=round(st.median(vals), 3), min=round(min(vals), 3), max=round(max(vals), 3))


out = {}
for c in ['off', 'on', 'on-frozen']:
    R = [r for r in rows if r['condition'] == c]
    o = dict(n_turns=len(R))
    o['total_excl_input'] = s([r['total_excl_input'] for r in R])
    o['banner'] = s([r['banner'] for r in R])
    o['move_anim_per_move'] = s([r['move_anim'] for r in R if r['move_anim'] > 0])
    for h in (1, 2, 3):
        o[f'move_anim_{h}hex'] = s([r['move_anim'] for r in R if r['hexes'] == h and r['move_anim'] > 0])
    o['per_hex'] = s([r['per_hex'] for r in R if r['per_hex'] != ''])
    o['attack_total'] = s([r['attack_total'] for r in R if r['attack_total'] > 0])
    o['modifiers'] = s([r['modifiers'] for r in R if r['modifiers'] > 0])
    o['dmg_label'] = s([r['dmg_label'] for r in R if r['dmg_label'] > 0])
    o['script_input_wait'] = s([r['script_input_wait'] for r in R if r['script_input_wait'] > 0])
    o['gap'] = s([r['gap_to_next_turn'] for r in R])
    o['other'] = s([r['other'] for r in R])
    o['ai_ms'] = s([r['ai_ms'] for r in R if r['ai_ms'] > 0])
    for k in ('move', 'attack', 'move+attack', 'other'):
        o['total_' + k] = s([r['total_excl_input'] for r in R if r['kind'] == k])
    P = [p for p in phases if p['condition'] == c and p['turns'] >= 3]
    o['phase_3enemies_sum'] = s([p['sum_turns_excl_input'] + p['sum_gaps'] for p in P])
    o['time_share'] = {}
    tot = sum(r['total_excl_input'] for r in R) or 1
    for k in ('banner', 'move_anim', 'attack_total', 'other'):
        o['time_share'][k] = round(sum(r[k] for r in R) / tot, 3)
    out[c] = o

# frames
allft = {}
for run, fts in frames.items():
    v = sorted(x[1] for x in fts)
    if not v:
        continue
    q = lambda p: v[min(len(v) - 1, int(p * len(v)))]
    allft[run] = dict(n=len(v), median=round(q(.5), 2), p95=round(q(.95), 2), p99=round(q(.99), 2), max=round(v[-1], 1),
                      over16_7=sum(1 for x in v if x > 16.7), over33=sum(1 for x in v if x > 33.4), over100=sum(1 for x in v if x > 100))
out['frames_from_tween_lines_ms'] = allft
def fq(v):
    v = sorted(v); q = lambda p: v[min(len(v) - 1, int(p * len(v)))]
    return dict(n=len(v), median=round(q(.5), 1), p95=round(q(.95), 1), p99=round(q(.99), 1), max=round(v[-1], 1), over16_7=sum(1 for x in v if x > 16.7), over33=sum(1 for x in v if x > 33.4))
out['enemy_turn_frames_ms'] = {c: fq(v) for c, v in enemy_frames.items()}
out['enemy_turn_hitches_over_25ms'] = hitches
out['all_hitches_over_100ms'] = [(run, hms(t), round(d)) for run, fts in frames.items() for t, d in fts if d > 100]
out['saves'] = saves
out['timeouts'] = timeouts
e = sorted(embedded_lag)
out['pipeline_lag_ms_vs_game_stamp'] = dict(n=len(e), median=round(e[len(e) // 2], 1), p99=round(e[int(.99 * len(e))], 1), max=round(e[-1], 1), min=round(e[0], 1)) if e else None

# CPU / GPU over enemy phases
def load_top(run):
    p = f'{B}/{run}.top.log'
    d = {}
    if not os.path.exists(p):
        return d
    t = None
    pid = None
    for l in open(p, errors='replace'):
        if l.startswith('# pid'):
            pid = l.split()[2]
        elif l.startswith('top -'):
            t = sec(l.split()[2] + '.000')
            d.setdefault(t, {})
        else:
            f = l.split()
            if len(f) >= 12 and t is not None:
                d[t][f[-1] if f[-1] != 'Worker' else ' '.join(f[-2:])] = d[t].get(f[-1], 0) + float(f[8])
    return d


def load_gpu(run):
    d = {}
    p = f'{B}/{run}.gpu.csv'
    if not os.path.exists(p):
        return d
    for l in open(p):
        f = [x.strip() for x in l.split(',')]
        try:
            d[sec(f[0][11:] + '')] = (float(f[1].split()[0]), float(f[3].split()[0]), float(f[5].split()[0]), float(f[4].split()[0]))
        except Exception:
            pass
    return d


res = {}
for c in ['off', 'on']:
    mains, totals, gfx, gpus, gmhz, gw = [], [], [], [], [], []
    for r in rows:
        if r['condition'] != c:
            continue
        a = sec(r['t'])
        b = a + r['total_excl_input'] + (r['script_input_wait'] or 0)
        top = load_top.cache[r['run']] if hasattr(load_top, 'cache') and r['run'] in load_top.cache else None
        if top is None:
            load_top.cache = getattr(load_top, 'cache', {})
            load_top.cache[r['run']] = load_top(r['run'])
            top = load_top.cache[r['run']]
        gpu = getattr(load_gpu, 'cache', {}).get(r['run'])
        if gpu is None:
            load_gpu.cache = getattr(load_gpu, 'cache', {})
            load_gpu.cache[r['run']] = load_gpu(r['run'])
            gpu = load_gpu.cache[r['run']]
        for t, th in top.items():
            if a <= t <= b:
                mains.append(th.get('Main', 0))
                totals.append(sum(th.values()))
                gfx.append(th.get('UnityGfxDeviceW', 0))
        for t, v in gpu.items():
            if a <= t <= b:
                gpus.append(v[0]); gmhz.append(v[3]); gw.append(v[1])
    res[c] = dict(main_thread_pct=s(mains), all_threads_pct=s(totals), gfx_worker_pct=s(gfx), gpu_util_pct=s(gpus), gpu_power_w=s(gmhz), gpu_mem_mib=s(gw))
out['cpu_gpu_enemy_phase'] = res
json.dump(out, open(f'{B}/stats.json', 'w'), indent=1)
print(json.dumps({k: v for k, v in out.items() if k not in ('saves',)}, indent=0)[:9000])
print('saves', saves)
