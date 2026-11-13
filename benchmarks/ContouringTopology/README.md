# ContouringTopology

Upper bound for a topology guarantee of the contouring
([docs/design/CONTOURING_TOPOLOGY.md](../../docs/design/CONTOURING_TOPOLOGY.md)): how often does the output of
`DualContouring` / `WindingContouring` differ in topology from the level set, and how many grid cells would fail a
local certificate? Measurement only, public API only; the tables and findings are in the design note (sections 2 and
3).

Topology = the number of components and the genus of each (`MeshTopologySummary`). Equal counts do not prove isotopy,
so the counts of wrong results are lower bounds.

```
B="dotnet run -c Release --project benchmarks/ContouringTopology --"
# feature-width families (truth by construction): shape in torus tunnel gap rod shell plate, k = width in cells
$B fam.jsonl imp:<shape>:<k>@<seed>... -- 48,96            # k in 0.25 0.5 0.75 1 1.5 2 3 4, seeds 0-19
# the shapes of benchmarks/DualContouringIntersections, and fine reference runs
$B old.jsonl imp:<shape>@<seed>... -- 24,48,96             # sphere rotbox boxminussphere twoboxes slab cone gyroid
$B ref.jsonl imp:<shape>@<seed>... -- 192,288              # boxminussphere gyroid slab, seeds 0-2
# the lever-6 set through MeshWindingNumberSolidify (adaptive, cell = diagonal / res): default result and raw contour
$B wind.jsonl mesh:all <T1 meshes>... -- 64,128,256,512
# cost probe of a per-cell certificate (Probe.cs, floating point)
$B probe.jsonl probe:mesh:<solid>... probe:<T1 mesh>... -- 64,128,256,512
python3 benchmarks/ContouringTopology/analyze.py fam.jsonl old.jsonl ref.jsonl wind.jsonl probe.jsonl
```

Inputs: implicit shapes under a random rotation and grid shift per seed; the lever-6 set of
`benchmarks/SharpFeatureProbe` (the 20 T1 Thingi10K meshes 96675 96773 64983 43551 1582399 57084 104640 94664 73088
1582404 37731 57714 116871 109459 472011 37111 113418 79961 1038438 66112, fetched with
`benchmarks/LevyClassification/fetch_thingi.py`, not committed; the 6 procedural CAD solids).

Truth and reference:
- implicit families and most shapes: known by construction (`truth_sig`);
- box minus sphere: the runs at 192 and 288 cells agree; gyroid: they do not (no reference);
- a clean input mesh (closed, oriented, no self-intersections, no bowties): exact, the input components across which
  the exact winding number steps between 0 and 1 (`InputTruth`);
- other meshes: the raw contour of the finest run, a reference only.

One JSON line per input and resolution; a rerun skips the pairs already in the file, failed ones included (delete
the line to retry; `analyze.py` lists the runs without a result and leaves them out of the tables). Mesh inputs are
keyed by file name. The cell size uses the bounds of all vertices, the solidify those of the triangles (they differ
only for files with unused vertices). Run on master 1f3b6b7, 4 cores,
2026-11-12.
