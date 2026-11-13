import json
import os
import tempfile
import unittest

import runtime_watch as rw

HERE = os.path.dirname(os.path.abspath(__file__))
RULES = dict(rw.DEFAULT_RULES)


def entry(t, ok=True, flags=()):
    return {"ok": ok, "median_s": t, "flags": list(flags)}


def res(t, ok=True, cid="c"):
    return {"id": cid, "area": "boolean", "kind": "procedural", "ok": ok, "median_s": t, "runs_s": [t] if t else []}


class OutlierRule(unittest.TestCase):
    def test_no_history_no_regression(self):
        self.assertEqual(rw.judge(res(5.0), [], RULES)["flags"], [])

    def test_regression_needs_ratio_and_absolute(self):
        hist = [entry(10.0)] * 5
        self.assertEqual(rw.judge(res(13.5), hist, RULES)["flags"], ["regression"])   # 1.35x and +3.5 s
        self.assertEqual(rw.judge(res(12.9), hist, RULES)["flags"], [])                # below 1.3x
        small = [entry(0.5)] * 5
        self.assertEqual(rw.judge(res(1.4), small, RULES)["flags"], [])                # 2.8x but only +0.9 s
        self.assertEqual(rw.judge(res(1.6), small, RULES)["flags"], ["regression"])    # +1.1 s

    def test_boundaries_are_strict(self):
        hist = [entry(10.0)] * 3
        self.assertEqual(rw.judge(res(13.0), hist, RULES)["flags"], [])                # exactly 1.3x
        self.assertEqual(rw.judge(res(20.0), hist, RULES)["flags"], ["regression"])    # 20 s is not over the hard limit
        self.assertEqual(rw.judge(res(20.01), hist, RULES)["flags"], ["hard", "regression"])

    def test_hard_limit_without_history(self):
        self.assertEqual(rw.judge(res(25.0), [], RULES)["flags"], ["hard"])

    def test_baseline_is_median_of_window_and_ignores_failures(self):
        hist = [entry(100.0)] * 3 + [entry(None, ok=False)] + [entry(t) for t in (1, 2, 3)]
        self.assertEqual(rw.baseline(hist, 3), 2)
        self.assertEqual(rw.baseline(hist, 10), 51.5)   # median of 100, 100, 100, 1, 2, 3
        self.assertIsNone(rw.baseline([entry(None, ok=False)], 10))

    def test_one_noisy_night_does_not_move_the_baseline(self):
        hist = [entry(10.0)] * 9 + [entry(40.0)]
        self.assertEqual(rw.baseline(hist, 10), 10.0)

    def test_failure(self):
        self.assertEqual(rw.judge(res(None, ok=False), [entry(1.0)], RULES)["flags"], ["failed"])

    def test_new_flags_only(self):
        hist = [entry(30.0, flags=["hard"])]
        j = rw.judge(res(31.0), hist, RULES)
        self.assertEqual(j["flags"], ["hard"])
        self.assertEqual(j["new"], [])
        self.assertEqual(rw.judge(res(31.0), [entry(5.0)], RULES)["new"], ["hard", "regression"])


