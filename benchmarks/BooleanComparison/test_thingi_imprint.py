"""Tests of the imprint comparison (README.md, "Imprint: ExactMeshImprint against MeshMeshCut"): the outcome and disagreement
rules and the summary of data/thingi_imprint.py, the imprint methods of compare.py, the baseline range, the workflow's matrix, and
the tool's imprint mode end to end on procedural cubes with a known curve. No mesh files: the meshes are written here.
The integration tests need the built tool (benchmarks/BooleanComparison/bin/Release/net10.0 or BOOLCMP_TOOL).
Run: python3 -m unittest discover -s benchmarks/BooleanComparison -p 'test_thingi_imprint.py' -v
"""
import csv, io, json, os, shutil, subprocess, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "data"))
DLL = os.environ.get("BOOLCMP_TOOL", os.path.join(HERE, "bin/Release/net10.0/BooleanComparison.dll"))
os.environ["BOOLCMP_TOOL"] = DLL
import thingi_pairs as tp
import thingi_imprint as ti
import compare
from test_thingi_pairs import write_off, cube


def cut_row(method, file="p0000_1+2", cls="thingi-pairs", **ops):
    """a result row of a cut method; ops: op -> dict of the fields of that operation (status ok and a clean, closed target by default)"""
    r = {"class": cls, "file": file, "method": method, "ops": list(ops)}
    for op, f in ops.items():
        base = {"status": "ok", "ms": 10, "target_closed": True, "target_reason": "", "result_closed": True, "result_reason": "",
                "curve_length": 1.0, "new_vertices": 5}
        if method == "float_cut":
            base.update(failed_insertions=0, segments=4)
        base.update(f)
        for k, v in base.items():
            r[f"{op}_{k}"] = v
    return r


def union_row(method, file="p0000_1+2", cls="thingi-pairs", vA=1.0, vB=1.0, **f):
    base = {"status": "ok", "ms": 10, "result_closed": True, "result_reason": "", "volume": 1.5}
    base.update(f)
    r = {"class": cls, "file": file, "method": method, "ops": ["union"], "input": {"vA": vA, "vB": vB}}
    for k, v in base.items():
        r[f"union_{k}"] = v
    return r


class OutcomeTests(unittest.TestCase):
    def test_cut_outcomes(self):
        self.assertEqual(ti.outcome(cut_row("exact_imprint", a_by_b={}), "a_by_b"), "good")
        self.assertEqual(ti.outcome(cut_row("exact_imprint", a_by_b={"status": "timeout"}), "a_by_b"), "timeout")
        self.assertEqual(ti.outcome(cut_row("exact_imprint", a_by_b={"status": "exception"}), "a_by_b"), "exception")
        self.assertEqual(ti.outcome(cut_row("exact_imprint", a_by_b={}), "b_by_a"), "missing")
        self.assertEqual(ti.outcome(cut_row("float_cut", a_by_b={"result_closed": False, "result_reason": "not closed"}), "a_by_b"), "opened")
        self.assertEqual(ti.outcome(cut_row("float_cut", a_by_b={"result_reason": "degenerate triangles"}), "a_by_b"), "unclean: degenerate triangles")
        self.assertEqual(ti.outcome(cut_row("float_cut", a_by_b={"failed_insertions": 3}), "a_by_b"), "gaps")

    def test_a_defective_target_is_not_held_against_the_result(self):
        # an open target: an open result is not "opened"; an unclean target: an unclean result is not "unclean"
        r = cut_row("exact_imprint", a_by_b={"target_closed": False, "target_reason": "not closed", "result_closed": False, "result_reason": "not closed"})
        self.assertEqual(ti.outcome(r, "a_by_b"), "good")

    def test_union_outcomes(self):
        self.assertEqual(ti.outcome(union_row("float_union"), "union"), "good")
        self.assertEqual(ti.outcome(union_row("float_union", result_closed=False, result_reason="not closed"), "union"), "not closed")
        self.assertEqual(ti.outcome(union_row("float_union", result_reason="bowties"), "union"), "unclean: bowties")
        self.assertEqual(ti.outcome(union_row("float_union", status="crash"), "union"), "crash")

    def test_inserted_nothing(self):
        self.assertTrue(ti.inserted_nothing(cut_row("float_cut", a_by_b={"new_vertices": 0}), "a_by_b"))
        self.assertFalse(ti.inserted_nothing(cut_row("float_cut", a_by_b={"new_vertices": 0, "segments": 0}), "a_by_b"))   # nothing to cut
        self.assertFalse(ti.inserted_nothing(cut_row("float_cut", a_by_b={}), "a_by_b"))
        self.assertFalse(ti.inserted_nothing(cut_row("float_cut", a_by_b={"new_vertices": 0, "status": "timeout"}), "a_by_b"))


