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
            # --only: just the named chains (the RC4 rerun of a few chains)
            only = os.path.join(d, "o.jsonl")
            with contextlib.redirect_stdout(io.StringIO()):
                ac.cmd_manifest(d, out, only, None, None, ["iud_p0003", "dui_p0001"])
            picked = [json.loads(l)["name"] for l in open(only)]
        self.assertEqual(len(cases), 5)
        self.assertTrue(all(c["variant"] == "dui" and len(c["chain"]) == 11 for c in cases))
        self.assertTrue(cases[0]["name"].startswith("dui_p0000_"))
        self.assertEqual(sorted(n.split("_")[0] + "_" + n.split("_")[1] for n in picked), ["dui_p0001", "iud_p0003"])

    def test_plans_chain_the_result_of_the_previous_step(self):
        for v, plan in ac.PLANS.items():
            self.assertEqual(len(plan), 3)
            self.assertNotIn("R", plan[0][1:])                  # the first step has only placed inputs
            self.assertEqual(plan[1][1], "R")
            self.assertTrue(any("R" in step[1:] for step in plan[1:]))
            self.assertTrue(all(op in ac.OPS for op, _, _ in plan))

class RunTests(unittest.TestCase):
    def manifest(self, d, names):
        path = os.path.join(d, "m.jsonl")
        with open(path, "w") as f:
            for n in names:
                f.write(json.dumps({"class": ac.CLASS, "name": n, "variant": "ucd", "chain": ["a", "b", "c", "face-contact", "2", "1", "0", "face-contact", "2", "-1", "0"]}) + "\n")
        return path

    def test_finished_chains_are_skipped_and_a_cut_off_last_line_is_ended(self):
        with tempfile.TemporaryDirectory() as d:
            out = os.path.join(d, "r.jsonl")
            with open(out, "w") as f:
                f.write(json.dumps({"file": "ucd_p0000_a+b+c", "method": "ours", "step": 0}) + '\n{"file": "ucd_p0001')   # cut off by an interrupted run
            with contextlib.redirect_stdout(io.StringIO()) as buf:
                ac.cmd_run(self.manifest(d, ["ucd_p0000_a+b+c"]), out, ["ours"], 10, budget_s=0)
            self.assertIn("1 chains finished, 0 to run", buf.getvalue())
            self.assertTrue(open(out).read().endswith("\n"))

    def test_budget_stops_before_the_next_chain_and_keeps_finished_ones(self):
        with tempfile.TemporaryDirectory() as d:
            out = os.path.join(d, "r.jsonl")
            with contextlib.redirect_stdout(io.StringIO()) as buf:
                ac.cmd_run(self.manifest(d, ["ucd_p0000_a+b+c", "ucd_p0001_d+e+f"]), out, ["ours"], 10, budget_s=-1)
            self.assertIn("2 chains not run", buf.getvalue())
            self.assertFalse(os.path.exists(out) and open(out).read())

