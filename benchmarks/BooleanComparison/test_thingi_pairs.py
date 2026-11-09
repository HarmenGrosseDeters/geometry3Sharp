"""Tests of the thingi-pairs class: the generator (determinism, ranges, the committed lists) and the oracle (NotCleanReason,
volume identities) on procedural pairs with known volumes. No mesh files: the meshes are written here.
The integration tests need the built tool (benchmarks/BooleanComparison/bin/Release/net10.0 or BOOLCMP_TOOL).
Run: python3 -m unittest discover -s benchmarks/BooleanComparison -p 'test_thingi_pairs.py' -v
"""
import json, math, os, shutil, subprocess, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "data"))
DLL = os.environ.get("BOOLCMP_TOOL", os.path.join(HERE, "bin/Release/net10.0/BooleanComparison.dll"))
os.environ["BOOLCMP_TOOL"] = DLL
import thingi_pairs as tp
import analyze

def read(path):
    with open(path, "rb") as f:
        return f.read()

class GeneratorTests(unittest.TestCase):
    def test_splitmix64_reference_values(self):
        r = tp.SplitMix64(0)
        self.assertEqual([r.next() for _ in range(3)], [0xE220A8397B1DCDAF, 0x6E789E6AA1B965F4, 0x06C45D188009454F])

    def models(self):
        return [(str(1000 + i), 1000 + 997 * i) for i in range(300)] + [(str(9000 + i), 150000 + 5000 * i) for i in range(60)]

    def test_deterministic_and_seeded(self):
        with tempfile.TemporaryDirectory() as d1, tempfile.TemporaryDirectory() as d2:
            tp.generate(self.models(), d1)
            tp.generate(self.models(), d2)
            for f in ("pairs.csv", "pairs_size.csv", "pairs_cgal_sample.csv"):
                self.assertEqual(read(os.path.join(d1, f)), read(os.path.join(d2, f)))
        pool = sorted(k for k, n in self.models() if tp.NORMAL_TRIS[0] <= n <= tp.NORMAL_TRIS[1])
        self.assertNotEqual(tp.pair_rows(pool, 1, 20), tp.pair_rows(pool, 2, 20))
        self.assertEqual(tp.pair_rows(pool, 1, 20)[:10], tp.pair_rows(pool, 1, 10))   # a prefix does not depend on the count

    def test_ranges(self):
        with tempfile.TemporaryDirectory() as d:
            tp.generate(self.models(), d)
            tris = dict(self.models())
            for name, (lo, hi), count in (("pairs.csv", tp.NORMAL_TRIS, tp.PAIRS_NORMAL), ("pairs_size.csv", tp.SIZE_TRIS, tp.PAIRS_SIZE)):
                rows = list(__import__("csv").DictReader(l for l in open(os.path.join(d, name)) if not l.startswith("#")))
                self.assertEqual(len(rows), count)
                for r in rows:
                    self.assertNotEqual(r["a"], r["b"])
                    self.assertTrue(lo <= tris[r["a"]] <= hi and lo <= tris[r["b"]] <= hi)
                    q = [float(r[k]) for k in ("qx", "qy", "qz", "qw")]
                    self.assertAlmostEqual(math.sqrt(sum(x * x for x in q)), 1.0, places=10)
                    self.assertTrue(all(abs(float(r[k])) <= 0.5 for k in ("tx", "ty", "tz")))

    def test_committed_lists_are_the_generator_output(self):
        if not os.path.exists(os.path.join(tp.LISTS, "clean_models.csv")):
            self.skipTest("no committed lists")
        with tempfile.TemporaryDirectory() as d:
            tp.generate(None, d)
            for f in ("pairs.csv", "pairs_size.csv", "pairs_cgal_sample.csv"):
                self.assertEqual(read(os.path.join(d, f)), read(os.path.join(tp.LISTS, f)), f)

    def test_cgal_sample_is_a_subset_of_the_pairs(self):
        with tempfile.TemporaryDirectory() as d:
            tp.generate(self.models(), d)
            rows = lambda n: [l for l in open(os.path.join(d, n)) if not l.startswith("#")][1:]
            full, sample = rows("pairs.csv"), rows("pairs_cgal_sample.csv")
            self.assertEqual(len(sample), tp.PAIRS_SAMPLE)
            self.assertEqual(len(set(sample)), tp.PAIRS_SAMPLE)
            self.assertTrue(set(sample) <= set(full))
            self.assertEqual(sample, [r for r in full if r in set(sample)])      # the order of the list
            self.assertNotEqual(sample, full[:tp.PAIRS_SAMPLE])