class DisagreementTests(unittest.TestCase):
    def kinds(self, *rows):
        by_case = {}
        for r in rows:
            by_case.setdefault((ti.group(r), r["file"]), {})[r["method"]] = r
        return [(d[2], ti.category(d[3])) for d in ti.disagreements(by_case)]

    def test_agreement_is_no_disagreement(self):
        self.assertEqual(self.kinds(cut_row("exact_imprint", a_by_b={}, b_by_a={}), cut_row("float_cut", a_by_b={}, b_by_a={}),
                                    union_row("exact_union"), union_row("float_union")), [])

    def test_kinds(self):
        e = cut_row("exact_imprint", a_by_b={}, b_by_a={"result_reason": "degenerate triangles"})
        f = cut_row("float_cut", a_by_b={"result_reason": "degenerate triangles"}, b_by_a={})
        self.assertEqual(self.kinds(e, f), [("a_by_b", "only exact good"), ("b_by_a", "only float good")])
        f = cut_row("float_cut", a_by_b={"new_vertices": 0}, b_by_a={"curve_length": 0.5})
        self.assertEqual(self.kinds(cut_row("exact_imprint", a_by_b={}, b_by_a={}), f), [("a_by_b", "float inserted nothing"), ("b_by_a", "curve length")])
        f = cut_row("float_cut", a_by_b={"curve_length": 1 + 0.5 * ti.LENGTH_TOL}, b_by_a={"curve_length": 0, "segments": 0, "new_vertices": 0})
        self.assertEqual(self.kinds(cut_row("exact_imprint", a_by_b={}, b_by_a={}), f), [("b_by_a", "curve only exact")])
        f = cut_row("float_cut", a_by_b={"curve_length": 1 + 2 * ti.LENGTH_TOL}, b_by_a={})
        self.assertEqual(self.kinds(cut_row("exact_imprint", a_by_b={}, b_by_a={}), f), [("a_by_b", "curve length")])
        e = cut_row("exact_imprint", a_by_b={"status": "timeout"})
        f = cut_row("float_cut", a_by_b={"status": "exception"})
        self.assertEqual(self.kinds(e, f), [("a_by_b", "both fail")])

    def test_union_volume(self):
        self.assertEqual(self.kinds(union_row("exact_union"), union_row("float_union", volume=1.5 + 3 * ti.VOLUME_TOL)), [("union", "volume differs")])
        self.assertEqual(self.kinds(union_row("exact_union"), union_row("float_union", volume=1.5 + 1.9 * ti.VOLUME_TOL)), [])
        self.assertEqual(self.kinds(union_row("exact_union"), union_row("float_union", result_closed=False)), [("union", "only exact good")])

    def test_contact_cases_group_by_variant(self):
        self.assertEqual(ti.group({"class": "thingi-contact", "file": "ulp-shift-few_p0003_1+1"}), "ulp-shift-few")
        self.assertEqual(ti.group({"class": "thingi-pairs", "file": "p0003_1+2"}), "thingi-pairs")


