"""Tests of the Geogram comparison harness (docs/research/GEOGRAM_BOOLEAN_COMPARISON_2026-11.md): the Triangle / TetGen path guard of
external/build_geogram.sh, the geogram method of compare.py (with a fake geogram_bool, no geogram is built here), the T6 report against
geogram (ratios, volume agreement) and the step-wise ABC comparison (data/abc_chained.py versus)."""
import io, json, os, re, subprocess, sys, tempfile, unittest
from contextlib import redirect_stdout

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "perf"))
sys.path.insert(0, os.path.join(HERE, "data"))
import compare            # noqa: E402
import t6                 # noqa: E402
import abc_chained as ac  # noqa: E402

BUILD = os.path.join(HERE, "external", "build_geogram.sh")

def touch(root, rel):
    p = os.path.join(root, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    open(p, "w").close()

def check(root):
    return subprocess.run(["bash", BUILD, "--check", root], capture_output=True, text=True)

class PathGuardTests(unittest.TestCase):
    """the check that fails the build if a Triangle or TetGen path is in the geogram checkout or build"""

    def tree(self, *files):
        d = tempfile.mkdtemp(prefix="geoguard_")
        self.addCleanup(lambda: subprocess.run(["rm", "-rf", d]))
        for f in files:
            touch(d, f)
        return d

    def test_geogram_own_files_pass(self):
        # geogram's own triangle code (the boolean needs it) and the permissive libraries are not excluded
        d = self.tree("src/lib/geogram/mesh/triangle_intersection.cpp", "src/lib/geogram/mesh/mesh_surface_intersection.cpp",
                      "src/lib/geogram/delaunay/CDT_2d.cpp", "src/lib/geogram/third_party/rply/rply.c", ".git/tetgen.pack")
        r = check(d)
        self.assertEqual(r.returncode, 0, r.stdout)

    def test_each_excluded_path_fails(self):
        for f in ("src/lib/geogram/third_party/triangle/triangle.c", "src/lib/geogram/third_party/tetgen/tetgen.cxx",
                  "src/lib/geogram/delaunay/delaunay_triangle.cpp", "src/lib/geogram/delaunay/delaunay_tetgen.h",
                  "build/src/lib/geogram/CMakeFiles/geogram.dir/delaunay/delaunay_TetGen.cpp.o"):
            with self.subTest(f=f):
                r = check(self.tree("src/lib/geogram/mesh/mesh.cpp", f))
                self.assertEqual(r.returncode, 1)
                self.assertIn(os.path.basename(f), r.stdout)

    def test_sparse_patterns_cover_the_excluded_paths(self):
        text = open(BUILD).read()
        block = re.search(r"EXCLUDED=\((.*?)\n\)", text, re.S).group(1)
        pats = re.findall(r"^\s*'([^']+)'", block, re.M)
        self.assertEqual(sorted(pats), sorted(["/src/lib/geogram/third_party/triangle/", "/src/lib/geogram/third_party/tetgen/",
                                               "/src/lib/geogram/delaunay/delaunay_triangle.*", "/src/lib/geogram/delaunay/delaunay_tetgen.*",
                                               "/src/lib/geogram/mesh/mesh_tetrahedralize.cpp", "**/third_party/triangle/",
                                               "**/*tetgen*", "**/*TetGen*", "**/*Tetgen*", "**/delaunay_triangle.*"]))
        for flag in ("-DGEOGRAM_WITH_TRIANGLE=OFF", "-DGEOGRAM_WITH_TETGEN=OFF", "--filter=blob:none", "core.sparseCheckoutCone false",
                     "-Wl,--no-allow-shlib-undefined", 'nm --defined-only "$lib"'):
            self.assertIn(flag, text)

class SparseCheckoutTests(unittest.TestCase):
    """the checkout of build_geogram.sh from a local stand-in repository: the excluded files are neither checked out nor downloaded"""

    def git(self, *a, cwd=None, env=None):
        return subprocess.run(["git", *a], cwd=cwd, capture_output=True, text=True, check=True, env=env).stdout

    def test_excluded_blobs_are_never_fetched(self):
        d = tempfile.mkdtemp(prefix="geosparse_")
        self.addCleanup(lambda: subprocess.run(["rm", "-rf", d]))
        src = os.path.join(d, "src")
        keep = ["src/lib/geogram/mesh/mesh.cpp", "src/lib/geogram/mesh/triangle_intersection.cpp", "src/lib/geogram/delaunay/CDT_2d.cpp"]
        drop = ["src/lib/geogram/third_party/triangle/triangle.c", "src/lib/geogram/third_party/tetgen/tetgen.cxx",
                "src/lib/geogram/delaunay/delaunay_triangle.cpp", "src/lib/geogram/delaunay/delaunay_tetgen.h",
                "src/lib/geogram/mesh/mesh_tetrahedralize.cpp", "src/lib/other/third_party/triangle/x.c", "src/lib/foo/TetGen_copy.h"]
        os.makedirs(src)
        self.git("init", "-q", cwd=src)
        for k in ("uploadpack.allowFilter", "uploadpack.allowAnySHA1InWant"):
            self.git("config", k, "true", cwd=src)
        for f in keep + drop:
            p = os.path.join(src, f)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            with open(p, "w") as fh:
                fh.write("content of " + f + "\n")          # distinct blobs
        self.git("add", "-A", cwd=src)
        self.git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "x", cwd=src)
        sha = self.git("rev-parse", "HEAD", cwd=src).strip()
        out = os.path.join(d, "out")
        r = subprocess.run(["bash", BUILD, "--sparse", "file://" + src, sha, out], capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, r.stderr)
        files = sorted(os.path.relpath(os.path.join(a, f), out) for a, ds, fs in os.walk(out) if ".git" not in a.split(os.sep) for f in fs)
        self.assertEqual(files, sorted(keep))
        env = dict(os.environ, GIT_NO_LAZY_FETCH="1")     # listing must not fetch a missing blob
        missing = {l[1:] for l in self.git("rev-list", "--objects", "--missing=print", "FETCH_HEAD", cwd=out, env=env).splitlines() if l.startswith("?")}
        blob = {f: self.git("rev-parse", "HEAD:" + f, cwd=src).strip() for f in keep + drop}
        self.assertEqual({f for f in keep + drop if blob[f] in missing}, set(drop))

