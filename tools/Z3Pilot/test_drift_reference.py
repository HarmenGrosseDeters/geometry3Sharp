import json
import os
import pathlib
import unittest
from fractions import Fraction
from unittest.mock import patch

import drift_reference as dr

# Z3_PILOT_TIMEOUT_MS is the per-query timeout (UNKNOWN is a failure, never a pass)
TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))

# The counterexamples Z3 found for the mutants of the rule (2026-11-09, z3-solver 4.15.4), as regression cases:
# (mutant, vertices, script, the claim it breaks, the witness: start positions p, edit parameters q, the point x or y).
# They are replayed with fractions, without Z3: the mutant breaks the claim, the rule keeps all four.
PINNED = [
    ('no_ghosts', 3, (('contract', 0, 1),), 'H2',
     {'p0': '0', 'p1': '1/4', 'p2': '1/2', 'q0': '0', 'x': '0', 'y': '0'}),
    ('ghost_dies_with_its_vertex', 4, (('contract', 0, 1), ('contract', 1, 2)), 'H2',
     {'p0': '-3/4', 'p1': '-1/2', 'p2': '-1/4', 'p3': '0', 'q0': '0', 'q1': '0', 'x': '0', 'y': '-3/4'}),
    ('generic_carry_only', 3, (('move', 0), ('reanchor', 0)), 'H1',
     {'p0': '-1/4', 'p1': '0', 'p2': '-1/2', 'q0': '1/4', 'q1': '0', 'x': '1/4', 'y': '0'}),
    ('generic_ignores_lost_ghosts', 5, (('contract', 2, 1), ('contract', 1, 3), ('remove', 3)), 'H2',
     {'p0': '-3/4', 'p1': '-1/2', 'p2': '-3/2', 'p3': '-1/4', 'p4': '0', 'q0': '0', 'q1': '0', 'q2': '0', 'x': '0', 'y': '-3/2'}),
    ('generic_without_delta', 3, (('remove', 1),), 'H2',
     {'p0': '-1/4', 'p1': '-1/2', 'p2': '0', 'q0': '-1/4', 'x': '0', 'y': '-1/2'}),
    ('kept_vertex_anchor', 3, (('move', 0), ('contract', 1, 0)), 'H1',
     {'p0': '1/2', 'p1': '3/4', 'p2': '1/4', 'q0': '0', 'q1': '0', 'x': '0', 'y': '0'}),
    ('move_skips_ghosts', 3, (('contract', 0, 1), ('move', 1)), 'reported',
     {'p0': '0', 'p1': '1/2', 'p2': '0', 'q0': '0', 'q1': '3/4', 'x': '0', 'y': '0'}),
]