class SummaryTests(unittest.TestCase):
    def write(self, d, rows, tail=""):
        p = os.path.join(d, "r.jsonl")
        with open(p, "w") as f:
            f.writelines(json.dumps(r) + "\n" for r in rows)
            f.write(tail)
        return p

    def test_table_ratio_and_disagreements(self):
        rows = [
            cut_row("exact_imprint", a_by_b={"ms": 10}, b_by_a={"ms": 40}),
            cut_row("float_cut", a_by_b={"ms": 20, "new_vertices": 0}, b_by_a={"ms": 10, "status": "exception"}),
            cut_row("exact_imprint", file="p0001_3+4", a_by_b={"ms": 999}, b_by_a={}),    # replaced by the later row
            cut_row("exact_imprint", file="p0001_3+4", a_by_b={"ms": 10, "rounding_clean": False}, b_by_a={"ms": 10}),
            cut_row("float_cut", file="p0001_3+4", a_by_b={"ms": 80}, b_by_a={"ms": 20, "failed_insertions": 2}),
            union_row("exact_union", ms=100), union_row("float_union", ms=400, result_closed=False),
            {"class": "thingi-contact", "file": "mirror_p0000_5+5", "method": "export", "error": "inexact transform"},
        ]
        with tempfile.TemporaryDirectory() as d:
            out = io.StringIO()
            dis = ti.summary([self.write(d, rows, tail='{"class": "thingi-pa')], out)
        text = out.getvalue()
        lines = {tuple(c.strip() for c in l.split("|")[1:3]): [c.strip() for c in l.split("|")[1:-1]] for l in text.splitlines() if l.startswith("| thingi-pairs")}
        e, f = lines[("thingi-pairs", "exact_imprint")], lines[("thingi-pairs", "float_cut")]
        self.assertEqual(e[2:6], ["2", "4", "4", "0 / 0 / 0 / 0"])
        self.assertEqual(e[11], "1")                                         # rounding not clean
        self.assertEqual(e[12], "10")                                        # median ms (the replaced row's 999 is gone)
        self.assertEqual(f[2:6], ["2", "4", "2", "0 / 1 / 0 / 0"])
        self.assertEqual(f[8], "1")                                          # gaps
        self.assertEqual(f[10], "1")                                         # nothing inserted
        u = lines[("thingi-pairs", "float_union")]
        self.assertEqual((u[4], u[6]), ("0", "1"))
        # ratios: cut 20/10, 80/10, 20/10 -> geomean 32^(1/3); union 4
        ratio = [l for l in text.splitlines() if l.startswith("| thingi-pairs | ") and l.count("|") == 6][0].split("|")
        self.assertAlmostEqual(float(ratio[2]), 32 ** (1 / 3), places=2)
        self.assertEqual((ratio[3].strip(), ratio[4].strip(), ratio[5].strip()), ("3", "4.00", "1"))
        self.assertEqual(sorted(ti.category(x[3]) for x in dis), ["float inserted nothing", "only exact good", "only exact good", "only exact good"])
        self.assertIn("Cases not written (placement refused as inexact, or load failed): 1", text)


class ToolingTests(unittest.TestCase):
    def test_compare_runs_the_imprint_methods_in_the_tool(self):
        self.assertEqual(compare.ops_of("exact_imprint"), ["a_by_b", "b_by_a"])
        self.assertEqual(compare.ops_of("float_union"), ["union"])
        self.assertEqual(compare.ops_of("ours"), compare.OPS)
        self.assertEqual(compare.commands("float_cut", "b_by_a", "A.off", "B.off", "x")[-5:], ["imprint", "A.off", "B.off", "float_cut", "b_by_a"])
        self.assertEqual(compare.status_of({"ok": False, "exception": "NullReferenceException", "error": "exception ..."}, 0), "exception")
        self.assertEqual(compare.status_of({"ok": False, "error": "x"}, 0), "failed")

    def test_pair_range_is_a_slice_of_pairs_csv(self):
        with open(os.path.join(tp.LISTS, "pairs.csv")) as f:
            rows = list(csv.DictReader(l for l in f if not l.startswith("#")))
        self.assertEqual([r["pair"] for r in ti.pair_range(250, 253)], [rows[k]["pair"] for k in range(250, 253)])
        self.assertEqual(len(ti.pair_range(750, 1000)), 250)

    def test_manifest_pairs(self):
        rows = ti.pair_range(3, 5)
        with tempfile.TemporaryDirectory() as d:
            os.makedirs(os.path.join(d, "meshes"))
            for r in rows:
                for k in ("a", "b"):
                    open(os.path.join(d, "meshes", r[k] + ".stl"), "w").close()
            out = os.path.join(d, "cases.jsonl")
            ti.cmd_manifest_pairs(d, out, 3, 5)
            with open(out) as f:
                cases = [json.loads(l) for l in f]
        self.assertEqual([c["name"] for c in cases], [f"p{r['pair']}_{r['a']}+{r['b']}" for r in rows])
        self.assertEqual(cases[0]["class"], "thingi-pairs")
        self.assertEqual(cases[0]["thingi_pair"][2:], [rows[0][k] for k in tp.HEADER[3:]])

    def test_workflow_matrix(self):
        with open(os.path.join(HERE, "..", "..", ".github", "workflows", "thingi-contact.yml")) as f:
            wf = f.read()
        self.assertIn("max-parallel: 6", wf)
        line = next(l for l in wf.splitlines() if "variant: ${{ fromJSON" in l)
        parts = line.split("'")      # ... inputs.mode == 'imprint' && '<imprint list>' || '<boolean list>' ...
        self.assertEqual(parts[1], "imprint")
        imprint = json.loads(parts[3])
        self.assertEqual(imprint[:4], ["pairs-0", "pairs-1", "pairs-2", "pairs-3"])
        self.assertEqual(imprint[4:], json.loads(parts[5]))       # the contact variants of both modes
        self.assertIn("exact_imprint,float_cut,exact_union,float_union", wf)


