import json
import os
import pathlib
import subprocess
import sys
import textwrap
import unittest


class ConservativeMcTests(unittest.TestCase):
    @staticmethod
    def child(code):
        # Keep this pilot's SMT context out of the pre-existing FP pilots.
        # Z3's nonlinear search heuristics depend on global expression IDs.
        result = subprocess.run([sys.executable, '-c', textwrap.dedent(code)],
                                cwd=pathlib.Path(__file__).parent, text=True, capture_output=True, check=True)
        return json.loads(result.stdout)

    def test_adapter_and_fp_envelopes(self):
        report = self.child('import json, conservative_mc as mc; print(json.dumps(mc.run()))')
        out = os.environ.get('Z3_CONSERVATIVE_MC_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2)+'\n')
        self.assertTrue(report['obligations_passed'], json.dumps(report, indent=2))
        self.assertTrue(report['sharpness_replayed'])
        self.assertTrue(report['wrong_crossing_replayed'])
        self.assertEqual(260, len(report['obligations']))
        self.assertTrue(all(r['assumptions_result'] == 'sat' and r['result'] == 'unsat' for r in report['obligations']))

    def test_source_drift_is_rejected(self):
        report = self.child("""
            import json, pathlib
            from unittest.mock import patch
            import conservative_mc as mc
            read = pathlib.Path.read_text
            rejected = []
            for before, after in [('cell.f[i] < IsoValue', 'cell.f[i] > IsoValue'),
                                  ('initialize_cell_values_nohash(cell, false)', 'initialize_cell_values_grid(cell, false)'),
                                  ('double t = (IsoValue - fa) / gap;', 'double t = 0.5;'),
                                  ('Mesh.AppendTriangle(a, b, c)', 'Mesh.AppendTriangle(a, c, b)')]:
                def changed(path, *args, **kwargs):
                    return read(path, *args, **kwargs).replace(before, after)
                try:
                    with patch.object(pathlib.Path, 'read_text', changed):
                        mc.source_contracts()
                except ValueError:
                    rejected.append(before)
            print(json.dumps(rejected))
        """)
        self.assertEqual(4, len(report))

    def test_wrong_winding_breaks_high_cap(self):
        rejected = self.child("""
            import copy, json, conservative_mc as mc
            rows, _ = mc.source_contracts()
            rows = copy.deepcopy(rows)
            rows[1][0], rows[1][1] = rows[1][1], rows[1][0]
            try:
                mc.table_certificate(rows)
                rejected = False
            except ValueError:
                rejected = True
            print(json.dumps(rejected))
        """)
        self.assertTrue(rejected)

    def test_no_vacuous_or_unknown_success(self):
        report = self.child("""
            import json
            from unittest.mock import patch
            import conservative_mc as mc
            z = mc.z
            vacuous = mc.check('vacuous', [z.BoolVal(False)], z.BoolVal(False))
            class UnknownSolver:
                def set(self, **kwargs): pass
                def add(self, *args): pass
                def check(self): return z.unknown
            with patch.object(z, 'Solver', UnknownSolver):
                unknown = mc.check('unknown', [], z.BoolVal(False))
            print(json.dumps(dict(vacuous=vacuous, unknown=unknown)))
        """)
        self.assertFalse(report['vacuous']['passed'])
        self.assertFalse(report['unknown']['passed'])

    def test_direct_all_low_is_nonvacuous(self):
        row = self.child("""
            import json, conservative_mc as mc
            rows, _ = mc.source_contracts()
            print(json.dumps(mc.direct_query(255, rows[255], 20000)))
        """)
        self.assertTrue(row['passed'], row)


if __name__ == '__main__':
    unittest.main()