FAKE = r'''#!/usr/bin/env python3
# a stand-in for geogram_bool: records its arguments and environment, writes a one-triangle OFF and prints the JSON line
import json, os, sys
log = os.environ["FAKE_LOG"]
open(log, "a").write(json.dumps({"argv": sys.argv[1:], "threads": os.environ.get("GEOGRAM_THREADS")}) + "\n")
if sys.argv[1] == "seq":
    ops = open(sys.argv[2]).read()
    open(log, "a").write(json.dumps({"ops": ops}) + "\n")
    out = sys.argv[3]
else:
    out = sys.argv[4]
open(out, "w").write("OFF\n3 1 0\n0 0 0\n1 0 0\n0 1 0\n3 0 1 2\n")
print("geogram log line")
print(json.dumps({"ok": True, "ms": 7, "done": 2} if sys.argv[1] == "seq" else {"ok": True, "ms": 5}))
'''

class CompareTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.mkdtemp(prefix="geocmp_")
        self.addCleanup(lambda: subprocess.run(["rm", "-rf", self.d]))
        self.fake = os.path.join(self.d, "geogram_bool")
        open(self.fake, "w").write(FAKE)
        os.chmod(self.fake, 0o755)
        self.log = os.path.join(self.d, "log.jsonl")
        self.env = {"GEOGRAM_BOOL": self.fake, "FAKE_LOG": self.log}
        self.old = {k: os.environ.get(k) for k in self.env}
        os.environ.update(self.env)
        self.addCleanup(self.restore)

    def restore(self):
        for k, v in self.old.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v

    def calls(self):
        return [json.loads(l) for l in open(self.log)]

    def test_command_of_one_boolean(self):
        self.assertEqual(compare.commands("geogram", "difference", "A.off", "B.off", "/t/geogram_difference"),
                         [self.fake, "difference", "A.off", "B.off", "/t/geogram_difference.off"])

    def test_run_one_parses_the_json_line_after_the_log(self):
        for f in ("A.off", "B.off"):
            open(os.path.join(self.d, f), "w").write("OFF\n0 0 0\n")
        r = compare.run_one("geogram", "union", self.d, 60)
        self.assertEqual((r["status"], r["ms"]), ("ok", 5))
        self.assertTrue(os.path.exists(os.path.join(self.d, "geogram_union.off")))

    def test_failure_of_an_accepted_input_is_failed_not_rejected(self):
        self.assertEqual(compare.status_of({"ok": False, "error": "exception: assertion failed"}, 0), "failed")

    def test_sequence_uses_the_seq_list_format(self):
        request = {"runs": [{"operations": [{"op": "load-mesh", "path": "/m/a.off"}, {"op": "load-mesh", "path": "/m/b.off"},
                                            {"op": "boolean-union", "args": [0, 1]}, {"op": "boolean-difference", "args": [2, 0]}]}]}
        r = compare.run_sequence("geogram", request, self.d, 60)
        self.assertEqual((r["status"], r["ms"], r["done"]), ("ok", 7, 2))
        calls = self.calls()
        self.assertEqual(calls[0]["argv"][0], "seq")
        self.assertEqual(calls[1]["ops"], "load /m/a.off\nload /m/b.off\nunion 0 1\ndifference 2 0\n")
        self.assertTrue(os.path.exists(os.path.join(self.d, "geogram_final.off")))

