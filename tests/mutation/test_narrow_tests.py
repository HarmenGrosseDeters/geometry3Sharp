#!/usr/bin/env python3
"""Tests of narrow_tests.py. Run by the changes job of the mutation workflow."""
import json
import os
import stat
import subprocess
import tempfile
import unittest

import narrow_tests as nt

ROOT = "/ci/work/repo"
LIB = "mesh_ops/Lib.cs"
TESTF = "tests/T.Tests/ATests.cs"
OTHER = "tests/T.Tests/BTests.cs"
CONFIG = {"stryker-config": {
    "mutate": ["**/mesh_ops/Lib.cs{0..99}"],
    "test-case-filter": "FullyQualifiedName~ATests|FullyQualifiedName~BTests&Category!=Nightly",
    "test-projects": ["../T.Tests/T.Tests.csproj"],
}}
SOURCES = {LIB: "class Lib { }\r\n", TESTF: "class ATests { }\n", OTHER: "class BTests { }\n"}


def mutant(i, status, killed_by=(), covered_by=()):
    return {"id": str(i), "status": status, "killedBy": list(killed_by), "coveredBy": list(covered_by)}


def report(mutants, tests):
    """tests: {id: (file, name)}"""
    tf = {}
    for tid, (path, name) in tests.items():
        entry = tf.setdefault(ROOT + "/" + path, {"source": SOURCES[path], "tests": []})
        entry["tests"].append({"id": tid, "name": name})
    tf[ROOT + "/tests/T.Tests/NoTests.cs"] = {"source": "class Helper { }\n", "tests": []}
    return {"projectRoot": ROOT,
            "files": {ROOT + "/" + LIB: {"source": SOURCES[LIB], "mutants": mutants},
                      ROOT + "/mesh_ops/Skipped.cs": {"source": "class S { }\n", "mutants": [mutant(90, "Ignored")]}},
            "testFiles": tf}


TESTS = {"a": (TESTF, "N.ATests.One"), "b": (TESTF, "N.ATests.Two"), "c": (OTHER, "N.BTests.Three"),
         "d": (OTHER, "N.BTests.Four")}