class OracleTests(unittest.TestCase):
    def row(self, **kw):
        r = {"class": "thingi-pairs", "ops": analyze.OPS, "all_results": True, "ie_error": 1e-12, "diff_error": 1e-12}
        for op in analyze.OPS:
            r.update({f"{op}_status": "ok", f"{op}_wclosed": True, f"{op}_manifold_closed": True, f"{op}_selfint": 0, f"{op}_clean_reason": ""})
        r.update(kw)
        return r

    def test_outcomes(self):
        self.assertEqual(analyze.outcome(self.row()), "clean")
        self.assertEqual(analyze.outcome(self.row(intersection_clean_reason="empty")), "clean")      # correct: the volumes agree
        self.assertEqual(analyze.outcome(self.row(ie_error=1e-8)), "clean")
        self.assertEqual(analyze.outcome(self.row(ie_error=1e-6)), "volume")                          # above the class tolerance 1e-7 ...
        r = self.row(ie_error=1e-6); r["class"] = "cgal"
        self.assertEqual(analyze.outcome(r), "clean")                                                  # ... below the default 1e-6 -> not above
        self.assertEqual(analyze.outcome(self.row(union_clean_reason="degenerate triangles")), "degenerate")
        self.assertEqual(analyze.outcome(self.row(union_selfint=2, union_clean_reason="self-intersections")), "self-int.")
        self.assertEqual(analyze.outcome(self.row(diff_error=1e-3)), "volume")
        self.assertEqual(analyze.outcome(self.row(union_clean_reason="inside-out components")), "inside-out")
        self.assertFalse(analyze.correct(self.row(union_clean_reason="inside-out components")))

    def test_concatenation_is_not_correct(self):
        # union = A and B concatenated, intersection empty, difference = A: both identities hold, the union
        # self-intersects. In the thingi-pairs classes that is not correct, and the empty intersection is not counted
        # as volume-consistent; elsewhere self-intersections stay a correct outcome (touching parts)
        r = self.row(union_selfint=12, union_clean_reason="self-intersections", intersection_clean_reason="empty", ie_error=2.8e-16, diff_error=0.0)
        self.assertEqual(analyze.outcome(r), "self-int.")
        self.assertFalse(analyze.correct(r))
        self.assertFalse(tp.consistent(r))
        other = dict(r, **{"class": "cgal"})
        self.assertTrue(analyze.correct(other))
        self.assertTrue(analyze.correct(self.row()) and tp.consistent(self.row(intersection_clean_reason="empty")))

    def test_summary_counts_each_case_once_and_inconsistent_empties(self):
        good = dict(self.row(intersection_clean_reason="empty"), file="p1", method="ours")
        bad = dict(self.row(union_selfint=12, union_clean_reason="self-intersections", intersection_clean_reason="empty"), file="p2", method="ours")
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "r.jsonl")
            with open(path, "w") as f:
                for r in (good, good, bad):      # p1 twice (a resumed run appended it again)
                    f.write(json.dumps(r) + "\n")
            import io, contextlib
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                tp.cmd_summary([path])
        row = out.getvalue().splitlines()[2].split("|")
        self.assertEqual(row[3].strip(), "2")                 # pairs
        self.assertEqual(row[4].strip(), "6")                 # operations
        self.assertEqual(row[9].strip(), "1")                 # empty (volume-consistent): p1's only
        self.assertIn("empty (inconsistent) 1", row[10])

def cube(lo, hi):
    v = [(x, y, z) for x in (lo, hi) for y in (lo, hi) for z in (lo, hi)]
    f = [(0, 1, 3), (0, 3, 2), (4, 6, 7), (4, 7, 5), (0, 4, 5), (0, 5, 1), (2, 3, 7), (2, 7, 6), (0, 2, 6), (0, 6, 4), (1, 5, 7), (1, 7, 3)]
    return v, f

