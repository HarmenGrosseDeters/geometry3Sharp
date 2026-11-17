#!/usr/bin/env python3
"""Checks of alpha_defaults.py and alpha_defaults_summary.py on synthetic rows (no meshes, no benchmark process).
Usage: python3 test_alpha_defaults.py"""
import json, os, subprocess, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import alpha_defaults as ad

LIST = os.path.join(HERE, "cases", "alpha_defaults_list800.txt")


class Select(unittest.TestCase):
    def test_shards_partition_the_range(self):
        ids = ad.read_ids(LIST)
        self.assertEqual(len(ids), 800)
        self.assertEqual(len(set(ids)), 800)
        for shards in (1, 16, 7):
            parts = [ad.select(ids, i, shards) for i in range(shards)]
            self.assertEqual(sorted(sum(parts, [])), sorted(ids))
        self.assertEqual(len(ad.select(ids, 3, 16)), 50)

    def test_range_and_bounds(self):
        ids = ad.read_ids(LIST)
        got = sum((ad.select(ids, i, 4, "10:30") for i in range(4)), [])
        self.assertEqual(sorted(got), sorted(ids[10:30]))
        self.assertEqual(ad.select(ids, 0, 1, ":5"), ids[:5])
        with self.assertRaises(ValueError):
            ad.select(ids, 4, 4)
        for bad in ("5:3", "0:801"):
            with self.assertRaises(ValueError):
                ad.select(ids, 0, 1, bad)

    def test_configs_are_the_documented_ones(self):
        self.assertEqual(ad.CONFIG_ENV["A"], {})
        self.assertEqual(ad.CONFIG_ENV["B"], {"BENCH_WRAP_FEATURES": "1"})
        self.assertEqual(ad.CONFIG_ENV["C"], {"BENCH_WRAP_BUDGET": "300000"})
        self.assertEqual(ad.CONFIG_ENV["D"], {"BENCH_WRAP_BUDGET": "300000", "BENCH_WRAP_COARSEN_CAP": "1.5"})
        self.assertEqual(ad.CONFIG_ENV["E"], {"BENCH_WRAP_BUDGET": "300000", "BENCH_WRAP_COARSEN_CAP": "1.2"})
        self.assertEqual(ad.CONFIG_ENV["F"], {"BENCH_WRAP_PULL": "1"})
        self.assertIn("BENCH_WRAP_PULL", ad.UNSET)


