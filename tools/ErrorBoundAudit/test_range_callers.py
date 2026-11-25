"""The callers of AdaptivePredicates check the range of the expansions (range_callers.py); runs in the linux job of ci.yml."""
import os
import pathlib
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import range_callers    # noqa: E402

GUARDED = ('class C { int f(double ax, double ay, double bx, double by, double cx, double cy) {\n'
           '    if (Expansion.InRange(ax, ay, bx, by, cx, cy) == false)\n        return 0;\n'
           '    return AdaptivePredicates.Orient2d(ax, ay, bx, by, cx, cy); } }\n')


class RangeCallersTests(unittest.TestCase):
    def test_every_library_call_is_guarded_or_a_known_exception(self):
        self.assertEqual(range_callers.KNOWN_UNGUARDED, range_callers.unguarded_calls(),
                         'a call of AdaptivePredicates without Expansion.InRange: guard it, or record it in '
                         'range_callers.KNOWN_UNGUARDED and docs/qa/exact-bound-inventory.md')

    def check(self, text):
        d = pathlib.Path(tempfile.mkdtemp())
        try:
            (d / 'mesh_ops').mkdir()
            (d / 'mesh_ops' / 'C.cs').write_text(text)
            return range_callers.unguarded_calls(d)
        finally:
            shutil.rmtree(d)

    def test_a_guard_passes(self):
        self.assertEqual({}, self.check(GUARDED))

    def test_no_guard_fails(self):
        self.assertEqual({'mesh_ops/C.cs': 1}, self.check(GUARDED.replace('if (Expansion.InRange(ax, ay, bx, by, cx, cy) == false)', 'if (false)')))

    def test_a_guard_of_other_arguments_fails(self):
        self.assertEqual({'mesh_ops/C.cs': 1}, self.check(GUARDED.replace('InRange(ax, ay, bx, by, cx, cy)', 'InRange(ax, ay, bx, by)')))

    def test_a_guard_of_the_other_branch_fails(self):
        self.assertEqual({'mesh_ops/C.cs': 1}, self.check(GUARDED.replace('== false)', '== true)')))

    def test_a_call_over_two_lines_is_found_and_guarded(self):
        text = GUARDED.replace('Orient2d(ax, ay, bx, by, cx, cy)', 'Orient2d(ax, ay,\n        bx, by, cx, cy)')
        self.assertEqual({}, self.check(text))
        self.assertEqual({'mesh_ops/C.cs': 1}, self.check(text.replace('InRange(ax, ay, bx, by, cx, cy)', 'InRange(ax, ay)')))

    def test_a_call_with_a_nested_parenthesis_is_found(self):
        text = GUARDED.replace('Orient2d(ax, ay,', 'Orient2d(Math.Abs(ax), ay,')
        self.assertEqual({'mesh_ops/C.cs': 1}, self.check(text))                    # Math.Abs(ax) is not a guarded argument
        guarded = text.replace('InRange(ax,', 'InRange(Math.Abs(ax),')
        self.assertEqual({}, self.check(guarded))

    def test_the_stage_d_entry_points_are_checked(self):
        for name in ('Orient2dStageD', 'Orient3dStageD', 'InCircleStageD'):
            self.assertEqual({}, self.check(GUARDED.replace('Orient2d(', name + '(')))
            unguarded = GUARDED.replace('InRange(ax, ay, bx, by, cx, cy)', 'InRange(ax)').replace('Orient2d(', name + '(')
            self.assertEqual({'mesh_ops/C.cs': 1}, self.check(unguarded))

    def test_an_unknown_member_call_fails(self):
        self.assertEqual({'mesh_ops/C.cs': 1}, self.check(GUARDED.replace('Orient2d(', 'NewStage(')))

    def test_an_unclosed_call_fails(self):
        self.assertEqual({'mesh_ops/C.cs': 1}, self.check(GUARDED.replace('cy); } }', 'cy; } }')))


if __name__ == '__main__':
    unittest.main()
