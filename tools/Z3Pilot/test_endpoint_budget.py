from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

from check_pilot_obligations import ROOT, run_test, source_contract
from endpoint_budget import ANCHORS, SOURCE, run


class EndpointBudgetTests(TestCase):
    def test_endpoint_budget(self):
        run_test(self, run, 'Z3_ENDPOINT_BUDGET_REPORT')

    def test_source_contract_catches_budget_and_clipping_mutations(self):
        original = (ROOT / SOURCE).read_text(encoding='utf-8-sig')
        for before, after in (
            ('if (lowerBound > MaxPieces)', 'if (lowerBound >= MaxPieces)'),
            ('MaxPieces > 0 && !RegionClipping)', 'MaxPieces > 0)'),
            ('if (o3 * o4 < 0)\n                        cands.Add', 'if (o3 * o4 <= 0)\n                        cands.Add'),
        ):
            with self.subTest(before=before), TemporaryDirectory() as directory:
                root = Path(directory)
                target = root / SOURCE
                target.parent.mkdir(parents=True)
                mutated = original.replace(before, after)
                self.assertNotEqual(original, mutated)
                target.write_text(mutated)
                with self.assertRaisesRegex(ValueError, 'review the solver transcription'):
                    source_contract(SOURCE, ANCHORS, root=root)