class RawBoundsSummary(unittest.TestCase):
    # the raw-wrap section (A against F, the quality-aware manifold step): aggregates, the per-mesh table of the meshes that
    # differ, and the TSV of every mesh
    def test_raw_bounds_section_and_tsv(self):
        def row(f, below, amin, vmax, flips, closed=True):
            return {"file": f, "after_tris": 10, "raw_tris": 100 + below, "raw_below_bound": below, "raw_angle_min": amin,
                    "raw_angle_max": 170.0, "raw_vdist_max_off": vmax, "raw_vdist_min_off": 1.0, "raw_sep_off": 1.2,
                    "raw_closed": closed, "raw_bowties": 0, "wrap_ms": 50, "pipe_ms": 80, "wrap_status": "Success",
                    "wrap_counts": {"manifold_flips": flips}}
        with tempfile.TemporaryDirectory() as t:
            os.makedirs(os.path.join(t, "raw"))
            a = [row("1.stl", 3, 0.5, 1.0, 12), row("2.stl", 0, 3.0, 1.0, 0)]
            f = [row("1.stl", 0, 2.9, 1.8, 1), row("2.stl", 0, 3.0, 1.0, 0)]
            for c, rows in (("A", a), ("F", f)):
                open(os.path.join(t, "raw", c + ".jsonl"), "w").write("".join(json.dumps(r) + "\n" for r in rows))
            lst = os.path.join(t, "list.txt")
            open(lst, "w").write("1\n2\n")
            r = subprocess.run([sys.executable, "-I", os.path.join(HERE, "alpha_defaults_summary.py"), t, lst], capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertIn("## Raw wrap: the derived bounds of the class doc", r.stdout)
            self.assertIn("| A (defaults) | 1 | 3 | 0.500 |", r.stdout)
            self.assertIn("| F (PullPinchPoints) | 0 | 0 | 2.900 |", r.stdout)
            self.assertIn("(1 meshes differ in the count below the bound or in clean; fewer below: 1)", r.stdout)
            self.assertIn("| 1.stl | 3 / 0 | 0.500 / 2.900 | 1.000 / 1.800 |", r.stdout)
            tsv = open(os.path.join(t, "raw_bounds_per_mesh.tsv")).read().splitlines()
            self.assertEqual(len(tsv), 3)
            self.assertTrue(tsv[0].startswith("file\tA_raw_below_bound"))


class RunAndMerge(unittest.TestCase):
    def test_run_resume_and_merge(self):
        with tempfile.TemporaryDirectory() as t:
            fake = os.path.join(t, "bench")
            os.makedirs(fake)
            # a fake run.sh: prints a row naming the config's env, crashes on mesh 2
            open(os.path.join(fake, "run.sh"), "w").write(
                '#!/bin/bash\nf=$(basename "$1")\n[ "$f" = 2.stl ] && exit 0\n'
                'echo "{\\"file\\":\\"$f\\",\\"f\\":\\"${BENCH_WRAP_FEATURES:-0}\\",\\"b\\":\\"${BENCH_WRAP_BUDGET:-0}\\",\\"after_tris\\":1}"\n')
            os.chmod(os.path.join(fake, "run.sh"), 0o755)
            row, wall = ad.run_one("/x/1.stl", "B", 5, fake)
            self.assertEqual(json.loads(row)["f"], "1")
            self.assertEqual(json.loads(row)["b"], "0")
            row, _ = ad.run_one("/x/1.stl", "C", 5, fake)
            self.assertEqual(json.loads(row)["b"], "300000")
            row, _ = ad.run_one("/x/2.stl", "A", 5, fake)
            self.assertEqual(json.loads(row), {"file": "2.stl", "status": "timeout_or_crash"})

            a, b = os.path.join(t, "a"), os.path.join(t, "b")
            for d, rows in ((a, ["1", "2"]), (b, ["2", "3"])):  # mesh 2 in both: the first shard's row wins
                os.makedirs(os.path.join(d, "raw"))
                for c in "ABCD":
                    open(os.path.join(d, "raw", c + ".jsonl"), "w").write("".join(json.dumps({"file": r + ".stl", "src": d}) + "\n" for r in rows))
                open(os.path.join(d, "wall.tsv"), "w").write("".join(f"{r}.stl\t{c}\t1.5\n" for r in rows for c in "ABCD"))
            out = os.path.join(t, "m")
            ad.cmd_merge([out, a, b])
            self.assertFalse(os.path.exists(os.path.join(out, "raw", "E.jsonl")))  # (not run: no file)
            for c in "ABCD":
                got = [json.loads(l) for l in open(os.path.join(out, "raw", c + ".jsonl"))]
                self.assertEqual([r["file"] for r in got], ["1.stl", "2.stl", "3.stl"])
                self.assertEqual(got[1]["src"], a)
            self.assertEqual(len(open(os.path.join(out, "wall.tsv")).readlines()), 12)

            # the summary reads the merge
            lst = os.path.join(t, "list.txt")
            open(lst, "w").write("1\n2\n3\n")
            r = subprocess.run([sys.executable, "-I", os.path.join(HERE, "alpha_defaults_summary.py"), out, lst], capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, r.stderr)
            self.assertIn("**3 of 3**", r.stdout)
            self.assertIn("D (budget 300k, coarsening <= 1.5)", r.stdout)
            self.assertNotIn("E (budget", r.stdout)

    def test_torn_rows_do_not_break_merge_or_run_one(self):
        with tempfile.TemporaryDirectory() as t:
            d = os.path.join(t, "a")
            os.makedirs(os.path.join(d, "raw"))
            for c in "ABC":
                open(os.path.join(d, "raw", c + ".jsonl"), "w").write('{"file":"1.stl"}\n{"file":"2.st')
            open(os.path.join(d, "wall.tsv"), "w").write("1.stl\tA\t1.0\n2.stl\tA")
            ad.cmd_merge([os.path.join(t, "m"), d])
            self.assertEqual(len(open(os.path.join(t, "m", "raw", "A.jsonl")).readlines()), 1)
            fake = os.path.join(t, "bench")
            os.makedirs(fake)
            open(os.path.join(fake, "run.sh"), "w").write('#!/bin/bash\necho "Unhandled exception"\n')
            os.chmod(os.path.join(fake, "run.sh"), 0o755)
            row, _ = ad.run_one("/x/9.stl", "A", 5, fake)
            self.assertEqual(json.loads(row), {"file": "9.stl", "status": "timeout_or_crash"})

    def test_resume_skips_done_rows_and_torn_line(self):
        with tempfile.TemporaryDirectory() as t:
            p = os.path.join(t, "A.jsonl")
            open(p, "w").write('{"file":"1.stl"}\n{"file":"2.st')
            self.assertEqual(ad.done_files(p), {"1.stl"})


if __name__ == "__main__":
    unittest.main()