class Dir(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.repo = os.path.join(self.tmp.name, "repo")
        for path, text in SOURCES.items():
            self.write(path, text)
        self.write("tests/T.Tests/T.Tests.csproj", "<Project/>")
        self.write("tests/mutation/c.json", json.dumps(CONFIG))
        self.config = os.path.join(self.repo, "tests/mutation/c.json")

    def write(self, path, text):
        full = os.path.join(self.repo, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "w", newline="") as f:
            f.write(text)

    def make(self, mutants, **kw):
        rp = os.path.join(self.tmp.name, "r.json")
        json.dump(report(mutants, TESTS), open(rp, "w"))
        return nt.make(rp, self.config, **kw)

    def apply(self, flt, config=None):
        fp = os.path.join(self.tmp.name, "f.json")
        out = os.path.join(self.tmp.name, "out.json")
        if flt is not None:
            json.dump(flt, open(fp, "w"))
        cfg = self.config
        if config is not None:
            cfg = os.path.join(self.tmp.name, "cfg.json")
            json.dump(config, open(cfg, "w"))
        msg = nt.apply(cfg, fp, self.repo, out)
        return msg, json.load(open(out))["stryker-config"]


class Make(Dir):
    BIG = [mutant(i, "Killed", "a", "abcd") for i in range(10)]

    def test_a_sole_killer_is_kept_and_a_covered_pair_needs_one_test(self):
        flt, _ = self.make(self.BIG + [mutant(20, "Killed", "bc", "abcd"), mutant(21, "Killed", "cd", "abcd")])
        self.assertEqual(flt["kept"], ["N.ATests.One", "N.BTests.Three"])

    def test_greedy_takes_the_test_that_covers_most_open_mutants(self):
        ms = self.BIG + [mutant(20, "Killed", "bc", "abcd"), mutant(21, "Killed", "bd", "abcd"),
                         mutant(22, "Killed", "bc", "abcd")]
        flt, _ = self.make(ms)
        self.assertEqual(flt["kept"], ["N.ATests.One", "N.ATests.Two"])

    def test_timeouts_count_as_kills(self):
        flt, _ = self.make(self.BIG + [mutant(20, "Timeout", "d", "abcd")])
        self.assertIn("N.BTests.Four", flt["kept"])

    def test_survivors_and_uncovered_mutants_need_no_test(self):
        flt, _ = self.make(self.BIG + [mutant(20, "Survived", (), "bcd"), mutant(21, "NoCoverage"),
                                       mutant(22, "Ignored", (), "bcd"), mutant(23, "CompileError")])
        self.assertEqual(flt["kept"], ["N.ATests.One"])

    def test_the_saving_is_what_the_dropped_tests_covering_the_mutants_cost(self):
        flt, msg = self.make(self.BIG)  # 4 covering tests each, 1 kept: 75 %
        self.assertEqual(flt["estimated_saving"], 0.75)
        self.assertIn("75 %", msg)

    def test_survivors_cost_every_covering_test_and_timeouts_one_and_a_half_times(self):
        ms = [mutant(0, "Killed", "a", "ab"), mutant(1, "Survived", (), "abcd"), mutant(2, "Timeout", "a", "abcd")]
        flt, _ = self.make(ms)
        all_cost = 2 + 4 + 1.5 * 4
        kept_cost = 1 + 1 + 1.5 * 1
        self.assertAlmostEqual(flt["estimated_saving"], round(1 - kept_cost / all_cost, 3))

    def test_below_the_minimum_saving_no_filter_is_made(self):
        ms = [mutant(0, "Killed", "a", "a"), mutant(1, "Killed", "b", "ab")]
        flt, msg = self.make(ms)
        self.assertIsNone(flt)
        self.assertIn("below 20 %", msg)
        flt, _ = self.make(ms, min_saving=0.0)
        self.assertIsNotNone(flt)

    def test_a_report_without_a_kill_makes_no_filter(self):
        flt, msg = self.make([mutant(0, "Survived", (), "ab")])
        self.assertIsNone(flt)
        self.assertIn("no killed mutant", msg)

    def test_a_killer_that_the_report_does_not_list_makes_no_filter(self):
        flt, msg = self.make(self.BIG + [mutant(20, "Killed", "zz", "abcd")])
        self.assertIsNone(flt)
        self.assertIn("not in the report", msg)

    def test_durations_steer_the_cover_to_the_cheap_test(self):
        trx = os.path.join(self.tmp.name, "t.trx")
        rows = "".join('<UnitTestResult testName="%s(x: %d)" duration="00:00:%s" />' % (n, i, d)
                       for n, i, d in [("N.BTests.Three", 1, "00.5"), ("N.BTests.Three", 2, "00.5"),
                                       ("N.BTests.Four", 1, "09.0"), ("N.ATests.One", 1, "01.0")])
        open(trx, "w").write('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                             "<Results>%s</Results></TestRun>" % rows)
        self.assertEqual(nt.durations_of(trx), {"N.BTests.Three": 1.0, "N.BTests.Four": 9.0, "N.ATests.One": 1.0})
        ms = self.BIG + [mutant(20, "Killed", "cd", "abcd")]
        flt, _ = self.make(ms, trx=trx)
        self.assertEqual(flt["kept"], ["N.ATests.One", "N.BTests.Three"])
        self.assertTrue(flt["weighted_by_duration"])

    def test_the_filter_records_what_it_was_made_from(self):
        flt, _ = self.make(self.BIG)
        self.assertEqual(flt["terms"], ["ATests", "BTests"])
        self.assertEqual(sorted(flt["mutated_files"]), [LIB])
        self.assertEqual(sorted(flt["test_files"]), [TESTF, OTHER])
        self.assertEqual(flt["tests_in_report"], 4)

    def test_a_mutant_that_was_not_tested_costs_nothing_even_with_a_covering_list(self):
        ms = [mutant(0, "Killed", "a", "ab"), mutant(1, "NoCoverage", (), "abcd")]
        self.assertEqual(nt.mutant_cost(ms, None, lambda t: 1.0), 2.0)

    def test_filter_terms_take_class_names_not_properties(self):
        self.assertEqual(nt.filter_terms("FullyQualifiedName~A.B|FullyQualifiedName=C&Category!=Slow"), {"A", "C"})
        self.assertEqual(nt.filter_terms(""), set())


class Apply(Dir):
    def current(self):
        flt, _ = self.make(Make.BIG + [mutant(20, "Killed", "cd", "abcd")])
        self.assertIsNotNone(flt)
        return flt

    def test_a_current_filter_narrows_to_the_exact_kept_tests(self):
        msg, cfg = self.apply(self.current())
        self.assertEqual(msg, "narrowed: 2 of 4 tests")
        self.assertEqual(cfg["test-case-filter"], "FullyQualifiedName=N.ATests.One|FullyQualifiedName=N.BTests.Four")
        self.assertEqual(cfg["mutate"], CONFIG["stryker-config"]["mutate"])

    def assertFull(self, flt, needle, config=None):
        msg, cfg = self.apply(flt, config)
        self.assertTrue(msg.startswith("full: "), msg)
        self.assertIn(needle, msg)
        want = (config or CONFIG)["stryker-config"]
        self.assertEqual(cfg["test-case-filter"], want["test-case-filter"])

    def test_a_changed_filter_or_mutate_of_the_config_is_stale(self):
        for key, value, needle in [("test-case-filter", "FullyQualifiedName~ATests", "test-case-filter"),
                                   ("mutate", ["**/mesh_ops/Lib.cs{0..98}"], "mutate")]:
            cfg = json.loads(json.dumps(CONFIG))
            cfg["stryker-config"][key] = value
            self.assertFull(self.current(), needle, cfg)

    def test_a_changed_mutated_file_is_stale(self):
        flt = self.current()
        self.write(LIB, "class Lib { int x; }\n")
        self.assertFull(flt, "mutated file changed: " + LIB)

    def test_a_changed_test_file_is_stale_even_if_it_holds_no_kept_test(self):
        flt = self.current()
        self.write(OTHER, "class BTests { void New() {} }\n")
        self.assertFull(flt, "test file changed: " + OTHER)

    def test_a_removed_file_is_stale(self):
        flt = self.current()
        os.remove(os.path.join(self.repo, TESTF))
        self.assertFull(flt, "test file is gone")

    def test_line_breaks_alone_do_not_make_a_filter_stale(self):
        flt = self.current()
        self.write(LIB, "class Lib { }\n")  # the report's source has \r\n
        self.assertTrue(self.apply(flt)[0].startswith("narrowed"))

    def test_a_byte_order_mark_alone_does_not_make_a_filter_stale(self):
        flt = self.current()
        self.write(TESTF, "﻿class ATests { }\n")
        self.assertTrue(self.apply(flt)[0].startswith("narrowed"))

    def test_a_new_test_file_that_names_a_class_of_the_filter_is_stale(self):
        flt = self.current()
        self.write("tests/T.Tests/Sub/MoreTests.cs", "partial class BTests { void Extra() {} }\n")
        self.assertFull(flt, "new test file mentions the filter's classes: tests/T.Tests/Sub/MoreTests.cs")

    def test_a_new_unrelated_test_file_and_build_output_are_not(self):
        flt = self.current()
        self.write("tests/T.Tests/Other.cs", "class Unrelated { }\n")
        self.write("tests/T.Tests/obj/Gen.cs", "class BTests { }\n")
        self.write("tests/T.Tests/bin/Gen.cs", "class ATests { }\n")
        self.assertTrue(self.apply(flt)[0].startswith("narrowed"))

    def test_a_missing_or_broken_filter_file_is_full(self):
        self.assertFull(None, "no usable filter file")
        fp = os.path.join(self.tmp.name, "f.json")
        open(fp, "w").write("{broken")
        msg = nt.apply(self.config, fp, self.repo, os.path.join(self.tmp.name, "o.json"))
        self.assertTrue(msg.startswith("full: no usable filter file"), msg)

    def test_another_format_or_an_empty_kept_list_is_full(self):
        flt = self.current()
        self.assertFull(dict(flt, format=99), "format")
        self.assertFull(dict(flt, kept=[]), "keeps no test")

    def test_the_cli_round_trip(self):
        rp = os.path.join(self.tmp.name, "r.json")
        fp = os.path.join(self.tmp.name, "f.json")
        out = os.path.join(self.tmp.name, "o.json")
        json.dump(report(Make.BIG, TESTS), open(rp, "w"))
        self.assertEqual(nt.main(["make", "--report", rp, "--config", self.config, "-o", fp]), 0)
        self.assertEqual(json.load(open(fp))["kept"], ["N.ATests.One"])
        self.assertEqual(nt.main(["apply", "--config", self.config, "--filter", fp, "--repo", self.repo, "-o", out]), 0)
        self.assertEqual(json.load(open(out))["stryker-config"]["test-case-filter"], "FullyQualifiedName=N.ATests.One")

    def test_make_writes_nothing_below_the_minimum_saving(self):
        rp = os.path.join(self.tmp.name, "r.json")
        fp = os.path.join(self.tmp.name, "f.json")
        json.dump(report([mutant(0, "Killed", "a", "a")], TESTS), open(rp, "w"))
        nt.main(["make", "--report", rp, "--config", self.config, "-o", fp])
        self.assertFalse(os.path.exists(fp))


HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))


