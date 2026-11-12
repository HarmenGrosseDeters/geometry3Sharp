# The shared profile categories of docs/perf/EXTREME_RUNTIME_BOOLEANS.md section 4.8 from dotnet-trace Speedscope files
# (sampled thread time, CPU samples only, inside ExactMeshBoolean.Compute). Each CPU sample goes to the first category
# whose frames it contains, checked from the leaf towards the root:
#   gc        the runtime's GC and allocation frames (PollGC, GC*, allocation helpers, CreateInstance*)
#   exact     exact stages: *_exact, *_bigint, BigInteger*, Expansion, AdaptivePredicates, FixedArith, ExactPoint3,
#             Det3 / Det4, the arrangement's exact Orient / InCircle on implicit points
#   filter    the floating-point filters and the predicates' entry points (ExactPredicates.Orient*/InCircle*/InSphere*,
#             TriangleTriangleFilter, pierce_prefilter*, plane_side*, *Filtered)
#   bvh       AABB / box-tree queries (TriangleBoxTree, DMeshAABBTree3, PointAABBTree3, segment_hits, fast winding)
#   rounding  anything else under MeshRoundingRepair
#   rest      the remainder
# A sample with an exact frame inside a box-tree query counts as exact (the leaf-most match wins).
#   python3 speedscope_categories.py [--root <frame substring>] <trace.speedscope.json>...
# One JSON line per file: shares in % of the CPU time under the root frame (default ExactMeshBoolean.Compute; for the
# repair units eg --root MeshFuzzPilot, the whole replay).
import json, re, sys
RULES = [
    ("gc", re.compile(r"PollGC|GCHeap|gc_heap|::GC|Allocat|JIT_New|CreateInstance|RhpNew|GarbageCollect")),
    ("exact", re.compile(r"_exact\(|_bigint\(|BigInteger|Expansion\.|AdaptivePredicates|FixedArith|ExactPoint3\.|Det3\(|Det4\(|ExactMeshArrangement\.(Orient|InCircle|in_circle|orient)|ExactTriangleIntersection|orient3d\(value class g3\.ExactPoint3")),
    ("filter", re.compile(r"ExactPredicates\.(Orient|InCircle|InSphere)|TriangleTriangleFilter|pierce_prefilter|plane_side|[Ff]iltered|orient3d_int")),
    ("bvh", re.compile(r"TriangleBoxTree|DMeshAABBTree3|PointAABBTree3|segment_hits|fast_winding|FastWinding")),
    ("rounding", re.compile(r"MeshRoundingRepair")),
]
args = sys.argv[1:]; root = "ExactMeshBoolean.Compute"
if args and args[0] == "--root": root = args[1]; args = args[2:]
for path in args:
    d = json.load(open(path)); frames = [f["name"] for f in d["shared"]["frames"]]
    cat_of = {}
    tot, acc = 0.0, {k: 0.0 for k, _ in RULES}; acc["rest"] = 0.0
    for p in d["profiles"]:
        stack, last = [], None
        for e in p["events"]:
            t = e["at"]
            if stack and last is not None and t > last and frames[stack[-1]] == "CPU_TIME":
                key = tuple(stack)
                c = cat_of.get(key)
                if c is None:
                    names = [frames[i] for i in stack]
                    if not any(root in x for x in names): c = "outside"
                    else:
                        c = "rest"
                        for x in reversed(names[:-1]):
                            hit = next((k for k, rx in RULES if rx.search(x)), None)
                            if hit: c = hit; break
                        if c in ("rest",) and any("MeshRoundingRepair" in x for x in names): c = "rounding"
                    cat_of[key] = c
                if c != "outside": tot += t - last; acc[c] += t - last
            last = t
            if e["type"] == "O": stack.append(e["frame"])
            elif stack: stack.pop()
    print(json.dumps({"trace": path.split("/")[-1].replace(".speedscope.json", ""), "cpu_ms": round(tot),
                      **{k: round(100 * v / tot, 1) for k, v in acc.items()}}))
