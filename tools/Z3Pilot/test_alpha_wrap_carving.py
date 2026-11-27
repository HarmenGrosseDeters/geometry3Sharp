"""Check the MeshAlphaWrap carving & empty-ball model and reject source drift."""
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

import alpha_wrap_carving
from check_pilot_obligations import ROOT, run_test, source_contract


class AlphaWrapCarvingTests(TestCase):
    def test_alpha_wrap_carving_obligations(self):
        run_test(self, alpha_wrap_carving.run, 'Z3_ALPHA_WRAP_CARVING_REPORT')

    def test_source_drift_invalidates_the_model(self):
        for source, anchors, before, after in (
            (alpha_wrap_carving.WRAP_SOURCE, alpha_wrap_carving.WRAP_ANCHORS,
             'return c >= 0 && roundMark[c] == roundMarkValue;',
             'return false;'),
            (alpha_wrap_carving.WRAP_SOURCE, alpha_wrap_carving.WRAP_ANCHORS,
             'if (label_of(n) != Outside)',
             'if (false && label_of(n) != Outside)'),
            (alpha_wrap_carving.WRAP_SOURCE, alpha_wrap_carving.WRAP_ANCHORS,
             'roundInner[c] = roundMarkValue;',
             'roundInner[c] = 0;'),
            (alpha_wrap_carving.WRAP_SOURCE, alpha_wrap_carving.WRAP_ANCHORS,
             'if (round_marked(q)) { clash = true; break; }',
             'if (false && round_marked(q)) { clash = true; break; }'),
            (alpha_wrap_carving.WRAP_SOURCE, alpha_wrap_carving.WRAP_ANCHORS,
             'round_mark(g.C); round_mark(g.N);',
             'round_mark(g.C);'),
            (alpha_wrap_carving.WRAP_SOURCE, alpha_wrap_carving.WRAP_ANCHORS,
             '// carve: writes label[c] only (n is read: it can be the outside cell of other gates of the round)\n                    round_mark(g.C);',
             '// carve: dropped mark'),
            (alpha_wrap_carving.WRAP_SOURCE, alpha_wrap_carving.WRAP_ANCHORS,
             'label[g.C] = Outside;',
             'label[g.N] = Outside;'),
            (alpha_wrap_carving.WRAP_SOURCE, alpha_wrap_carving.WRAP_ANCHORS,
             'const double FastW2Min = 1.1833923015014174e-271;',
             'const double FastW2Min = 0;'),
            (alpha_wrap_carving.WRAP_SOURCE, alpha_wrap_carving.WRAP_ANCHORS,
             'return 4.0 * ((0.25 * p - 0.25 * a) - 0.25 * x).Dot(nrm);',
             'return 2.0 * ((0.5 * p - 0.5 * a) - 0.5 * x).Dot(nrm);'),
            (alpha_wrap_carving.DELAUNAY_SOURCE, alpha_wrap_carving.DELAUNAY_ANCHORS,
             'if (n >= 0 && seen.Add(n)) plan.Reads.Add(n);',
             '// omit boundary neighbor from Reads'),
            (alpha_wrap_carving.DELAUNAY_SOURCE, alpha_wrap_carving.DELAUNAY_ANCHORS,
             'cn[4 * pc.Outer + pc.OuterFacet] = id;',
             '// omit neighbor link write'),
        ):
            with self.subTest(source=source, mutation=before), TemporaryDirectory() as directory:
                root = Path(directory)
                original = (ROOT / source).read_text(encoding='utf-8-sig')
                altered = original.replace(before, after, 1)
                self.assertNotEqual(original, altered)
                copy = root / source
                copy.parent.mkdir(parents=True)
                copy.write_text(altered, encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'review the solver transcription'):
                    source_contract(source, anchors, root=root)
