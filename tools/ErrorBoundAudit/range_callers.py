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

CALL = re.compile(r'AdaptivePredicates\.(Orient2d|Orient3d|InCircle)\(([^)]*)\)')
GUARD = re.compile(r'if \(Expansion\.InRange\(([^)]*)\) == false\)')


def _args(text):
    return [a.strip() for a in text.split(',') if a.strip()]


def unguarded_calls(root=ROOT):
    """{file: count} of the library's calls of AdaptivePredicates' public stages without a guard"""
    out = {}
    for path in sorted(root.rglob('*.cs')):
        rel = path.relative_to(root).as_posix()
        if rel.startswith(('tests/', 'tools/', 'benchmarks/', 'addons/')) or '/obj/' in rel or '/bin/' in rel:
            continue
        lines = path.read_text(encoding='utf-8', errors='replace').split('\n')
        for i, line in enumerate(lines):
            for m in CALL.finditer(line):
                call = set(_args(m.group(2)))
                guarded = False
                for k in range(max(0, i - 3), i):
                    g = GUARD.search(lines[k])
                    if g and call <= set(_args(g.group(1))):
                        guarded = True
                if not guarded:
                    out[rel] = out.get(rel, 0) + 1
    return out
