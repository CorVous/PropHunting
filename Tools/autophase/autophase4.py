"""Splits the generator's room plans, and the search helpers they call, into fine phases: about one placement a step.

A plan (a function PlanStep's switch calls), unsplit or already in phases, and each helper named in STEPPED are
rewritten in place:
  - statements are grouped into units up to a cost limit (plain phases);
  - an `if / else if / else` chain with enough work in it is opened: a selector stores which branch runs, the
    branches' statements become units guarded by it;
  - a heavy loop (`for (int v = A; COND; v++ | v-- | v += K)` or `while (COND)`) is a loop phase, one iteration a
    step; if its body is itself several units, the body becomes a function of its own with its own phases, run a
    level down (PhPush / PhPop);
  - a call of a stepped helper as a statement (`H(..);`, `x = H(..);`, `T x = H(..);`, `if (H(..) ..`) runs the
    helper's phases a level down, a step at a time.
A stepped helper H becomes H_Body() (its phases) and H(...) (runs them to the end, for the callers that are not
in phases). Locals used in a later unit are saved to fields at a unit's end and restored at the next one's start.

usage: python autophase4.py [exclude,names] [only,names]
"""
import re, glob, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ap_norm import normalize

SP = os.path.dirname(os.path.abspath(__file__))
COST = {}
for l in open(os.path.join(SP, 'costs2.txt')):
    p = l.split()
    if len(p) == 2 and not p[0].startswith('Plan'):
        COST[p[0]] = float(p[1])
for n in ('Step', 'PopulateRoomStep', 'PopulateRoom', 'PlanStep', 'Begin', 'Alloc', 'Finish', 'FinishStep'):
    COST.pop(n, None)
UNIT_COST = 0.006
LOOP_COST = 0.001
IF_COST = 0.006
TYPES = r'(?:int|bool|uint|float|string|GenRng|string\[\]|int\[\]|bool\[\])'
INIT = {'int': '0', 'uint': '0', 'float': '0f', 'bool': 'false', 'string': 'null', 'GenRng': 'new GenRng()',
        'string[]': 'null', 'int[]': 'null', 'bool[]': 'null'}
STEPPED_WANT = ['CentrePiece', 'FireRing', 'FireSeats', 'PondBank', 'RackRows', 'RackRun', 'Stand', 'Island', 'LawnTree',
                'BenchAtEdge', 'AroundFire', 'WoodPile', 'DrumRow', 'KitchenRun', 'FarmAnimals', 'Beasts', 'DeadWood',
                'WallRow', 'CornerBed', 'VegPlot', 'FillFromPool', 'Borders', 'WallAny', 'DumpSpot', 'ComedicSpot', 'PalletBlock',
                'Extras', 'FrontMarkers', 'FillOne', 'LeakAttempt', 'Border', 'LayoutSite', 'ValidateSite', 'LeakKey', 'Finish', 'Keep', 'KeepIfWalkable', 'RoomWalkable', 'OpenSpot', 'BuildClearance', 'StampPlot', 'LayoutWood', 'SplitLane', 'StonePaths', 'FireGear', 'Signpost', 'FarmStand', 'TypeYard', 'SofaFacing', 'CornerExtra', 'WallKeep']
STEPPED = {}     # name -> (ret type, [(mod, type, name)])


def ind(l): return len(l) - len(l.lstrip(' '))


def nocomment(l):
    i = l.find('//')
    while i >= 0:
        if l[:i].count('"') % 2 == 0: return l[:i]
        i = l.find('//', i + 2)
    return l


def code(lines): return [l for l in lines if nocomment(l).strip()]


def cost(lines):
    txt = '\n'.join(nocomment(l) for l in lines)
    c = sum(COST.get(n, 0.0) for n in re.findall(r'\b(\w+)\s*\(', txt))
    if re.search(r'\b(for|while)\s*\(', txt): c *= 3
    return c


def statements(lines, n):
    """statements of a block whose statements sit at indent n; comments and blank lines go with the next one"""
    out = []; cur = []; pend = []; depth = 0; last = ''
    for l in lines:
        c = nocomment(l); st = c.strip()
        if not st:
            if cur and depth > 0: cur.append(l)
            else: pend.append(l)
            continue
        starts = ind(l) == n and st not in ('{', '}') and not st.startswith('else') and not st.startswith('{') and depth == 0
        if ind(l) == n and st == '{' and depth == 0 and (not cur or last.endswith(';') or last.endswith('}')): starts = True
        last = st
        if starts:
            if cur: out.append(cur)
            cur = pend + [l]; pend = []
        else:
            cur += pend + [l]; pend = []
        depth += c.count('{') - c.count('}')
    if cur: out.append(cur + pend)
    elif pend and out: out[-1] += pend
    return out


def split_args(s):
    parts = []; depth = 0; part = ''; q = False
    for ch in s:
        if ch == '"': q = not q
        if not q:
            if ch in '({[': depth += 1
            if ch in ')}]': depth -= 1
            if ch == ',' and depth == 0: parts.append(part.strip()); part = ''; continue
        part += ch
    if part.strip(): parts.append(part.strip())
    return parts


def match_paren(s, i):
    """index of the paren closing the one at s[i]"""
    depth = 0; q = False
    for j in range(i, len(s)):
        ch = s[j]
        if ch == '"': q = not q
        if q: continue
        if ch == '(': depth += 1
        elif ch == ')':
            depth -= 1
            if depth == 0: return j
    return -1