class EvaluateAndNotify(unittest.TestCase):
    def run_eval(self, hist_entries, t):
        results = {"sha": "abc", "date": "d", "machine": {"cpu": "x"}, "cases": [res(t, ok=t is not None)]}
        return rw.evaluate(results, {"version": 1, "entries": {"c": hist_entries}}, RULES)

    def test_history_appended_and_capped(self):
        judged, h = self.run_eval([entry(1.0)] * 100, 1.0)
        self.assertEqual(len(h["entries"]["c"]), rw.HISTORY_KEEP)
        self.assertEqual(h["entries"]["c"][-1]["sha"], "abc")
        self.assertFalse(rw.notify(judged))

    def test_notify_regression_and_new_hard_but_not_repeated_hard(self):
        self.assertTrue(rw.notify(self.run_eval([entry(10.0)] * 3, 20.0)[0]))
        self.assertTrue(rw.notify(self.run_eval([entry(5.0)], 21.0)[0]))
        again = self.run_eval([entry(21.0, flags=["hard"])] * 3, 21.5)[0]
        self.assertEqual(again[0]["flags"], ["hard"])
        self.assertFalse(rw.notify(again))

    def test_persistent_regression_and_failure_notify_once(self):
        # night 1 regresses; night 2 is still slow: the flag is not new, no second comment
        h1 = [entry(10.0)] * 5
        j1, hist = self.run_eval(h1, 20.0)
        self.assertTrue(rw.notify(j1))
        j2, _ = self.run_eval(hist["entries"]["c"], 20.0)
        self.assertEqual(j2[0]["flags"], ["regression"])
        self.assertFalse(rw.notify(j2))
        f1, hist = self.run_eval([entry(1.0)], None)
        f2, _ = self.run_eval(hist["entries"]["c"], None)
        self.assertTrue(rw.notify(f1))
        self.assertFalse(rw.notify(f2))

    def test_flaky_failure_between_slow_runs_does_not_renotify(self):
        hist = [entry(30.0, flags=["hard"]), entry(None, ok=False, flags=["failed"])]
        self.assertFalse(rw.notify(self.run_eval(hist, 30.0)[0]))
        # but after NOTIFY_MEMORY quiet nights the same flag is new again
        quiet = [entry(30.0, flags=["hard"])] + [entry(5.0)] * rw.NOTIFY_MEMORY
        self.assertTrue(rw.notify(self.run_eval(quiet, 30.0)[0]))

    def test_timeout_result_stays_out_of_the_baseline(self):
        results = {"sha": "a", "machine": {}, "cases": [res(900.0, ok=False)]}
        _, h = rw.evaluate(results, {}, RULES)
        self.assertIsNone(rw.baseline(h["entries"]["c"], 10))

    def test_zero_baseline_does_not_divide(self):
        j = rw.judge(res(2.0), [entry(0.0)] * 3, RULES)
        self.assertEqual(j["flags"], ["regression"])   # 0 s -> 2 s is more than 1 s above, whatever the ratio
        self.assertIsNone(j["ratio"])

    def test_failure_is_recorded_and_notifies(self):
        judged, h = self.run_eval([entry(1.0)], None)
        self.assertTrue(rw.notify(judged))
        self.assertFalse(h["entries"]["c"][-1]["ok"])

    def test_report_lists_flags(self):
        results = {"sha": "abcdef0123456789", "machine": {"cpu": "x"}, "cases": [res(30.0)]}
        judged, _ = rw.evaluate(results, {}, RULES)
        text = rw.render_report(results, judged, RULES)
        self.assertIn("1 outlier(s) of 1", text)
        self.assertIn("hard", text)


class CaseList(unittest.TestCase):
    def write(self, obj):
        fd, path = tempfile.mkstemp(suffix=".json")
        with os.fdopen(fd, "w") as f:
            json.dump(obj, f)
        self.addCleanup(os.remove, path)
        return path

    def case(self, **kw):
        c = {"id": "a-1", "area": "boolean", "kind": "procedural", "description": "d", "run": ["x", "{work}/A.off"]}
        c.update(kw)
        return c

    def test_shipped_case_list_is_valid(self):
        rules, cases = rw.load_cases(os.path.join(HERE, "runtime-watch", "cases.json"))
        self.assertGreaterEqual(len(cases), 1)
        self.assertEqual(rules["hard_s"], 20.0)
        self.assertEqual(len({c["id"] for c in cases}), len(cases))

    def test_minimal_valid(self):
        rules, cases = rw.load_cases(self.write({"version": 1, "cases": [self.case()]}))
        self.assertEqual(rules, RULES)
        self.assertEqual(len(cases), 1)

    def test_rejects(self):
        bad = [
            {"version": 2, "cases": []},
            {"version": 1, "rules": {"ratio": -1}, "cases": []},
            {"version": 1, "rules": {"nope": 1}, "cases": []},
            {"version": 1, "cases": [self.case(), self.case()]},                       # duplicate id
            {"version": 1, "cases": [self.case(id="a b")]},
            {"version": 1, "cases": [self.case(area="mesh")]},
            {"version": 1, "cases": [self.case(run=[])]},
            {"version": 1, "cases": [self.case(kind="thingi")]},                      # thingi without ids
            {"version": 1, "cases": [self.case(thingi=["1"])]},                       # ids on a procedural case
            {"version": 1, "cases": [self.case(kind="thingi", thingi=["x"])]},
            {"version": 1, "cases": [self.case(run=["{thingi:5}"])]},                 # id not listed
            {"version": 1, "cases": [self.case(run=["{bogus}"])]},
            {"version": 1, "cases": [self.case(description="")]},
        ]
        for obj in bad:
            with self.subTest(obj=obj), self.assertRaises(ValueError):
                rw.load_cases(self.write(obj))

    def test_thingi_case_with_placeholder(self):
        c = self.case(kind="thingi", thingi=["55928"], run=["x", "{thingi:55928}", "{dll:Foo}"])
        rw.load_cases(self.write({"version": 1, "cases": [c]}))

    def test_expand(self):
        self.assertEqual(rw.expand("{work}/A.off", "/w", {}), "/w/A.off")
        self.assertEqual(rw.expand("{thingi:7}", "/w", {"7": "/m/7.stl"}), "/m/7.stl")
        self.assertTrue(rw.expand("{dll:Foo}", "/w", {}, "/r").endswith(os.path.join("Foo", "bin", "Release", "net10.0", "Foo.dll")))


