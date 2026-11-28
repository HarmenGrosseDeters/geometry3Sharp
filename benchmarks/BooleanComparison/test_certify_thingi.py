"""Tests of data/certify_thingi.py (the driver of `certify thingi` and `region-compare thingi`): the plan with contact variants, the run with a faked tool
(the stderr tail of a failing case in its row and in the log, the exit code), the region summary (verdict counts, the exact fields, the exit code).
No mesh files and no tool: subprocess.run is replaced. Run: python3 -m unittest discover -s benchmarks/BooleanComparison -p 'test_certify_thingi.py' -v
"""
import contextlib, io, json, os, subprocess, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "data"))
import certify_thingi as ct

def rows_file(rows):
    f = tempfile.NamedTemporaryFile("w", suffix=".jsonl", delete=False)
    f.write("".join(json.dumps(r) + "\n" for r in rows))
    f.close()
    return f.name

SAME = {"case": "fuzz-a-1", "op": "union", "verdict": "same", "t_off_ms": 10, "t_on_ms": 5, "tris": 100}
DIFF = {"case": "fuzz-slab-52", "op": "pred-only-second", "verdict": "surface differs", "message": "the exact difference has 6 triangles",
        "exact": "equal", "exact_tris": "12/12", "min_edge": 2.27e-13, "round_res": 4.44e-16, "repair_tol": 1.14e-13, "t_off_ms": 3, "t_on_ms": 2, "tris": 24}

def summary(rows):
    out = io.StringIO()
    path = rows_file(rows)
    try:
        with contextlib.redirect_stdout(out):
            code = ct.summary_region([path])
    finally:
        os.unlink(path)
    return code, out.getvalue()

class SummaryTests(unittest.TestCase):
    def test_same_and_not_compared_are_green(self):
        code, text = summary([SAME, dict(SAME, verdict="not compared (not clean)")])
        self.assertEqual(code, 0)
        self.assertIn("not compared (not clean)", text)

    def test_a_difference_is_red_and_prints_the_exact_fields(self):
        code, text = summary([SAME, DIFF])
        self.assertEqual(code, 1)
        self.assertIn("exact column of the differing rows", text)
        self.assertIn("equal 1", text)
        self.assertIn("fuzz-slab-52 pred-only-second surface differs", text)
        self.assertIn("min edge 2.27e-13, rounding resolution 4.44e-16, repair tolerance 1.14e-13", text)

    def test_error_timeout_and_process_failed_are_red_and_show_the_stderr_tail(self):
        for verdict in ("error", "timeout", "process failed"):
            code, text = summary([SAME, {"case": "contact-0001-mirror", "verdict": verdict, "message": "boom", "stderr_tail": "line1\nline2"}])
            self.assertEqual(code, 1, verdict)
            self.assertIn("contact-0001-mirror - " + verdict + ": boom", text)
            self.assertIn("stderr| line2", text)

    def test_no_rows_is_red(self):
        self.assertEqual(summary([])[0], 1)

class PlanTests(unittest.TestCase):
    def plan(self, variants):
        d = tempfile.mkdtemp()
        out = os.path.join(d, "cases.jsonl")
        with contextlib.redirect_stdout(io.StringIO()):
            ct.plan(out, 0, 0, 3, 1 << 30, variants)
        with open(out) as f:
            return [json.loads(l) for l in f if l.strip()]

    def test_the_variants_filter_the_contact_cases(self):
        everything = self.plan(None)
        only = self.plan(["ulp-shift"])
        self.assertGreater(len(everything), len(only))
        self.assertTrue(only)
        self.assertTrue(all(c["case"].endswith("-ulp-shift") for c in only))
        self.assertEqual(self.plan(["no-such-variant"]), [])

class RunTests(unittest.TestCase):
    def run_cases(self, fake):
        d = tempfile.mkdtemp()
        os.makedirs(os.path.join(d, "meshes"))
        open(os.path.join(d, "meshes", "1.stl"), "w").close()
        cases = os.path.join(d, "cases.jsonl")
        with open(cases, "w") as f:
            f.write(json.dumps({"case": "self-1", "args": ["self", "1"], "ids": ["1"]}) + "\n")
        out = os.path.join(d, "rows.jsonl")
        log = io.StringIO()
        real = subprocess.run
        subprocess.run = fake
        try:
            with contextlib.redirect_stdout(log), contextlib.redirect_stderr(io.StringIO()):
                code = ct.run(cases, d, out, 5, "tool.dll", "region-compare")
        finally:
            subprocess.run = real
        with open(out) as f:
            return code, [json.loads(l) for l in f if l.strip()], log.getvalue()

    def test_a_failing_process_keeps_the_last_stderr_lines_in_its_row_and_the_log(self):
        noise = "\n".join("line %d" % i for i in range(50))
        code, rows, log = self.run_cases(lambda *a, **k: subprocess.CompletedProcess(a, 1, stdout="", stderr=noise))
        self.assertEqual(code, 1)
        self.assertEqual(rows[-1]["verdict"], "process failed")
        tail = rows[-1]["stderr_tail"].splitlines()
        self.assertEqual(len(tail), ct.STDERR_LINES)
        self.assertEqual(tail[-1], "line 49")
        self.assertIn("stderr| line 49", log)
        self.assertNotIn("stderr| line 10", log)

    def test_an_error_row_of_the_tool_is_red_and_printed_with_its_message(self):
        row = json.dumps({"kind": "self", "verdict": "error", "message": "inexact transform: x"})
        code, rows, log = self.run_cases(lambda *a, **k: subprocess.CompletedProcess(a, 0, stdout=row + "\n", stderr="warning: something\n"))
        self.assertEqual(code, 1)
        self.assertEqual(rows[0]["stderr_tail"], "warning: something")
        self.assertIn("error: inexact transform: x", log)

    def test_same_rows_are_green_and_carry_no_tail(self):
        row = json.dumps({"op": "union", "verdict": "same"})
        code, rows, log = self.run_cases(lambda *a, **k: subprocess.CompletedProcess(a, 0, stdout=row + "\n", stderr="noise\n"))
        self.assertEqual(code, 0)
        self.assertNotIn("stderr_tail", rows[0])

    def test_a_timeout_keeps_the_tail(self):
        def fake(*a, **k):
            raise subprocess.TimeoutExpired(a, 5, output=b"", stderr=b"a\nb\n")
        code, rows, log = self.run_cases(fake)
        self.assertEqual(code, 1)
        self.assertEqual(rows[-1]["verdict"], "timeout")
        self.assertEqual(rows[-1]["stderr_tail"], "a\nb")

class TailTests(unittest.TestCase):
    def test_tail(self):
        self.assertEqual(ct.stderr_tail(None), "")
        self.assertEqual(ct.stderr_tail(b"x\ny"), "x\ny")
        self.assertEqual(ct.stderr_tail("\n".join(map(str, range(100)))).splitlines()[0], "80")

if __name__ == "__main__":
    unittest.main()
