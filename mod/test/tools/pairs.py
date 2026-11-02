#!/usr/bin/env python3
"""Matched control/mod comparison. Times exclude prompt-answer time unless named otherwise. Writes pairs.json and prints tables."""
import json, os, re, statistics as st

T = os.path.expanduser('~/code/gloomhaven-perf/mod/test/')
TURN = re.compile(r"^(\S+) (.+?)'s Turn$")
HERO = ('Brute', 'Cragheart')


def sec(s):
    return int(s[:2]) * 3600 + int(s[3:5]) * 60 + float(s[6:])


def load(run):
    out = []
    for l in open(f'{T}run-{run}.log', errors='replace'):
        l = l.rstrip('\n')
        if len(l) > 13 and l[2] == ':' and '[DLL Log]' not in l and 'COMBAT LOG' not in l:
            out.append(l)
    return out


def passes(run):
    """One pass = enemy turns contiguous between hero turns or round starts."""
    L = load(run)
    idx = [(i, TURN.match(l)) for i, l in enumerate(L) if TURN.match(l)]
    res, cur, rnd = [], [], 0
    rlines = [(i, int(m[1])) for i, m in ((i, re.match(r'^\S+ Round (\d+)$', l)) for i, l in enumerate(L)) if m]
    for n, (i, m) in enumerate(idx):
        actor = m.group(2)
        j = idx[n + 1][0] if n + 1 < len(idx) else len(L)
        cut = next((k for k, _ in rlines if i < k < j), None)
        restart = next((k for k in range(i, j) if 'Restart Round' in L[k]), None)
        seg = L[i:(cut if cut else j)]
        r = max([v for k, v in rlines if k < i] or [0])
        if actor in HERO:
            if cur:
                res.append(cur)
            cur = []
            continue
        cur.append(turn(actor, seg, r, sec(L[j][:12]) if j < len(L) and not cut and TURN.match(L[j]) else None))
        if cut:
            res.append(cur)
            cur = []
    if cur:
        res.append(cur)
    return res


def turn(actor, seg, r, nxt):
    t0 = sec(seg[0][:12])

    def first(p, after=-1):
        for l in seg:
            t = sec(l[:12])
            if t >= after and p in l[13:]:
                return t
        return None

    def every(p):
        return [sec(l[:12]) for l in seg if p in l[13:]]

    banner_end = first('Finished phase banner')
    banner = banner_end - t0 if banner_end else 0.0
    move = 0.0
    for k, l in enumerate(seg):
        if 'ActorHasMoved starting' in l:
            for l2 in seg[k + 1:]:
                if 'WaitingForMoveAnim Choreographer state moved' in l2:
                    move += sec(l2[:12]) - sec(l[:12])
                    break
    hexes = sum(int(x) for l in seg for x in re.findall(r'Moves \[Move\] (\d+)', l))
    atk = []
    for a in every('[MessageHandler] ActorIsAttacking'):
        finish = first('ShowModifiers - Finish', a)
        been = first('[MessageHandler] ActorBeenAttacked', a)
        lab_s = first('ShowAttackModifDamage show label', a)
        lab_e = first('ShowAttackModifDamage finish', a)
        sel = first('[MessageHandler] PlayerSelectedToNotAvoidDamage', a)
        idle = first('[MessageHandler] PlayerWaitForIdle', a)
        done = first('[MessageHandler] AttackDone', a)
        if not done:
            continue
        prompt = (sel - lab_e) if sel and lab_e and sel <= done else 0.0
        atk.append(dict(total_excl_prompt=done - a - prompt, prompt=prompt,
                        modifiers=(finish - a) if finish else 0.0,
                        to_damage=(been - finish) if been and finish else 0.0,
                        label=(lab_e - lab_s) if lab_s and lab_e and lab_e <= done else 0.0,
                        idle_gap=(done - idle) if idle else 0.0, promptless=prompt == 0))
    prompt = sum(a['prompt'] for a in atk)
    end = first('[MessageHandler] EndTurn')
    tend = end if end else sec(seg[-1][:12])
    applies = [re.search(r' applies (\S+)', l).group(1) for l in seg if ' applies ' in l]
    act = [re.sub(r'^\S+ ', '', l) for l in seg if re.search(r' Moves \[Move\]| deals | applies | draws |moved from', l)]
    return dict(actor=actor, round=r, t=seg[0][:12], total=tend - t0 - prompt, prompt=prompt, banner=banner, move=move, hexes=hexes,
                attack=sum(a['total_excl_prompt'] for a in atk), modifiers=sum(a['modifiers'] for a in atk),
                to_damage=sum(a['to_damage'] for a in atk),
                label=sum(a['label'] for a in atk), idle_gap=sum(a['idle_gap'] for a in atk), n_atk=len(atk), atk=atk, applies=applies,
                gap=(nxt - tend) if (nxt and end) else None, sig=act)


