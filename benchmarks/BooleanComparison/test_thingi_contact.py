"""Tests of the thingi-contact class: the pair list (determinism, ranges, the committed file), the manifest, the summary and the
oracle's class settings, and the placements end to end on procedural meshes with known volumes. No mesh files: the meshes are written here.
The integration tests need the built tool (benchmarks/BooleanComparison/bin/Release/net10.0 or BOOLCMP_TOOL).
The transforms themselves are unit-tested in tests/geometry3Sharp.Tests/ThingiContactTransformTests.cs.
Run: python3 -m unittest discover -s benchmarks/BooleanComparison -p 'test_thingi_contact.py' -v
"""
import csv, io, contextlib, json, os, shutil, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "data"))
DLL = os.environ.get("BOOLCMP_TOOL", os.path.join(HERE, "bin/Release/net10.0/BooleanComparison.dll"))
os.environ["BOOLCMP_TOOL"] = DLL
import thingi_pairs as tp
import thingi_contact as tc
import analyze
from test_thingi_pairs import write_off, cube

def read(path):
    with open(path, "rb") as f:
        return f.read()

class ListTests(unittest.TestCase):
    def models(self):
        return [(str(1000 + i), 1000 + 397 * i) for i in range(300)]

    def test_deterministic_and_seeded(self):
        with tempfile.TemporaryDirectory() as d1, tempfile.TemporaryDirectory() as d2:
            tc.generate(self.models(), d1)
            tc.generate(self.models(), d2)
            self.assertEqual(read(os.path.join(d1, tc.NAME)), read(os.path.join(d2, tc.NAME)))
        pool = sorted(k for k, n in self.models() if tc.CONTACT_TRIS[0] <= n <= tc.CONTACT_TRIS[1])
        for v in tc.VARIANTS:
            self.assertEqual(tc.pair_rows(pool, v, 20)[:10], tc.pair_rows(pool, v, 10))        # a prefix does not depend on the count
        self.assertNotEqual([r[1:] for r in tc.pair_rows(pool, "face-contact", 20)], [r[1:] for r in tc.pair_rows(pool, "partial-overlap", 20)])

    def test_rows(self):
        with tempfile.TemporaryDirectory() as d:
            tc.generate(self.models(), d)
            rows = tc.read_list(os.path.join(d, tc.NAME))
        tris = dict(self.models())
        self.assertEqual(len(rows), tc.PAIRS_PER_VARIANT * len(tc.VARIANTS))
        self.assertEqual([r["variant"] for r in rows], [v for v in tc.VARIANTS for _ in range(tc.PAIRS_PER_VARIANT)])
        for r in rows:
            self.assertTrue(tc.CONTACT_TRIS[0] <= tris[r["a"]] <= tc.CONTACT_TRIS[1])
            self.assertTrue(tc.CONTACT_TRIS[0] <= tris[r["b"]] <= tc.CONTACT_TRIS[1])
            self.assertEqual(r["a"] == r["b"], r["variant"] in tc.SAME_MODEL)
            self.assertIn(int(r["axis"]), (0, 1, 2))
            self.assertIn(int(r["sign"]), (-1, 1))
            self.assertTrue(0 <= int(r["param"]) < tc.PARAMS[r["variant"]])
        for v in tc.VARIANTS:       # every value of axis, sign and param occurs
            vr = [r for r in rows if r["variant"] == v]
            self.assertEqual({int(r["axis"]) for r in vr}, {0, 1, 2})
            self.assertEqual({int(r["sign"]) for r in vr}, {-1, 1})
            self.assertEqual({int(r["param"]) for r in vr}, set(range(tc.PARAMS[v])))

    def test_variants_match_the_tool(self):
        src = open(os.path.join(HERE, "ContactTransforms.cs")).read()
        names = src[src.index("VariantNames = {"):].split("}")[0]
        self.assertEqual([x.strip().strip('"') for x in names.split("{")[1].split(",")], tc.VARIANTS)

    def test_committed_list_is_the_generator_output(self):
        if not os.path.exists(os.path.join(tp.LISTS, "clean_models.csv")):
            self.skipTest("no committed lists")
        with tempfile.TemporaryDirectory() as d:
            tc.generate(None, d)
            self.assertEqual(read(os.path.join(d, tc.NAME)), read(os.path.join(tp.LISTS, tc.NAME)))

    def test_select_takes_a_prefix_per_variant(self):
        with tempfile.TemporaryDirectory() as d:
            tc.generate(self.models(), d)
            rows = tc.read_list(os.path.join(d, tc.NAME))
        sel = tc.select(rows, 30)
        self.assertEqual(len(sel), 30 * len(tc.VARIANTS))
        self.assertEqual([r["pair"] for r in sel if r["variant"] == "mirror"], [f"{i:04d}" for i in range(30)])
        self.assertEqual({r["variant"] for r in tc.select(rows, 5, ["mirror"])}, {"mirror"})
        self.assertEqual(len(tc.select(rows)), len(rows))

    def test_manifest(self):
        with tempfile.TemporaryDirectory() as d:
            os.makedirs(os.path.join(d, "meshes"))
            rows = tc.read_list()
            for k in {r[c] for r in tc.select(rows, 2) for c in ("a", "b")}:
                open(os.path.join(d, "meshes", f"{k}.stl"), "w").close()
            out = os.path.join(d, "cases.jsonl")
            with contextlib.redirect_stdout(io.StringIO()):
                tc.cmd_manifest(d, out, 2, ["face-contact", "mirror"])
            cases = [json.loads(l) for l in open(out)]
        self.assertEqual(len(cases), 4)
        self.assertEqual({c["class"] for c in cases}, {"thingi-contact"})
        c = cases[0]
        self.assertTrue(c["name"].startswith("face-contact_p0000_"))
        self.assertEqual(c["thingi_contact"][2], "face-contact")
        self.assertEqual(len(c["thingi_contact"]), 6)

