import os, sys, unittest
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "perf"))
import t6

def row(f, m, ms, reason="", status="ok"):
    r = {"file": f, "method": m, "class": "thingi-pairs"}
    for op in t6.OPS:
        r.update({f"{op}_status": status, f"{op}_ms": ms, f"{op}_clean_reason": reason, f"{op}_tris": 10, f"{op}_phases": {"a": 5, "b": 9}})
    return r

class T6Tests(unittest.TestCase):
    def test_sample_restricted_and_topup(self):
        tris = {"1": 500, "2": 600, "3": 70000, "4": 40000, "5": 2000}
        mk = lambda p, a, b: {"pair": p, "a": a, "b": b}
        sample = [mk("1", "1", "2"), mk("2", "3", "4"), mk("3", "3", "3")]    # 1100, 110000 (out), 140000 (out)
        self.assertEqual([r["pair"] for r in t6.select(sample, [], tris, min_pairs=1)], ["1"])
        full = sample + [mk("4", "4", "5"), mk("5", "5", "5")]
        got = t6.select(sample, full, tris, min_pairs=3)
        self.assertEqual(len(got), 3)
        self.assertEqual(got, t6.select(sample, full, tris, min_pairs=3))     # fixed seed

    def test_ratios_and_allclean(self):
        tris = {"1": 3000, "2": 4000, "3": 30000, "4": 30000}
        rows = {}
        for f, ours, cgal in (("p1_1+2", 200, 100), ("p2_3+4", 100, 400)):
            rows[(f, "ours")], rows[(f, "cgal_epeck")] = row(f, "ours", ours), row(f, "cgal_epeck", cgal)
        out, recs = t6.analyse(rows, tris)
        self.assertEqual(out["1k-10k"]["ratios"], [2.0] * 3)
        self.assertEqual(out["10k-100k"]["ratios"], [0.25] * 3)
        self.assertIn("**yes**", t6.report("x", rows, tris))
        rows[("p2_3+4", "ours")] = row("p2_3+4", "ours", 100, reason="degenerate triangles")
        self.assertIn("**no**", t6.report("x", rows, tris))

if __name__ == "__main__":
    unittest.main()
