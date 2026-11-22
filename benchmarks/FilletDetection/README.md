# FilletDetection

Research spike (2026-11): can the fillets of a CAD mesh be found, and the sharp design edge rebuilt? Study and numbers in
[docs/research/DEFILLET_SPIKE_2026-11.md](../../docs/research/DEFILLET_SPIKE_2026-11.md). No library change; everything here is
prototype code.

```
dotnet run -c Release --project benchmarks/FilletDetection -- crease   > results/crease.md
dotnet run -c Release --project benchmarks/FilletDetection -- detect   > results/detect.md      # filters: box plate lprofile gear rod, cad, fine, n=8, fast, bound=4
dotnet run -c Release --project benchmarks/FilletDetection -- defillet > results/defillet.md
python3 benchmarks/FilletDetection/summarize.py benchmarks/FilletDetection/results/detect.md > results/summary.md
# results/bound.md: detect cad n=8 fast bound=1.1 | 4 | 10 (the medial rows)
```

The raw output of the runs behind the study is in `results/` (2026-11-22, cloud container, 4 cores; about 15 minutes in all).

## Inputs (`Solids.cs`, generated, with the answer per triangle)

| solid | fillets | what it tests |
|---|---|---|
| box | all 12 edges of a 2 x 1.4 x 1 box rounded with 0.15: quarter cylinders and 8 sphere octants | edge and vertex fillets, no sharp edge left |
| plate | the same on a 2 x 1.4 x 0.2 plate with radius 0.04 | parallel walls close to the fillets |
| lprofile | extruded L, vertical edges rounded (5 convex with 0.1, the concave one with 0.2), sharp rims | convex and concave, two radii, fillets that end on a sharp rim |
| gear | 12 trapezoid teeth, tips rounded with 0.03, roots with 0.04, a bore of radius 0.3, sharp rims | small radii, many fillets, a cylinder that is no fillet |
| rod | cylinder of radius 0.5 with sharp rims | nothing is a fillet (the radius bound given to the detectors is 1.0, above the cylinder) |

Each with 2, 4, 8, 16 and 32 chords per quarter circle, as **CAD strips** (one quad per chord along the whole edge, one quad per
face, as an STL export of a CAD system gives) and **subdivided** (every edge longer than 1.5 chords of the smallest fillet split at
its midpoint, the same faceted surface with near uniform triangles: the tessellation the paper assumes; only where that stays
below 150k triangles), and for 8 chords also rotated by 37 degrees about (1, 2, 3) and moved. The triangle group carries the
class (planar, edge fillet, vertex fillet, cylinder) and the radius; `Solid.Sharp` is the solid before filleting.

## Detectors

| name | what | parameters given |
|---|---|---|
| curvature | `MeshPrincipalCurvature`: a triangle is a fillet if the mean of max(abs K1, abs K2) of its vertices is at least 1 / bound | bound = 2 x the largest fillet radius |
| curvature (oracle band) | the band of that curvature with the best F1 against the answer (40 x 40 bounds): the upper bound of any curvature threshold | the answer |
| DeFillet | `VoronoiDetector`: section 4.2 of the paper on `Delaunay3` (see the class comment for the differences) | paper defaults (tolerance 0.006, radius bound 0.06 of the bounding sphere radius), and with the bound of the other detectors |
| medial ball | `MedialDetector`: the largest empty ball per planar facet and side, accepted if it rolls in a groove (class comment) | bound = 2 x the largest fillet radius |

Scores are weighted by area. Radius error: area weighted median of abs(r - r_true) / r_true over the true positives.

`Defillet.cs` moves the vertices of the detected fillet triangles to the least squares point of the planes of the nearest
tangent supports (one closed-form step, connectivity unchanged) and compares with `Solid.Sharp`.

## Results

See the study for the tables and their reading. In short (`results/summary.md`): on CAD strips the curvature threshold reaches F1
0.72 (0.87 with the oracle band), the paper's detector as implemented here 0.43 (radius bound set; 0.05 with the paper's default bound, which is below the radii
of the box and the L profile), the medial ball 1.00 (worst 0.99); on the subdivided meshes 0.45 (0.55), 0.48 and 1.00. The reconstruction is exact on the box, the plate and the gear when the
labels are right and breaks where a support face shares vertices with a fillet (the caps of the L profile) or a few fillet
triangles were missed (the coarse sphere octants).

## Second measurement: ABC models (manual workflow)

Owner decision 2026-11-22: measure the medial-ball detector on real CAD meshes before anything is built. The ABC dataset
(Koch et al., CVPR 2019) has no fillet label, so the answer is a **heuristic of this project** on the B-rep patches
(`AbcHeuristic` in `Corpus.cs`: Cylinder or Torus patches with smooth borders to two other patches and a sectional angle of
at most 150 degrees are fillets; other curved patches with two smooth neighbours are left out of the score). The detector's
thresholds (as merged in #1386), the heuristic, the sample rule (`abc/sample.py`: SHA-256 order of the model ids; the first
200 models with and 50 without a fillet, at most 100k triangles) and the six radius-bound candidates (0.05 / 0.1 / 0.2 of
the bounding box diagonal, 0.5 / 1 / 2 times the median wall thickness) were fixed before the first run on the data.