class WorkflowFile(unittest.TestCase):
    """The workflow's trigger and concurrency contract (owner 2026-11-13: daily at 01:17 UTC, a running run is never cancelled)."""

    def setUp(self):
        with open(os.path.join(HERE, "..", ".github", "workflows", "runtime-watch.yml"), encoding="utf-8") as f:
            self.text = f.read()
        self.lines = [l for l in self.text.splitlines() if not l.lstrip().startswith("#")]

    def test_daily_schedule_and_dispatch(self):
        code = "\n".join(self.lines)
        self.assertIn("- cron: '17 1 * * *'", code)
        self.assertIn("workflow_dispatch:", code)

    def test_running_run_is_not_cancelled(self):
        code = "\n".join(self.lines)
        self.assertIn("group: runtime-watch", code)
        self.assertIn("cancel-in-progress: false", code)
        self.assertNotIn("cancel-in-progress: true", code)

    def test_not_a_pull_request_workflow(self):
        self.assertNotIn("pull_request", "\n".join(self.lines))


class TimeReading(unittest.TestCase):
    def test_json_ms_last_line(self):
        self.assertEqual(rw.read_time('log\n{"ms": 1500, "ok": true}\n', "ms", 9.0), 1.5)

    def test_other_field_and_wall(self):
        self.assertEqual(rw.read_time('{"repair_ms": 2500}', "repair_ms", 9.0), 2.5)
        self.assertEqual(rw.read_time("anything", "wall", 9.0), 9.0)

    def test_last_json_line_wins(self):
        self.assertEqual(rw.read_time('{"ms": 1000}\n{"ms": 3000}\n', "ms", 9.0), 3.0)

    def test_seconds_field_is_not_divided(self):
        self.assertEqual(rw.read_time('{"seconds": 2.5}', "seconds", 9.0), 2.5)

    def test_missing_field_raises(self):
        with self.assertRaises(ValueError):
            rw.read_time('{"x": 1}', "ms", 1.0)
        with self.assertRaises(ValueError):
            rw.read_time("no json", "ms", 1.0)


