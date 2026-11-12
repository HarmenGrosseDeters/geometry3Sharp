# Sharp-feature study, raw rows (2026-11-12)

Data only: JSON rows, analysis scripts and the tables printed from them. No library changes, no meshes. The probe prototypes
(variants K, L, WDC, BS and the class metric) were local patches to `benchmarks/SharpFeatureProbe` and are on this branch only as a patch file: `probe/kobbelt.patch` (base 30099f6, caveats at its top; never merged).

**Commit set:** master `30099f659c8436a2f29e9ef001a9ec994b34097e`, no open PR merged; plus local probe patches (not pushed):
new variants in `benchmarks/SharpFeatureProbe`, and for variants L / KL a gate hook in `mesh_ops/MeshAlphaWrap.cs`.
Machine: cloud container, 4 cores, .NET SDK 10.0.115, warm process. Inputs: 20 T1 Thingi10K meshes (fetched with
`benchmarks/LevyClassification/fetch_thingi.py`) and the 6 procedural solids of `Procedural.cs`; wrap 300 / 3000.

| file | what |
|---|---|
| `rows/base.jsonl` | A, R, B, C1, C2 with the unpatched probe of master (B's cell sizes come from here) |
| `rows/full.jsonl` | A, R, B, BS, C1, KA, KR, KU on all 26 inputs, with the class metric (`ec_*`) |
| `rows/local.jsonl` | A, KR, L, KL on the 6 solids and the 9 T1 meshes with a raw wrap of at most 120k triangles |
| `rows/wdc.jsonl` | A, WDC on the 6 solids and 3 T1 meshes |
| `scripts/analyze2.py` | `python3 analyze2.py <jsonl>...` prints `tables_*.md` |

Variants: A the pipeline (`MeshAlphaWrap`, `SimplifyWithin` 0.5 offset with the input kept clear); R the raw wrap; B
`MeshWindingNumberSolidify` (adaptive DC) at the cell size found for A's triangle count; BS B by stage, its result row is B's
cleaned raw contour through `SimplifyWithin`; C1 the vertex snap of #498; KA / KR feature recovery on the wrap (vertex onto the
shifted plane of its nearest input triangle, vertices near a crease onto the plane pair's line, crossing edges split there, all
through the exact checks of C), on A or on the raw wrap followed by the simplification; KU the same without the checks; L the
wrap with alpha / 4 for gates within 1.5 alpha of an input sharp edge, then the simplification; KL K on L's raw wrap;
WDC dual contouring of the raw wrap's solid at h = alpha / 2 with the input's planes shifted by one offset as Hermite data.

Class metric (`ec_*`): the input's sharp edges (> 30 degrees) split into convex (`cvx`) and concave (`ccv`), taken where they are
(`in`) and on the miter line of the offset surface (`mi`: edge + offset (n1 + n2) / (1 + n1.n2)); for 20000 samples the distance
to the nearest sharp edge of the result, clamped at 2 alpha: `_mean` in offsets, `_w1` / `_w3` the share within 1 / 3 offsets.
`ec_prec_*`: the result's sharp edges against the input's.
