# Tests of the UV atlas comparison report (compare_report.py): NaN values written by the benchmark as the JSON
# strings "NaN" are read back as floats and are no value in the medians and percentiles.
#   python3 -m unittest discover -s benchmarks/UVAtlasBenchmark -p 'test_compare_report.py' -v
import json
import math
import os
import tempfile
import unittest

import compare_report


def row(**kw):
    r = {"file": "a.stl", "tool": "ours", "status": "ok", "producer_status": "Success", "category": "clean",
         "overlap_pairs": 0, "inverted": 0, "degenerate_uv": 0, "discs": 1, "components": 1, "charts": 3,
         "coverage": 0.4, "seam_rel": 2.0, "mean_conformal": 1.2, "mean_area": 0.1, "density_logstd": 0.2,
         "min_gap_px": 5, "ms": 1000.0}
    r.update(kw)
    return r


class LoadTests(unittest.TestCase):
    def test_nan_strings_are_read_as_floats(self):
        # the benchmark writes NaN as the JSON string "NaN" (System.Text.Json)
        line = json.dumps(row(ms="NaN", seam_rel="NaN", mean_conformal="Infinity"))
        fd, path = tempfile.mkstemp(suffix=".jsonl")
        try:
            with os.fdopen(fd, "w") as f:
                f.write(line + "\n")
            (r,) = compare_report.load(path)
        finally:
            os.remove(path)
        self.assertTrue(math.isnan(r["ms"]))
        self.assertTrue(math.isnan(r["seam_rel"]))
        self.assertEqual(r["mean_conformal"], float("inf"))
        self.assertEqual(r["coverage"], 0.4)   # other values untouched

    def test_no_path_gives_no_rows(self):
        self.assertEqual(compare_report.load(None), [])


class SummaryTests(unittest.TestCase):
    def test_nan_time_is_left_out_of_median_and_p90(self):
        rows = [row(ms=1000.0), row(ms=float("nan")), row(ms=3000.0)]
        s = compare_report.summary(rows)
        # median of 1 s and 3 s; p90 nearest rank over the two values: index int(0.9 * 2) = 1, the larger one
        self.assertEqual(s["time median (s)"], "2.00")
        self.assertEqual(s["time p90 (s)"], "3.00")

    def test_percentile_ignores_nan_wherever_it_sits(self):
        # ten values with a NaN first: unsorted NaN shifts the nearest-rank index, so the p90 would be 9, not 10
        xs = [float("nan")] + [float(i) for i in range(1, 11)]
        self.assertEqual(compare_report.percentile(xs, 0.9), 10.0)

    def test_all_nan_time_is_dash(self):
        s = compare_report.summary([row(ms=float("nan")), row(ms=float("nan"))])
        self.assertEqual(s["time median (s)"], "-")
        self.assertEqual(s["time p90 (s)"], "-")

    def test_nan_row_from_the_benchmark_counts_as_failed_output_not_as_time(self):
        # a row evaluated without an atlas: status no_output, ms NaN, no metrics
        rows = [row(ms=1000.0), row(status="no_output", producer_status="Failed", ms="NaN")]
        loaded = [{k: (float("nan") if v == "NaN" else v) for k, v in r.items()} for r in rows]
        s = compare_report.summary(loaded)
        self.assertEqual(s["meshes"], 2)
        self.assertEqual(s["time median (s)"], "1.00")
        self.assertEqual(s["failed (no atlas / mismatch)"], 1)


if __name__ == "__main__":
    unittest.main()