FOR = re.compile(r'^\s*for \(int (\w+) = (.+?); (.+); \1(\+\+|--| \+= \d+| -= \d+)\)\s*$')
WHILE = re.compile(r'^\s*while \((.*)\)\s*$')
IF = re.compile(r'^\s*if \((.*)\)\s*$')
ELIF = re.compile(r'^\s*else if \((.*)\)\s*$')


def as_loop(s):
    """(var or None, start, cond, step, leading comments, body lines) of a loop statement, or None"""
    c = code(s)
    head = nocomment(c[0]).rstrip()
    m = FOR.match(head)
    if m:
        var, a, cond, inc = m.groups()
        step = 1 if inc == '++' else (-1 if inc == '--' else int(inc.replace('+= ', '').replace('-= ', '-').strip()))
    else:
        m = WHILE.match(head)
        if not m or m.group(1).count('(') != m.group(1).count(')'): return None
        var, a, cond, step = None, '0', m.group(1), 1
    if len(c) < 2: return None
    i0 = s.index(c[0])
    body = s[i0 + 1:]
    bc = code(body)
    if bc[0].strip() == '{':
        if bc[-1].strip() != '}' or ind(bc[-1]) != ind(c[0]): return None
        j0 = body.index(bc[0]); j1 = len(body) - 1 - body[::-1].index(bc[-1])
        body = body[j0 + 1:j1]
    txt = '\n'.join(nocomment(l) for l in body)
    if var:
        if re.search(r'\b' + var + r'\s*(\+\+|--|[-+*/]?=(?!=))', txt) or re.search(r'(\+\+|--)\s*' + var + r'\b', txt): return None
        if re.search(r'\b(out|ref)\s+' + var + r'\b', txt): return None
    return var, a, cond, step, s[:i0], body


def as_chain(s, n):
    """if (C) { } [else if (C) { }]* [else { }] with every branch in braces at indent n -> (pre, [(cond, lines)])"""
    c = [l for l in s if nocomment(l).strip()]
    pre = s[:s.index(c[0])]
    rest = s[s.index(c[0]):]
    out = []; i = 0
    while i < len(rest):
        l = rest[i]; st = nocomment(l).strip()
        if not st: i += 1; continue
        if ind(l) != n: return None
        if not out: m = IF.match(nocomment(l).rstrip()); cond = m.group(1) if m else None
        else:
            m = ELIF.match(nocomment(l).rstrip())
            cond = m.group(1) if m else ('' if st == 'else' else None)
        if cond is None: return None
        if cond.count('(') != cond.count(')'): return None
        if re.search(r'\bout\s+(int|var|bool)\b', cond): return None
        j = i + 1
        while j < len(rest) and not nocomment(rest[j]).strip(): j += 1
        if j >= len(rest) or nocomment(rest[j]).strip() != '{' or ind(rest[j]) != n: return None
        depth = 1; k = j + 1
        while k < len(rest) and depth:
            cc = nocomment(rest[k]); depth += cc.count('{') - cc.count('}')
            k += 1
        if depth: return None
        if nocomment(rest[k - 1]).strip() != '}' or ind(rest[k - 1]) != n: return None
        out.append((cond, rest[j + 1:k - 1]))
        i = k
        if cond == '' and any(nocomment(x).strip() for x in rest[i:]): return None
    return (pre, out) if out else None


def as_call(s, n):
    """a stepped helper called as a statement: (pre, decl type, target, helper, args) or None"""
    c = code(s)
    if ind(c[0]) != n: return None
    txt = ' '.join(nocomment(l).strip() for l in c)
    m = re.match(r'^(?:(' + TYPES + r')\s+)?(?:(\w+)\s*=\s*)?(\w+)\(', txt)
    if not m or m.group(3) not in STEPPED: return None
    if m.group(1) and not m.group(2): return None
    e = match_paren(txt, m.end() - 1)
    if e < 0 or txt[e + 1:].strip() != ';': return None
    args = split_args(txt[m.end():e])
    ret, params = STEPPED[m.group(3)]
    if params and params[-1][0] == 'params':
        k = len(params) - 1
        if len(args) < k: return None
        tail = args[k:]
        args = args[:k] + [tail[0] if len(tail) == 1 and not tail[0].startswith('"') else 'new string[] { ' + ', '.join(tail) + ' }']
    if len(args) != len(params): return None
    for a, (mod, typ, nm) in zip(args, params):
        if (mod in ('ref', 'out')) != bool(re.match(r'(ref|out)\s', a)): return None
        if re.match(r'out\s+(int|var|bool|string)\s', a): return None
    if (m.group(2) is not None) and ret == 'void': return None
    return s[:s.index(c[0])], m.group(1), m.group(2), m.group(3), args


class Unit:
    def __init__(self, kind, guards, scope, n):
        self.kind = kind; self.guards = list(guards); self.scope = scope; self.n = n
        self.stmts = []; self.cost = 0.0; self.loop = None; self.decls = {}; self.sub = []; self.call = None
        self.restore = []; self.save = []; self.bodyscope = None

    def text(self):
        g = ' '.join(self.guards)
        if self.kind == 'loop':
            var, a, cond, step, pre, body = self.loop
            return a + '\n' + cond + '\n' + '\n'.join(nocomment(l) for l in body) + '\n' + g
        if self.kind == 'loopsub':
            var, a, cond, step, pre, body = self.loop
            return a + '\n' + cond + '\n' + g
        if self.kind == 'call':
            pre, typ, target, h, args = self.call
            return ' '.join(args) + ' ' + (target or '') + '\n' + g
        return '\n'.join(nocomment(l) for s in self.stmts for l in s) + '\n' + g