def row(f, m, ms, vol, reason="", status="ok", vA=1.0, vB=1.0):
    r = {"file": f, "method": m, "class": "thingi-pairs", "input": {"vA": vA, "vB": vB}}
    for op in t6.OPS:
        r.update({f"{op}_status": status, f"{op}_ms": ms, f"{op}_clean_reason": reason, f"{op}_tris": 10,
                  f"{op}_phases": {}, f"{op}_volume": vol[op] if isinstance(vol, dict) else vol})
    return r

class T6GeogramTests(unittest.TestCase):
    tris = {"1": 3000, "2": 4000, "3": 30000, "4": 30000}

    def rows(self):
        rows = {}
        for f, ours, geo in (("p1_1+2", 100, 400), ("p2_3+4", 300, 100)):
            rows[(f, "ours")] = row(f, "ours", ours, 0.5)
            rows[(f, "geogram")] = row(f, "geogram", geo, 0.5 + 1e-9)       # rounding-size difference: agrees
        return rows

    def test_ratio_and_volume_agreement_against_geogram(self):
        out, recs = t6.analyse(self.rows(), self.tris, "geogram")
        self.assertEqual(out["1k-10k"]["ratios"], [0.25] * 3)
        self.assertEqual(out["10k-100k"]["ratios"], [3.0] * 3)
        self.assertTrue(all(abs(x - 5e-10) < 1e-15 for x in out["1k-10k"]["vdiff"]))
        rep = t6.report("1 thread", self.rows(), self.tris, "geogram")
        self.assertIn("Geogram clean", rep)
        self.assertIn("| 3/3 |", rep)                          # volumes agree in each bucket
        self.assertIn("volumes agree in 6 of 6", rep)
        self.assertIn("Summed times (both ok): ours 1.2 s, Geogram 1.5 s, ratio 0.80.", rep)
        self.assertNotIn("CGAL", rep)

    def test_a_volume_that_disagrees_is_counted_and_listed(self):
        rows = self.rows()
        rows[("p2_3+4", "geogram")] = row("p2_3+4", "geogram", 100, {"union": 0.5, "intersection": 0.4, "difference": 0.5})
        out, _ = t6.analyse(rows, self.tris, "geogram")
        self.assertEqual(sum(1 for x in out["10k-100k"]["vdiff"] if x <= t6.VOL_TOL), 2)
        self.assertEqual([(f, op) for f, op, *_ in t6.volume_disagreements([rows], "geogram")], [("p2_3+4", "intersection")])
        with tempfile.NamedTemporaryFile("w", suffix=".jsonl", delete=False) as fh:
            for r in rows.values():
                fh.write(json.dumps(r) + "\n")
        self.addCleanup(os.remove, fh.name)
        buf = io.StringIO()
        old = t6.tris_of
        self.addCleanup(setattr, t6, "tris_of", old)
        t6.tris_of = lambda: self.tris            # the pair list of the repository is not needed for this table
        with redirect_stdout(buf):
            t6.cmd_report(["--other", "geogram", f"all cores={fh.name}"])
        self.assertIn("| p2_3+4 | intersection | 0.5 | 0.4 | 5.0e-02 |", buf.getvalue())

    def test_geogram_failure_and_timeout_do_not_enter_the_ratios(self):
        rows = self.rows()
        rows[("p1_1+2", "geogram")] = row("p1_1+2", "geogram", 0, 0.5, status="timeout")
        out, _ = t6.analyse(rows, self.tris, "geogram")
        self.assertEqual(out["1k-10k"]["ratios"], [])
        self.assertEqual(out["1k-10k"]["m"]["geogram"]["timeout"], 3)
        rep = t6.report("x", rows, self.tris, "geogram")
        self.assertIn("of Geogram: **no**", rep)
        self.assertIn("ours clean or correctly empty, no refusal, failure or timeout): **yes**", rep)

    def test_unclean_results_leave_the_both_clean_ratio_and_fast_ops_stay(self):
        rows = self.rows()
        rows[("p1_1+2", "geogram")] = row("p1_1+2", "geogram", 400, 0.5, reason="degenerate triangles")
        rows[("p2_3+4", "geogram")] = row("p2_3+4", "geogram", 0, 0.5)          # under 1 ms: counted at MIN_MS, not dropped
        out, _ = t6.analyse(rows, self.tris, "geogram")
        self.assertEqual(out["1k-10k"]["ratios"], [0.25] * 3)
        self.assertEqual(out["1k-10k"]["clean_ratios"], [])
        self.assertEqual(out["10k-100k"]["ratios"], [300 / t6.MIN_MS] * 3)
        self.assertEqual(out["10k-100k"]["clean_ratios"], [300 / t6.MIN_MS] * 3)
        self.assertIn("| nan (0) |", t6.report("x", rows, self.tris, "geogram"))

    def test_non_finite_volumes_are_not_compared(self):
        rows = self.rows()
        rows[("p1_1+2", "geogram")] = row("p1_1+2", "geogram", 400, float("nan"))
        out, _ = t6.analyse(rows, self.tris, "geogram")
        self.assertEqual(out["1k-10k"]["vdiff"], [])
        self.assertEqual(t6.volume_disagreements([rows], "geogram"), [])

    def test_no_data_is_not_all_clean(self):
        self.assertFalse(t6.all_clean([]))
        self.assertFalse(t6.all_clean([dict(ops=0, ok=0, clean=0, empty=0, bad=0, timeout=0, rejected=0)]))

    def test_default_other_is_still_cgal(self):
        rows = {(f, "cgal_epeck" if m == "geogram" else m): dict(r, method="cgal_epeck" if m == "geogram" else m)
                for (f, m), r in self.rows().items()}
        self.assertIn("CGAL clean", t6.report("x", rows, self.tris))