@unittest.skipUnless(os.path.exists(DLL), "build benchmarks/BooleanComparison first")
class ToolTests(unittest.TestCase):
    """the imprint mode end to end: A = the unit cube, B = A shifted by half its side along each axis (thingi-pair normalizes and
    translates): on each surface the curve is six segments of length 1/2 (total 3), and the union has the volume 1.875"""
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp(prefix="thingi_imprint_test_")
        write_off(os.path.join(cls.tmp, "cube.off"), *cube(-3.0, 5.0))
        case = {"class": "thingi-pairs", "name": "cubes", "thingi_pair": [os.path.join(cls.tmp, "cube.off")] * 2 + ["0", "0", "0", "1", "0.5", "0.5", "0.5"]}
        cls.rows = {r["method"]: r for r in compare.process(case, list(compare.IMPRINT_OPS), 300)}

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def test_exact_imprint_cuts_the_known_curve(self):
        r = self.rows["exact_imprint"]
        for op in ("a_by_b", "b_by_a"):
            self.assertEqual(ti.outcome(r, op), "good", r)
            self.assertAlmostEqual(r[f"{op}_curve_length"], 3.0, places=12)
            self.assertGreater(r[f"{op}_curve_edges"], 0)
            self.assertGreater(r[f"{op}_new_vertices"], 0)
            self.assertEqual(r[f"{op}_target_tris"], 12)
            self.assertTrue(r[f"{op}_rounding_clean"])

    def test_float_cut_finds_the_same_curve(self):
        r = self.rows["float_cut"]
        for op in ("a_by_b", "b_by_a"):
            self.assertEqual(ti.outcome(r, op), "good", r)
            self.assertAlmostEqual(r[f"{op}_curve_length"], 3.0, places=9)
            self.assertEqual(r[f"{op}_failed_insertions"], 0)
            self.assertEqual(r[f"{op}_new_vertices"], self.rows["exact_imprint"][f"{op}_new_vertices"])
            self.assertFalse(ti.inserted_nothing(r, op))

    def test_unions_report_the_volume(self):
        for m in ("exact_union", "float_union"):
            r = self.rows[m]
            self.assertEqual(ti.outcome(r, "union"), "good", r)
            self.assertAlmostEqual(r["union_volume"], 1.875, places=12)
            self.assertNotIn("union_new_vertices", r)

    def test_agreeing_methods_give_no_disagreement(self):
        self.assertEqual(ti.disagreements({("thingi-pairs", "cubes"): self.rows}), [])

    def test_disjoint_pair_has_no_curve_and_no_disagreement(self):
        case = {"class": "thingi-pairs", "name": "apart", "thingi_pair": [os.path.join(self.tmp, "cube.off")] * 2 + ["0", "0", "0", "1", "3", "0", "0"]}
        rows = {r["method"]: r for r in compare.process(case, ["exact_imprint", "float_cut"], 300)}
        for op in ("a_by_b", "b_by_a"):
            for m in ("exact_imprint", "float_cut"):
                self.assertEqual(ti.outcome(rows[m], op), "good", rows[m])
                self.assertFalse(ti.has_curve(rows[m], op))
                self.assertEqual(rows[m][f"{op}_new_vertices"], 0)
            self.assertFalse(ti.inserted_nothing(rows["float_cut"], op))
        self.assertEqual(ti.disagreements({("thingi-pairs", "apart"): rows}), [])

    def test_exception_and_timeout_statuses(self):
        d = os.path.join(self.tmp, "case")
        subprocess.run(compare.tool + ["thingi-pair", os.path.join(self.tmp, "cube.off"), os.path.join(self.tmp, "cube.off"), d,
                                       "0", "0", "0", "1", "0.5", "0.5", "0.5"], capture_output=True, check=True)
        p = subprocess.run(compare.tool + ["imprint", os.path.join(d, "A.off"), os.path.join(d, "B.off"), "bogus", "a_by_b"], capture_output=True, text=True)
        res = compare.parse_json_line(p.stdout)
        self.assertEqual(compare.status_of(res, p.returncode), "exception")
        self.assertEqual(compare.run_one("exact_imprint", "a_by_b", d, 0.01)["status"], "timeout")


if __name__ == "__main__":
    unittest.main()