def decls_of(stmt, n):
    c = code(stmt)
    if not c or ind(c[0]) != n: return {}
    m = re.match(r'^\s*(' + TYPES + r')\s+(\w+)\s*(=|;|,)', nocomment(c[0]))
    if not m: return {}
    typ = m.group(1)
    txt = ''
    for l in c:   # the declaration's own lines (a bare block may follow it in the same statement)
        txt += ' ' + nocomment(l).strip()
        if txt.endswith(';'): break
    txt = txt.strip()
    if not txt.endswith(';') or txt.count(';') != 1: return {}
    rest = txt[len(typ):-1]
    names = {}
    for part in split_args(rest):
        names[part.split('=')[0].strip()] = (typ, '=' in part)
    return {k: v for k, v in names.items() if re.fullmatch(r'\w+', k)}


class Ctx:
    def __init__(self, fname, kind, ret='void'):
        self.fname = fname; self.kind = kind; self.ret = ret
        self.sel = 0; self.sid = 0; self.subn = 0; self.tmp = 0
        self.fields = {}; self.subfns = []; self.head = []; self.params = []; self.outer = {}

    def field(self, name, typ):
        f = 'hq_%s_%s' % (self.fname, name)
        if f in self.fields and self.fields[f] != typ:
            f += '_' + re.sub(r'\W', 'A', typ)
        self.fields[f] = typ
        return f


def build(stmts, n, guards, scope, units, ctx):
    cur = None
    stmts = list(stmts)
    q = 0
    while q < len(stmts):
        s = stmts[q]; q += 1
        if not code(s):
            if cur is not None: cur.stmts.append(s)
            continue
        c0 = code(s)[0]
        s = normalize(s, n, STEPPED); stmts[q - 1] = s
        c0 = code(s)[0]
        # if (H(...) ...  ->  T t = H(...); if (t ...
        m = re.match(r'^(\s*)if \((!?)(\w+)\(', nocomment(c0))
        if m and m.group(3) in STEPPED and STEPPED[m.group(3)][0] != 'void' and ind(c0) == n:
            line = nocomment(c0)
            o = m.end() - 1; e = match_paren(line, o)
            if e > 0:
                ctx.tmp += 1
                t = 'ht%d' % ctx.tmp
                ret = STEPPED[m.group(3)][0]
                i0 = s.index(c0)
                decl = s[:i0] + [m.group(1) + '%s %s = %s;' % (ret, t, line[m.end() - 1 - len(m.group(3)):e + 1])]
                rest = [m.group(1) + 'if (' + m.group(2) + t + line[e + 1:] + c0[len(line):]] + s[i0 + 1:]
                stmts[q - 1:q] = [decl, rest]
                q -= 1
                continue
        cl = as_call(s, n)
        if cl:
            u = Unit('call', guards, scope, n); u.call = cl
            if cl[1]: u.decls[cl[2]] = (cl[1], True)
            units.append(u); cur = None
            continue
        cut = any('@cut' in l for l in s[:s.index(code(s)[0])])
        if cut: cur = None
        # a bare block: opened if it holds more than one unit
        cb = code(s)
        if cb[0].strip() == '{' and cb[-1].strip() == '}' and ind(cb[0]) == n and ind(cb[-1]) == n:
            i0 = s.index(cb[0]); i1 = len(s) - 1 - s[::-1].index(cb[-1])
            keep = (ctx.sel, ctx.sid, ctx.tmp, dict(ctx.fields))
            trial = []
            build(statements(s[i0 + 1:i1], n + 4), n + 4, [], scope + (-1,), trial, ctx)
            ctx.sel, ctx.sid, ctx.tmp, ctx.fields = keep
            if len(trial) >= 2 or any(v.kind != 'plain' for v in trial):
                ctx.sid += 1
                if s[:i0] and cur is not None: cur.stmts.append(s[:i0])
                build(statements(s[i0 + 1:i1], n + 4), n + 4, guards, scope + (ctx.sid,), units, ctx)
                cur = None
                continue
        lp = as_loop(s)
        mchunk = re.search(r'@step (\d+)', code(s)[0])
        if lp and mchunk:
            # so many rounds a step: the loop stays one piece
            u = Unit('loop', guards, scope, n); u.loop = lp; u.chunk = int(mchunk.group(1))
            units.append(u); cur = None
            continue
        if lp and (cost(lp[5]) >= LOOP_COST or '@step' in code(s)[0]):
            ctx.sid += 1
            bscope = scope + (ctx.sid,)
            sub = []
            save = (ctx.sel, ctx.sid, ctx.tmp, dict(ctx.fields))
            build(statements(lp[5], n + 4), n + 4, [], bscope, sub, ctx)
            if len(sub) >= 2 or any(v.kind != 'plain' for v in sub):
                u = Unit('loopsub', guards, scope, n); u.loop = lp; u.sub = sub; u.bodyscope = bscope
            else:
                ctx.sel, ctx.sid, ctx.tmp, ctx.fields = save
                u = Unit('loop', guards, scope, n); u.loop = lp
            units.append(u); cur = None
            continue
        ch = as_chain(s, n)
        if ch:
            pre, branches = ch
            inner = [statements(b, n + 4) for c, b in branches]
            total = sum(cost(b) for c, b in branches)
            worth = False
            btxt = ' '.join(nocomment(l) for c, b in branches for l in b)
            if total >= IF_COST or any('@cut' in l or '@step' in l for l in s) or any(re.search(r'\b' + h + r'\(', btxt) for h in STEPPED):
                keep = (ctx.sel, ctx.sid, ctx.tmp, dict(ctx.fields))
                for st in inner:
                    trial = []
                    build(st, n + 4, [], scope + (-1,), trial, ctx)
                    if len(trial) >= 2 or any(v.kind != 'plain' for v in trial): worth = True
                ctx.sel, ctx.sid, ctx.tmp, ctx.fields = keep
            if worth:
                ctx.sel += 1
                f = ctx.field('sel%d' % ctx.sel, 'int')
                pad = ' ' * n
                sel = list(pre) + [pad + f + ' = 0;']
                for i, (c, b) in enumerate(branches):
                    if i == 0: sel.append(pad + 'if (%s) %s = 1;' % (c, f))
                    elif c: sel.append(pad + 'else if (%s) %s = %d;' % (c, f, i + 1))
                    else: sel.append(pad + 'else %s = %d;' % (f, i + 1))
                cs = cost(sel)
                if cur is None or (cs > 0 and cur.cost > 0 and cur.cost + cs > UNIT_COST):
                    cur = Unit('plain', guards, scope, n); units.append(cur)
                cur.stmts.append(sel); cur.cost += cs
                for i, st in enumerate(inner):
                    ctx.sid += 1
                    build(st, n + 4, guards + ['%s == %d' % (f, i + 1)], scope + (ctx.sid,), units, ctx)
                cur = None
                continue
        c = cost(s)
        if cur is None or (c > 0 and cur.cost > 0 and cur.cost + c > UNIT_COST):
            cur = Unit('plain', guards, scope, n); units.append(cur)
        cur.stmts.append(s); cur.cost += c
        cur.decls.update(decls_of(s, n))


