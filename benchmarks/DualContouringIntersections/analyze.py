#!/usr/bin/env python3
"""Summary tables of DualContouringIntersections runs.

usage: python3 analyze.py <out.jsonl>... [--per-input]
Rows with the diagnostics hook on give the counts table; rows from --timing runs (hook off) give the timing table.
Groups by input family (T1 / mesh:procedural / imp:<shape> without the @seed) and resolution."""
import collections, json, sys

CLASSES = ['envelope_in', 'envelope_out', 'multipatch', 'transition']


def family(d):
    n = d['input']
    if n.startswith('imp:'):
        return n.split('@')[0]
    if n.startswith('mesh:'):
        return 'mesh:procedural'
    return 'T1'


def groups(rows, per_input):
    g = collections.OrderedDict()
    for d in sorted(rows, key=lambda d: (family(d), d['res'], d['input'])):
        g.setdefault((d['input'] if per_input else family(d), d['res']), []).append(d)
    return g


def main():
    files = [a for a in sys.argv[1:] if not a.startswith('--')]
    per_input = '--per-input' in sys.argv
    rows = [json.loads(l) for f in files for l in open(f)]
    diag = [d for d in rows if d.get('diagnostics', True)]
    timing = [d for d in rows if not d.get('diagnostics', True)]
    if diag:
        print('| input | res | runs | raw SI runs | SI pairs | left after repair (runs) | final SI (runs) | non-manifold tris (runs) '
              '| moved / vertices | multi-patch polys | 1 / 2 concave (of uniform single-patch quads) '
              '| SI triangles in / out / multi-patch / transition | covered / all pairs |')
        print('|' + '---|' * 13)
        for (name, res), g in groups(diag, per_input).items():
            s = lambda k: sum(d.get(k, 0) for d in g)
            n = lambda pred: sum(1 for d in g if pred(d))
            polys = max(s('polys'), 1)
            uniform = max(s('polys') - s('multipatch_polys') - s('transition_polys') - s('degenerate_polys'), 1)
            cls = ' / '.join(str(s('si_tris_' + c)) for c in CLASSES)
            print(f"| {name} | {res} | {len(g)} | {n(lambda d: d.get('si_found', 0) > 0)} | {s('si_found')} | {n(lambda d: d.get('si_left', 0) > 0)} "
                  f"| {n(lambda d: d.get('final_si', 0) > 0)} | {s('nonmanifold_tris')} ({n(lambda d: d.get('nonmanifold_tris', 0) > 0)}) "
                  f"| {s('resolve_moved')} / {s('verts')} | {100 * s('multipatch_polys') / polys:.2f} % "
                  f"| {100 * s('concave1') / uniform:.2f} % / {100 * s('concave2') / uniform:.3f} % | {cls} | {s('covered_pairs')} / {s('si_pairs_diag')} |")
        notin = sum(d.get('verts_not_in_cell', 0) for d in diag)
        print(f"\nvertices not strictly inside their cell: {notin} of {sum(d.get('verts', 0) for d in diag)}; "
              f"quads with 3 or 4 concave vertices: {sum(d.get('concave3', 0) + d.get('concave4', 0) for d in diag)}")
        for d in diag:
            if 'stage_si_contour' in d:
                print(f"stages of {d['input']} at {d['res']}: final SI {d['final_si']}, contour (with hole fill) {d['stage_si_contour']}, "
                      f"after cleanup {d['stage_si_cleanup']}, non-manifold triangles left out {d['stage_nonmanifold_tris']}, "
                      f"last DC pass SI left {d['si_left']}")
    if timing:
        print('\n| input | res | runs | total s | DC phases s | check + repair s | share of DC | share of total |')
        print('|' + '---|' * 8)
        for (name, res), g in groups(timing, per_input).items():
            tot = sum(d.get('ms', 0) for d in g)
            dc = sum(v for d in g for k, v in d.items() if k.startswith('dc_ms_'))
            rs = sum(d.get('resolve_ms', 0) for d in g)
            print(f"| {name} | {res} | {len(g)} | {tot / 1000:.1f} | {dc / 1000:.1f} | {rs / 1000:.1f} | {100 * rs / max(dc, 1):.0f} % | {100 * rs / max(tot, 1):.0f} % |")
    bad = [d for d in rows if d.get('status') != 'ok']
    if bad:
        print('\nnot ok:', [(d['input'], d['res'], d.get('status'), d.get('error')) for d in bad])


if __name__ == '__main__':
    main()