class RunnerWithoutNetwork(unittest.TestCase):
    """The runner on a stub command; a case without Thingi ids never fetches (a PR-tier test never uses the network)."""

    def test_median_of_repeats_and_failure(self):
        import sys
        rules = dict(RULES, repeats=3)
        with tempfile.TemporaryDirectory() as d:
            c = {"id": "stub", "area": "boolean", "kind": "procedural", "description": "d", "time_field": "ms",
                 "run": [sys.executable, "-c", "print('{\"ms\": 2000}')"]}
            r = rw.run_case(c, rules, d, d)
            self.assertTrue(r["ok"])
            self.assertEqual(r["runs_s"], [2.0, 2.0, 2.0])
            self.assertEqual(r["median_s"], 2.0)
            c["run"] = [sys.executable, "-c", "import sys; sys.exit(3)"]
            r = rw.run_case(c, rules, d, d)
            self.assertFalse(r["ok"])
            self.assertIn("exit 3", r["error"])

    def test_timeout_counts_as_failure_at_the_limit(self):
        import sys
        with tempfile.TemporaryDirectory() as d:
            c = {"id": "slow", "area": "boolean", "kind": "procedural", "description": "d", "timeout_s": 1,
                 "run": [sys.executable, "-c", "import time; time.sleep(30)"]}
            r = rw.run_case(c, RULES, d, d)
            self.assertFalse(r["ok"])
            self.assertIn("timeout", r["error"])

    def test_repeat_budget_stops_repeating(self):
        import sys
        rules = dict(RULES, repeat_budget_s=1.0)
        with tempfile.TemporaryDirectory() as d:
            c = {"id": "big", "area": "boolean", "kind": "procedural", "description": "d", "time_field": "ms",
                 "run": [sys.executable, "-c", "print('{\"ms\": 5000}')"]}
            self.assertEqual(rw.run_case(c, rules, d, d)["runs_s"], [5.0])


    def test_setup_timeout_is_a_failed_case_not_a_crash(self):
        import sys
        with tempfile.TemporaryDirectory() as d:
            c = {"id": "hang", "area": "boolean", "kind": "procedural", "description": "d", "timeout_s": 1,
                 "setup": [[sys.executable, "-c", "import time; time.sleep(30)"]], "run": [sys.executable, "-c", "pass"]}
            r = rw.run_case(c, RULES, d, d)
            self.assertFalse(r["ok"])
            self.assertTrue(r["error"])

    def test_budget_boundary_is_strict(self):
        import sys
        rules = dict(RULES, repeat_budget_s=5.0)
        with tempfile.TemporaryDirectory() as d:
            c = {"id": "edge", "area": "boolean", "kind": "procedural", "description": "d", "time_field": "ms",
                 "run": [sys.executable, "-c", "print('{\"ms\": 5000}')"]}
            self.assertEqual(len(rw.run_case(c, rules, d, d)["runs_s"]), 3)   # exactly the budget: keeps repeating


class Cli(unittest.TestCase):
    def test_output_contract_for_github_output(self):
        import contextlib
        import io
        with tempfile.TemporaryDirectory() as d:
            p = lambda n: os.path.join(d, n)
            results = {"sha": "abc", "machine": {}, "cases": [res(30.0, cid="boolean-knot-default-union")]}
            with open(p("r.json"), "w") as f:
                json.dump(results, f)
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                rw.main(["evaluate", p("r.json"), "--history", p("none.json"), "--out-history", p("h.json"), "--report", p("rep.md")])
            self.assertEqual(out.getvalue().split(), ["outliers=1", "notify=1"])

    def test_corrupt_history_fails_loudly(self):
        with tempfile.TemporaryDirectory() as d:
            p = lambda n: os.path.join(d, n)
            open(p("h.json"), "w").write("{not json")
            json.dump({"sha": "a", "machine": {}, "cases": []}, open(p("r.json"), "w"))
            with self.assertRaises(ValueError):
                rw.main(["evaluate", p("r.json"), "--history", p("h.json"), "--out-history", p("o.json"), "--report", p("rep.md")])

    def test_evaluate_roundtrip(self):
        with tempfile.TemporaryDirectory() as d:
            p = lambda n: os.path.join(d, n)
            results = {"sha": "abc", "date": "d", "machine": {}, "cases": [res(30.0, cid="boolean-knot-default-union"),
                       {"id": "net", "ok": False, "infra": True, "error": "fetch failed"}]}
            with open(p("r.json"), "w") as f:
                json.dump(results, f)
            self.assertEqual(rw.main(["evaluate", p("r.json"), "--history", p("none.json"), "--out-history", p("h.json"),
                                      "--report", p("rep.md"), "--cases", os.path.join(HERE, "runtime-watch", "cases.json")]), 0)
            h = json.load(open(p("h.json")))
            self.assertEqual(list(h["entries"]), ["boolean-knot-default-union"])   # the infra failure is not recorded
            self.assertIn("Not measured", open(p("rep.md")).read())


if __name__ == "__main__":
    unittest.main()