def shift(lines, frm, to):
    out = []
    for l in lines:
        if not l.strip(): out.append(l); continue
        k = ind(l) - frm + to
        out.append(' ' * max(k, 0) + l.lstrip(' '))
    return out


def flat(units):
    for u in units:
        yield u
        if u.kind == 'loopsub':
            for v in flat(u.sub): yield v


def uses(u, name): return re.search(r'\b' + re.escape(name) + r'\b', u.text()) is not None


def analyze(units, ctx):
    fl = list(flat(units))
    for u in fl: u.restore = []; u.save = []
    for name, (typ, vt) in ctx.outer.items():
        us = [v for v in fl if uses(v, name)]
        if not us: continue
        if vt and len(us) < 2 and not any(v.kind == 'loopsub' for v in fl): continue
        f = ctx.field(name, typ)
        for v in us: v.restore.append((typ, name, f, vt)); v.save.append((name, f))
    for i, u in enumerate(fl):
        if u.kind == 'loopsub' and u.loop[0]:
            var = u.loop[0]
            f = ctx.field(var, 'int')
            u.varfield = f
            for v in flat(u.sub):
                if uses(v, var): v.restore.append(('int', var, f, False))
        for name, (typ, has_init) in u.decls.items():
            later = [v for v in fl[i + 1:] if v.scope[:len(u.scope)] == u.scope and uses(v, name)]
            if not later: continue
            f = ctx.field(name, typ)
            u.save.append((name, f))
            if not has_init:
                for s in u.stmts:
                    for q, l in enumerate(s):
                        m = re.match(r'^(\s*' + TYPES + r'\s+)(.*);(\s*(//.*)?)$', l)
                        if m and ind(l) == u.n and re.search(r'\b' + name + r'\b', m.group(2)):
                            parts = [p + ' = ' + INIT[typ] if p.strip() == name else p for p in split_args(m.group(2))]
                            s[q] = m.group(1) + ', '.join(parts) + ';' + m.group(3)
            for v in later:
                v.restore.append((typ, name, f, False)); v.save.append((name, f))


def exits(lines, ctx, level, saves, in_sub, loop_body=False, decl_after=None):
    """rewrite return (and, in a body that is a function of its own, the break and continue of its loop)"""
    out = []; stack = []; pending = False
    avail_saves = list(saves)
    for idx, l in enumerate(lines):
        c = nocomment(l); tail = l[len(c):]; st = c.strip()
        sv = ' '.join('%s = %s;' % (f, nm) for nm, f in avail_saves if not (decl_after and nm in decl_after and decl_after[nm] >= idx))
        if sv: sv += ' '
        if re.match(r'(for|while|foreach|do|switch)\b', st): pending = True
        nested = any(stack) or (pending and not re.match(r'(for|while|foreach|do|switch)\b', st) and '{' not in st)
        one_line_loop = re.match(r'(for|while|foreach)\s*\(', st) and '{' not in st and st.endswith(';')

        def ret(m):
            x = (m.group(1) or '').strip()
            pre = ''
            if ctx.kind == 'helper' and ctx.ret != 'void': pre = '%s = %s; ' % (ctx.field('ret', ctx.ret), x)
            return '{ ' + pre + sv + ('phReturn = true; ' if level > 0 else '') + 'PhEnd(); return; }'
        c2 = re.sub(r'\breturn\b([^;]*);', ret, c)
        if not nested and not one_line_loop:
            if loop_body:
                c2 = re.sub(r'\bbreak;', '{ PhBreak(); break; }', c2)
            elif in_sub:
                c2 = re.sub(r'\bbreak;', '{ ' + sv + 'phBreak = true; PhEnd(); return; }', c2)
                c2 = re.sub(r'\bcontinue;', '{ ' + sv + 'PhEnd(); return; }', c2)
        for chh in c:
            if chh == '{': stack.append(pending); pending = False
            elif chh == '}':
                if stack: stack.pop()
        if st.endswith(';') and '{' not in c and not re.match(r'(for|while|foreach)\s*\(', st): pending = False
        if one_line_loop: pending = False
        out.append(c2 + tail)
    return out