class OracleSettingsTests(unittest.TestCase):
    def test_class_is_judged_like_thingi_pairs(self):
        self.assertEqual(analyze.CLASS_TOL["thingi-contact"], analyze.CLASS_TOL["thingi-pairs"])
        self.assertIn("thingi-contact", analyze.NO_SELFINT)

    def row(self, **kw):
        r = {"class": "thingi-contact", "ops": analyze.OPS, "all_results": True, "ie_error": 0.0, "diff_error": 0.0, "expected_error": 0.0}
        for op in analyze.OPS:
            r.update({f"{op}_status": "ok", f"{op}_wclosed": True, f"{op}_manifold_closed": True, f"{op}_selfint": 0, f"{op}_clean_reason": ""})
        r.update(kw)
        return r

    def test_a_wrong_expected_volume_is_a_volume_error(self):
        self.assertEqual(analyze.outcome(self.row()), "clean")
        self.assertEqual(analyze.outcome(self.row(expected_error=1e-6)), "volume")
        self.assertEqual(analyze.outcome(self.row(union_selfint=4, union_clean_reason="self-intersections")), "self-int.")
        self.assertFalse(analyze.correct(self.row(union_selfint=4, union_clean_reason="self-intersections")))

    def test_summary_groups_by_variant_and_counts_face_contacts_and_refusals(self):
        def r(name, kind, method="ours"):
            return dict(self.row(file=name, method=method, input={"kind": kind}), **{f"{op}_ms": 5 for op in analyze.OPS})
        rows = [r("face-contact_p0000_1+2", "face"), r("face-contact_p0001_3+4", "bbox"), r("mirror_p0000_5+5", "mirror"),
                {"class": "thingi-contact", "file": "mirror_p0001_6+6", "method": "export", "error": "RuntimeError('inexact transform: mirror not exact')"}]
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "r.jsonl")
            with open(path, "w") as f:
                f.writelines(json.dumps(x) + "\n" for x in rows)
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                tc.cmd_summary([path])
        lines = out.getvalue().splitlines()
        face = [l for l in lines if l.startswith("| face-contact")][0].split("|")
        self.assertEqual((face[3].strip(), face[4].strip(), face[5].strip()), ("2", "1", "6"))      # pairs, face contact, operations
        self.assertTrue(any(l.startswith("| mirror") for l in lines))
        self.assertTrue(any("mirror_p0001_6+6" in l and "inexact" in l for l in lines))

