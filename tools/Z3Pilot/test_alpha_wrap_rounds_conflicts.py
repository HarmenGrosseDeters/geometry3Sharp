"""Unit tests, source-contract drift checks, and replay mutation checks for alpha_wrap_rounds_conflicts."""
from pathlib import Path
import tempfile
import unittest

import alpha_wrap_rounds_conflicts as pilot
from check_pilot_obligations import ROOT, run_test, source_contract


class AlphaWrapRoundsConflictsZ3Tests(unittest.TestCase):
    def test_obligations_and_replays(self):
        run_test(self, pilot.run, 'Z3_ALPHA_WRAP_ROUNDS_CONFLICTS_REPORT')

    def test_replay_mutation_is_caught(self):
        # Mutating Relaxation A1 so both cavities share slot (2, 0) must break slot disjointness
        cn = {0: [2, 3, -1, -1], 1: [2, 4, -1, -1], 2: [0, 1, -1, -1], 3: [0, -1, -1, -1], 4: [1, -1, -1, -1]}
        lbl = {0: True, 1: True, 2: False, 3: False, 4: False}
        bad_ins = [
            dict(kind=pilot.KIND_INSERT, c=0, n=3, cav={0}, outer={2, 3}, boundary=[(2, 0), (3, 0)]),
            dict(kind=pilot.KIND_INSERT, c=1, n=4, cav={1}, outer={2, 4}, boundary=[(2, 0), (4, 0)]),
        ]
        kept, _ = pilot._pop_and_select(bad_ins, mark_outer_on_insert=False)
        _, _, writes, _ = pilot._apply_and_compute_pushes(cn, lbl, kept)
        slots = [(c, s) for c, s, _ in writes]
        self.assertLess(len(set(slots)), len(slots))

    def test_source_drift_is_rejected(self):
        cases = [
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'bool round_marked(int c) { return c >= 0 && roundMark[c] == roundMarkValue; }',
             'bool round_marked(int c) { return false; }'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'if (++roundMarkValue == int.MaxValue) {',
             'if (roundMarkValue == int.MaxValue) {'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'if (roundInner[c] == roundMarkValue) {',
             'if (false) {'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'bool clash = round_marked(g.C) || round_marked(g.N);',
             'bool clash = round_marked(g.C);'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'foreach (int q in g.Plan.Reads) round_mark(q);',
             'foreach (int q in g.Plan.Cavity) round_mark(q);'),
            (pilot.WRAP_SOURCE, pilot.WRAP_ANCHORS,
             'if (label_of(dt.Neighbour(nc, j)) == Outside) add_push(g, nc, j);',
             'if (label_of(dt.Neighbour(nc, j)) == Inside) add_push(g, nc, j);'),
            (pilot.DT_SOURCE, pilot.DT_ANCHORS,
             'if (n >= 0 && seen.Add(n)) plan.Reads.Add(n);',
             'if (n < 0 && seen.Add(n)) plan.Reads.Add(n);'),
            (pilot.DT_SOURCE, pilot.DT_ANCHORS,
             'Outer = n, OuterFacet = n >= 0 ? FacetTo(n, c) : -1',
             'Outer = n, OuterFacet = -1'),
            (pilot.DT_SOURCE, pilot.DT_ANCHORS,
             'cn[4 * pc.Outer + pc.OuterFacet] = id;',
             'cn[4 * pc.Outer] = id;'),
        ]
        for rel, anchors, old, new in cases:
            with self.subTest(rel=rel, old=old):
                with tempfile.TemporaryDirectory() as tmp:
                    dst = Path(tmp) / rel
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    text = (ROOT / rel).read_text(encoding='utf-8-sig')
                    self.assertIn(old, text)
                    dst.write_text(text.replace(old, new, 1), encoding='utf-8')
                    with self.assertRaises(ValueError):
                        source_contract(rel, anchors, root=Path(tmp))


if __name__ == '__main__':
    unittest.main()
