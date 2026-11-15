import json
import os
import pathlib
import unittest

import plateau_search

TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
ROOT = pathlib.Path(__file__).resolve().parents[2]

# the prototype of the acceptance rule and of the rollback (a benchmark tool, not library code; its acceptance is by
# the set of defects, which is stricter than the counts of the model): a change to these lines must be checked against
# the model again
CONTRACT = [
    'addsNone = isNow.IsSubsetOf(was);',
    'if (ok && (strict == false || addsNone) && (now < defects || (plateau && now == defects))) {',
    'for (int i = 0; i < moved.Count; ++i) journal.Add(new KeyValuePair<int, Vector3d>(moved[i], saved[i]));',
    'for (int i = journal.Count - 1; i >= 0; --i) m.SetVertex(journal[i].Key, journal[i].Value);',
    'if (stall > 0 && sinceImproving >= stall) { stalled = true; break; }',
]


class PlateauSearchTests(unittest.TestCase):
    """Pilot 8: the plateau variant of the position search (docs/design/SNAP_ROUNDING_3D.md section 10, not built)."""

    def test_lemma_and_mutants(self):
        ok, report = plateau_search.run(TIMEOUT_MS)
        out = os.environ.get('Z3_PLATEAU_SEARCH_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2) + '\n')
        self.assertTrue(ok, report)
        self.assertEqual(28, len(report))
        self.assertEqual(9, sum(1 for name in report if name.startswith('mutant_')))
        for name, row in report.items():
            if name.startswith('mutant_'):
                self.assertEqual('sat', row['got'], name)

    def test_the_draw_bound_is_attained_and_not_exceeded(self):
        for n0, s, b in ((1, 1, 1), (4, 2, 3), (6, 3, 5)):
            self.assertEqual(b * s * n0 * (n0 + 1) // 2, plateau_search.longest_run(n0, s, b))
        self.assertEqual(0, plateau_search.longest_run(0, 3, 5))

    def test_every_transition_is_enabled_somewhere(self):
        # the transitions cover a state with defects and chains left (a draw or the end of the chain) and the end of
        # a round: no state of the search is stuck except the end (a round that committed nothing)
        import z3
        s, t, trans = plateau_search.transitions()
        stuck = plateau_search.inv(s) + [z3.Not(z3.Exists(t, z3.And(*rel))) for rel in trans.values()]
        N, q, ch, dr, sw = s
        solver = z3.Solver()
        solver.set(timeout=TIMEOUT_MS)
        solver.add(*stuck)
        solver.add(z3.Not(z3.And(z3.Or(ch == 0, N == 0), q == 0)))      # not the end
        self.assertEqual(z3.unsat, solver.check())

    def test_source_contract(self):
        text = (ROOT / 'benchmarks' / 'SnapRoundingExperiment' / 'LatticeProbe.cs').read_text(encoding='utf-8')
        for line in CONTRACT:
            self.assertIn(line, text, 'the prototype changed: check it against tools/Z3Pilot/plateau_search.py')


if __name__ == '__main__':
    unittest.main()
