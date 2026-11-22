import contextlib, io, json, os, sys, tempfile, unittest
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "perf"))
import seam_levers_report as rep


def boolean(half, ms, phases, cores=1, op="union", tris=1000, **more):
    p = {k: 0 for k in rep.TOP}
    p.update({"intersections.trees": 0, "intersections.pairs": 0})
    p.update(phases)
    r = {"part": "boolean", "case": f"tubes:8:{half}", "half": half, "op": op, "cores": cores, "input_triangles": tris, "triangles": tris,
         "phases_ms": p, "best_ms": ms, "ms": [ms + 5, ms], "alloc_mb": 1024.0, "gc": {"pause_ms": 10}, "hash": f"h{half}{op}"}
    r.update(more)
    return r


def run(command, *row_sets):
    with tempfile.TemporaryDirectory() as d:
        paths = []
        for i, rows in enumerate(row_sets):
            paths.append(os.path.join(d, f"r{i}.jsonl"))
            with open(paths[-1], "w") as f:
                f.write("\n".join(json.dumps(r) for r in rows) + "\n")
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            command(paths)
        return out.getvalue()


class SeamLeversReportTests(unittest.TestCase):
    def test_phase_sums_rest_and_microseconds(self):
        rows = [boolean(3, 100, {"setup": 10, "rounding": 50, "assembly.vertices_us": 4000}), boolean(12, 200, {"setup": 30, "rounding": 120})]
        sums, total = rep.phase_sums(rows)
        self.assertEqual(total, 300)
        self.assertEqual(sums["setup"], 40)
        self.assertEqual(sums["assembly.vertices_us"], 4.0)                 # microseconds become ms
        self.assertEqual(sums["rest (outside the timers)"], 300 - 40 - 170)  # sub-steps do not count twice

    def test_best_is_the_fastest_run_of_a_profile_row(self):
        row = boolean(3, 100, {})
        del row["best_ms"]                       # the rows of `profile` have only "ms"
        row["ms"] = [130, 90, 110]
        self.assertEqual(rep.best(row), 90)
        self.assertEqual(rep.phase_sums([row])[1], 90)

    def test_phases_speedup_and_hash_drift(self):
        one = [boolean(3, 400, {"rounding": 200})]
        four = [boolean(3, 200, {"rounding": 50}, cores=4)]
        text = run(rep.phases, one, four)
        self.assertIn("1 thr: allocated 1.0 GB, GC pauses 10 ms (2.5 %)", text)
        self.assertIn("4 thr: allocated 1.0 GB, GC pauses 10 ms (5.0 %)", text)
        self.assertIn("| rounding | 200 | 50 | 4.00 | 50.0 % | 25.0 % |", text)
        self.assertIn("| **total** | 400 | 200 | 2.00 |", text)
        self.assertIn("differ between the two files: 0", text)
        four[0]["hash"] = "other"
        self.assertIn("differ between the two files: 1", run(rep.phases, one, four))

    def test_seam_far_work_and_cost_per_far_triangle(self):
        rows = [boolean(3, 100, {"rounding": 40, "setup": 7}, tris=1000), boolean(48, 500, {"rounding": 240, "setup": 7, "simplify": 50, "intersections": 60, "intersections.trees": 20, "intersections.pairs": 30}, tris=101000)]
        rows[1]["triangles"] = 5000                                                # the result is not the input
        text = run(rep.seam, rows)
        self.assertIn("| 1 | union | 48 | 101000 | 5000 | 500 | 80 % |", text)     # 1 - 100 / 500
        self.assertIn("| 1 | union | 3 | 1000 | 1000 | 100 | 0 % |", text)
        self.assertIn("4.00 us", text)                                             # 400 ms over 100 000 far input triangles
        self.assertIn("| 2.00 |", text)                                            # the rounding's part of it
        self.assertIn("| 240 | 193 |", text)                                       # rest: 500 - 240 - 7 - 60 (the phase totals, not their sub-steps)

    def test_selfjoin_share(self):
        arr = {"part": "arrangement", "case": "c", "half": 0, "set": "AB", "cores": 1, "input_triangles": 10, "self_ms": 80.0, "noself_ms": 20.0,
               "same_pieces": True, "self_intersecting_pairs": 0}
        text = run(rep.selfjoin, [arr, dict(arr, self_ms=150.0, noself_ms=50.0, same_pieces=False)])
        self.assertIn("| 1 | AB | 2 | 20 | 230 | 70 | 160 | 70 % | 1 of 2 | 0 |", text)

    def test_local_rounding_saving(self):
        local = {"result_triangles": 100, "new_triangles": 10, "full_build_ms": 30.0, "full_query_ms": 10.0, "local_build_ms": 1.0, "local_join_ms": 1.0,
                 "local_scan_ms": 2.0, "full_pairs": 7, "local_pairs": 7, "same_pairs": True}
        contacts = {"vertices": 50, "dictionary_ms": 6.0, "flat_ms": 2.0, "same_count": True, "contacts": 0}
        rows = [boolean(0, 400, {"rounding": 72}, local_rounding=local, contacts=contacts)]
        text = run(rep.local, rows)
        self.assertIn("| 40 | 4 | 36 | 50 % | 9.0 % | 1 of 1 | 6 | 2 |", text)
        self.assertIn("search structure 40 -> 4 ms (-9.0 % of the booleans); contacts 6 -> 2 ms (-1.0 %), counts equal in 1 of 1", text)
        bad = boolean(0, 400, {"rounding": 72}, local_rounding=dict(local, same_pairs=False), contacts=dict(contacts, same_count=False))
        text = run(rep.local, rows + [bad])
        self.assertIn("| 1 of 2 |", text)
        self.assertIn("counts equal in 1 of 2", text)

    def test_tree_sums_and_local_set(self):
        def case(i, near, tris):
            base = {"case": f"c{i}", "half": 0, "cores": 1, "input_triangles": tris}
            return [dict(base, part="traversal", tree_build_ms=10.0, self_traversal_ms=30.0, self_box_pairs=100, cross_query_ms=5.0, cross_box_hits=7),
                    dict(base, part="near", near_a=near, near_b=0, triangles_a=tris // 2, triangles_b=tris // 2, near_query_ms=2.0),
                    dict(base, part="stamp", mix64_ms=1.0, sha256_ms=3.0)]
        rows = case(0, 10, 1000) + case(1, 100, 1000) + case(2, 600, 2000)
        profile = [dict(boolean(0, 500, {}), case=f"c{i}", op=op) for i in range(3) for op in ("union", "difference")]
        text = run(rep.tree, rows, profile)
        self.assertIn("3 cases, 4000 input triangles", text)
        self.assertIn("tree build 30 ms, self-join traversal 90 ms (300 box pairs), A in B queries 15 ms (21 hits)", text)
        self.assertIn("local set 710 triangles = 17.8 % of the input (per case: median 10.0 %, p10 1.0 %, p90 30.0 %), found in 6 ms", text)
        self.assertIn("content stamp: 64-bit mix 3 ms, SHA-256 9 ms", text)
        self.assertIn("(3000 ms, 2 operations per case): the input traversals are 7.0 % of the time", text)   # 2 * 105 / 3000


if __name__ == "__main__":
    unittest.main()