class SummaryTests(unittest.TestCase):
    def step(self, name, k, op, reason="", method="ours", status="ok", removed=None, ie=1e-15, ms=None, tris=10):
        r = {"class": ac.CLASS, "file": name, "method": method, "step": k, "op": op, "variant": "ucd", "ie_error": ie, "diff_error": 0.0,
             f"{op}_status": status, f"{op}_clean_reason": reason, f"{op}_ms": 10 + k if ms is None else ms, "input": {"inputs_clean": True},
             "result_tris": tris}
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
                self.step("ucd_p0001_d+e+f", 0, "union"), self.step("ucd_p0001_d+e+f", 1, "difference", reason="not closed", ie=2e-6),   # chain stops here (the result of this step is not empty: a failure)
                self.step("ucd_p0003_j+k+l", 0, "union", tris=0, reason="empty"),                                                           # an empty result ends a chain: expected
                {"class": ac.CLASS, "file": "ucd_p0002_g+h+i", "method": "export", "error": "RuntimeError('inexact transform: B: x')"}]
        out = self.summary(rows)
        cells = [l.split("|") for l in out.splitlines() if l.startswith("| ucd")][0]
        self.assertEqual(cells[3].strip(), "3")                      # chains
        self.assertEqual(cells[4].strip(), "3/2/1")                  # steps run
        self.assertEqual(cells[5].strip(), "3/1/1")                  # chosen result clean (or empty)
        self.assertEqual(cells[6].strip(), "1")                      # all steps clean
        self.assertEqual(cells[7].strip(), "1 / 1")                  # ended early: empty result (p0003) / failed (p0001)
        self.assertIn("not closed 1", cells[8])
        self.assertEqual(cells[9].strip().split("/")[1], "2.0e-06")  # identity error of step 2
        self.assertEqual(cells[10].strip(), "3")                     # removed components of the chosen results
        self.assertIn("ucd_p0002_g+h+i", out)                        # refused chain listed

    def test_removed_components_count_only_the_chosen_result(self):
        r = self.step("ucd_p0000_a+b+c", 0, "union", removed=2)
        r["intersection_removed_components"] = 40                    # an operation of the step's identities that nobody chains on
        cells = [l.split("|") for l in self.summary([r]).splitlines() if l.startswith("| ucd")][0]
        self.assertEqual(cells[10].strip(), "2")

    def test_a_timeout_is_not_a_fast_run(self):
        rows = [self.step("ucd_p0000_a+b+c", 0, "union", ms=100, tris=0), self.step("ucd_p0001_d+e+f", 0, "union", ms=300, tris=0),
                self.step("ucd_p0002_g+h+i", 0, "union", ms=0, status="timeout", tris=-1)]
        cells = [l.split("|") for l in self.summary(rows).splitlines() if l.startswith("| ucd")][0]
        self.assertEqual(cells[11].strip().split("/")[0], "200")      # the median of 100 and 300, the timeout is not a 0
        self.assertIn("timeout 1", cells[8])
        self.assertEqual(cells[7].strip(), "2 / 1")                   # two empty results (expected), a timeout (a failure)

    def test_runner_errors_are_listed_and_count_as_failed_chains(self):
        rows = [self.step("ucd_p0000_a+b+c", 0, "union"),
                {"class": ac.CLASS, "file": "ucd_p0000_a+b+c", "method": "cgal_epeck", "runner_error": "TimeoutExpired('chain-step', 3600)"}]
        out = self.summary(rows)
        self.assertIn("Runner errors", out)
        self.assertIn("ucd_p0000_a+b+c cgal_epeck: TimeoutExpired", out)
        cells = [l.split("|") for l in out.splitlines() if l.startswith("| ucd") and "cgal_epeck" in l][0]
        self.assertEqual(cells[7].strip(), "0 / 1")
        self.assertNotIn("Chains refused", out)                        # it is not an inexact placement

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

def unwelded_cube_obj(path, jitter=0.0, side=1.0):
    """a cube as ABC writes tessellations: one quad per face with its own four vertices (24 vertices), normals, `f a//n ...` indices"""
    corners = [(x, y, z) for x in (0, side) for y in (0, side) for z in (0, side)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]      # outward orientation
    lines, n = [], 0
    for fi, face in enumerate(faces):
        for k, c in enumerate(face):
            x, y, z = corners[c]
            lines.append(f"v {x + jitter * ((fi + k) % 3)!r} {y!r} {z!r}")
        lines.append("vn 0 0 1")
    for fi in range(6):
        b = 4 * fi + 1
        lines.append(f"f {b}//{fi + 1} {b + 1}//{fi + 1} {b + 2}//{fi + 1} {b + 3}//{fi + 1}")
    with open(path, "w") as f:
        f.write("\n".join(lines) + "\n")

def f32_of(x):
    import struct
    return struct.unpack("f", struct.pack("f", x))[0]