class RemovedComponentsTests(unittest.TestCase):
    """an empty result that the rounding stage produced by removing components is told apart from a really empty one"""
    def row(self, name, removed, method="ours", key="counts"):
        r = {"class": "thingi-contact", "file": name, "method": method, "ops": analyze.OPS, "all_results": True, "ie_error": 0.0, "diff_error": 0.0,
             "input": {"kind": "ulp"}}
        for op in analyze.OPS:
            r.update({f"{op}_status": "ok", f"{op}_ms": 5, f"{op}_wclosed": True, f"{op}_selfint": 0, f"{op}_clean_reason": ""})
        r["difference_clean_reason"] = "empty"
        if key == "counts":
            r["difference_counts"] = {"rounding_removed_components": removed}
        else:
            r["difference_removed_components"] = removed
        return r

    def summary(self, rows):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "r.jsonl")
            with open(path, "w") as f:
                f.writelines(json.dumps(x) + "\n" for x in rows)
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                tc.cmd_summary([path])
        return [l.split("|") for l in out.getvalue().splitlines() if l.startswith("| ulp-shift")]

    def test_removed_helper_reads_both_forms(self):
        self.assertEqual(tc.removed(self.row("a", 4), "difference"), 4)                      # counts of the first run
        self.assertEqual(tc.removed(self.row("a", 7, key="flat"), "difference"), 7)          # the field compare.py writes now
        self.assertIsNone(tc.removed(self.row("a", 4, method="manifold") | {"difference_counts": None}, "difference"))
        self.assertIsNone(tc.removed(self.row("a", 4), "union"))

    def test_summary_separates_removed_from_really_empty(self):
        rows = [self.row("ulp-shift_p0000_1+1", 3), self.row("ulp-shift_p0001_2+2", 0), self.row("ulp-shift_p0002_3+3", 5, key="flat")]
        cells = self.summary(rows)[0]
        self.assertEqual(cells[9].strip(), "1")      # empty, really (removed 0)
        self.assertEqual(cells[10].strip(), "2")     # empty, removed by rounding
        self.assertEqual(cells[-2].strip(), "8")     # removed components, sum (3 + 0 + 5)

    def test_other_methods_have_no_removed_column(self):
        cells = self.summary([self.row("ulp-shift_p0000_1+1", 0, method="manifold")])[0]
        self.assertEqual(cells[-2].strip(), "–")

    def test_compare_records_removed_components_as_a_field(self):
        src = open(os.path.join(HERE, "compare.py")).read()
        self.assertIn('res["removed_components"] = counts["rounding_removed_components"]', src)