def phase(p):
    # first enemy turn start -> end of last enemy turn, minus prompts; includes the gaps inside the phase
    span = sum(x['total'] for x in p) + sum(x['gap'] or 0 for x in p[:-1])
    return span


ALL = {r: passes(r) for r in 'c1 m1 c2 m2 c3 m3 c4 m4 c5 m5 c6 m6 c7 m7 x1 x2'.split()}


def find(run, rnd, nth=0):
    ps = [p for p in ALL[run] if p and p[0]['round'] == rnd]
    return ps[nth] if len(ps) > nth else None


PAIRS = [
    ('A attempt2 R5 long rest (rep1)', 'c1', 5, 0, 'm1', 5, 0),
    ('A attempt2 R5 long rest (rep2)', 'c1', 5, 1, 'm1', 5, 1),
    ('A attempt3 R1 shield', 'c2', 1, 0, 'm1', 6, 1),
    ('A attempt3 R2 move+attack', 'c2', 2, 0, 'm2', 2, 0),
    ('A attempt3 R3 shield+retaliate', 'c3', 3, 0, 'm2', 3, 0),
    ('A attempt3 R4 shield+retaliate', 'c3', 4, 0, 'm3', 4, 0),
    ('B door R1 (6 enemies)', 'c4', 1, 0, 'm4', 1, 0),
    ('B R2 (6 enemies)', 'c4', 2, 0, 'm5', 2, 0),
    ('B R3 (6 enemies)', 'c5', 3, 0, 'm5', 3, 0),
    ('B R4 (6 enemies)', 'c5', 4, 0, 'm6', 4, 0),
    ('B R5 (6 enemies)', 'c6', 5, 0, 'm6', 5, 0),
    ('B R6 (6 enemies, archer ranged)', 'c6', 6, 0, 'm7', 6, 0),
    ('B R7 (hero dies, archer ranged)', 'c7', 7, 0, 'm7', 7, 0),
]


def clean(sig):
    return [re.sub(r'\s+', ' ', s) for s in sig]


rows = []
turnrows = []
for name, c, cr, cn, m, mr, mn in PAIRS:
    pc, pm = find(c, cr, cn), find(m, mr, mn)
    if not pc or not pm:
        print('MISSING', name, c, cr, cn, m, mr, mn, [(p[0]['round'], len(p)) for p in ALL[c]], [(p[0]['round'], len(p)) for p in ALL[m]])
        continue
    same = [x['actor'] for x in pc] == [x['actor'] for x in pm] and all(clean(a['sig']) == clean(b['sig']) for a, b in zip(pc, pm))
    pc2, pm2 = phase(pc), phase(pm)
    rows.append(dict(name=name, c=c, m=m, n=len(pc), same=same, off=pc2, on=pm2, ratio=pc2 / pm2 if pm2 else 0,
                     comp={k: (sum(x[k] for x in pc), sum(x[k] for x in pm)) for k in ('banner', 'move', 'attack', 'modifiers', 'to_damage', 'label', 'idle_gap')},
                     gaps=(sum(x['gap'] or 0 for x in pc[:-1]), sum(x['gap'] or 0 for x in pm[:-1]))))
    for a, b in zip(pc, pm):
        turnrows.append((name, a, b))
    if not same:
        for a, b in zip(pc, pm):
            if clean(a['sig']) != clean(b['sig']):
                print('DIFF', name, a['actor'], b['actor'])
                print('  off', clean(a['sig'])[:6])
                print('  on ', clean(b['sig'])[:6])

print('%-34s %2s %5s %7s %7s %6s  %s' % ('pair', 'n', 'same', 'off', 'on', 'x', 'off/on: banner move attack [mods todmg label idle] gaps'))
for r in rows:
    c = r['comp']
    print('%-34s %2d %5s %7.2f %7.2f %6.2f  b %.2f/%.2f mv %.2f/%.2f atk %.2f/%.2f [%.2f/%.2f %.2f/%.2f %.2f/%.2f %.2f/%.2f] g %.2f/%.2f' % (
        r['name'], r['n'], r['same'], r['off'], r['on'], r['ratio'], *c['banner'], *c['move'], *c['attack'], *c['modifiers'], *c['to_damage'], *c['label'], *c['idle_gap'], *r['gaps']))


def med(v):
    v = [x for x in v]
    return (round(st.median(v), 3), round(min(v), 3), round(max(v), 3), len(v)) if v else None