def emit(units, ctx, k0, level, in_sub, base=8):
    out = []; k = k0
    for u in units:
        p = ' ' * base

        def rs(b):
            r = []
            seen = set()
            for typ, name, f, vt in u.restore:
                if name in seen: continue
                seen.add(name)
                r.append(' ' * b + ('%s = %s;' % (name, f) if vt else '%s %s = %s;' % (typ, name, f)))
            return r

        def sv(b):
            r = []; seen = set()
            for name, f in u.save:
                if name in seen: continue
                seen.add(name); r.append(' ' * b + '%s = %s;' % (f, name))
            return r
        saves = []
        seen = set()
        for name, f in u.save:
            if name not in seen: seen.add(name); saves.append((name, f))
        if u.kind == 'plain':
            if not any(code(s) for s in u.stmts): continue
            out.append(p + 'if (Ph(%d))' % k); out.append(p + '{')
            b = base + 4
            if u.guards:
                out.append(' ' * b + 'if (' + ' && '.join(u.guards) + ')'); out.append(' ' * b + '{'); b += 4
            out += rs(b)
            body = []; decl_after = {}
            for s in u.stmts:
                for nm in decls_of(s, u.n): decl_after[nm] = len(body) + len(s) - 1
                body += s
            # a local declared in this unit can only be saved by an exit after its declaration
            body = exits(body, ctx, level, saves, in_sub, decl_after=decl_after)
            out += shift(body, u.n, b)
            out += sv(b)
            if u.guards: out.append(' ' * (b - 4) + '}')
            out.append(p + '}')
        elif u.kind == 'loop':
            var, a, cond, step, pre, body = u.loop
            out += shift(pre, u.n, base)
            out.append(p + 'if (PhLoop(%d, 100000))' % k); out.append(p + '{')
            b = base + 4
            if u.guards:
                out.append(' ' * b + 'if (!(' + ' && '.join(u.guards) + ')) PhBreak();'); out.append(' ' * b + 'else'); out.append(' ' * b + '{'); b += 4
            out += rs(b)
            if getattr(u, 'chunk', 0):
                fa = ctx.field(var + 'At', 'int') if var else None
                if var:
                    out.append(' ' * b + 'if (planIter == 0) %s = %s;' % (fa, a))
                    out.append(' ' * b + 'int %s = %s;' % (var, fa))
                inc = (', %s += %d' % (var, step)) if var else ''
                out.append(' ' * b + 'for (int chunkN = 0; chunkN < %d; chunkN++%s)   // so many rounds a step' % (u.chunk, inc))
                out.append(' ' * b + '{')
                out.append(' ' * (b + 4) + 'if (!(%s)) { PhBreak(); break; }' % cond)
                out += shift(exits(body, ctx, level, saves, in_sub, loop_body=True), u.n + 4, b + 4)
                out.append(' ' * b + '}')
                if var: out.append(' ' * b + '%s = %s;' % (fa, var))
                out += sv(b)
                if u.guards: out.append(' ' * (b - 4) + '}')
                out.append(p + '}')
                k += 1
                continue
            if var:
                it = 'planIter' if step == 1 else ('planIter * %d' % step if step > 0 else '- planIter' + (' * %d' % -step if step != -1 else ''))
                if re.fullmatch(r'-?\d+', a.strip()):
                    out.append(' ' * b + 'int %s = %s;' % (var, it if a.strip() == '0' and step > 0 else a.strip() + (' + ' if step > 0 else ' ') + it))
                else:
                    f0 = ctx.field(var + '0', 'int')
                    out.append(' ' * b + 'if (planIter == 0) %s = %s;' % (f0, a))
                    out.append(' ' * b + 'int %s = %s%s%s;' % (var, f0, ' + ' if step > 0 else ' ', it))
            out.append(' ' * b + 'if (!(%s)) PhBreak();' % cond)
            out.append(' ' * b + 'else for (int once = 0; once < 1; once++)   // one pass: break and continue leave it')
            out.append(' ' * b + '{')
            out += shift(exits(body, ctx, level, saves, in_sub, loop_body=True), u.n + 4, b + 4)
            out.append(' ' * b + '}')
            out += sv(b)
            if u.guards: out.append(' ' * (b - 4) + '}')
            out.append(p + '}')
        elif u.kind == 'loopsub':
            var, a, cond, step, pre, body = u.loop
            ctx.subn += 1
            fn = '%s_L%d' % (ctx.fname, ctx.subn)
            sub, kk = emit(u.sub, ctx, 0, level + 1, True)
            ctx.subfns.append((fn, sub, kk))
            out += shift(pre, u.n, base)
            out.append(p + 'if (PhLoop(%d, 100000))' % k); out.append(p + '{')
            b = base + 4
            if u.guards:
                out.append(' ' * b + 'if (!(' + ' && '.join(u.guards) + ')) PhBreak();'); out.append(' ' * b + 'else'); out.append(' ' * b + '{'); b += 4
            out += rs(b)
            out.append(' ' * b + 'if (!PhSubActive())'); out.append(' ' * b + '{')
            c = b + 4
            if var:
                it = 'planIter' if step == 1 else ('planIter * %d' % step if step > 0 else '- planIter' + (' * %d' % -step if step != -1 else ''))
                if re.fullmatch(r'-?\d+', a.strip()):
                    out.append(' ' * c + 'int %s = %s;' % (var, it if a.strip() == '0' and step > 0 else a.strip() + (' + ' if step > 0 else ' ') + it))
                else:
                    f0 = ctx.field(var + '0', 'int')
                    out.append(' ' * c + 'if (planIter == 0) %s = %s;' % (f0, a))
                    out.append(' ' * c + 'int %s = %s%s%s;' % (var, f0, ' + ' if step > 0 else ' ', it))
            out.append(' ' * c + 'if (!(%s)) PhBreak();' % cond)
            out.append(' ' * c + 'else { ' + ('%s = %s; ' % (u.varfield, var) if var else '') + 'PhSubBegin(); }')
            out.append(' ' * b + '}')
            out += sv(b)
            out.append(' ' * b + 'if (PhSubActive())'); out.append(' ' * b + '{')
            out.append(' ' * c + 'PhPush(); %s(); bool fin = PhAdvance(); PhPop();' % fn)
            out.append(' ' * c + 'if (fin) PhSubEnd(); else PhStay();')
            out.append(' ' * c + 'if (phBreak) { phBreak = false; PhBreak(); }')
            out.append(' ' * c + 'if (phReturn) { ' + ('phReturn = false; ' if level == 0 else '') + 'PhEnd(); return; }')
            out.append(' ' * b + '}')
            if u.guards: out.append(' ' * (b - 4) + '}')
            out.append(p + '}')
        else:
            pre, typ, target, h, args = u.call
            ret, params = STEPPED[h]
            out += shift(pre, u.n, base)
            out.append(p + 'if (PhLoop(%d, 100000))' % k); out.append(p + '{')
            b = base + 4
            if u.guards:
                out.append(' ' * b + 'if (!(' + ' && '.join(u.guards) + ')) PhBreak();'); out.append(' ' * b + 'else'); out.append(' ' * b + '{'); b += 4
            out += rs(b)
            if typ: out.append(' ' * b + '%s %s = %s;' % (typ, target, INIT[typ]))
            sets = []; back = []
            for arg, (mod, pt, pn) in zip(args, params):
                f = 'hq_%s_%s' % (h, pn)
                if mod == 'ref': sets.append('%s = %s;' % (f, arg[3:].strip())); back.append('%s = %s;' % (arg[3:].strip(), f))
                elif mod == 'out': back.append('%s = %s;' % (arg[3:].strip(), f))
                else: sets.append('%s = %s;' % (f, arg))
            out.append(' ' * b + 'if (planIter == 0) { ' + ' '.join(sets) + ' PhSubReset(); }')
            out.append(' ' * b + 'PhPush(); %s_Body(); bool fin = PhAdvance(); PhPop();' % h)
            if target: back.append('%s = hq_%s_ret;' % (target, h))
            out.append(' ' * b + 'if (fin) { ' + ' '.join(back) + ' PhBreak(); }')
            out += sv(b)
            if u.guards: out.append(' ' * (b - 4) + '}')
            out.append(p + '}')
        k += 1
    return out, k


