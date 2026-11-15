import csv
import io
import os
import tempfile
import unittest

import report


class ReportTests(unittest.TestCase):
    def write(self, rows):
        f = tempfile.NamedTemporaryFile('w', suffix='.csv', delete=False, newline='')
        w = csv.writer(f)
        w.writerow('stratum,runtime,round,threads,mask,case,op,phase,ms'.split(','))
        w.writerows(rows)
        f.close()
        self.addCleanup(os.remove, f.name)
        return f.name

    def test_serial_fraction_recovers_amdahl(self):
        s = 0.25
        times = {n: 1000 * (s + (1 - s) / n) for n in (1, 2, 4, 6, 12)}
        self.assertAlmostEqual(report.serial_fraction(times), s, places=9)

    def test_serial_fraction_fully_parallel_and_fully_serial(self):
        self.assertAlmostEqual(report.serial_fraction({1: 100.0, 2: 50.0, 4: 25.0}), 0.0)
        self.assertAlmostEqual(report.serial_fraction({1: 100.0, 2: 100.0, 4: 100.0}), 1.0)
        self.assertIsNone(report.serial_fraction({2: 50.0, 4: 25.0}))
        self.assertIsNone(report.serial_fraction({1: 0.0, 2: 0.0}))

    def test_phases_summed_over_cases_median_over_rounds_and_assembly_without_simplify(self):
        rows = []
        for rd, factor in ((1, 1), (2, 2), (3, 3)):
            for case in ('a', 'b'):
                rows += [['s', 'net10.0', rd, 1, '0x1', case, 'union', 'intersections', 100 * factor],
                         ['s', 'net10.0', rd, 1, '0x1', case, 'union', 'simplify', 10 * factor],
                         ['s', 'net10.0', rd, 1, '0x1', case, 'union', 'assembly', 30 * factor]]
        res = report.per_phase(report.load(self.write(rows)))
        self.assertEqual(res['net10.0']['intersections'][1], 400)      # medians of 200, 400, 600
        self.assertEqual(res['net10.0']['assembly'][1], 80)            # (60 - 20), (120 - 40), (180 - 60) summed: 40, 80, 120
        self.assertEqual(res['net10.0']['total'][1], 400 + 40 + 80)

    def test_tables_and_runtime_comparison(self):
        rows = []
        for rt, k in (('net10.0', 1), ('net48', 2)):
            for n in (1, 4):
                rows.append(['s', rt, 1, n, '0x1', 'a', 'union', 'rounding', 4000 * k / n])
        res = report.per_phase(report.load(self.write(rows)))
        text = report.table('net48', res['net48']) + report.compare(res)
        self.assertIn('| rounding | 8.0 | 2.0 | 4.00 | 0.00 | 0.0 |', text)
        self.assertIn('| 4 | 1.0 | 2.0 | 2.00 |', text)


if __name__ == '__main__':
    unittest.main()