def typ(a):
    return re.sub(r' \(\d+\)', '', a)


out = {}
for side, ix in (('off', 1), ('on', 2)):
    ts = [t[ix] for t in turnrows]
    out[side] = dict(
        banner=med([x['banner'] for x in ts if x['banner'] > 0]),
        banner_shown=sum(1 for x in ts if x['banner'] > 0), turns=len(ts),
        modifiers_1draw=med([a['modifiers'] for x in ts for a in x['atk'] if a['modifiers'] < 1.25 or side == 'on']),
        modifiers=med([a['modifiers'] for x in ts for a in x['atk']]),
        to_damage=med([a['to_damage'] for x in ts for a in x['atk']]),
        label=med([a['label'] for x in ts for a in x['atk'] if a['label'] > 0]),
        idle=med([a['idle_gap'] for x in ts for a in x['atk'] if a['label'] > 0 and a['prompt'] > 0]),
        attack=med([a['total_excl_prompt'] for x in ts for a in x['atk'] if a['prompt'] > 0]),
        move1=med([x['move'] for x in ts if x['hexes'] == 1 and x['move'] > 0]),
        move2=med([x['move'] for x in ts if x['hexes'] == 2 and x['move'] > 0]),
        move3=med([x['move'] for x in ts if x['hexes'] == 3 and x['move'] > 0]),
        gap=med([x['gap'] for x in ts if x['gap'] is not None]),
        per_type={typ(k): med([x['total'] for x in ts if typ(x['actor']) == typ(k)]) for k in {t['actor'] for t in ts}},
        total=med([x['total'] for x in ts]))
    print(side, json.dumps(out[side]))

# per-enemy-type by what it did, within the bigger group only
print('\nType/action, section B only: off median, on median (n pairs)')
groups = {}
for name, a, b in turnrows:
    if not name.startswith('B '):
        continue
    kind = ('move+attack' if a['move'] > 0 and a['n_atk'] else 'attack' if a['n_atk'] else 'move+buff' if a['move'] > 0 and a['applies'] else 'move' if a['move'] > 0 else 'buff' if a['applies'] else 'other')
    groups.setdefault((typ(a['actor']), kind, a['banner'] > 0), []).append((a['total'], b['total'], a['t'], b['t']))
for k in sorted(groups, key=str):
    v = groups[k]
    print('  %-22s %-12s banner_off=%-5s off %.2f on %.2f saved %.2f n=%d' % (*k, st.median(x[0] for x in v), st.median(x[1] for x in v), st.median(x[0] - x[1] for x in v), len(v)))

print('\nArcher ranged attack turns:')
for name, a, b in turnrows:
    if 'Archer' in a['actor'] and a['n_atk']:
        print(' ', name, a['actor'], {k: (round(a[k], 3), round(b[k], 3)) for k in ('total', 'banner', 'move', 'attack', 'modifiers', 'to_damage', 'label', 'idle_gap')}, 'hexes', a['hexes'])

print('\nIdle gap consistency, every attack with a damage prompt:')
for r in ['c1', 'c2', 'c3', 'c4', 'c5', 'c6', 'c7', 'm1', 'm2', 'm3', 'm4', 'm5', 'm6', 'm7', 'x1', 'x2']:
    g = [(a['idle_gap'], a['prompt']) for p in ALL[r] for x in p for a in x['atk'] if a['prompt'] > 0]
    n = [a['idle_gap'] for p in ALL[r] for x in p for a in x['atk'] if a['prompt'] == 0]
    if g or n:
        print(' ', r, 'with prompt', [round(x[0], 2) for x in g], 'median prompt %.2f' % (st.median(x[1] for x in g) if g else 0), '| no prompt', [round(x, 3) for x in n])
json.dump(dict(rows=rows, summary=out), open(T + 'pairs.json', 'w'), indent=1)

# door sequence
print('\nDoor: Open Door click -> Crag ready for next action; and ability end')
for run in ('c4', 'm4'):
    L = load(run)
    i = next(k for k, l in enumerate(L) if 'READY BUTTON - ON CLICK' in l and any('EREADYBUTTONOPENDOOR' in L[z] for z in range(k, min(k + 3, len(L)))))
    t = sec(L[i][:12])
    marks = ['[MessageHandler] Spawn', '[MessageHandler] ActivateProp', 'Moves [Move] 5', 'gains 1 XP', 'Bandit Archer Elite', 'ActionSelection']
    res = {}
    for l in L[i:i + 3000]:
        for m in marks:
            if m in l and m not in res:
                res[m] = round(sec(l[:12]) - t, 3)
    print(' ', run, L[i][:12], res)