def fast(lines, k):
    """the generated phase tests as comparisons; planMax set once, at the top"""
    out = ['        planMax = %d;' % (k - 1)]
    for l in lines:
        m = re.match(r'^        if \(Ph\((\d+)\)\)$', l)
        if m: l = '        if (planPhase == %s)' % m.group(1)
        else:
            m = re.match(r'^        if \(PhLoop\((\d+), 100000\)\)$', l)
            if m: l = '        if (planPhase == %s && PhLoop(%s, 100000))' % (m.group(1), m.group(1))
        out.append(l)
    return out


def subfn_text(ctx):
    out = []
    for fn, lines, kk in ctx.subfns:
        out.append('')
        out.append('    void %s()' % fn)
        out.append('    {')
        for typ, name in ctx.params: out.append('        %s %s = hq_%s_%s;' % (typ, name, ctx.fname, name))
        out += ctx.head
        out += fast(lines, kk)
        out.append('    }')
    return out


def unphase(lines):
    """the loops an earlier splitter made (`int v = planIter; if (!(C)) PhBreak(); else for (once) { }`) back into loops"""
    out = []; i = 0
    while i < len(lines):
        l = lines[i]
        if re.match(r'^        if \(PhLoop\(\d+, 100000\)\)$', l) and i + 5 < len(lines) and lines[i + 1] == '        {':
            m1 = re.match(r'^            int (\w+) = planIter;$', lines[i + 2])
            m2 = re.match(r'^            if \(!\((.*)\)\) PhBreak\(\);$', lines[i + 3])
            m3 = re.match(r'^            else for \(int once = 0; once < 1; once\+\+\)', lines[i + 4])
            if m1 and m2 and m3 and lines[i + 5] == '            {':
                j = i + 6
                while j < len(lines) and lines[j] != '            }': j += 1
                if j + 1 < len(lines) and lines[j + 1] == '        }':
                    body = [x.replace('{ PhBreak(); break; }', 'break;') for x in lines[i + 6:j]]
                    out.append('        for (int %s = 0; %s; %s++)' % (m1.group(1), m2.group(1), m1.group(1)))
                    out.append('        {')
                    out += shift(body, 16, 12)
                    out.append('        }')
                    i = j + 2
                    continue
        m = re.match(r'^(        if \(Ph\(\d+\)\)) (\w.*;)\s*$', l)
        if m and not re.search(r'\b(planIter|PhBreak|PhLoop|planBroken)\b', m.group(2)):
            out += [m.group(1), '        {', '            ' + m.group(2), '        }']
            i += 1; continue
        out.append(l); i += 1
    return out


