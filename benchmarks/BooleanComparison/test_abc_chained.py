"""Tests of the abc-chained class: selection from a 7z listing, the chain list (determinism, ranges, the plans), the summary and the chains
end to end on procedural meshes with known volumes. No ABC file is needed or fetched; the meshes are written here.
The integration tests need the built tool (benchmarks/BooleanComparison/bin/Release/net10.0 or BOOLCMP_TOOL).
Run: python3 -m unittest discover -s benchmarks/BooleanComparison -p 'test_abc_chained.py' -v
"""
import contextlib, io, json, os, shutil, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "data"))
DLL = os.environ.get("BOOLCMP_TOOL", os.path.join(HERE, "bin/Release/net10.0/BooleanComparison.dll"))
os.environ["BOOLCMP_TOOL"] = DLL
import abc_chained as ac
import thingi_contact as tc
from test_thingi_pairs import write_off, cube

LISTING = """7-Zip 23.01

Path = abc_0000_obj_v00.7z
Type = 7z
Physical Size = 7940858722

----------
Path = abc_0000_obj_v00
Folder = +
Size = 0

Path = abc_0000_obj_v00/00000002/00000002_aaaa_trimesh_000.obj
Folder = -
Size = 120000

Path = abc_0000_obj_v00/00000002/00000002_aaaa_trimesh_001.obj
Folder = -
Size = 40000

Path = abc_0000_obj_v00/00000005/00000005_bbbb_trimesh_000.obj
Folder = -
Size = 2900000

Path = abc_0000_obj_v00/00000005/00000005_bbbb_trimesh_000.mtl
Folder = -
Size = 300000

Path = abc_0000_obj_v00/00000007/00000007_cccc_trimesh_000.obj
Folder = -
Size = 9000000
"""

class SelectionTests(unittest.TestCase):
    def test_listing_parse_skips_header_folders_and_counts_files(self):
        files = ac.parse_listing(LISTING)
        self.assertEqual(len(files), 5)
        self.assertNotIn(("abc_0000_obj_v00", 0), files)
        self.assertIn(("abc_0000_obj_v00/00000002/00000002_aaaa_trimesh_000.obj", 120000), files)

    def test_selection_filters_by_size_and_extension_and_is_seeded(self):
        files = ac.parse_listing(LISTING)
        sel = ac.select_candidates(files, 10, 1)
        self.assertEqual(sel, ["abc_0000_obj_v00/00000002/00000002_aaaa_trimesh_000.obj", "abc_0000_obj_v00/00000005/00000005_bbbb_trimesh_000.obj"])
        many = [(f"d/{i:06d}.obj", 100000 + i) for i in range(500)]
        a, b = ac.select_candidates(many, 50, 7), ac.select_candidates(many, 50, 7)
        self.assertEqual(a, b)
        self.assertEqual(len(set(a)), 50)
        self.assertNotEqual(a, ac.select_candidates(many, 50, 8))
        self.assertEqual(len(ac.select_candidates(many, 5000, 7)), 500)      # fewer files than asked for: all

    def test_model_id_is_the_file_stem(self):
        self.assertEqual(ac.model_id("abc_0000_obj_v00/00000002/00000002_aaaa_trimesh_000.obj"), "00000002_aaaa_trimesh_000")