class DriftReferenceTests(unittest.TestCase):
    """Pilot 2, step 1b: the reference rule for SafeRounding's drift (drift_reference.py,
    docs/design/SAFE_ROUNDING_D1_DRIFT.md)."""

    def test_reference_rule_obligations_mutants_and_replays(self):
        report = dr.run(TIMEOUT_MS)
        out = os.environ.get('Z3_DRIFT_REFERENCE_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2) + '\n')
        self.assertTrue(report['obligations_passed'], json.dumps(report, indent=2))
        rows = report['obligations']
        self.assertTrue(all(r['result'] == 'unsat' and r['assumptions_result'] == 'sat' and r['assumptions_replayed']
                            for r in rows))
        self.assertEqual([], report['failures'])
        # the rows: convexity (2), the lemma (5), one per configuration of the search, the distance (3), the chain
        self.assertEqual(2 + 5 + len(dr.SEARCH) + 3 + 1, len(rows))
        # every script of every configuration was asked
        for (n, length), row in zip(dr.SEARCH, [r for r in rows if r['part'] == 'C']):
            self.assertEqual(sum(1 for _ in dr.scripts(n, length)), row['scripts'], row['name'])
        self.assertEqual(7038, sum(r['scripts'] for r in rows if r['part'] == 'C'))
        # every mutant has a counterexample, replayed outside Z3
        muts = {m['name']: m for m in report['mutants']}
        self.assertEqual(set(dr.MUTANTS) | {'convexity_no_sum', 'convexity_two_corners', 'convexity_no_dot_bound',
                                            'lemma_G_J1_carry_only', 'lemma_G_J2_carry_only', 'lemma_G_J1_no_delta',
                                            'lemma_G_J2_no_delta', 'lemma_G_bound_no_acceptance',
                                            'chain_step_with_2_4_model_only'}, set(muts))
        self.assertTrue(all(m['result'] == 'sat' and m['replayed'] for m in muts.values()))

    def test_pinned_counterexamples_break_the_mutant_and_not_the_rule(self):
        self.assertEqual(set(dr.MUTANTS), {p[0] for p in PINNED})
        for mutant, n, script, claim, witness in PINNED:
            with self.subTest(mutant=mutant):
                values = {k: Fraction(v) for k, v in witness.items()}
                self.assertEqual((n, script), dr.MUTANT_SCRIPTS[mutant])
                broken = dr.run_script(dr.QB, n, script, values, mutant)
                self.assertTrue(all(broken.side))
                self.assertTrue(broken.bad(claim, x=values['x'], y=values['y']))
                rule = dr.run_script(dr.QB, n, script, values)
                self.assertTrue(all(rule.side))
                for c in dr.CLAIMS:
                    self.assertFalse(rule.bad(c, x=values['x'], y=values['y']), c)
                # the true distance on the line (both sets are intervals) is within the rule's bound, and the
                # rule's bound within the apex rule's
                distance, bound, apex = dr.hausdorff_on_the_line(n, script, values)
                self.assertLessEqual(distance, bound)
                self.assertLessEqual(bound, apex)

    def test_pinned_lost_ghost_is_the_spike(self):
        # the case that needs ghosts and their loss: the start vertex 2 is a spike (at -3/2), both its segments
        # collapse onto vertex 3, which is then removed; without the lost ghosts in N the bound is 1/2, the spike's
        # tip is 3/4 from what is left
        mutant, n, script, _, witness = next(p for p in PINNED if p[0] == 'generic_ignores_lost_ghosts')
        values = {k: Fraction(v) for k, v in witness.items()}
        distance, bound, _ = dr.hausdorff_on_the_line(n, script, values, mutant)
        self.assertEqual((Fraction(3, 4), Fraction(1, 2)), (distance, bound))
        distance, bound, _ = dr.hausdorff_on_the_line(n, script, values)
        self.assertEqual((Fraction(3, 4), Fraction(3, 2)), (distance, bound))

    def test_the_rule_is_tighter_on_a_chain_of_contractions(self):
        # D1 on the line: 0, 1, .., 4, each vertex contracted into the next (kept in place). The apex rule adds up
        # 1 + 1 + 1 = 3 at vertex 3; the reference rule measures 3 from the anchors (the true move of start vertex
        # 0), but for the segments in between, and after moving back, far less: a second pass that moves vertex 3
        # back to 1.5 leaves 1.5 where the apex rule has 4.5
        values = {f'p{i}': Fraction(i) for i in range(5)}
        script = (('contract', 0, 1), ('contract', 1, 2), ('contract', 2, 3))
        distance, bound, apex = dr.hausdorff_on_the_line(5, script, values)
        self.assertEqual((Fraction(3), Fraction(3), Fraction(3)), (distance, bound, apex))
        values['q3'] = Fraction(3, 2)
        distance, bound, apex = dr.hausdorff_on_the_line(5, script + (('move', 3),), values)
        self.assertEqual((Fraction(3, 2), Fraction(3, 2), Fraction(9, 2)), (distance, bound, apex))
        # contractions side by side, each kept vertex read by the next edit: the apex rule chains them through M,
        # the reference rule does not (1 against 2)
        values = {f'p{i}': Fraction(i) for i in range(5)}
        script = (('contract', 0, 1), ('contract', 2, 1))
        distance, bound, apex = dr.hausdorff_on_the_line(5, script, values)
        self.assertEqual((Fraction(1), Fraction(1), Fraction(2)), (distance, bound, apex))

    def test_search_finds_a_mutant_and_nothing_for_the_rule(self):
        found = dr.search('no_ghosts', TIMEOUT_MS, configurations=[(3, 1)])
        self.assertIsNotNone(found)
        self.assertEqual('sat', found['result'])
        self.assertTrue(found['replayed'])
        self.assertIsNone(dr.search(None, TIMEOUT_MS, configurations=[(3, 1), (3, 2)]))

    def test_specification_is_what_the_model_reads(self):
        for key in dr.RULE:
            with self.subTest(key=key), patch.dict(dr.RULE, {key: 'something else'}):
                with self.assertRaises(ValueError):
                    dr.run(TIMEOUT_MS, configurations=[(3, 1)])
        with self.assertRaises(ValueError):
            dr.Chain(dr.QB, [Fraction(0), Fraction(1)], 'not a mutant')

    def test_unknown_or_sat_is_not_a_pass(self):
        original = dr._check

        def unknown(assumptions, bad, timeout_ms):
            if dr.z.is_true(bad):
                return original(assumptions, bad, timeout_ms)
            return None, dr.z.unknown, 0.0
        with patch.object(dr, '_check', unknown), patch.object(dr, 'search', lambda *a, **k: None):
            report = dr.run(TIMEOUT_MS, configurations=[(3, 1)])
        self.assertFalse(report['obligations_passed'])
        self.assertTrue(all(r['result'] == 'unknown' for r in report['obligations']))
        # a rule that is wrong fails the search: the chain without ghosts
        with patch.object(dr, 'run_script', lambda B, n, script, values, mutant=None: _no_ghosts(B, n, script, values)):
            report = dr.run(TIMEOUT_MS, configurations=[(3, 1)])
        self.assertFalse(report['obligations_passed'])
        self.assertTrue(any(f['script'] == [['contract', 0, 1]] and 'H2' in f['claims'] for f in report['failures']))

    def test_distance_constant(self):
        # the bridge of part D: r^2 >= R^2 (1 - u)^7 gives R <= r (1 + u)^4
        self.assertLessEqual(1 / (1 - dr.U) ** 7, (1 + dr.U) ** 8)
        self.assertGreater(1 / (1 - dr.U) ** 7, (1 + dr.U) ** 7)

    def test_source_contract(self):
        dr.source_contract()

    def test_source_contract_sees_code_drift(self):
        original = dr.ROOT.__class__.read_text
        changes = [
            ('MeshRoundingRepair.cs', 'movedV = -1;\n            vanishing = -1;', 'movedV = -1; Mesh.SetVertex(0, movedPos);\n            vanishing = -1;'),
            ('MeshRoundingRepair.cs', 'removed.Add(tT); removed.Add(tU);', 'removed.Add(tT); removed.Add(tU); movedV = c; movedPos = pa;'),
            ('MeshRoundingRepair.cs', 'if (p != Mesh.GetVertex(b)) {\n                foreach (int t in Mesh.VtxTrianglesItr(b)) {',
             'if (p != Mesh.GetVertex(b) && a > b) {\n                foreach (int t in Mesh.VtxTrianglesItr(b)) {'),
            ('Vector3d.cs', 'return Math.Sqrt(dx*dx + dy*dy + dz*dz);\n\t\t}\n        public double Distance(ref',
             'return Math.Sqrt(dx*dx + dy*dy) + Math.Abs(dz);\n\t\t}\n        public double Distance(ref'),
            # the seven mutants of the rule (PINNED), as changes of the code
            ('MeshRoundingRepair.cs', 'if (editKeptRef[i])\n                        continue;',
             'if (editKeptRef[i] || true)\n                        continue;'),                                    # no_ghosts
            ('MeshRoundingRepair.cs', 'if (listed == false)\n                            ghost_add(editKept, g);', ''),  # ghost_dies_with_its_vertex
            ('MeshRoundingRepair.cs', 'most = Math.Max(most, triangle_bound(t, Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c)));\n            }\n            foreach (int v in editCorners)',
             'most = Math.Max(most, refCarry[t]);\n            }\n            foreach (int v in editCorners)'),  # generic_carry_only
            ('MeshRoundingRepair.cs', 'editLost.Add(g);\n                    most = Math.Max(most, ghost_bound(g, -1, -1, Vector3d.Zero));',
             'editLost.Add(g);'),                                                                         # generic_ignores_lost_ghosts
            ('MeshRoundingRepair.cs', 'return most + step;', 'return most;'),                             # generic_without_delta
            ('MeshRoundingRepair.cs', 'Vector3d p0 = (g.at.a == v || g.at.a == gone) ? p : Mesh.GetVertex(g.at.a);',
             'Vector3d p0 = (g.at.a == v || g.at.a == gone) ? g.a0 : Mesh.GetVertex(g.at.a);'),          # kept_vertex_anchor (a ghost's)
            ('MeshRoundingRepair.cs', 'most = Math.Max(most, Math.Max(ghosts_bound(movedV, movedV, vanishing, movedPos), ghosts_bound(vanishing, movedV, vanishing, movedPos)));',
             'most = Math.Max(most, ghosts_bound(vanishing, movedV, vanishing, movedPos));'),             # move_skips_ghosts
            # the bookkeeping around the rule
            ('MeshRoundingRepair.cs', 'if (within_bound(proposed_drift(step), bound) == false)', 'if (within_bound(proposed_drift(0), bound) == false)'),
            ('MeshRoundingRepair.cs', 'within_bound(move_bound(v, p), maxLength_of(v));', 'within_bound(0, maxLength_of(v));'),
            ('MeshRoundingRepair.cs', 'addedIds.Add(tid);\n            commit_drift();', 'commit_drift();'),
            ('MeshRoundingRepair.cs', 'set_reference(t, editDrift);', 'set_reference(t, 0);'),
            ('MeshRoundingRepair.cs', 'refCarry = new double[Mesh.MaxTriangleID];\n            foreach (int t in Mesh.TriangleIndices()) {',
             'refCarry = new double[Mesh.MaxTriangleID];\n            foreach (int t in new int[0]) {'),
        ]
        for name, old, new in changes:
            with self.subTest(change=new):
                def changed(path, *args, **kwargs):
                    text = original(path, *args, **kwargs)
                    if path.name != name:
                        return text
                    self.assertEqual(1, text.count(old), old)
                    return text.replace(old, new)
                with patch.object(dr.ROOT.__class__, 'read_text', changed):
                    with self.assertRaises(ValueError):
                        dr.source_contract()


def _no_ghosts(B, n, script, values):
    return _ORIGINAL_RUN_SCRIPT(B, n, script, values, 'no_ghosts')


_ORIGINAL_RUN_SCRIPT = dr.run_script


if __name__ == '__main__':
    unittest.main()
