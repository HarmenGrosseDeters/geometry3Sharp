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


class PairedFidelity(unittest.TestCase):
    def setUp(self):
        import copy
        import paired_fidelity as pf
        self.pf = pf
        self.a = row('wrap.stl', apply_returned=True, after_closed=True, fm_ok=True,
                     robust_used_wrap=True, robust_topology_changed=False,
                     fm_topo_out={'components': 1}, fm_topo_changes='None',
                     fm_sharp_in=1, fm_sharp_out=1, fm_outer_share=1,
                     fm_outer_edge_share=1, fm_outer_face_share=1, fm_outer_converged=True,
                     wrap_certificate_hi_over_offset=0.4, robust_phases={'wrap.simplify': 2})
        for key in pf.LOW:
            self.a.setdefault(key, 0.001)
        for key in pf.HIGH | pf.SCORES:
            self.a.setdefault(key, 0.999)
        self.a['fm_q_out'] = dict(tris=100, degenerate=0,
                                  **{key: [20, 20, 25, 40] for key in pf.DIST},
                                  **{key: 10 for key in pf.BINS})
        self.z = copy.deepcopy(self.a)
        self.raw = copy.deepcopy(self.a)
        self.raw['after_tris'] = 300

    def result(self, **kw):
        return self.pf.compare([self.a], [self.z], [self.raw], expected=1, **kw)

    def test_identical_pass_and_certificate(self):
        self.assertTrue(self.result(require_certificate=True)['pass'])
        self.z['wrap_certificate_hi_over_offset'] = 0.50001
        self.assertFalse(self.result(require_certificate=True)['pass'])
        del self.z['wrap_certificate_hi_over_offset']
        self.assertFalse(self.result(require_certificate=True)['pass'])
        self.assertTrue(self.result()['pass'])

    def test_each_relative_metric_and_boundary(self):
        for key in self.pf.LOW | self.pf.HIGH | self.pf.SCORES:
            with self.subTest(key=key):
                self.setUp()
                self.a['iou'] = self.z['iou'] = 0.98
                old = self.z[key]
                high = key in self.pf.HIGH | self.pf.SCORES
                self.z[key] = self.a[key] * (0.95 if high else 1.05)
                self.assertTrue(self.result()['pass'])
                self.z[key] = self.a[key] * (0.949 if high else 1.051)
                self.assertFalse(self.result()['pass'])
                self.z[key] = old

    def test_zero_error_has_no_absolute_allowance(self):
        self.a['fm_k_err'] = self.z['fm_k_err'] = 0
        self.assertTrue(self.result()['pass'])
        self.z['fm_k_err'] = 1e-15
        self.assertFalse(self.result()['pass'])

    def test_missing_and_nonfinite_are_serializable_failures(self):
        for value in (None, 'NaN', 'Infinity', float('nan'), float('inf')):
            with self.subTest(value=value):
                self.z['fm_k_err'] = value
                report = self.result()
                self.assertFalse(report['pass'])
                json.dumps(report, allow_nan=False)
        del self.a['fm_f_0001']
        del self.z['fm_f_0001']
        self.z['fm_k_err'] = self.a['fm_k_err']
        self.assertFalse(self.result()['pass'])

    def test_every_quality_distribution_entry(self):
        for key, high in self.pf.DIST.items():
            for n in range(4):
                with self.subTest(key=key, entry=n):
                    old = self.z['fm_q_out'][key][n]
                    self.z['fm_q_out'][key][n] *= 0.94 if high else 1.06
                    self.assertFalse(self.result()['pass'])
                    self.z['fm_q_out'][key][n] = old
        self.z['fm_q_out']['minangle'] = [20]
        self.assertFalse(self.result()['pass'])

    def test_adverse_bins_are_shares(self):
        self.z['fm_q_out']['tris'] = 50
        for key in self.pf.BINS:
            self.z['fm_q_out'][key] = 5
        self.assertTrue(self.result()['pass'])
        for key in self.pf.BINS:
            self.z['fm_q_out'][key] = 6
            self.assertFalse(self.result()['pass'])
            self.z['fm_q_out'][key] = 5
        self.z['fm_q_out']['degenerate'] = 1
        self.assertFalse(self.result()['pass'])

    def test_id_sets_duplicates_and_default_full_count(self):
        self.assertFalse(self.pf.compare([self.a], [self.z], [self.raw])['pass'])
        for side in range(3):
            rows = [[self.a], [self.z], [self.raw]]
            rows[side] = rows[side] * 2
            self.assertFalse(self.pf.compare(*rows, expected=1)['pass'])
        self.z['file'] = 'other.stl'
        self.assertFalse(self.result()['pass'])
        del self.z['file']
        self.assertFalse(self.result()['pass'])
        self.a['file'] = None
        self.assertFalse(self.result()['pass'])

    def test_wrap_union_includes_path_changes(self):
        self.z['robust_used_wrap'] = False
        self.assertEqual(self.result()['aggregate']['wrap_cases'], 1)
        self.a['robust_used_wrap'] = False
        self.z['robust_used_wrap'] = True
        self.assertEqual(self.result()['aggregate']['wrap_cases'], 1)
        self.raw['robust_used_wrap'] = False
        self.assertFalse(self.result()['pass'])

    def test_subset_cannot_hide_wrap_case(self):
        self.assertTrue(self.result(subset=True)['subset_only'])
        self.assertFalse(self.pf.compare([self.a], [], [self.raw], expected=1, subset=True)['pass'])

    def test_status_cleanliness_topology_and_repaired_loss(self):
        for key, value in [('status', 'timeout'), ('status', None), ('apply_returned', False),
                           ('after_valid', False), ('after_closed', False),
                           ('fm_topo_out', {'components': 2}), ('fm_ok', False),
                           ('robust_topology_changed', True)]:
            old = self.z[key]
            self.z[key] = value
            self.assertFalse(self.result()['pass'])
            self.z[key] = old
        for key in self.pf.ZERO:
            self.z[key] = 1
            self.assertFalse(self.result()['pass'])
            self.z[key] = 0
        self.z['iou'] = 0.989
        report = self.result()
        self.assertIn('lost repaired case', [f['metric'] for f in report['failures']])

    def test_inapplicability_is_explicit_and_cannot_hide_available_metric(self):
        self.a['fm_sharp_in'] = 0
        self.z['fm_er_0001'] = 'NaN'
        report = self.result()
        self.assertTrue(report['pass'])
        self.assertTrue(any(d['metric'] == 'fm_er_0001' for d in report['diagnostic']))
        self.a['fm_sharp_in'] = 1
        self.assertFalse(self.result()['pass'])
        self.z['fm_er_0001'] = self.a['fm_er_0001']
        self.a['fm_sharp_out'] = 0
        self.z['fm_ecd'] = 'Infinity'
        self.assertTrue(self.result()['pass'])
        self.a['fm_sharp_out'] = 1
        self.assertFalse(self.result()['pass'])

    def test_outer_and_no_solid_inapplicability(self):
        self.a['fm_outer_share'] = 0
        self.z['fm_outer_max'] = 'NaN'
        self.assertTrue(self.result()['pass'])
        self.a['fm_outer_share'] = 1
        self.assertFalse(self.result()['pass'])
        self.z['fm_outer_max'] = self.a['fm_outer_max']
        self.a['fm_outer_converged'] = False
        self.z['fm_oviou'] = 'NaN'
        self.assertTrue(self.result()['pass'])
        self.a['fm_outer_converged'] = True
        self.assertFalse(self.result()['pass'])
        for key in ('iou', 'iou_missing', 'iou_extra', 'fm_viou', 'fm_viou_missing',
                    'fm_viou_extra', 'fm_oviou', 'fm_oviou_missing', 'fm_oviou_extra'):
            self.a[key] = self.z[key] = 'NaN'
        self.assertTrue(self.result()['pass'])

    def test_full_timing_gate_includes_nonwrap_pairs(self):
        import copy
        control = copy.deepcopy(self.a)
        control.update(file='control.stl', robust_used_wrap=False)
        candidate = copy.deepcopy(control)
        candidate['repair_ms'] = 100
        report = self.pf.compare([self.a, control], [self.z, candidate],
                                 [self.raw, control], expected=2)
        self.assertFalse(report['pass'])
        self.assertEqual(report['aggregate']['repair_time_ratio'], 5.5)
        self.assertEqual(report['aggregate']['time_paired_cases'], 2)
        subset = self.pf.compare([self.a, control], [self.z], [self.raw, control],
                                 expected=2, subset=True)
        self.assertTrue(subset['pass'])
        self.assertEqual(subset['aggregate']['repair_time_ratio'], 1)
        candidate['repair_ms'] = None
        self.assertFalse(self.pf.compare([self.a, control], [self.z, candidate],
                                         [self.raw, control], expected=2)['pass'])

    def test_legacy_iou_cannot_hide_finite_fidelity_iou(self):
        self.a['iou'] = self.z['iou'] = 'NaN'
        for key in ('fm_viou', 'fm_oviou'):
            with self.subTest(key=key):
                old = self.z[key]
                self.z[key] = 0.1
                report = self.result(require_certificate=True)
                self.assertFalse(report['pass'])
                self.assertIn(key, [failure['metric'] for failure in report['failures']])
                self.z[key] = old

    def test_outer_applicability_metadata_is_required(self):
        for key in ('fm_outer_converged', 'fm_outer_face_share'):
            for value in (None, 'NaN', 'invalid'):
                with self.subTest(key=key, value=value):
                    self.setUp()
                    self.a[key] = value
                    self.z['fm_oviou'] = None
                    self.assertFalse(self.result(require_certificate=True)['pass'])
            self.setUp()
            del self.a[key]
            self.assertFalse(self.result(require_certificate=True)['pass'])

    def test_aggregate_gates(self):
        for key, value in [('repair_ms', 12.51), ('after_tris', 121)]:
            old = self.z[key]
            self.z[key] = value
            self.assertFalse(self.result()['pass'])
            self.z[key] = old
        self.z['fm_q_out']['minangle'][1] = 9.99
        self.assertFalse(self.result()['pass'])

    def test_cli_exit_and_legacy_invocation(self):
        with tempfile.TemporaryDirectory() as directory:
            paths = []
            for name, value in [('before', self.a), ('after', self.z), ('raw', self.raw)]:
                path = os.path.join(directory, name + '.jsonl')
                with open(path, 'w', encoding='utf-8') as stream:
                    stream.write(json.dumps(value) + '\n')
                paths.append(path)
            command = [sys.executable, os.path.join(HERE, 'analyze_fidelity.py'), '--paired',
                       *paths, '--expected', '1', '--require-certificate']
            success = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(success.returncode, 0, success.stderr)
            self.assertTrue(json.loads(success.stdout)['pass'])
            failure = subprocess.run(command + ['--expected', '400'], capture_output=True, text=True)
            self.assertEqual(failure.returncode, 1, failure.stderr)
            self.assertFalse(json.loads(failure.stdout)['pass'])


if __name__ == '__main__':
    unittest.main()