class ChainListTests(unittest.TestCase):
    def models(self):
        return [(f"m{i:04d}", 600 + 11 * i) for i in range(200)]

    def test_deterministic_prefix_and_ranges(self):
        pool = sorted(k for k, n in self.models() if ac.CLEAN_TRIS[0] <= n <= ac.CLEAN_TRIS[1])
        for v in ac.VARIANTS:
            self.assertEqual(ac.chain_rows(pool, v, 20)[:10], ac.chain_rows(pool, v, 10))
            for r in ac.chain_rows(pool, v, 60):
                pair, variant, a, b, c, kb, axb, sgb, pb, kc, axc, sgc, pc = r
                self.assertEqual(variant, v)
                self.assertNotEqual(a, b)
                self.assertIn(kb, ac.KINDS_B[v])
                self.assertIn(kc, ac.KINDS_C)
                self.assertEqual(c == a, kc in tc.SAME_MODEL)             # a copy of A for the offsets of A itself
                if kc not in tc.SAME_MODEL:
                    self.assertNotIn(c, (a, b))
                self.assertTrue(0 <= pb < tc.PARAMS[kb] and 0 <= pc < tc.PARAMS[kc])
                self.assertIn(sgb, (-1, 1)); self.assertIn(sgc, (-1, 1))
        self.assertNotEqual(ac.chain_rows(pool, "ucd", 20), ac.chain_rows(pool, "dui", 20))

    def test_the_variants_cover_every_kind_axis_and_sign(self):
        pool = sorted(k for k, n in self.models())
        rows = ac.chain_rows(pool, "ucd", 100)
        self.assertEqual({r[9] for r in rows}, set(ac.KINDS_C))
        self.assertEqual({r[5] for r in rows}, set(ac.KINDS_B["ucd"]))
        self.assertEqual({r[6] for r in rows}, {0, 1, 2})
        self.assertEqual({r[7] for r in rows}, {-1, 1})
        self.assertEqual({r[5] for r in ac.chain_rows(pool, "iud", 100)}, {"partial-overlap"})

    def test_generate_and_manifest_roundtrip(self):
        with tempfile.TemporaryDirectory() as d:
            clean = os.path.join(d, "clean.csv")
            os.makedirs(os.path.join(d, "meshes"))
            with open(clean, "w") as f:
                f.write("id,tris\n")
                for k, n in self.models():
                    f.write(f"{k},{n}\n")
                    open(os.path.join(d, "meshes", k + ".obj"), "w").close()
            out = os.path.join(d, "chains.csv")
            ac.generate(ac.read_clean(clean), out, 12)
            rows = ac.read_chains(out)
            self.assertEqual(len(rows), 12 * len(ac.VARIANTS))
            with open(out, "rb") as f1, tempfile.TemporaryDirectory() as d2:
                ac.generate(ac.read_clean(clean), os.path.join(d2, "c.csv"), 12)
                self.assertEqual(f1.read(), open(os.path.join(d2, "c.csv"), "rb").read())
            man = os.path.join(d, "m.jsonl")
            with contextlib.redirect_stdout(io.StringIO()):
                ac.cmd_manifest(d, out, man, 5, ["dui"])
            cases = [json.loads(l) for l in open(man)]
        self.assertEqual(len(cases), 5)
        self.assertTrue(all(c["variant"] == "dui" and len(c["chain"]) == 11 for c in cases))
        self.assertTrue(cases[0]["name"].startswith("dui_p0000_"))

    def test_plans_chain_the_result_of_the_previous_step(self):
        for v, plan in ac.PLANS.items():
            self.assertEqual(len(plan), 3)
            self.assertNotIn("R", plan[0][1:])                  # the first step has only placed inputs
            self.assertEqual(plan[1][1], "R")
            self.assertTrue(any("R" in step[1:] for step in plan[1:]))
            self.assertTrue(all(op in ac.OPS for op, _, _ in plan))

class SummaryTests(unittest.TestCase):
    def step(self, name, k, op, reason="", method="ours", status="ok", removed=None, ie=1e-15):
        r = {"class": ac.CLASS, "file": name, "method": method, "step": k, "op": op, "variant": "ucd", "ie_error": ie, "diff_error": 0.0,
             f"{op}_status": status, f"{op}_clean_reason": reason, f"{op}_ms": 10 + k, "input": {"inputs_clean": True}}
        if removed is not None:
            r[f"{op}_removed_components"] = removed
        return r

    def summary(self, rows):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "r.jsonl")
            with open(path, "w") as f:
                f.writelines(json.dumps(x) + "\n" for x in rows)
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                ac.cmd_summary([path])
        return out.getvalue()

    def test_counts_steps_clean_results_and_early_stops(self):
        rows = [self.step("ucd_p0000_a+b+c", 0, "union"), self.step("ucd_p0000_a+b+c", 1, "difference", removed=3), self.step("ucd_p0000_a+b+c", 2, "intersection"),
                self.step("ucd_p0001_d+e+f", 0, "union"), self.step("ucd_p0001_d+e+f", 1, "difference", reason="not closed", ie=2e-6),   # chain stops here
                {"class": ac.CLASS, "file": "ucd_p0002_g+h+i", "method": "export", "error": "RuntimeError('inexact transform: B: x')"}]
        out = self.summary(rows)
        cells = [l.split("|") for l in out.splitlines() if l.startswith("| ucd")][0]
        self.assertEqual(cells[3].strip(), "2")                      # chains
        self.assertEqual(cells[4].strip(), "2/2/1")                  # steps run
        self.assertEqual(cells[5].strip(), "2/1/1")                  # chosen result clean
        self.assertEqual(cells[6].strip(), "1")                      # all steps clean
        self.assertEqual(cells[7].strip(), "1")                      # stopped early
        self.assertIn("not closed 1", cells[8])
        self.assertEqual(cells[9].strip().split("/")[1], "2.0e-06")  # identity error of step 2
        self.assertEqual(cells[10].strip(), "3")                     # removed components
        self.assertIn("ucd_p0002_g+h+i", out)                        # refused chain listed

    def test_other_methods_have_no_removed_column_and_failed_ops_are_counted(self):
        out = self.summary([self.step("ucd_p0000_a+b+c", 0, "union", method="manifold", status="failed")])
        cells = [l.split("|") for l in out.splitlines() if l.startswith("| ucd")][0]
        self.assertEqual(cells[10].strip(), "–")
        self.assertIn("failed 1", cells[8])
        self.assertEqual(cells[5].strip(), "0/0/0")

    def test_a_resumed_run_counts_a_step_once(self):
        rows = [self.step("ucd_p0000_a+b+c", 0, "union")] * 2
        cells = [l.split("|") for l in self.summary(rows).splitlines() if l.startswith("| ucd")][0]
        self.assertEqual(cells[4].strip(), "1/0/0")

