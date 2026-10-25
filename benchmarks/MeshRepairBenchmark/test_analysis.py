#!/usr/bin/env python3
"""Checks of the analysis scripts on synthetic result rows (no meshes needed): they count the same meshes as
repaired, and failed runs stay in the denominators. Usage: python3 test_analysis.py"""
import json, os, re, subprocess, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))


def row(name, **kw):
    r = {'file': name, 'status': 'closed', 'after_valid': True, 'after_bowties': 0, 'after_inconsistent_orient_edges': 0,
         'after_duplicate_tris': 0, 'after_degenerate_tris': 0, 'after_inverted_components': 0, 'after_selfint_exact': 0,
         'hd_fwd_hi_rel': 0.001, 'hd_back_hi_rel': 0.001, 'iou': 0.999, 'iou_missing': 0.001, 'after_tris': 100,
         'before_tris': 100, 'repair_ms': 10, 'robust_used_exact': False}
    for m in ['boundary_edges', 'bowties', 'inconsistent_orient_edges', 'duplicate_tris', 'degenerate_tris',
              'folded_edges_gt175deg', 'self_intersecting_pairs', 'inverted_components', 'sliver_tris']:
        r['before_' + m] = r.setdefault('after_' + m, 0)
    r.update(kw)
    return r


ROWS = [
    row('ok.stl'),
    row('nosolid.stl', iou='NaN'),                          # input without a solid: passes the IoU
    row('large.stl', after_selfint_exact=-1),               # self-intersections not computed: not repaired
    row('noiou.stl', iou=None),                             # IoU missing: not repaired
    row('selfint.stl', after_selfint_exact=3),
    row('far.stl', hd_back_hi_rel=0.02),
    {'file': 'crash.stl', 'status': 'timeout_or_crash'},    # no result
]


def run(script, *args):
    with tempfile.TemporaryDirectory() as d:
        f = os.path.join(d, 'robust.jsonl')
        with open(f, 'w') as o:
            for r in ROWS:
                o.write(json.dumps({k: v for k, v in r.items() if v is not None}) + '\n')
        p = subprocess.run([sys.executable, os.path.join(HERE, script), f] + list(args), cwd=d, capture_output=True, text=True)
        if p.returncode != 0:
            raise AssertionError(f"{script} failed:\n{p.stderr}")
        return p.stdout


class AnalysisScripts(unittest.TestCase):
    def test_fidelity_repaired(self):
        out = run('analyze_fidelity.py')
        self.assertRegex(out, r'faithful .*:\s+2\s+<- repaired')

    def test_paths_repaired_same_as_fidelity(self):
        out = run('analyze_paths.py')
        line = next(l for l in out.splitlines() if l.startswith('repair '))
        cells = line.split()
        # stage, meshes, repaired, self-int. not checked, ...
        self.assertEqual(cells[1], '6')
        self.assertEqual(cells[2], '2')
        self.assertEqual(cells[3], '1')

    def test_wrap_repaired_same_as_fidelity(self):
        out = run('analyze_wrap.py')
        line = next(l for l in out.splitlines() if l.startswith('repaired'))
        self.assertEqual(line.split()[-1], '2')
        line = next(l for l in out.splitlines() if l.startswith('guarantees met'))
        self.assertEqual(line.split()[-1], '4')     # not the one with 3 self-intersections nor the unchecked one

    def test_fidelity_without_any_iou(self):
        global ROWS
        old = ROWS
        try:
            ROWS = [row('a.stl', iou='NaN'), row('b.stl', iou='NaN')]
            self.assertRegex(run('analyze_fidelity.py'), r'<- repaired')
        finally:
            ROWS = old

    def test_failed_runs_in_the_denominators(self):
        out = run('analyze_quality.py')
        self.assertIn('7 meshes, 6 with results', out)
        self.assertRegex(out, r'topologically clean .*: 6/7')
        out = run('compare_modes.py')
        self.assertRegex(out, r'meshes\s+7')
        self.assertRegex(out, r'without result \(exception, timeout\)\s+1')


if __name__ == '__main__':
    unittest.main()