class WeldTests(unittest.TestCase):
    def test_read_obj_handles_face_formats_negative_indices_and_polygons(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "m.obj")
            open(path, "w").write("# c\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nvn 0 0 1\nvt 0 0\nf 1/1/1 2/2/1 3/3/1 4/4/1\nf -4//1 -3//1 -2\n")
            v, t = ac.read_obj(path)
        self.assertEqual(len(v), 4)
        self.assertEqual(t, [(0, 1, 2), (0, 2, 3), (0, 1, 2)])         # a quad is a fan of two, -4 is the first vertex

    def test_exact_weld_merges_equal_positions_only(self):
        verts = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (-0.0, 0.0, 0.0), (1.0 + 1e-12, 0.0, 0.0), (1.0, 1.0, 1.0)]
        v, t = ac.weld(verts, [(0, 1, 4), (2, 3, 4)], 0.0)
        self.assertEqual(len(v), 4)                                      # 0 and -0.0 are one position, 1 and 1 + 1e-12 are two
        self.assertEqual(t, [(0, 1, 3), (0, 2, 3)])

    def test_tolerance_weld_merges_within_the_cell_and_keeps_the_first_vertex(self):
        verts = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0 + 1e-12, 0.0, 0.0), (1.0, 1.0, 1.0)]
        v, t = ac.weld(verts, [(0, 1, 3), (0, 2, 3)], 1e-9)
        self.assertEqual(len(v), 3)
        self.assertEqual(v[1], (1.0, 0.0, 0.0))                          # the first vertex of the cell
        self.assertEqual(t, [(0, 1, 2), (0, 1, 2)])

    def test_tolerance_weld_meets_across_a_cell_boundary(self):
        # 0.0 and -1e-17 are on both sides of a grid line at 0: a grid hash alone would keep them apart
        v, t = ac.weld([(0.0, 0.0, 0.0), (-1e-17, 0.0, 0.0), (1.0, 1.0, 1.0)], [(0, 1, 2)], 1e-9)
        self.assertEqual(len(v), 2)
        self.assertEqual(t, [(0, 0, 1)])

    def test_tolerance_weld_is_not_transitive_and_moves_by_at_most_the_tolerance(self):
        diag = (1.0 + 1.0 + 1.0) ** 0.5
        tol = 1e-3
        step = 0.8 * tol * diag
        verts = [(0.0, 0.0, 0.0), (step, 0.0, 0.0), (2 * step, 0.0, 0.0), (1.0, 1.0, 1.0)]
        v, t = ac.weld(verts, [(0, 1, 2), (1, 2, 3)], tol)
        self.assertEqual(len(v), 3)                                  # the first two meet, the third is 1.6 steps from the first: its own vertex
        self.assertEqual(t[0], (0, 0, 1))
        for orig, new in zip(verts, [v[0], v[0], v[1], v[2]]):
            self.assertLessEqual(sum((a - b) ** 2 for a, b in zip(orig, new)) ** 0.5, tol * diag * (1 + 1e-12))

    def test_read_obj_reads_comments_tabs_colours_and_crlf_and_rejects_bad_indices(self):
        with tempfile.TemporaryDirectory() as d:
            ok = os.path.join(d, "ok.obj")
            open(ok, "w", newline="").write("v\t0 0 0 1 0 0 # colour\r\nv 1 0 0\r\nv 1 1 0\r\ng part\r\nusemtl m\r\nl 1 2\r\nf\t1 2 3 # c\r\n")
            v, t = ac.read_obj(ok)
            self.assertEqual((len(v), t), (3, [(0, 1, 2)]))
            for name, text in {"zero": "v 0 0 0\nv 1 0 0\nv 1 1 0\nf 0 1 2\n", "range": "v 0 0 0\nv 1 0 0\nv 1 1 0\nf 1 2 99\n",
                               "negative": "v 0 0 0\nv 1 0 0\nv 1 1 0\nf -9 1 2\n", "short": "v 1 2\n", "text": "v 0 0 0\nv 1 0 0\nv 1 1 0\nf 1 2 x\n"}.items():
                path = os.path.join(d, name + ".obj")
                open(path, "w").write(text)
                with self.assertRaises(ValueError, msg=name):
                    ac.read_obj(path)

    def test_snap_rounds_to_single_precision_and_flushes_tiny_coordinates(self):
        import struct
        f32 = lambda x: struct.unpack("f", struct.pack("f", x))[0]
        v = ac.snap([(0.1, 1 / 3, 2.5), (-1.0, 0.0, 1.0)])
        self.assertEqual(v[0], (0.10000000149011612, f32(1 / 3), 2.5))   # 0.1 is not a single precision value: its neighbour is used
        self.assertNotEqual(0.1, v[0][0])
        self.assertEqual(v[1], (-1.0, 0.0, 1.0))                        # a large negative coordinate is not "tiny"
        self.assertEqual(ac.snap([]), [])
        self.assertEqual(ac.snap([(0.0, 0.0, 0.0), (0.0, 0.0, 0.0)]), [(0.0, 0.0, 0.0)] * 2)      # extent 0: nothing to flush
        self.assertEqual(repr(ac.snap([(-0.0, 0.0, 1.0), (1.0, 1.0, 1.0)])[0][0]), "-0.0")        # a negative zero stays (it is not nonzero)

    def test_the_flush_threshold_is_2_to_the_minus_26_of_the_extent(self):
        extent = 4.0                                                    # the extent of the points below along x
        tiny = extent * 2.0 ** -26
        inside = ac.snap([(0.0, 0.0, 0.0), (extent, 0.0, 0.0), (0.0, 0.9 * tiny, 0.0), (0.0, 0.0, -0.9 * tiny)])
        self.assertEqual((inside[2][1], inside[3][2]), (0.0, 0.0))     # below the threshold: flushed, positive and negative
        outside = ac.snap([(0.0, 0.0, 0.0), (extent, 0.0, 0.0), (0.0, 1.1 * tiny, 0.0), (0.0, 0.0, -1.1 * tiny)])
        self.assertNotEqual(outside[2][1], 0.0)
        self.assertNotEqual(outside[3][2], 0.0)                         # above: kept
        self.assertEqual(ac.snap([(0.0, 0.0, 0.0), (extent, 0.0, 0.0), (0.0, 0.9 * tiny, 0.0)], tiny_exponent=40)[2][1], f32_of(0.9 * tiny))   # the parameter works

    def test_non_finite_coordinates_are_an_error_and_a_value_beyond_single_precision_overflows(self):
        for bad in (float("inf"), float("-inf"), float("nan")):
            with self.assertRaises(ValueError):
                ac.snap([(0.0, 0.0, 0.0), (bad, 1.0, 1.0)])
        with self.assertRaises(OverflowError):
            ac.snap([(1e39, 0.0, 0.0)])

    def test_snap_distortion_is_relative_to_the_extent(self):
        verts = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.1, 0.0, 0.0)]
        self.assertAlmostEqual(ac.snap_distortion(verts, ac.snap(verts)), abs(0.1 - 0.10000000149011612), places=15)      # extent 1
        self.assertEqual(ac.snap_distortion([], []), 0.0)
        self.assertEqual(ac.snap_distortion([(1.0, 1.0, 1.0)], [(1.0, 1.0, 1.0)]), 0.0)
        far = [(1e6 + 0.1, 0.0, 0.0), (1e6 + 1.1, 0.0, 0.0)]            # far from the origin: single precision rounds by 0.0x, a share of the extent 1
        self.assertGreater(ac.snap_distortion(far, ac.snap(far)), 1e-3)

    def test_collapsed_triangles_are_kept_for_the_filter_to_report(self):
        v, t = ac.weld([(0.0, 0.0, 0.0), (0.0, 0.0, 0.0), (1.0, 0.0, 0.0)], [(0, 1, 2)], 0.0)
        self.assertEqual(t, [(0, 0, 1)])

    def test_written_off_roundtrips_the_doubles(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "m.off")
            v = [(0.1, 1 / 3, 1e-17), (1.0, 2.0, 3.0), (0.0, 0.5, 0.25)]
            ac.write_off(p, v, [(0, 1, 2)])
            lines = open(p).read().splitlines()
        self.assertEqual(lines[0], "OFF")
        self.assertEqual([tuple(float(x) for x in l.split()) for l in lines[2:5]], v)
        self.assertEqual(lines[5], "3 0 1 2")

