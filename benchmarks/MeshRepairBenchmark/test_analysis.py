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

# the further measures of analyze_fidelity.py: flags on repaired meshes
FM = dict(fm_ok=True, fm_f_0001=0.9, fm_f_0005=1.0, fm_f_001=1.0, fm_chamfer=1e-4, fm_nc=0.99, fm_reversed_fwd=0.0,
          fm_ecd=1e-3, fm_er_0005=1.0, fm_ep_0005=1.0, fm_sharp_in=2.0, fm_topo_changes='None',
          fm_q_out={'tris': 100, 'minangle_lt10': 0, 'minangle': [5, 20, 25, 40]}, fm_size=1.0, fm_bbox_diag=1.0,
          fm_outer_max=0.001, fm_of_001=1.0, fm_outer_share=1.0, fm_max_back=0.001, fm_oviou=0.999,
          fm_topo_in={'components': 1, 'closed': 1, 'bowties': 0},
          fm_q_in={'tris': 100, 'minangle_lt10': 0})
FM_ROWS = [
    row('fm_ok.stl', **FM),
    row('fm_reversed.stl', **dict(FM, fm_reversed_fwd=0.2)),
    row('fm_edges.stl', **dict(FM, fm_er_0005=0.5, fm_oer_0005=0.5)),
    row('fm_union.stl', **dict(FM, fm_er_0005=0.5, fm_oer_0005=1.0)),      # edges inside the solid: not lost
    row('fm_noouter.stl', **dict(FM, fm_er_0005=0.2, fm_oer_0005='NaN')),  # no outer edges: judged by all edges
    row('fm_smooth.stl', **dict(FM, fm_er_0005='NaN', fm_sharp_in=0.0, fm_ecd='Infinity')),   # no sharp edges in the input
    row('fm_handle.stl', **dict(FM, fm_topo_changes='HandlesClosed, BoundaryLoopsRemoved')),
    row('fm_far.stl', **dict(FM, fm_reversed_fwd=0.5, hd_back_hi_rel=0.02, fm_outer_max=0.02, fm_max_back=0.02)),              # flagged, not repaired
    row('fm_interior.stl', **dict(FM, hd_back_hi_rel=0.03, fm_max_back=0.03, fm_outer_max=0.002)),  # dropped interior faces
    row('fm_openin.stl', **dict(FM, fm_topo_changes='HandlesClosed', fm_topo_in={'components': 1, 'closed': 0, 'bowties': 0},
                                fm_q_in={'tris': 100, 'minangle_lt10': 30}, fm_q_out={'tris': 100, 'minangle_lt10': 30, 'minangle': [1, 2, 3, 4]})),
    row('fm_slivers.stl', **dict(FM, fm_q_out={'tris': 100, 'minangle_lt10': 5, 'minangle': [1, 2, 3, 4]})),
    # the winding number IoU distorted by interior faces, the outer faces' IoU not; and the other way round
    row('fm_layers.stl', **dict(FM, iou=0.95, fm_oviou=0.999)),
    row('fm_lostcavity.stl', **dict(FM, fm_oviou=0.9)),
    row('fm_nosolid.stl', **dict(FM, iou='NaN', fm_oviou='NaN')),
    row('fm_oldrun.stl', **{k: v for k, v in FM.items() if k != 'fm_oviou'}),   # older run: judged by the winding number IoU
    row('fm_unconverged.stl', **dict(FM, fm_oviou=0.5, fm_outer_converged=False)),   # unfinished rounds: likewise
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

    def test_fidelity_flags(self):
        global ROWS
        old = ROWS
        try:
            ROWS = FM_ROWS
            out = run('analyze_fidelity.py')
        finally:
            ROWS = old
        self.assertRegex(out, r'16 meshes with them, 13 of them repaired')
        self.assertRegex(out, r'triangles\s+1 /\s+1 ')     # not the open input whose slivers were there before
        self.assertRegex(out, r'from the whole input: 13, from its outer surface: 14 \(\+1 \[.fm_interior.stl.\]\)')
        self.assertRegex(out, r"IoU against the solid of the input's outer faces: 14 \(\+1 \[.fm_layers.stl.\], -1 \[.fm_lostcavity.stl.\]\)")
        self.assertRegex(out, r"repaired \(certified Hausdorff\) with the IoU against the outer faces' solid: 13 \(with the winding number IoU: 13\)")
        self.assertRegex(out, r'reversed\s+2 /\s+1 ')
        self.assertRegex(out, r'edges\s+2 /\s+2 ')
        self.assertRegex(out, r'handles\s+1 /\s+1 ')
        self.assertRegex(out, r'repaired with any flag: 5 of 13')

    def test_failed_runs_in_the_denominators(self):
        out = run('analyze_quality.py')
        self.assertIn('7 meshes, 6 with results', out)
        self.assertRegex(out, r'topologically clean .*: 6/7')
        out = run('compare_modes.py')
        self.assertRegex(out, r'meshes\s+7')
        self.assertRegex(out, r'without result \(exception, timeout\)\s+1')


class WrapQualityGate(unittest.TestCase):
    """paired_fidelity.py (#199 gate, and its noise-aware variant) and noise_floor.py"""

    def base(self, name, **kw):
        r = row(name, robust_used_wrap=True, fm_ok=True, fm_topo_out={}, fm_topo_changes={}, robust_topology_changed=False,
                apply_returned=True, after_closed=True, fm_max_fwd=0.002, fm_reversed_fwd=0.0, fm_f_0001=0.9,
                fm_q_out={'tris': 100, 'degenerate': 0, 'minangle': [2, 10, 20, 40], 'sj': [.1, .3, .5, .8],
                          'maxangle': [170, 120, 100, 80], 'aspect': [20, 5, 3, 1.5], 'cond': [20, 5, 3, 1.3],
                          **{k: 1 for k in ('minangle_lt30', 'minangle_lt10', 'minangle_lt1', 'maxangle_gt90', 'maxangle_gt160',
                                            'aspect_gt13', 'sj_lt05', 'cond_gt13')}})
        r.update(kw)
        return r

    def test_noise_floor_and_gates(self):
        sys.path.insert(0, HERE)
        import noise_floor, paired_fidelity
        # baseline with two other seeds: fm_max_fwd varies by 0.0004, fm_reversed_fwd by 0.0003, the rest not
        b = self.base('a.stl', fm_seeds={'2': {'fm_max_fwd': 0.0024, 'fm_reversed_fwd': 0.0003, 'fm_f_0001': 0.9},
                                         '3': {'fm_max_fwd': 0.0021, 'fm_reversed_fwd': 0.0, 'fm_f_0001': 0.9}})
        with tempfile.TemporaryDirectory() as d:
            src, out = os.path.join(d, 'base.jsonl'), os.path.join(d, 'noise.json')
            open(src, 'w').write(json.dumps(b) + '\n')
            noise_floor.main([out, src])
            with open(out) as fo:
                n = json.load(fo)
        self.assertAlmostEqual(n['a.stl']['fm_max_fwd'], 0.0004)
        self.assertAlmostEqual(n['a.stl']['fm_reversed_fwd'], 0.0003)
        self.assertEqual(n['a.stl']['fm_f_0001'], 0)
        self.assertEqual(n['a.stl']['fm_q_out.minangle[0]'], 0)
        # a candidate within the noise (but above 5 %): fails the #199 gate, passes the noise-aware one
        c = self.base('a.stl', fm_max_fwd=0.0023, fm_reversed_fwd=0.0002)
        old = paired_fidelity.compare([b], [c], [b], expected=1, subset=True)
        new = paired_fidelity.compare([b], [c], [b], expected=1, subset=True, noise=n)
        # (the synthetic rows have only some of the measures: the 'missing' or 'unavailable' failures of the others are left out)
        per_mesh = lambda r: sorted({f['metric'] for f in r['failures'] if f['file'] != '*' and ':' not in f['metric']})
        self.assertEqual(per_mesh(old), ['fm_max_fwd', 'fm_reversed_fwd'])
        self.assertEqual(per_mesh(new), [])
        # beyond the noise: fails both; a worse worst angle (deterministic, 5 %) fails both
        c = self.base('a.stl', fm_max_fwd=0.003)
        self.assertEqual(per_mesh(paired_fidelity.compare([b], [c], [b], expected=1, subset=True, noise=n)), ['fm_max_fwd'])
        q = dict(b['fm_q_out']); q['minangle'] = [1.8, 10, 20, 40]
        c = self.base('a.stl', fm_q_out=q)
        self.assertEqual(per_mesh(paired_fidelity.compare([b], [c], [b], expected=1, subset=True, noise=n)), ['fm_q_out.minangle[0]'])
        # better is never a failure
        c = self.base('a.stl', fm_max_fwd=0.001, fm_f_0001=0.95)
        self.assertEqual(per_mesh(paired_fidelity.compare([b], [c], [b], expected=1, subset=True, noise=n)), [])


class WrapQualityAcceptance(unittest.TestCase):
    def test_criteria(self):
        sys.path.insert(0, HERE)
        import wrap_quality_acceptance as wqa
        q = lambda p01: {'minangle': [1, p01, 5, 30]}
        base = {'file': 'a.stl', 'robust_used_wrap': True, 'q_min_angle': 1.0, 'q_max_angle': 175, 'q_lt05': 0,
                'q_gt170_share': 0.005, 'q_lt10_share': 0.07, 'q_lt20_share': 0.2, 'q_gt150_share': 0.05, 'fm_q_out': q(3)}
        good = dict(base, q_min_angle=1.5, q_max_angle=170, q_gt170_share=0.0005, q_lt10_share=0.02, q_lt20_share=0.1,
                    q_gt150_share=0.01, fm_q_out=q(8))
        self.assertEqual(wqa.check({'a.stl': base}, {'a.stl': good}), {'a.stl': []})
        for change, crit in [({'q_lt05': 1}, 'c1a'), ({'q_min_angle': 0.9}, 'c1a'), ({'q_max_angle': 176}, 'c1b'),
                             ({'q_gt170_share': 0.002}, 'c1c'), ({'fm_q_out': q(3)}, 'c2'), ({'q_lt20_share': 0.2}, 'c2'),
                             ({'q_gt150_share': 0.06}, 'c2')]:
            self.assertEqual(wqa.check({'a.stl': base}, {'a.stl': dict(good, **change)}), {'a.stl': [crit]}, change)
        # not a wrap case: not judged
        self.assertEqual(wqa.check({'a.stl': base}, {'a.stl': dict(good, robust_used_wrap=False)}), {})


if __name__ == '__main__':
    unittest.main()