`.github/workflows/fillet-abc.yml` (workflow_dispatch only) downloads one chunk to the runner's temp disk, extracts only the
sampled files, runs `corpus` and deletes the data. Nothing is cached or committed; the artifact is a CSV of scores per
model id. `mode: probe` prints the structure of three feature files and stops (the file format is known here only from the
paper's supplement).

```
dotnet run -c Release --project benchmarks/FilletDetection -- spotcheck            # the heuristic against the known answer
dotnet run -c Release --project benchmarks/FilletDetection -- boundcheck n=8       # one shared run against a run per bound
dotnet run -c Release --project benchmarks/FilletDetection -- export-abc <dir> n=8 # procedural solids in the ABC layout
bash benchmarks/FilletDetection/abc/prepare.sh <dir> <dir> <data> && dotnet run ... -- corpus <data> <data>/ids.txt scores.csv 600 8 2
python3 -m unittest discover -s benchmarks/FilletDetection/abc -p 'test_*.py'
```

Checks on the procedural solids (`results/spotcheck.md`, `results/boundcheck.md`):

- The heuristic on the solids written as patches (the 30 strip cases, 24 with fillets): precision 1.00 wherever it marks anything; recall 1.00 from
  4 chords up. With 2 chords the patches made for this check are wrong (a fillet's two facets fold by 45 degrees and become
  two patches with a "sharp" curve between them), so recall is 0 on the L profile and 0.51 on the gear there: a limit of the
  check, not of the ABC files, where patches come from the B-rep. The vertex fillets (sphere octants: 7 to 8 % of the box's
  fillet area) are left out of the score by design.
- One detector run with the largest bound stands for all six bounds (a run per bound would cost six times the runner time).
  That is not exact: the ball search depends on its start radius. On the plate, where the largest bound is 2.4 to 3.3 times
  the wall thickness, the shared run marks 940 (rotated: 936) triangles where a run per bound marks 960; the other solids
  agree triangle for triangle (220 differing verdicts of 46176 over five bounds). Every difference seen here is a lost mark,
  which lowers recall; that it can never add one is not shown. The recall the workflow reports for a bound well below the
  largest one may therefore be somewhat too low.
- The sectional angle of the heuristic is measured on triangle normals and is short by one facet step, and the workflow's
  time budget (default 2400 s of detection) can end the run before 250 models are scored; the sample order is fixed, so the
  scored models are a prefix of it, and the summary says how many.

### Result of the second measurement (2026-11-22)

Run: `fillet-abc.yml` on master, chunk 0050, 300 candidates, a 2700 s budget (checked before each model, so the last
model ends past it). 63 models were scored before the budget ended (the prefix of the fixed sample order); 27 have fillet area under the heuristic, 36 have none. The models
have 1.6k to 93k triangles (median 31k); detection took 46 s per model on average (at most 211 s). The per-model CSV
stays in the run's artifact and is not committed; only the aggregates below are recorded.

Precision and recall are area-weighted per model (tp / (tp + fp), tp / (tp + fn)); "pooled" sums the areas over all 63
models first. The mean precision over the 27 models with fillets is the fairest reading for the detector (a model without
fillets has precision 0 as soon as anything is marked).

| radius bound | precision, mean (27) | precision, pooled (63) | recall, mean (27) | recall, pooled (63) | models with precision >= 0.95 | models with recall >= 0.9 | non-fillet area marked, median over 63 |
|---|---|---|---|---|---|---|---|
| 0.05 diagonal | 0.46 | 0.20 | 0.82 | 0.95 | 4 of 27 | 15 of 27 | 10 % |
| 0.1 diagonal | 0.45 | 0.19 | 0.90 | 0.98 | 4 of 27 | 20 of 27 | 11 % |
| 0.2 diagonal | 0.45 | 0.18 | 0.90 | 0.98 | 4 of 27 | 21 of 27 | 11 % |
| 0.5 thickness | 0.47 | 0.23 | 0.43 | 0.46 | 3 of 27 | 1 of 27 | 3 % |
| 1 thickness | 0.45 | 0.23 | 0.82 | 0.88 | 4 of 27 | 15 of 27 | 5 % |
| 2 thickness | 0.45 | 0.22 | 0.87 | 0.96 | 4 of 27 | 18 of 27 | 7 % |

One model has no wall thickness, so the three thickness bounds give no verdict on it (62 models; the pooled values do not
change, the medians over 62 are 3 / 6 / 9 %). 24 of the 36 models without a fillet get marks (23 under 0.5 thickness).
**The stop rule (precision below 0.95 or recall below 0.9 without a principled fix) is met under every bound**: pooled
recall reaches the bar with all three diagonal bounds, the mean only at 0.2 of the diagonal (0.901; 0.898 at 0.1);
precision is about half of the bar. Only the ABC part of the planned set was run; the procedural variable-radius and
curved-support solids were not, since a failure on the ABC part already decides the rule.
The rule and the heuristic were fixed before the run and are not changed after it. The heuristic counts every curved
patch that is not a fillet by its rule (bores, bosses, blends of other types) as a non-fillet, so part of the false marks
may be fillet-like geometry the heuristic does not call a fillet. The procedural solids (where the detector left the
rod and the bore unmarked) do not show which share that is. Telling the two apart would need a label source this project
does not have; without it there is no principled fix in the sense of the rule. Outcome: the analysis
class (option A of the [study](../../docs/research/DEFILLET_SPIKE_2026-11.md)) is not built, and with it the label
consumers (option B).
