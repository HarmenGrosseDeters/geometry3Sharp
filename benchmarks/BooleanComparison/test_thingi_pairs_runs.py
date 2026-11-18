"""Tests of data/thingi_pairs_runs.py (the full runs of the thingi-pairs classes): the plan, the shards, the summary and the
baseline-against-final comparison on synthetic result rows. No mesh and no network.
Run: python3 -m unittest discover -s benchmarks/BooleanComparison -p 'test_thingi_pairs_runs.py' -v
"""
import csv, json, os, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "data"))
import thingi_pairs as tp
import thingi_pairs_runs as run

def row(file, method="ours", cls="thingi-pairs", ms=(100, 100, 100), reasons=("", "", ""), status=("ok", "ok", "ok"), **extra):
    r = {"class": cls, "file": file, "method": method, "ie_error": 1e-12, "diff_error": 1e-12}
    for op, m, why, s in zip(run.OPS, ms, reasons, status):
        r.update({f"{op}_status": s, f"{op}_ms": m, f"{op}_clean_reason": why, f"{op}_selfint": 0})
    r.update(extra)
    return r

def write(path, rows):
    with open(path, "w") as f:
        f.write("".join(json.dumps(r) + "\n" for r in rows))

class PlanTests(unittest.TestCase):
    def test_default_plan_covers_every_pair_once(self):
        jobs = run.plan()["include"]
        for kind, n in (("normal", 1000), ("size", 100), ("cgal", 100)):
            spans = sorted((j["lo"], j["hi"]) for j in jobs if j["kind"] == kind)
            self.assertEqual(spans[0][0], 0)
            self.assertEqual(spans[-1][1], n)
            self.assertTrue(all(a[1] == b[0] for a, b in zip(spans, spans[1:])))
        self.assertEqual({j["methods"] for j in jobs if j["kind"] != "cgal"}, {"ours,manifold"})
        self.assertEqual({j["methods"] for j in jobs if j["kind"] == "cgal"}, {"cgal_epeck"})
        self.assertEqual(len({j["id"] for j in jobs}), len(jobs))

    def test_methods_classes_and_max_pairs(self):
        jobs = run.plan(shard=7, methods="ours", classes="normal,cgal", max_pairs=20)["include"]
        self.assertEqual([(j["lo"], j["hi"]) for j in jobs], [(0, 7), (7, 14), (14, 20)])
        self.assertEqual({j["methods"] for j in jobs}, {"ours"})
        jobs = run.plan(methods="cgal_epeck", classes="normal,size,cgal")["include"]
        self.assertEqual({j["kind"] for j in jobs}, {"cgal"})

    def test_bad_input(self):
        for kw in ({"methods": "ours,cherchi"}, {"classes": "nope"}, {"shard": 0}):
            with self.assertRaises(ValueError):
                run.plan(**kw)

class ShardTests(unittest.TestCase):
    def test_shard_rows_are_the_committed_rows(self):
        with tempfile.TemporaryDirectory() as d:
            out = os.path.join(d, "pairs_shard.csv")
            run.write_shard("cgal", 10, 15, out)
            rows = list(csv.DictReader(l for l in open(out) if not l.startswith("#")))
            self.assertEqual(rows, run.read_list("cgal")[1][10:15])
            self.assertEqual(open(out).read().splitlines()[0][:2], "# ")

    def test_size_shard_name_decides_the_class(self):
        with tempfile.TemporaryDirectory() as d:
            run.write_shard("size", 0, 2, os.path.join(d, "shard_size.csv"))
            with self.assertRaises(ValueError):
                run.write_shard("size", 0, 2, os.path.join(d, "shard.csv"))
            with self.assertRaises(ValueError):
                run.write_shard("normal", 0, 2, os.path.join(d, "shard_size.csv"))
            with self.assertRaises(ValueError):
                run.write_shard("normal", 990, 1001, os.path.join(d, "shard.csv"))

    def test_cgal_sample_is_part_of_pairs(self):
        normal = {r["pair"]: r for r in run.read_list("normal")[1]}
        for r in run.read_list("cgal")[1]:
            self.assertEqual(r, normal[r["pair"]])