def step(name, m, k, op, ms, vol, status="ok", reason=""):
    return {"class": ac.CLASS, "file": name, "method": m, "step": k, "op": op, "variant": name.split("_p")[0],
            f"{op}_status": status, f"{op}_ms": ms, f"{op}_volume": vol, f"{op}_clean_reason": reason, "input": {"vA": 2.0, "vB": 2.0}}

class AbcVersusTests(unittest.TestCase):
    def test_ratios_per_step_and_volume_agreement(self):
        rows = [step("ucd_p0001_1+2+3", "ours", 0, "union", 100, 3.0), step("ucd_p0001_1+2+3", "geogram", 0, "union", 200, 3.0),
                step("ucd_p0001_1+2+3", "ours", 1, "difference", 50, 1.0), step("ucd_p0001_1+2+3", "geogram", 1, "difference", 25, 1.4),
                step("ucd_p0002_4+5+6", "ours", 0, "union", 30, 2.0), step("ucd_p0002_4+5+6", "geogram", 0, "union", 10, 2.0, status="failed")]
        out = ac.versus(rows, "geogram")
        self.assertEqual(out[0]["ratios"], [0.5])                # the failed geogram step is not a ratio
        self.assertEqual(out[1]["ratios"], [2.0])
        self.assertEqual(out[0]["vdiff"], [0.0])
        self.assertAlmostEqual(out[1]["vdiff"][0], 0.1)
        self.assertEqual(len(out[1]["worst"]), 1)
        buf = io.StringIO()
        with tempfile.NamedTemporaryFile("w", suffix=".jsonl", delete=False) as fh:
            fh.write("".join(json.dumps(r) + "\n" for r in rows))
        self.addCleanup(os.remove, fh.name)
        with redirect_stdout(buf):
            ac.cmd_versus("geogram", [fh.name])
        text = buf.getvalue()
        self.assertIn("| 1 | 1 | 0.50 |", text)
        self.assertIn("| all | 2 | ", text)
        self.assertIn("Volumes that disagree: 1", text)

    def test_last_row_of_a_step_wins_and_other_methods_are_ignored(self):
        rows = [step("dui_p0003_1+2+3", "ours", 0, "difference", 100, 1.0), step("dui_p0003_1+2+3", "ours", 0, "difference", 40, 1.0),
                step("dui_p0003_1+2+3", "geogram", 0, "difference", 20, 1.0), step("dui_p0003_1+2+3", "cgal_epeck", 0, "difference", 1, 9.0)]
        out = ac.versus(rows, "geogram")
        self.assertEqual(out[0]["ratios"], [2.0])
        self.assertEqual(out[0]["vdiff"], [0.0])

if __name__ == "__main__":
    unittest.main()
