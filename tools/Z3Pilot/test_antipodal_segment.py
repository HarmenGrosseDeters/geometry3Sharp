import json
import os
import pathlib
import unittest

import antipodal_segment

TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
ROOT = pathlib.Path(__file__).resolve().parents[2]

# the code the model describes (mesh_ops/ExactMeshBoolean.cs, antipodal_sum, and the Vector3d members it calls): a change
# to these lines must be checked against tools/Z3Pilot/antipodal_segment.py again
CONTRACT = {
    'mesh_ops/ExactMeshBoolean.cs': [
        'double ea = (2.3e-16 * (pm + Math.Abs(segments[k].x) + Math.Abs(segments[k].y) + Math.Abs(segments[k].z)) + pointError) / la;',
        'double eb = (2.3e-16 * (pm + Math.Abs(segments[k + 1].x) + Math.Abs(segments[k + 1].y) + Math.Abs(segments[k + 1].z)) + pointError) / lb;',
        'double tol = 16 * (ea + eb + 1e-15 + directionError);',
        'if (!(tol < 0.01))',
        'Vector3d ab = a + b;',
        'if (ab.Length <= tol)',
        'double num = a.Dot(b.Cross(x1)), den = 1 + a.Dot(b) + a.Dot(x1) + b.Dot(x1);',
        'if (Math.Abs(num) <= tol && den <= tol)',
        'double hypot = Math.Sqrt(num * num + den * den);',
        'if (maxBound > 1e-4 && !(tol <= 0.4 * hypot))',
        'double termError = 4 * tol / hypot;',
    ],
    'math/Vector3d.cs': [
        'get { return x * x + y * y + z * z; }',
        'get { return Math.Sqrt(LengthSquared); }',
        'return x * v2.x + y * v2.y + z * v2.z;',
        'return new Vector3d(v0.x + v1.x, v0.y + v1.y, v0.z + v1.z);',
        'return new Vector3d(v.x / f, v.y / f, v.z / f);',
    ],
}


class AntipodalSegmentTests(unittest.TestCase):
    def test_lemmas_and_mutants(self):
        ok, report = antipodal_segment.run(TIMEOUT_MS)
        out = os.environ.get('Z3_ANTIPODAL_SEGMENT_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2) + '\n')
        self.assertTrue(ok, report)
        self.assertEqual(20, len(report))

    def test_constants_are_the_codes(self):
        self.assertEqual(antipodal_segment.U, 2.0 ** -53)
        self.assertEqual(antipodal_segment.TOL_FACTOR, 16)
        self.assertEqual(float(antipodal_segment.TOL_FLOOR), 1e-15)
        self.assertEqual(float(antipodal_segment.CONDITION), 0.4)
        # the rounded tol is at least 15.99 (ea + eb + 1e-15): three roundings of at most u each
        self.assertLess(float(antipodal_segment.ROUNDED_TOL), 16 * (1 - 2.0 ** -53) ** 3)
        # B1: 2.3e-16 covers the two roundings of a difference of rounded doubles, 2.22e-16 (1 + u)
        self.assertGreater(2.3e-16, 2 * 2.0 ** -53 * (1 + 2.0 ** -53))

    def test_source_contract(self):
        for path, lines in CONTRACT.items():
            text = (ROOT / path).read_text(encoding='utf-8')
            for line in lines:
                self.assertIn(line, text, f'{path} changed: check it against tools/Z3Pilot/antipodal_segment.py')


if __name__ == '__main__':
    unittest.main()