@unittest.skipUnless(os.path.exists(DLL), "build benchmarks/BooleanComparison first")
class ChainEndToEndTests(unittest.TestCase):
    """a chain on procedural cubes: B on the +z face of A, C on the -z face; the volumes of every step are known"""
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp(prefix="abc_chain_test_")
        for name, (lo, hi) in {"a": (-0.375, 0.375), "b": (0, 0.5), "c": (-0.25, 0.25)}.items():
            write_off(os.path.join(cls.tmp, name + ".off"), *cube(lo, hi))

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def case(self, variant, kb, pb, kc, pc, axb=2, sgb=1, axc=2, sgc=-1):
        m = lambda n: os.path.join(self.tmp, n + ".off")
        return {"class": ac.CLASS, "name": f"{variant}_test", "variant": variant,
                "chain": [m("a"), m("b"), m("c"), kb, str(axb), str(sgb), str(pb), kc, str(axc), str(sgc), str(pc)]}

    def test_ucd_of_touching_cubes_returns_a(self):
        rows = ac.run_chain(self.case("ucd", "face-contact", 0, "face-contact", 0), ["ours"], 300)
        steps = {r["step"]: r for r in rows}
        self.assertEqual(sorted(steps), [0, 1, 2], rows)
        va, vb = 0.75 ** 3, 0.5 ** 3       # A: 0.75 cube; B and C: 0.5 cubes (their longest side 0.5 is already in [0.5, 1): not rescaled)
        # step 1: A + B touching; step 2: minus C (touching only: unchanged); step 3: ∩ A = A
        self.assertAlmostEqual(steps[0]["union_volume"], va + vb, places=12)
        self.assertAlmostEqual(steps[1]["difference_volume"], va + vb, places=12)
        self.assertAlmostEqual(steps[2]["intersection_volume"], va, places=12)
        for r in steps.values():
            self.assertEqual(r[f"{r['op']}_clean_reason"], "", r)
            self.assertLess(max(r["ie_error"], r["diff_error"]), 1e-12)
            self.assertTrue(r["input"]["inputs_clean"])
            self.assertIsNotNone(ac.removed(r, r["op"]))            # ours records what the rounding removed
        self.assertTrue(steps[0]["chain_inputs"]["inputs_clean"])
        # the second step's input A is the result of the first (the union of the two cubes, not A's 12 triangles); the third step's input A is the second result
        self.assertEqual(steps[1]["input"]["trisA"], steps[0]["union_tris"])
        self.assertNotEqual(steps[1]["input"]["trisA"], 12)
        self.assertEqual(steps[2]["input"]["trisA"], steps[1]["difference_tris"])
        self.assertEqual(steps[2]["input"]["trisB"], 12)             # ... and its B is the original A

    def test_the_second_step_gets_the_result_of_the_first_not_the_original(self):
        rows = ac.run_chain(self.case("dui", "partial-overlap", 0, "face-contact", 0), ["ours"], 300)
        steps = {r["step"]: r for r in rows}
        # dui: (A - B) ∪ C ∩ A; B flush inside A (A and B the same size, so A - B is empty when B covers A): the chain ends after an empty step
        self.assertIn(0, steps)
        if steps[0]["result_tris"] <= 0:
            self.assertEqual(sorted(steps), [0])

    def test_inexact_placement_is_an_error_row_for_the_chain(self):
        v, f = cube(0, 0.5)
        v[0] = (1e-30, 1e-30, 1e-30)
        write_off(os.path.join(self.tmp, "tiny.off"), v, f)
        case = self.case("ucd", "face-contact", 0, "face-contact", 0)
        case["chain"][1] = os.path.join(self.tmp, "tiny.off")
        rows = ac.run_chain(case, ["ours"], 300)
        self.assertEqual(rows[0]["method"], "export")
        self.assertIn("inexact transform", rows[0]["error"])

if __name__ == "__main__":
    unittest.main()
