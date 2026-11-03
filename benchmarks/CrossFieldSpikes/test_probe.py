import json
from fractions import Fraction as F
from concurrent.futures import ThreadPoolExecutor
import unittest

import probe


class ProbeTests(unittest.TestCase):
    def test_clearance_equality_is_not_safe(self):
        self.assertFalse(probe.clearance(F(1), F(1, 2), F(1, 2)))
        self.assertTrue(probe.clearance(F(1), F(1, 4), F(1, 2)))
        self.assertFalse(probe.clearance(F(1), F(1, 4), F(1)))
        with self.assertRaises(ValueError):
            probe.clearance(F(-1), F(0), F(0))

    def test_clearance_enumeration_and_scaled_bounds(self):
        for row in probe.clearance_probe():
            self.assertEqual(row['queries'], 81)
            self.assertEqual(row['certified'], 36)
            self.assertEqual(row['false_accepts'], 0)
            self.assertEqual(row['ignore_second_motion_false_accepts'], 36)
        for k in (-100, -2, 0, 10, 100):
            scale = F(2) ** k
            for i in range(9):
                for j in range(9):
                    self.assertEqual(probe.clearance(scale, i*scale/8, j*scale/8), i+j < 8)

    def test_segment_predicates(self):
        self.assertTrue(probe.intersects((0, 0), (2, 2), (0, 2), (2, 0)))
        self.assertTrue(probe.intersects((0, 0), (1, 0), (1, 0), (2, 0)))
        self.assertFalse(probe.intersects((0, 0), (1, 0), (2, 0), (3, 0)))
        self.assertTrue(probe.overlaps_edge((0, 0), (2, 0), (1, 0), (3, 0)))
        self.assertFalse(probe.overlaps_edge((0, 0), (1, 0), (1, 0), (2, 0)))
        self.assertFalse(probe.overlaps_edge((0, 0), (0, 0), (0, 0), (1, 0)))

    def test_vertex_pinch_without_edge_overlap(self):
        a, b = probe.q17_probe()
        self.assertEqual((a['components_before'], a['components_after']), (2, 1))
        self.assertEqual((b['components_before'], b['components_after']), (1, 1))
        for row in (a, b):
            self.assertEqual(row['positive_length_edge_overlaps'], 0)
            self.assertEqual(row['new_common_vertices'], 1)

    def test_shared_tip_is_allowed_but_new_identification_is_not(self):
        a, b, new_a, new_b = probe.pinch_fixture(True)
        self.assertFalse(probe.new_common_vertices(a, b, a, b))
        self.assertEqual(probe.new_common_vertices(a, b, new_a, new_b), {(F(0), F(0))})
        for scale in (F(1, 8), F(1), F(16)):
            # Translation, quarter-turn and reflection, plus exact power-of-two scaling.
            for reflect in (-1, 1):
                def transform(chain):
                    return [(F(7) - scale*y, F(-3) + reflect*scale*x) for x, y in chain]
                transformed = [transform(c) for c in (a, b, new_a, new_b)]
                self.assertEqual(len(probe.new_common_vertices(*transformed)), 1)
                self.assertTrue(probe.contacts(*transformed[:2]))

    def test_small_budget_only_contact_is_rejected(self):
        row = probe.lattice_probe(F(1, 4))
        self.assertEqual(row['candidates'], 1)
        self.assertEqual(row['feasible'], [])
        self.assertFalse(row['nearest_rounding_separated'])

    def test_larger_budget_has_exact_feasible_candidates(self):
        row = probe.lattice_probe(F(3, 4))
        self.assertEqual(row['candidates'], 4)
        self.assertEqual([r['centers'] for r in row['feasible']], [[-1, 0], [-1, 1], [0, 1]])
        self.assertTrue(all(F(r['max_displacement']) == F(3, 4) for r in row['feasible']))
        with self.assertRaises(ValueError):
            probe.lattice_probe(F(-1))

    def test_fan_correspondence_and_independent_height_samples(self):
        original_low, original_high = probe.fan(-1, F(-1, 4)), probe.fan(1, F(1, 4))
        for row in probe.lattice_probe(F(3, 4))['feasible']:
            low, high = (probe.fan(-1, row['centers'][0]), probe.fan(1, row['centers'][1]))
            self.assertEqual(low[:4], original_low[:4])
            self.assertEqual(high[:4], original_high[:4])
            for face in probe.FACES:
                self.assertGreater(probe.orientation(*(low[i][:2] for i in face)), 0)
                for i in range(5):
                    for j in range(5-i):
                        weights = (F(i, 4), F(j, 4), F(4-i-j, 4))
                        def height(vertices):
                            return sum(w * vertices[v][2] for w, v in zip(weights, face))
                        self.assertGreater(height(high), height(low))
                        self.assertLessEqual(abs(height(low)-height(original_low)), F(3, 4))
                        self.assertLessEqual(abs(height(high)-height(original_high)), F(3, 4))

    def test_fan_checker_rejects_out_of_scope_projection(self):
        low = list(probe.fan(-1, 0))
        low[4] = (F(1), F(0), F(0))
        with self.assertRaises(ValueError):
            probe.fan_pair_is_separated(low, probe.fan(1, 1))

    def test_repeated_and_parallel_runs_equal_serial(self):
        expected = json.dumps(probe.run(), sort_keys=True)
        self.assertEqual(json.dumps(probe.run(), sort_keys=True), expected)
        with ThreadPoolExecutor(max_workers=2) as pool:
            for result in pool.map(lambda _: probe.run(), range(4)):
                self.assertEqual(json.dumps(result, sort_keys=True), expected)


if __name__ == '__main__':
    unittest.main()
