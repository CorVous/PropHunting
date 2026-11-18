"""Statement shapes rewritten into the ones the splitter opens (same behaviour):
   if (C) a;  /  if (C) { a; b; }  /  if (C) a; else b;   (one-line branches)  ->  branches in brace blocks
   for (...) a;  /  while (C) a;   (body on the header's line)                ->  header, then the body below it
Only where a helper in phases is called in them."""
import re


def nocomment(l):
    i = l.find('//')
    while i >= 0:
        if l[:i].count('"') % 2 == 0: return l[:i]
        i = l.find('//', i + 2)
    return l


def ind(l): return len(l) - len(l.lstrip(' '))


def match_paren(s, i):
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


def simple_parts(rest):
    """`a; b;` or `{ a; b; }` -> ['a;', 'b;'], or None if it is not a run of simple statements"""
    rest = rest.strip()
    if rest.startswith('{') and rest.endswith('}'): rest = rest[1:-1].strip()
    if not rest or not rest.endswith(';'): return None
    depth = 0
    for ch in rest:   # a brace outside any parentheses is a block: not a simple statement
        if ch == '(': depth += 1
        elif ch == ')': depth -= 1
        elif ch in '{}' and depth == 0: return None
    if re.search(r'\b(if|else|for|while|switch|foreach|do)\b', rest): return None
    parts = []; depth = 0; part = ''; q = False
    for ch in rest:
        if ch == '"': q = not q
        if not q:
            if ch in '({[': depth += 1
            if ch in ')}]': depth -= 1
        part += ch
        if ch == ';' and depth == 0 and not q: parts.append(part.strip()); part = ''
    if part.strip(): return None
    return parts


def normalize(s, n, stepped):
    """s: a statement's lines at indent n. Returns the rewritten lines, or s."""
    code = [l for l in s if nocomment(l).strip()]
    if not code or ind(code[0]) != n: return s
    txt = '\n'.join(nocomment(l) for l in code)
    if not any(re.search(r'\b' + h + r'\(', txt) for h in stepped): return s
    i0 = s.index(code[0])
    pad = ' ' * n
    first = nocomment(code[0]).rstrip()
    # a loop with its body on the header's line
    m = re.match(r'^\s*(for|while) \(', first)
    if m and len(code) == 1:
        e = match_paren(first, m.end() - 1)
        body = first[e + 1:].strip() if e > 0 else ''
        if body and body != '{' and body.endswith(';'):
            return s[:i0] + [first[:e + 1], pad + '    ' + body]
        return s
    if not re.match(r'^\s*if \(', first): return s
    # segments: the if, then each else at indent n
    segs = []
    for l in code:
        st = nocomment(l).strip()
        if ind(l) == n and (not segs or st.startswith('else')): segs.append([l])
        else: segs[-1].append(l)
    out = []
    for g in segs:
        head = nocomment(g[0]).rstrip()
        st = head.strip()
        if st.startswith('else if (') or st.startswith('if ('):
            o = head.index('(')
            e = match_paren(head, o)
            if e < 0: return s
            hd, rest = head[:e + 1], head[e + 1:]
        elif st.startswith('else'):
            o = head.index('else') + 4
            hd, rest = head[:o], head[o:]
        else: return s
        restlines = [rest] + [nocomment(x) for x in g[1:]]
        joined = ' '.join(x.strip() for x in restlines).strip()
        if joined == '' or joined.startswith('{') and len(g) > 1: return s    # already a block (or not one we know)
        parts = simple_parts(joined)
        if parts is None: return s
        out.append(hd); out.append(pad + '{')
        out += [pad + '    ' + x for x in parts]
        out.append(pad + '}')
    return s[:i0] + out
