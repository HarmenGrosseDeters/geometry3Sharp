# Summarizes results/<tag>.jsonl: one table per case; volume cases against the numpy reference
# (reference.py, cached in results/reference_cache.json).
# Usage: python3 analyze.py results/all.jsonl
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reference

ARGS = ["before", "after", "grid", "points", "win", "coords", "variant", "mode", "terrain", "d", "simplify",
        "heightcheck", "tol", "cell", "src", "tf", "res", "threshold"]
here = os.path.dirname(os.path.abspath(__file__))


def label(r):
    return " ".join(f"{k}={r[k]}" for k in ARGS if k in r)


def f(v, fmt="{:.3g}"):
    if v is None: return "-"
    if isinstance(v, bool): return "yes" if v else "no"
    if isinstance(v, (int, float)): return fmt.format(v)
    return str(v)


def table(title, rows, cols):
    if not rows: return
    print(f"\n## {title}\n")
    print("| case | " + " | ".join(c[0] for c in cols) + " |")
    print("|---|" + "---|" * len(cols))
    for r in rows:
        print(f"| {label(r)} | " + " | ".join(f(c[1](r)) for c in cols) + " |")


def main(path):
    rows = [json.loads(l) for l in open(path) if l.strip()]
    bad = [r for r in rows if r.get("status") != "ok"]
    print(f"{len(rows)} runs, {len(bad)} with exception / timeout / crash")
    for r in bad:
        print(f"- {r.get('case')} {label(r) or r.get('args')}: {r.get('status')} {r.get('stage', '')} {r.get('error', '')} {r.get('trace', '')}")
    ok = [r for r in rows if r.get("status") == "ok"]
    by = lambda c: [r for r in ok if r["case"] == c]

    cache_path = os.path.join(os.path.dirname(path), "reference_cache.json")
    cache = json.load(open(cache_path)) if os.path.exists(cache_path) else {}
    for r in by("volume"):
        key = f"{r['before']}|{r['after']}|{r['win']}"
        if key not in cache:
            cache[key] = reference.reference(os.path.join(here, "data"), r["before"], r["after"], [float(v) for v in r["win"].split(",")])
            json.dump(cache, open(cache_path, "w"), indent=1)
        ref = cache[key]
        r["_ref"] = ref.get("exact") or ref.get("sampled")
        r["_raster"] = ref.get("raster")
    rel = lambda a, b: abs(a - b) / max(abs(b), 1e-300)
    table("TerrainVolumeDifference vs reference (exact: same grid; sampled: 0.125 m midpoint rule)", by("volume"), [
        ("tris before+after", lambda r: r["before_tris"] + r["after_tris"]),
        ("time s", lambda r: r["volume_s"]), ("peak MB", lambda r: r["peak_mb"]), ("ok", lambda r: r["ok"]),
        ("cut m³", lambda r: r["cut"]), ("fill m³", lambda r: r["fill"]),
        ("rel err cut", lambda r: rel(r["cut"], r["_ref"]["cut"])), ("rel err fill", lambda r: rel(r["fill"], r["_ref"]["fill"])),
        ("rel err area", lambda r: rel(r["overlap_area"], r["_ref"]["overlap_area"])),
        ("raster DoD net vs ref", lambda r: rel(r["_raster"]["net"], r["_ref"]["net"]) if r["_raster"] else None),
    ])
    table("Repair of grid TINs (MeshAutoRepair / MeshRobustRepair)", by("repair"), [
        ("tris", lambda r: r["before_tris"]), ("time s", lambda r: r["repair_s"]), ("ok", lambda r: r.get("ok")),
        ("bdry edges before→after", lambda r: f"{r['before_boundary_edges']}→{r['after_boundary_edges']}"),
        ("tris after", lambda r: r["after_tris"]), ("closed", lambda r: r["after_closed"]),
        ("down-facing area m²", lambda r: r["after_footprint_down"]),
        ("XY overlaps after", lambda r: r["after_xy_overlaps"]),
        ("top dz p50 m", lambda r: r.get("after_dz_p50")), ("top dz max m", lambda r: r.get("after_dz_max")),
        ("samples off >1cm", lambda r: r.get("after_samples_dz_gt_1cm")), ("solidify", lambda r: r.get("used_solidify")),
    ])
    table("TerrainDualContouring (vertical error at the DGM nodes)", by("tdc"), [
        ("input", lambda r: r.get("input_tris") or r.get("points")), ("time s", lambda r: r["tdc_s"]),
        ("peak MB", lambda r: r["peak_mb"]), ("result tris", lambda r: r.get("result_tris")),
        ("dz rms m", lambda r: r.get("dz_rms")), ("dz p99 m", lambda r: r.get("dz_p99")), ("dz max m", lambda r: r.get("dz_max")),
        ("XY overlaps", lambda r: r.get("xy_overlaps")), ("NaN verts", lambda r: r.get("nan_verts")),
    ])
    table("TerrainOffset (distance of the result vertices to the terrain - |d|)", by("offset"), [
        ("time s", lambda r: r["offset_s"]), ("result tris", lambda r: r.get("result_tris")),
        ("checked", lambda r: r.get("checked_verts")), ("wrong side", lambda r: r.get("wrong_side")),
        ("err p01 m", lambda r: r.get("dist_err_p01")), ("err p99 m", lambda r: r.get("dist_err_p99")),
        ("err min m", lambda r: r.get("dist_err_min")), ("err max m", lambda r: r.get("dist_err_max")),
    ])
    table("TerrainSafetyLines (crests from TerrainBreakLines)", by("safety"), [
        ("terrain tris", lambda r: r["terrain_tris"]), ("crest km", lambda r: r["crest_length"] / 1000),
        ("time s", lambda r: r["safety_s"]), ("ok", lambda r: r["ok"]), ("line km", lambda r: r["line_length"] / 1000),
        ("h-dist - d min", lambda r: r.get("hdist_err_min")), ("h-dist - d max", lambda r: r.get("hdist_err_max")),
        ("surface too close", lambda r: r.get("surface_too_close")),
        ("surface - d p01", lambda r: r.get("surface_err_p01")), ("surface - d p50", lambda r: r.get("surface_err_p50")),
        ("excess p50", lambda r: r.get("excess_p50")), ("excess p99", lambda r: r.get("excess_p99")),
        ("excess >0.2 m", lambda r: f"{r['excess_gt_tol']}/{r['excess_n']}" if "excess_n" in r else None),
        ("max excess (segments)", lambda r: r.get("max_excess")), ("certified", lambda r: r.get("certified")),
        ("3D dist - d min", lambda r: r.get("euclid_err_min")),
        ("uncovered src", lambda r: f"{r['source_pts_uncovered']}/{r['source_pts']}"),
    ])
    table("MeshGeodesicDistance (heat method) vs surface Dijkstra", by("geodesic"), [
        ("verts", lambda r: r["verts"]), ("sources", lambda r: r["sources"]), ("time s", lambda r: r["geodesic_s"]),
        ("peak MB", lambda r: r["peak_mb"]), ("ok", lambda r: r["ok"]),
        ("rel err p01", lambda r: r.get("rel_err_p01")), ("rel err p50", lambda r: r.get("rel_err_p50")),
        ("rel err p99", lambda r: r.get("rel_err_p99")), ("below 3D distance", lambda r: r.get("below_euclid_beyond_accuracy")),
    ])
    table("ClothSimulationFilter vs LAS classes (ground = 2, non-ground = 1)", by("csf"), [
        ("points", lambda r: r["points"]), ("time s", lambda r: r["csf_s"]), ("peak MB", lambda r: r["peak_mb"]),
        ("type I", lambda r: r.get("type1")), ("type II", lambda r: r.get("type2")), ("total", lambda r: r.get("total_error")),
        ("kappa", lambda r: r.get("kappa")),
        ("class 20 as ground", lambda r: r.get("class20_as_ground", 0) / max(1, r.get("class20_n", 0))),
        ("accepted >0.5 m above DGM", lambda r: r.get("accepted_above_dgm_0_5", 0) / max(1, r.get("accepted", 0))),
    ])


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "results/all.jsonl")