@unittest.skipUnless(os.path.exists(DLL), "build benchmarks/BooleanComparison first")
class PlacementEndToEndTests(unittest.TestCase):
    """the case writer and the oracle on procedural cubes: the volumes of the results are known exactly"""
    @classmethod
    def setUpClass(cls):
        import compare
        cls.compare = compare
        cls.tmp = tempfile.mkdtemp(prefix="thingi_contact_test_")
        write_off(os.path.join(cls.tmp, "cube.off"), *cube(-0.375, 0.375))          # side 0.75, volume 0.421875
        v, f = cube(0, 0.5)
        write_off(os.path.join(cls.tmp, "tall.off"), [(x, y, 2 * z) for x, y, z in v], f)   # 0.5 x 0.5 x 1: the longest side 1 is scaled by 1/2

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def run_case(self, a, b, variant, axis, sign, param):
        case = {"class": "thingi-contact", "name": f"{variant}_test", "thingi_contact": [os.path.join(self.tmp, a), os.path.join(self.tmp, b), variant, str(axis), str(sign), str(param)]}
        rows = self.compare.process(case, ["ours"], 300)
        self.assertEqual(rows[0]["method"], "ours", rows[0])
        return rows[0]

    def test_face_contact_has_exactly_known_volumes(self):
        r = self.run_case("cube.off", "cube.off", "face-contact", 2, 1, 0)
        self.assertEqual(r["input"]["kind"], "face")
        self.assertEqual(r["input"]["expected"], {"union": 0.84375, "intersection": 0.0, "difference": 0.421875})
        self.assertEqual(r["intersection_tris"], 0)
        self.assertAlmostEqual(r["union_volume"], 0.84375, places=12)
        self.assertLess(r["expected_error"], 1e-12)
        self.assertEqual(r["input"]["inputs_clean"], True)
        self.assertEqual(analyze.outcome(r), "clean")
        for op in analyze.OPS:      # what the rounding stage dropped is a field of the row (ours only)
            self.assertEqual(r[f"{op}_removed_components"], r[f"{op}_counts"]["rounding_removed_components"])
        self.assertEqual(r["intersection_removed_components"], 0)      # really empty, nothing was removed

    def test_partial_overlap_flush(self):
        # param 0: B's far face is flush with A's face (tall: 0.5 x 0.5 x 1, scaled by 1/2 to 0.25 x 0.25 x 0.5; A is 0.75 cube)
        r = self.run_case("cube.off", "tall.off", "partial-overlap", 0, 1, 0)
        self.assertEqual(r["input"]["kind"], "face")
        self.assertLess(max(r["ie_error"], r["diff_error"]), 1e-12)
        self.assertAlmostEqual(r["intersection_volume"], 0.25 * 0.25 * 0.5, places=12)      # B lies inside A, flush at the face
        self.assertAlmostEqual(r["union_volume"], 0.421875, places=12)

    def test_embedded_depths_are_not_face_contacts(self):
        r = self.run_case("cube.off", "tall.off", "partial-overlap", 0, 1, 1)
        self.assertEqual(r["input"]["kind"], "embedded")
        self.assertAlmostEqual(r["intersection_volume"], 0.25 * 0.25 * 0.25, places=12)     # half of B's extent 0.5 is inside A

    def test_ulp_shift_keeps_the_volume_identities(self):
        for variant in ("ulp-shift", "ulp-shift-few", "mirror"):
            r = self.run_case("cube.off", "cube.off", variant, 1, -1, 1)
            self.assertLess(max(r["ie_error"], r["diff_error"]), 1e-12, variant)
            self.assertEqual(r["input"]["variant"], variant)
            if variant == "mirror":     # the plane is a vertex coordinate of A and B is a different mesh: the union differs from A's volume
                self.assertEqual(r["input"]["kind"], "mirror")
                self.assertGreater(r["union_volume"], 0.421875 - 1e-12)
                self.assertAlmostEqual(r["intersection_volume"] + r["union_volume"], 2 * 0.421875, places=12)
            else:
                self.assertEqual(r["input"]["kind"], "ulp")
                self.assertGreaterEqual(r["input"]["ulps"], 1)
                self.assertLess(abs(r["union_volume"] - 0.421875), 1e-14)       # A against A moved by 1e-16: the union is A up to that

    def test_inexact_placement_is_an_error_row_not_a_rounded_case(self):
        v, f = cube(0, 0.5)
        v[0] = (1e-30, 1e-30, 1e-30)
        write_off(os.path.join(self.tmp, "tiny.off"), v, f)
        case = {"class": "thingi-contact", "name": "x", "thingi_contact": [os.path.join(self.tmp, "cube.off"), os.path.join(self.tmp, "tiny.off"), "face-contact", "2", "1", "0"]}
        rows = self.compare.process(case, ["ours"], 300)
        self.assertEqual(rows[0]["method"], "export")
        self.assertIn("inexact transform", rows[0]["error"])

if __name__ == "__main__":
    unittest.main()