@unittest.skipUnless(os.path.exists(DLL), "build benchmarks/BooleanComparison first")
class PrepareEndToEndTests(unittest.TestCase):
    def prepare(self, jitter, tolerances=None, name="cube"):
        d = tempfile.mkdtemp(prefix="abc_prepare_test_")
        os.makedirs(os.path.join(d, "meshes"))
        unwelded_cube_obj(os.path.join(d, "meshes", name + ".obj"), jitter=jitter)
        old = ac.CLEAN_TRIS
        ac.CLEAN_TRIS = (1, 100)
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                result = ac.cmd_prepare(d, os.path.join(d, "clean.csv"), tolerances, min_clean=1)
        finally:
            ac.CLEAN_TRIS = old
        return d, result

    def test_an_unwelded_cube_is_not_closed_as_read_and_clean_after_the_exact_weld(self):
        d, (tol, n) = self.prepare(0.0)
        try:
            self.assertEqual((tol, n), (0.0, 1))                      # the exact weld is enough: no tolerance used
            self.assertEqual(open(os.path.join(d, "weld_tol.txt")).read().strip(), "0")
            self.assertEqual(os.listdir(os.path.join(d, "meshes")), ["cube.off"])        # the obj is replaced by the welded OFF
            self.assertEqual(open(os.path.join(d, "clean.csv.clean")).read().splitlines(), ["id,tris", "cube,12"])
            self.assertEqual([x for x in os.listdir(d) if x.startswith("welded_")], [])
        finally:
            shutil.rmtree(d)

    def test_a_cube_with_copies_that_differ_by_1e_6_needs_the_tolerance(self):
        d, (tol, n) = self.prepare(1e-6)
        try:
            self.assertEqual((tol, n), (1e-5, 1))                     # exact, 1e-9, 1e-7: not closed (the copies 1e-6 apart stay apart); 1e-5 of the diagonal: closed
            self.assertIn("cube,12", open(os.path.join(d, "clean.csv.clean")).read())
        finally:
            shutil.rmtree(d)

    def test_copies_below_single_precision_are_one_vertex_at_the_exact_weld_thanks_to_the_snap(self):
        d, (tol, n) = self.prepare(1e-12)                              # 1e-12 is far below the resolution of a single precision value near 1
        try:
            self.assertEqual((tol, n), (0.0, 1))
        finally:
            shutil.rmtree(d)

    def test_the_first_tolerance_with_enough_models_stops_the_search_and_leaves_no_temporary_files(self):
        d, (tol, n) = self.prepare(0.0, tolerances=[0.0, 1e-9, 1e-7])
        try:
            self.assertEqual(tol, 0.0)
            self.assertEqual(sorted(os.listdir(d)), ["clean.csv", "clean.csv.clean", "meshes", "unreadable.txt", "weld_tol.txt"])   # no clean.csv.1e-09, no welded_*
        finally:
            shutil.rmtree(d)
        d, (tol, n) = self.prepare(1e-6, tolerances=[0.0, 1e-7, 1e-5])
        try:
            self.assertEqual(tol, 1e-5)
            self.assertEqual(sorted(os.listdir(d)), ["clean.csv", "clean.csv.clean", "meshes", "unreadable.txt", "weld_tol.txt"])   # out.0 and welded_0 are gone too
        finally:
            shutil.rmtree(d)

    def test_without_enough_clean_models_the_tolerance_with_the_most_is_used(self):
        d = tempfile.mkdtemp(prefix="abc_prepare_test_")
        try:
            os.makedirs(os.path.join(d, "meshes"))
            unwelded_cube_obj(os.path.join(d, "meshes", "a.obj"), jitter=1e-6)        # closes at 1e-5 and above
            unwelded_cube_obj(os.path.join(d, "meshes", "b.obj"), jitter=0.0)         # closes at every tolerance
            old = ac.CLEAN_TRIS
            ac.CLEAN_TRIS = (1, 100)
            try:
                with contextlib.redirect_stdout(io.StringIO()):
                    tol, n = ac.cmd_prepare(d, os.path.join(d, "clean.csv"), [0.0, 1e-5], min_clean=3)     # 3 never reached
            finally:
                ac.CLEAN_TRIS = old
            self.assertEqual((tol, n), (1e-5, 2))                       # 1 at the exact weld, 2 at 1e-5
            self.assertEqual(sorted(os.listdir(os.path.join(d, "meshes"))), ["a.off", "b.off"])
        finally:
            shutil.rmtree(d)

    def test_the_best_tolerance_is_kept_even_if_a_later_one_is_worse(self):
        d = tempfile.mkdtemp(prefix="abc_prepare_test_")
        try:
            os.makedirs(os.path.join(d, "meshes"))
            unwelded_cube_obj(os.path.join(d, "meshes", "a.obj"), jitter=1e-6)
            unwelded_cube_obj(os.path.join(d, "meshes", "b.obj"), jitter=0.0)
            old = ac.CLEAN_TRIS
            ac.CLEAN_TRIS = (1, 100)
            try:
                with contextlib.redirect_stdout(io.StringIO()):
                    tol, n = ac.cmd_prepare(d, os.path.join(d, "clean.csv"), [1e-5, 0.0], min_clean=3)    # 2 clean at 1e-5, then only 1 at the exact weld
            finally:
                ac.CLEAN_TRIS = old
            self.assertEqual((tol, n), (1e-5, 2))
            self.assertEqual(sorted(os.listdir(os.path.join(d, "meshes"))), ["a.off", "b.off"])
        finally:
            shutil.rmtree(d)

    def test_an_unreadable_file_is_skipped_and_listed_and_no_obj_is_an_error(self):
        d = tempfile.mkdtemp(prefix="abc_prepare_test_")
        try:
            os.makedirs(os.path.join(d, "meshes"))
            unwelded_cube_obj(os.path.join(d, "meshes", "good.obj"))
            open(os.path.join(d, "meshes", "bad.obj"), "w").write("v 0 0 0\nf 1 2 3\n")
            old = ac.CLEAN_TRIS
            ac.CLEAN_TRIS = (1, 100)
            try:
                with contextlib.redirect_stdout(io.StringIO()):
                    tol, n = ac.cmd_prepare(d, os.path.join(d, "clean.csv"), min_clean=1)
            finally:
                ac.CLEAN_TRIS = old
            self.assertEqual(n, 1)
            self.assertIn("bad:", open(os.path.join(d, "unreadable.txt")).read())
            self.assertEqual(os.listdir(os.path.join(d, "meshes")), ["good.off"])
            with self.assertRaises(SystemExit):                          # a second run: the obj files are gone
                ac.cmd_prepare(d, os.path.join(d, "clean2.csv"), min_clean=1)
        finally:
            shutil.rmtree(d)

    def test_full_precision_models_get_exact_placements_only_after_the_snap(self):
        # ABC's coordinates are text of full 53-bit doubles: a cube with such coordinates cannot be translated by a shift of its own size exactly
        import compare
        d = tempfile.mkdtemp(prefix="abc_snap_test_")
        try:
            os.makedirs(os.path.join(d, "meshes"))
            for name, off, side in (("a", 0.1234567890123456, 0.7312345678901234), ("b", 0.3141592653589793, 0.5718281828459045), ("c", 0.2718281828459045, 0.6180339887498949)):
                unwelded_cube_obj(os.path.join(d, "meshes", name + ".obj"), side=side)
                text = open(os.path.join(d, "meshes", name + ".obj")).read()
                shifted = "".join(  # a full precision offset on every vertex
                    f"v {float(l.split()[1]) + off!r} {float(l.split()[2]) + off / 3!r} {float(l.split()[3]) + off / 7!r}\n" if l.startswith("v ") else l + "\n"
                    for l in text.splitlines())
                open(os.path.join(d, "meshes", name + ".obj"), "w").write(shifted)
            case = lambda dd: {"class": ac.CLASS, "name": "ucd_test", "variant": "ucd",
                               "chain": [os.path.join(dd, "a.off"), os.path.join(dd, "b.off"), os.path.join(dd, "c.off"), "face-contact", "2", "1", "0", "face-contact", "2", "-1", "0"]}
            raw = os.path.join(d, "raw")
            os.makedirs(raw)
            for name in "abc":      # welded exactly, but not snapped
                ac.write_off(os.path.join(raw, name + ".off"), *ac.weld(*ac.read_obj(os.path.join(d, "meshes", name + ".obj")), 0.0))
            rows = ac.run_chain(case(raw), ["ours"], 300)
            self.assertEqual(rows[0]["method"], "export", rows)
            self.assertIn("inexact transform: B", rows[0]["error"])
            old = ac.CLEAN_TRIS
            ac.CLEAN_TRIS = (1, 100)
            try:
                with contextlib.redirect_stdout(io.StringIO()):
                    tol, n = ac.cmd_prepare(d, os.path.join(d, "clean.csv"), [0.0], min_clean=3)
            finally:
                ac.CLEAN_TRIS = old
            self.assertEqual(n, 3)
            rows = ac.run_chain(case(os.path.join(d, "meshes")), ["ours"], 300)
            self.assertEqual([r.get("step") for r in rows], [0, 1, 2], rows)               # the snapped models take the exact placements
            self.assertTrue(rows[0]["chain_inputs"]["inputs_clean"])
        finally:
            shutil.rmtree(d)

    def test_a_model_no_tolerance_closes_leaves_no_clean_model(self):
        d, (tol, n) = self.prepare(0.3)                                # copies 0.3 apart: farther than any tolerance
        try:
            self.assertEqual(n, 0)
            self.assertEqual(open(os.path.join(d, "clean.csv.clean")).read().splitlines(), ["id,tris"])
        finally:
            shutil.rmtree(d)

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

    def test_each_method_runs_on_its_own_results_and_a_failing_method_keeps_the_others(self):
        # dui: A - B with B touching A only is A (not empty), then ∪ C (touching only), then ∩ A: every step has a result
        rows = ac.run_chain(self.case("dui", "face-contact", 0, "face-contact", 0), ["ours", "boom", "ours"], 300)
        ours = {r["step"]: r for r in rows if r["method"] == "ours" and "step" in r}
        self.assertEqual(sorted(ours), [0, 1, 2], rows)
        # step 2's input A is step 1's chosen result (the difference), step 3's input A is step 2's (the union)
        self.assertEqual(ours[1]["input"]["trisA"], ours[0]["difference_tris"])
        self.assertEqual(ours[2]["input"]["trisA"], ours[1]["union_tris"])
        self.assertEqual(ours[1]["input"]["trisB"], 12)                            # C is a cube
        self.assertEqual(ours[2]["input"]["trisB"], 12)                            # ... and the third step's B is the original A
        # an exception of one method (an unknown one: no command) is a runner error of that method, the rows of ours stay
        boom = [r for r in rows if r["method"] == "boom"]
        self.assertTrue(boom and all("runner_error" in r for r in boom), rows)
        self.assertNotIn("export", {r["method"] for r in rows})

    def test_the_chosen_operation_runs_first_and_the_others_only_if_it_worked(self):
        import compare
        calls, real = [], compare.run_one
        def fake(m, op, d, timeout):
            calls.append(op)
            return {"ok": False, "error": "boom", "status": "failed", "ms": 1} if op == "difference" else real(m, op, d, timeout)
        compare.run_one = fake
        try:
            rows = ac.run_chain(self.case("ucd", "face-contact", 0, "face-contact", 0), ["ours"], 300)
        finally:
            compare.run_one = real
        self.assertEqual(calls[:3], ["union", "intersection", "difference"])        # step 1: union chosen and ok, the others follow
        self.assertEqual(calls[3:], ["difference"])                                  # step 2: the chosen difference failed: nothing else, no step 3
        self.assertEqual(sorted(r["step"] for r in rows), [0, 1])
        self.assertEqual(rows[1]["difference_status"], "failed")

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