def split_plan(name, params, body):
    """body: the function's lines (between its braces). Returns (new lines, ctx, phases before, after) or None."""
    ctx = Ctx(name, 'plan')
    for p in split_args(params):
        typ, nm = p.split()
        ctx.params.append((typ, nm))
    lines = list(body)
    out = []; k = 0; before = 0
    pre_lines = ['        hq_%s_%s = %s;' % (name, nm, nm) for typ, nm in ctx.params]
    for typ, nm in ctx.params: ctx.fields['hq_%s_%s' % (name, nm)] = typ
    if not re.search(r'\bPh(Loop)?\(', '\n'.join(lines)):
        head = []
        while lines and re.match(r'^        int room = pRoom', lines[0]):
            raw = re.match(r'^        int (.+);', nocomment(lines[0]).rstrip())
            if not raw or any('=' not in part for part in split_args(raw.group(1))) or re.search(r'\b[A-Z]\w+\(', raw.group(1).replace('AreaM2(', '')): break
            head.append(lines.pop(0))
        ctx.head = list(head)
        units = []
        build(statements(lines, 8), 8, [], (), units, ctx)
        analyze(units, ctx)
        new, k = emit(units, ctx, 0, 0, False)
        if k < 2 and not ctx.subfns and all(u.kind == 'plain' for u in units): return None
        return pre_lines + head + fast(new, k), ctx, 1, k
    lines = unphase(lines)
    for s in statements(lines, 8):
        for nm, (typ, has_init) in decls_of(s, 8).items():
            if not has_init: ctx.outer[nm] = (typ, True)
    for i, l in enumerate(lines):
        m = re.match(r'^(        ' + TYPES + r'\s+)(.*);(\s*(//.*)?)$', l)
        if m and any(re.search(r'\b' + nm + r'\b', m.group(2)) for nm in ctx.outer):
            parts = [p + ' = ' + INIT[ctx.outer[p.strip()][0]] if p.strip() in ctx.outer else p for p in split_args(m.group(2))]
            lines[i] = m.group(1) + ', '.join(parts) + ';' + m.group(3)
    head_ok = True; changed = False; opaque_return = False
    for s in statements(lines, 8):
        c = code(s)
        if not c: out += s; continue
        first = nocomment(c[0]).rstrip()
        m = re.match(r'^        if \(Ph\((\d+)\)\)$', first)
        inner_txt = '\n'.join(nocomment(l) for l in c[1:])
        is_block = m and len(c) > 2 and c[1].strip() == '{' and c[-1].strip() == '}' and ind(c[-1]) == 8 and not re.search(r'\b(planIter|PhBreak|PhLoop|Ph|planBroken)\b', inner_txt)
        is_loop = (FOR.match(first) or WHILE.match(first)) and ind(c[0]) == 8
        if is_block or is_loop:
            before += 1
            units = []
            ctx.sid += 1
            n0 = len(ctx.subfns)
            if is_block:
                i0 = s.index(c[1]); i1 = len(s) - 1 - s[::-1].index(c[-1])
                pre = s[:s.index(c[0])]
                build(statements(s[i0 + 1:i1], 12), 12, [], (ctx.sid,), units, ctx)
            else:
                pre = []
                build([s], 8, [], (ctx.sid,), units, ctx)
            analyze(units, ctx)
            if any(u.kind != 'plain' for u in units): changed = True
            new, k = emit(units, ctx, k, 0, False)
            if len(ctx.subfns) > n0 and not head_ok: return None
            out += pre + new
            continue
        if re.match(r'^        if \(Ph(Loop)?\(\d+\b', first):
            before += 1
            if re.search(r'\breturn\b', '\n'.join(nocomment(l) for l in c)): opaque_return = True
            i = s.index(c[0])
            s = list(s); s[i] = re.sub(r'\b(Ph(?:Loop)?)\(\d+\b', lambda mm: '%s(%d' % (mm.group(1), k), s[i], count=1)
            k += 1
            out += s
            continue
        if re.search(r'\bPh(Loop)?\(', '\n'.join(nocomment(l) for l in c)): return None   # a shape this tool does not know
        d = decls_of(s, 8)
        if d and len(c) == 1: ctx.head += s
        else: head_ok = False
        out += s
    if k == before and not ctx.subfns and not changed: return None
    if not opaque_return: out = fast(out, k)
    return pre_lines + out, ctx, before, k