def sphere(radius, n=24):
    v = [(0, 0, radius)]
    for i in range(1, n):
        for j in range(2 * n):
            th, ph = math.pi * i / n, math.pi * j / n
            v.append((radius * math.sin(th) * math.cos(ph), radius * math.sin(th) * math.sin(ph), radius * math.cos(th)))
    v.append((0, 0, -radius))
    ring = lambda i, j: 1 + (i - 1) * 2 * n + j % (2 * n)
    f = [(0, ring(1, j), ring(1, j + 1)) for j in range(2 * n)]
    for i in range(1, n - 1):
        for j in range(2 * n):
            f += [(ring(i, j), ring(i + 1, j), ring(i + 1, j + 1)), (ring(i, j), ring(i + 1, j + 1), ring(i, j + 1))]
    last = len(v) - 1
    f += [(last, ring(n - 1, j + 1), ring(n - 1, j)) for j in range(2 * n)]
    return v, f

def write_off(path, v, f):
    with open(path, "w") as o:
        o.write(f"OFF\n{len(v)} {len(f)} 0\n")
        o.writelines(f"{x!r} {y!r} {z!r}\n" for x, y, z in v)
        o.writelines(f"3 {a} {b} {c}\n" for a, b, c in f)

@unittest.skipUnless(os.path.exists(DLL), "build benchmarks/BooleanComparison first")
class PairOracleTests(unittest.TestCase):
    """the pair preparation and the oracle of eval, end to end on procedural pairs with known volumes"""
    @classmethod
    def setUpClass(cls):
        import compare
        cls.compare = compare
        cls.tmp = tempfile.mkdtemp(prefix="thingi_pairs_test_")
        write_off(os.path.join(cls.tmp, "cube.off"), *cube(-3.0, 5.0))        # normalized to the unit box by the tool
        write_off(os.path.join(cls.tmp, "sphere.off"), *sphere(7.0))

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def run_pair(self, a, b, params):
        case = {"class": "thingi-pairs", "name": "test", "thingi_pair": [os.path.join(self.tmp, a), os.path.join(self.tmp, b)] + params}
        rows = self.compare.process(case, ["ours"], 300)
        self.assertEqual(rows[0]["method"], "ours", rows[0])
        return rows[0]

    def test_overlapping_cubes_known_volumes(self):
        r = self.run_pair("cube.off", "cube.off", ["0", "0", "0", "1", "0.5", "0.5", "0.5"])
        self.assertEqual(r["input"]["tris"], 24)
        self.assertAlmostEqual(r["union_volume"], 1.875, places=12)
        self.assertAlmostEqual(r["intersection_volume"], 0.125, places=12)
        self.assertAlmostEqual(r["difference_volume"], 0.875, places=12)
        self.assertLess(max(r["ie_error"], r["diff_error"]), 1e-12)
        for op in analyze.OPS:
            self.assertEqual(r[f"{op}_clean_reason"], "")
        self.assertEqual(analyze.outcome(r), "clean")

    def test_rotated_spheres_identities(self):
        # a rotation by 90 degrees about the z axis and a shift; the inputs are not unit spheres: normalization scales them
        s = math.sqrt(0.5)
        r = self.run_pair("sphere.off", "sphere.off", ["0", "0", repr(s), repr(s), "0.3", "-0.2", "0.1"])
        self.assertLess(max(r["ie_error"], r["diff_error"]), 1e-10)
        self.assertGreater(r["intersection_volume"], 0)
        self.assertEqual(analyze.outcome(r), "clean")

    def test_disjoint_pair_has_a_correct_empty_intersection(self):
        r = self.run_pair("cube.off", "sphere.off", ["0", "0", "0", "1", "3", "0", "0"])
        self.assertEqual(r["intersection_clean_reason"], "empty")
        self.assertEqual(r["intersection_tris"], 0)
        self.assertEqual(r["union_clean_reason"], "")
        self.assertLess(max(r["ie_error"], r["diff_error"]), 1e-12)
        self.assertEqual(analyze.outcome(r), "clean")

    def test_resume_skips_finished_cases(self):
        cube_path = os.path.join(self.tmp, "cube.off")
        cases = os.path.join(self.tmp, "resume_cases.jsonl")
        out = os.path.join(self.tmp, "resume_out.jsonl")
        with open(cases, "w") as f:
            for i, t in enumerate(("0.5", "0.25")):
                f.write(json.dumps({"class": "thingi-pairs", "name": f"c{i}", "thingi_pair": [cube_path, cube_path, "0", "0", "0", "1", t, t, t]}) + "\n")
        def run(*extra):
            return subprocess.run([sys.executable, os.path.join(HERE, "compare.py"), cases, out, "--methods", "ours", "--resume", *extra],
                                  check=True, capture_output=True, text=True).stdout
        run("--only", "c0")
        with open(out) as f:
            self.assertEqual([json.loads(l)["file"] for l in f], ["c0"])
        with open(out, "a") as f:
            f.write('{"class": "thingi-pairs", "file": "c1", "method": "ours", "uni')    # an interrupted write is ignored
        text = run()
        self.assertIn("resume: 1 cases finished, 1 to run", text)
        with open(out) as f:
            rows = []
            for l in f:
                try:
                    rows.append(json.loads(l))
                except ValueError:
                    pass
        self.assertEqual(sorted(r["file"] for r in rows), ["c0", "c1"])
        self.assertIn("resume: 2 cases finished, 0 to run", run())
        # a method added later runs alone: the finished method's rows are not appended again
        def run_methods(methods):
            return subprocess.run([sys.executable, os.path.join(HERE, "compare.py"), cases, out, "--methods", methods, "--resume"],
                                  check=True, capture_output=True, text=True).stdout
        with open(out) as f:
            before = sum(1 for l in f if '"method": "ours"' in l)
        self.assertIn("resume: 2 cases finished, 0 to run", run_methods("ours"))
        with open(out) as f:
            self.assertEqual(before, sum(1 for l in f if '"method": "ours"' in l))

    def test_a_wrong_result_is_caught(self):
        # A and B as the tool prepares them, results by the tool, then the union damaged in three ways
        d = os.path.join(self.tmp, "damage")
        tool = ["dotnet", DLL]
        subprocess.run(tool + ["thingi-pair", os.path.join(self.tmp, "cube.off"), os.path.join(self.tmp, "cube.off"), d, "0", "0", "0", "1", "0.5", "0.5", "0.5"], check=True, capture_output=True)
        def results(union_edit):
            for op in analyze.OPS:
                subprocess.run(tool + ["run", os.path.join(d, "A.off"), os.path.join(d, "B.off"), op, os.path.join(d, f"ours_{op}.off")], check=True, capture_output=True)
            with open(os.path.join(d, "ours_union.off")) as f:
                lines = f.read().splitlines()
            with open(os.path.join(d, "ours_union.off"), "w") as f:
                f.write("\n".join(union_edit(lines)) + "\n")
            ev = subprocess.run(tool + ["eval", d], check=True, capture_output=True, text=True).stdout
            r = json.loads(next(l for l in ev.splitlines() if l.startswith("{")))
            r.update({"class": "thingi-pairs", "ops": analyze.OPS, **{f"{op}_status": "ok" for op in analyze.OPS}})
            return r
        def drop_triangle(lines):
            n_v, n_f = map(int, lines[1].split()[:2])
            lines[1] = f"{n_v} {n_f - 1} 0"
            return lines[:-1]
        def move_vertex(lines):      # one vertex moved by 1e-4: closed, manifold, but the volume is off
            x, y, z = map(float, lines[2].split())
            lines[2] = f"{x + 1e-4!r} {y!r} {z!r}"
            return lines
        r = results(drop_triangle)
        self.assertEqual(r["union_clean_reason"], "not closed")
        self.assertEqual(analyze.outcome(r), "not closed")
        r = results(move_vertex)
        self.assertEqual(r["union_clean_reason"], "")
        self.assertEqual(analyze.outcome(r), "volume")
        self.assertGreater(r["ie_error"], 1e-7)
        r = results(lambda l: l)    # the intact results are clean again
        self.assertEqual(analyze.outcome(r), "clean")

if __name__ == "__main__":
    unittest.main()
