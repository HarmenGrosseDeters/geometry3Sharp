"""The callers of AdaptivePredicates' public stages must check the range of the expansions first (AdaptivePredicates'
precondition: every coordinate 0 or in [2^-100, 2^100], Expansion.InRange; tools/Z3Pilot/adaptive_range.py proves what
that range gives). Standard library only, so that the check runs with the ErrorBoundAudit tests in the `linux` job of
ci.yml on every library change (the Z3 pilot runs only on its paths).

A call is guarded when one of the three lines before it is `if (Expansion.InRange(<args>) == false)` and every
argument of the call is one of the guard's arguments. KNOWN_UNGUARDED lists the exceptions (docs/qa/exact-bound-inventory.md);
a new one fails.
"""
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]

# file -> number of unguarded calls; each an exception with an issue (docs/qa/exact-bound-inventory.md section 6)
KNOWN_UNGUARDED = {
    'mesh_generators/DualContouring.cs': 2,         # place_face_vertex, issue #1589
}

# every call of any member of AdaptivePredicates is found (a name not listed here fails as unguarded, so a new entry point cannot be missed)
ANY_CALL = re.compile(r'AdaptivePredicates\.(\w+)\(')
GUARDED_NAMES = {'Orient2d', 'Orient3d', 'InCircle', 'Orient2dStageD', 'Orient3dStageD', 'InCircleStageD'}
GUARD = re.compile(r'if\s*\(\s*Expansion\.InRange\(')


def _balanced(text, start):
    """the text between the parenthesis at text[start - 1] and its partner (nested parentheses counted), or None if it never closes"""
    depth = 1
    for j in range(start, len(text)):
        if text[j] == '(':
            depth += 1
        elif text[j] == ')':
            depth -= 1
            if depth == 0:
                return text[start:j]
    return None


def _args(text):
    """the arguments split at the commas of the top level (a comma inside nested parentheses stays in its argument)"""
    out, depth, cur = [], 0, []
    for ch in text:
        if ch == ',' and depth == 0:
            out.append(''.join(cur).strip())
            cur = []
            continue
        depth += (ch == '(') - (ch == ')')
        cur.append(ch)
    out.append(''.join(cur).strip())
    return [a for a in out if a]


def _guard_args(window):
    """the argument sets of the guards `if (Expansion.InRange(<args>) == false)` in the window"""
    out = []
    for g in GUARD.finditer(window):
        inner = _balanced(window, g.end())
        if inner is not None and re.match(r'\s*==\s*false\s*\)', window[g.end() + len(inner) + 1:]):
            out.append(set(_args(inner)))
    return out


def unguarded_calls(root=ROOT):
    """{file: count} of the library's calls of AdaptivePredicates without a guard; a call may span lines and have nested parentheses
    in its arguments; a call whose parentheses never close or whose name is not a known stage counts as unguarded"""
    out = {}
    for path in sorted(root.rglob('*.cs')):
        rel = path.relative_to(root).as_posix()
        if rel.startswith(('tests/', 'tools/', 'benchmarks/', 'addons/')) or '/obj/' in rel or '/bin/' in rel:
            continue
        text = path.read_text(encoding='utf-8', errors='replace')
        lines = text.split('\n')
        starts = [0]
        for line in lines:
            starts.append(starts[-1] + len(line) + 1)
        for m in ANY_CALL.finditer(text):
            inner = _balanced(text, m.end()) if m.group(1) in GUARDED_NAMES else None
            guarded = False
            if inner is not None:
                call = set(_args(inner))
                i = max(k for k in range(len(starts)) if starts[k] <= m.start())      # the line of the call
                window = '\n'.join(lines[max(0, i - 3):i])
                guarded = any(call <= g for g in _guard_args(window))
            if not guarded:
                out[rel] = out.get(rel, 0) + 1
    return out