def split_helper(name, ret, params, body):
    ctx = Ctx(name, 'helper', ret)
    for mod, typ, nm in params: ctx.outer[nm] = (typ, False); ctx.fields['hq_%s_%s' % (name, nm)] = typ
    if ret != 'void': ctx.field('ret', ret)
    units = []
    build(statements(list(body), 8), 8, [], (), units, ctx)
    analyze(units, ctx)
    new, k = emit(units, ctx, 0, 0, False)
    if k < 2 and not ctx.subfns and all(u.kind == 'plain' for u in units): return None
    w = []
    for mod, typ, nm in params:
        if mod != 'out': w.append('        hq_%s_%s = %s;' % (name, nm, nm))
    w.append('        PhSubReset();')
    w.append('        bool fin = false;')
    w.append('        while (!fin) { PhPush(); %s_Body(); fin = PhAdvance(); PhPop(); }' % name)
    for mod, typ, nm in params:
        if mod in ('ref', 'out'): w.append('        %s = hq_%s_%s;' % (nm, name, nm))
    if ret != 'void': w.append('        return hq_%s_ret;' % name)
    w += ['    }', '', '    // %s in phases, a step a call (autophase4); %s() above runs them to the end' % (name, name), '    void %s_Body()' % name, '    {']
    return w + fast(new, k), ctx, 1, k


def run(files, exclude, only, plans):
    """one pass over the sources (in memory); returns (new files, converted helpers, log)"""
    out_files = {}; done = set(); log = []
    for p, s in files.items():
        pos = 0; out = []; fields = {}
        spans = []
        for m in re.finditer(r'\n    void (Plan\w+)\(([^)]*)\)\n    \{\n', s):
            if m.group(1) in plans and m.group(1) not in exclude and (not only or m.group(1) in only): spans.append((m, 'plan'))
        for m in re.finditer(r'\n    (int|bool|void) (\w+)\(([^)]*)\)\n    \{\n', s):
            if m.group(2) in STEPPED and m.group(2) not in exclude and (not only or m.group(2) in only): spans.append((m, 'helper'))
        spans.sort(key=lambda x: x[0].start())
        for m, kind in spans:
            start = m.end(); depth = 1; i = start
            while depth:
                if s[i] == '{': depth += 1
                elif s[i] == '}': depth -= 1
                i += 1
            end = s.rindex('\n', start, i) + 1     # the line of the closing brace
            body = s[start:end].rstrip('\n').split('\n')
            if kind == 'plan':
                name = m.group(1)
                r = split_plan(name, m.group(2), body)
            else:
                name = m.group(2)
                r = split_helper(name, STEPPED[name][0], STEPPED[name][1], body)
            if not r: continue
            new, ctx, before, after = r
            fields.update(ctx.fields)
            tail = subfn_text(ctx)
            out.append(s[pos:start]); out.append('\n'.join(new) + '\n')
            # the closing brace, then the bodies that became functions
            out.append(s[end:i]); out.append('\n'.join(tail) + ('\n' if tail else '')); pos = i
            if tail and s[i] == '\n': pass
            done.add(name)
            log.append('%s %d -> %d%s' % (name, before, after, ' +%d sub' % len(ctx.subfns) if ctx.subfns else ''))
        out.append(s[pos:]); s2 = ''.join(out)
        if fields:
            j = s2.rstrip().rfind('}')
            s2 = s2[:j] + '\n    // locals of the split plans and helpers, kept from one phase to the next (autophase4)\n' + ''.join('    %s %s;\n' % (t, f) for f, t in sorted(fields.items())) + s2[j:]
        out_files[p] = s2
    return out_files, done, log


def main():
    exclude = set(sys.argv[1].split(',')) if len(sys.argv) > 1 and sys.argv[1] else set()
    only = set(sys.argv[2].split(',')) if len(sys.argv) > 2 and sys.argv[2] else None
    files = {p: open(p, encoding='utf8').read() for p in sorted(glob.glob('MapGen*.cs'))}
    plans = set(re.findall(r'case GenData\.R_\w+: (Plan\w+)\(', files['MapGenPlace.cs']))
    allsrc = '\n'.join(files.values())
    want = [h for h in STEPPED_WANT if h not in exclude and (not only or h in only)]
    while True:
        STEPPED.clear()
        for h in want:
            ms = re.findall(r'\n    (int|bool|void) ' + h + r'\(([^)]*)\)\n    \{\n', allsrc)
            if len(ms) != 1: print('skip helper', h, len(ms)); continue
            params = []
            ok = True
            for a in split_args(ms[0][1]):
                mm = re.match(r'^(?:(ref|out|params)\s+)?(' + TYPES + r')\s+(\w+)$', a)
                if not mm: ok = False; break
                params.append((mm.group(1) or '', mm.group(2), mm.group(3)))
            if ok: STEPPED[h] = (ms[0][0], params)
            else: print('skip helper', h, 'params')
        new, done, log = run(files, exclude, only, plans)
        failed = [h for h in STEPPED if h not in done]
        if not failed: break
        print('not split, left as they are:', failed)
        want = [h for h in want if h in STEPPED and h not in failed]
    for l in log: print(l)
    for p, s in new.items():
        if s != files[p]: open(p, 'w', encoding='utf8', newline='').write(s)
    print('split', len(log))


if __name__ == '__main__':
    main()
