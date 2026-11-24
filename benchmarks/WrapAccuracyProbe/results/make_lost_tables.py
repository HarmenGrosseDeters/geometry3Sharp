"""Tables of the `lost` mode (issue #1479) from its JSONL files: python make_lost_tables.py lost_5.jsonl lost_16.jsonl > tables_lost.md
Ids and numbers only."""
import json, sys

rows = []
for f in sys.argv[1:]:
    for line in open(f):
        rows.append(json.loads(line))
rows.sort(key=lambda r: -r["regions"].get("lost_area_share_of_input", 0))

def g(x, p=3):
    return "-" if x is None else ("%.*f" % (p, x) if isinstance(x, float) else str(x))

def stage(r, name):
    for s in r["stages"]:
        if s["stage"] == name:
            return s
    return {}

print("## Robust result: path, lost and added share (area of the input / of the result farther than tau * diagonal from the other)\n")
print("| id | tris | path | repair IoU | lost 1e-4 | lost 4e-4 | lost 1e-3 | lost 4e-3 | added 4e-4 | added 4e-3 | area ratio |")
print("|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|")
for r in rows:
    s = stage(r, "robust"); rb = r["robust"]
    print("| %s | %d | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (r["input"], r["structure"]["tris"], rb["path"], g(rb["repair_volume_iou"], 4),
          g(s.get("lost@0.0001")), g(s.get("lost@0.0004")), g(s.get("lost@0.001")), g(s.get("lost@0.004")), g(s.get("added@0.0004")), g(s.get("added@0.004")), g(s.get("area_ratio"))))

names = ["robust", "auto", "resolve_input", "resolve_auto", "solidify_input", "solidify_auto", "wrap_robust_defaults", "wrap_d100_1e-4", "robust_MinVolumeIoU0", "robust_NoExactResolve"]
print("\n## Lost / added share at tau = 4e-4 * diagonal per stage on its own (blank: stage gave no mesh)\n")
print("| id | " + " | ".join(names) + " |")
print("|---|" + "---:|" * len(names))
for r in rows:
    cells = []
    for n in names:
        s = stage(r, n)
        cells.append("-" if "lost@0.0004" not in s else "%s / %s%s" % (g(s["lost@0.0004"], 3), g(s["added@0.0004"], 3), "" if s.get("verdict") == "clean" else " (" + str(s.get("verdict")) + ")"))
    print("| %s | %s |" % (r["input"], " | ".join(cells)))

print("\n## Structure of the input\n")
print("| id | raw facets / tris | components (closed) | boundary loops | bowties | self-int pairs | winding box: ~0 / (0.1-0.9) / ~1 / ~2+ | skin / interior / outside / ambiguous surface | doubled <= 2 tau / 20 tau |")
print("|---|---|---|---:|---:|---:|---|---|---|")
for r in rows:
    s = r["structure"]; h = [float(x) for x in s["wn_box_hist(<-.1,~0,(.1-.9),~1,(1.1-1.9),~2,>2.1)"].split(",")]
    print("| %s | %s / %s | %s (%s) | %s | %s | %s | %.2f / %.2f / %.2f / %.2f (neg %.2f) | %.2f / %.2f / %.2f / %.2f | %.2f / %.2f |" % (
        r["input"], s["raw_stl_facets"], s["tris"], s["components"], s["components_closed"], s["boundary_loops"], s["bowties"], s["selfint_pairs_at_most_1e6"],
        h[1], h[2], h[3], h[5] + h[4] + h[6], h[0], s["surface_skin_share"], s["surface_interior_share"], s["surface_outside_share"], s["surface_ambiguous_share"],
        s["doubled_share_within_2tau"], s["doubled_share_within_20tau"]))

print("\n## Where the lost area is (robust result, tau = 4e-4 * diagonal; shares of the LOST area unless named)\n")
print("| id | lost / input | regions | whole components lost (share of lost) | skin | interior wall | outside sheet | ambiguous | inside the result's solid | visible (>= 10 % of rays) | lost / visible surface | lost / skin | lost outside the result's solid / input |")
print("|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
for r in rows:
    o = r["regions"]
    if o["lost_tris"] == 0:
        print("| %s | 0 | 0 | - | - | - | - | - | - | - | 0 | 0 | 0 |" % r["input"]); continue
    print("| %s | %s | %d | %d (%s) | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (r["input"], g(o["lost_area_share_of_input"]), o["regions"], o["lost_whole_components"], g(o["lost_whole_components_area_share_of_lost"], 2),
          g(o["lost_skin_share_of_lost"], 2), g(o["lost_interior_share_of_lost"], 2), g(o["lost_outside_sheet_share_of_lost"], 2), g(o["lost_ambiguous_share_of_lost"], 2),
          g(o["lost_inside_result_solid_share_of_lost"], 2), g(o["lost_visible_ge10pct_share_of_lost"], 2), g(o["lost_share_of_visible_surface"], 4), g(o["lost_share_of_skin"], 4), g(o["lost_area_share_of_input"] * (1 - o["lost_inside_result_solid_share_of_lost"]), 4)))

print()
print("## Solid of the result against the input (share of the input's box with winding number > 0.5)")
print()
print("| id | input w>0.5 | robust | auto | robust_MinVolumeIoU0 | solidify_input | iou(robust) | iou(auto) |")
print("|---|---:|---:|---:|---:|---:|---:|---:|")
for r in rows:
    if "input_solid_share_of_box_w>0.5" not in r["structure"]:
        continue
    print("| %s | %s | %s | %s | %s | %s | %s | %s |" % (r["input"], g(r["structure"]["input_solid_share_of_box_w>0.5"]), g(stage(r, "robust").get("solid_share_of_input_box")), g(stage(r, "auto").get("solid_share_of_input_box")),
          g(stage(r, "robust_MinVolumeIoU0").get("solid_share_of_input_box")), g(stage(r, "solidify_input").get("solid_share_of_input_box")), g(stage(r, "robust").get("iou_vs_input_solid"), 4), g(stage(r, "auto").get("iou_vs_input_solid"), 4)))