class RunStryker(unittest.TestCase):
    """run_stryker.sh in full mode with a stub `dotnet`: which config does Stryker get?"""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        t = self.tmp.name
        os.makedirs(os.path.join(t, "bin"))
        os.makedirs(os.path.join(t, "filters"))
        stub = os.path.join(t, "bin", "dotnet")
        open(stub, "w").write('#!/bin/sh\nfor a in "$@"; do last=$a; done\ncp "$last" "$G3_STUB_OUT"\n')
        os.chmod(stub, os.stat(stub).st_mode | stat.S_IEXEC)
        term = "Zz" + "NoSuchClass"  # not spelled out: this file is a test file that the guard scans
        self.config = {"stryker-config": {"mutate": ["x"], "test-case-filter": "FullyQualifiedName~" + term,
                                          "test-projects": [os.path.join(REPO, "tests/geometry3Sharp.Tests/x.csproj")]}}
        self.cfg_path = os.path.join(t, "stryker-test.json")
        json.dump(self.config, open(self.cfg_path, "w"))
        src = os.path.join(HERE, "narrow_tests.py")
        self.flt = {"format": nt.FORMAT, "config_filter": nt.digest(self.config["stryker-config"]["test-case-filter"]),
                    "config_mutate": nt.digest_json(["x"]), "terms": [term], "kept": ["N.C.M"], "tests_in_report": 3,
                    "mutated_files": {"tests/mutation/narrow_tests.py": nt.digest(nt.read_text(src))}, "test_files": {}}
        self.flt_path = os.path.join(t, "filters", "stryker-test.json")

    def run_script(self, **env):
        out = os.path.join(self.tmp.name, "seen.json")
        e = dict(os.environ, PATH=os.path.join(self.tmp.name, "bin") + os.pathsep + os.environ["PATH"], MODE="full",
                 G3_STUB_OUT=out, G3_TEST_FILTERS=os.path.join(self.tmp.name, "filters"),
                 GITHUB_STEP_SUMMARY=os.path.join(self.tmp.name, "summary.md"), **env)
        subprocess.run(["bash", os.path.join(HERE, "run_stryker.sh"), self.cfg_path], cwd=self.tmp.name, env=e,
                       check=True, capture_output=True)
        return json.load(open(out))["stryker-config"]["test-case-filter"]

    def full_filter(self):
        return self.config["stryker-config"]["test-case-filter"]

    def test_a_current_filter_file_narrows_the_full_run(self):
        json.dump(self.flt, open(self.flt_path, "w"))
        self.assertEqual(self.run_script(), "FullyQualifiedName=N.C.M")
        self.assertIn("narrowed: 1 of 3 tests", open(os.path.join(self.tmp.name, "summary.md")).read())

    def test_no_filter_file_a_stale_one_and_G3_FULL_TESTS_run_the_config_as_it_is(self):
        self.assertEqual(self.run_script(), self.full_filter())
        self.assertFalse(os.path.exists(os.path.join(self.tmp.name, "summary.md")))  # no filter file: nothing to say
        json.dump(dict(self.flt, config_mutate="stale"), open(self.flt_path, "w"))
        self.assertEqual(self.run_script(), self.full_filter())
        self.assertIn("full: mutate of the config changed", open(os.path.join(self.tmp.name, "summary.md")).read())
        json.dump(self.flt, open(self.flt_path, "w"))
        self.assertEqual(self.run_script(G3_FULL_TESTS="1"), self.full_filter())


if __name__ == "__main__":
    unittest.main()