class AnalysisTests(unittest.TestCase):
    def test_outcome(self):
        self.assertEqual(run.outcome(row("a"), "union"), "clean")
        self.assertEqual(run.outcome(row("a", status=("timeout", "ok", "ok")), "union"), "timeout")
        self.assertEqual(run.outcome(row("a", reasons=("", "", "degenerate triangles")), "difference"), "degenerate triangles")
        e = row("a", reasons=("", "empty", ""))
        self.assertEqual(run.outcome(e, "intersection"), "empty")
        e["union_selfint"] = 3          # a union that self-intersects makes the empty intersection inconsistent
        self.assertEqual(run.outcome(e, "intersection"), "empty (inconsistent)")
        e = row("a", reasons=("", "empty", "")); e["ie_error"] = 1e-3
        self.assertEqual(run.outcome(e, "intersection"), "empty (inconsistent)")

    def test_last_row_wins_and_directories(self):
        with tempfile.TemporaryDirectory() as d:
            os.makedirs(os.path.join(d, "run-1"))
            write(os.path.join(d, "run-1", "results-a.jsonl"), [row("p0", status=("crash", "ok", "ok")), row("p0")])
            with open(os.path.join(d, "run-1", "results-a.jsonl"), "a") as f:
                f.write('{"class": "thingi-pairs", "file": "cut')           # an interrupted line
            write(os.path.join(d, "run-1", "other.jsonl"), [row("zz")])    # not a results file
            rows = run.load([d])
            self.assertEqual(list(rows), [("thingi-pairs", "p0", "ours")])
            self.assertEqual(run.outcome(rows[("thingi-pairs", "p0", "ours")], "union"), "clean")

    def test_summary_numbers(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "results.jsonl")
            write(p, [row("p0", ms=(10, 20, 6000)),
                      row("p1", ms=(30, 40, 25000), reasons=("", "empty", "self-intersections")),
                      row("p2", ms=(0, 0, 0), status=("timeout", "ok", "ok")),
                      row("p0", method="manifold", cls="thingi-pairs-size")])
            lines = run.summary([p]).splitlines()
            cells = [c.strip() for c in next(l for l in lines if "| ours |" in l).split("|")[1:-1]]
            self.assertEqual(cells[:7], ["thingi-pairs", "ours", "3", "9", "8", "6", "0"])
            # p1: the empty intersection is inconsistent because its difference self-intersects
            self.assertEqual(cells[7], "empty (inconsistent) 1, self-intersections 1, timeout 1")
            self.assertEqual(cells[8], "1.00e-12")
            self.assertEqual(cells[9:], ["30", "25000", "25000", "2", "1"])    # median, p90, max over the 8 timed operations; > 5 s, > 20 s
            self.assertIn("| thingi-pairs-size | manifold | 1 | 3 | 3 | 3 | 0 | – |", "\n".join(lines))

    def test_compare_finds_regressions_and_slowdowns(self):
        with tempfile.TemporaryDirectory() as d:
            b, f = os.path.join(d, "base.jsonl"), os.path.join(d, "final.jsonl")
            write(b, [row("p0", ms=(100, 100, 100)), row("p1", ms=(100, 100, 100)), row("p2", ms=(100, 100, 100)), row("p3"), row("p0", method="manifold")])
            write(f, [row("p0", ms=(100, 250, 40)),                                        # intersection slower than 2x, difference faster than 2x
                      row("p1", reasons=("degenerate triangles", "", "")),                 # union clean before, not after
                      row("p2", status=("ok", "ok", "timeout")),                           # difference ok before, timeout after
                      row("p0", method="manifold", ms=(1, 1, 1))])                         # another method: not compared for ours
            out = run.compare(b, f)
            self.assertIn("3 pairs, 9 operations (1 pairs only in the baseline, 0 only in the final run)", out)
            self.assertIn("| clean (or volume-consistent empty) before, not after | 2 |", out)
            self.assertIn("| slower than 2x (final ms > 2 x baseline ms) | 1 |", out)
            self.assertIn("| faster than 2x | 1 |", out)
            self.assertIn("| thingi-pairs p1 union | clean | degenerate triangles |", out)
            self.assertIn("| thingi-pairs p2 difference | clean | timeout |", out)
            self.assertIn("| thingi-pairs p0 intersection | 100 | 250 | 2.5 |", out)

    def test_compare_of_equal_runs_reports_nothing(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "r.jsonl")
            write(p, [row("p0"), row("p1", reasons=("", "empty", ""))])
            out = run.compare(p, p)
            self.assertIn("| clean (or volume-consistent empty) before, not after | 0 |", out)
            self.assertIn("| slower than 2x (final ms > 2 x baseline ms) | 0 |", out)
            self.assertNotIn("Regressions", out)

    def test_compare_method_not_in_both_runs(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "r.jsonl")
            write(p, [row("p0")])
            self.assertIn("0 pairs, 0 operations", run.compare(p, p, method="manifold"))

if __name__ == "__main__":
    unittest.main()
