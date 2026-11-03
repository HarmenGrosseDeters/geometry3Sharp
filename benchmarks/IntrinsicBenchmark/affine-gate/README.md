# Affine log-map prototype: failed accuracy gate

**Status: preserve as a draft experiment, not ready to merge.** Both AffineLocal and the subsequently
owner-approved AffineAdaptive trial fail brief 2's unchanged pointwise/cut-locus acceptance. The owner
then authorized high-ROI experiments and preserving the prototype plus findings in a PR. A bounded
diffusion-time sweep also failed. No legacy fallback, new solver, parallel path, or relaxed acceptance
was substituted. Research PR #364 remains separate and unmerged; brief 3 was not started.

## Scope and prior art

Tier M; feature branch based on fetched `origin/master`, `8364ac74499bb1fadca580c68ec38b0d99bb0803`,
not the research branch. The original dirty checkout was not used.

- Additive `gs.LogMapStrategy` and `VectorHeatMethod.ComputeLogMap(int, LogMapStrategy)`.
- The existing method body and constructors/defaults are unchanged; Legacy dispatches directly to it.
- AffineLocal solves the block-triangular homogeneous system with existing scalar/vector heat factors.
- AffineAdaptive develops translations in the transported source frame, then uses scalar factors for
  both coordinates. The upstream full nonsymmetric solver is unnecessary for these blocks.
- Negative cotangent weights are clamped consistently with existing library heat operators. This is
  an adaptation, not a claim of identical output to geometry-central.
- MIT notice is in the source header; attribution is in [SOURCES](../../../docs/SOURCES.md).

How others solve it: [Soliman and Sharp, CGF/SGP 2025](https://www.yousufsoliman.com/projects/the-affine-heat-method.html)
distinguish prefactorable local affine heat from a cut-locus-oriented variant.
[geometry-central documentation](https://geometry-central.net/surface/algorithms/vector_heat_method/)
also distinguishes near-source AffineLocal from higher-quality AffineAdaptive. The port follows the
[MIT source pinned to 019669dd](https://github.com/nmwsharp/geometry-central/blob/019669ddabda05e0f71fa3587cfb3c1dadf19cb8/src/surface/vector_heat_method.cpp),
whose license was verified before reading it. The [2019 Vector Heat Method](https://www.cs.cmu.edu/~kmcrane/Projects/VectorHeatMethod/)
is the baseline context; this library's legacy log-map construction is not identical to upstream's.
The initial trials did not use the full paper. After the owner supplied the PDF, sections 3.3–4.3 and 5
were read locally: equations 10–17 and the backward-Euler step support the implementation; section 5,
page 12 explicitly describes inaccurate distance approximations at the cut locus. No universal
pointwise guarantee over this library's different legacy construction was found.

Initial estimate: approximately 400–600 source lines, conditional on the first accuracy gate.
The approved adaptive trial estimated an additional 80–150 library and 150–250 test/benchmark lines;
reuse of the split machinery required less. Actual source additions remain below the original gate:
inspect with `git diff --numstat 8364ac74` and count the two new `.cs` files. Raw measurement CSV rows
are evidence, not implementation scope. Further algorithm expansion is stopped.

## Reproduction and raw results

Run from the repository root (PowerShell):

```powershell
$evidence = 'benchmarks\IntrinsicBenchmark\affine-gate'
dotnet run --project benchmarks\IntrinsicBenchmark -c Release -- --affine-logmap "$evidence\affine-points.csv"
dotnet run --project benchmarks\IntrinsicBenchmark -c Release -- --affine-adaptive "$evidence\adaptive-points.csv"
foreach ($strategy in @('affine-logmap','affine-adaptive')) {
    foreach ($time in @('0.0625','0.25','4')) {
        dotnet run --project benchmarks\IntrinsicBenchmark -c Release -- --$strategy "$evidence\$strategy-t$time.csv" $time
    }
}
```

Every invocation above returns **exit 1 (accuracy gate failed)**, not a compilation failure. The optional
third argument changes only the candidate's TimeFactor; the legacy reference always uses its default 1.
This focused selector returns without running the existing large scaling benchmark.

The executed commands used the same arguments with output paths under
`C:\Users\Harmen\.copilot\session-state\20fc9bdf-7df0-4f72-84e1-972051f09ee0\files` and saved console output
there before copying it here. Original command for the default local run:

```powershell
dotnet run --project benchmarks\IntrinsicBenchmark -c Release -- --affine-logmap C:\Users\Harmen\.copilot\session-state\20fc9bdf-7df0-4f72-84e1-972051f09ee0\files\affine-points.csv 2>&1 | Tee-Object -FilePath C:\Users\Harmen\.copilot\session-state\20fc9bdf-7df0-4f72-84e1-972051f09ee0\files\affine-gate-output.txt
```

| Variant / TimeFactor | Console output: all radius bands + timings | Per-vertex output |
|---|---|---|
| Local / 1 | [raw](affine-gate-output.txt) | [CSV](affine-points.csv) |
| Adaptive / 1 | [raw](adaptive-gate-output.txt) | [CSV](adaptive-points.csv) |
| Local / .0625 | [raw](affine-logmap-t0.0625.txt) | [CSV](affine-logmap-t0.0625.csv) |
| Local / .25 | [raw](affine-logmap-t0.25.txt) | [CSV](affine-logmap-t0.25.csv) |
| Local / 4 | [raw](affine-logmap-t4.txt) | [CSV](affine-logmap-t4.csv) |
| Adaptive / .0625 | [raw](affine-adaptive-t0.0625.txt) | [CSV](affine-adaptive-t0.0625.csv) |
| Adaptive / .25 | [raw](affine-adaptive-t0.25.txt) | [CSV](affine-adaptive-t0.25.csv) |
| Adaptive / 4 | [raw](affine-adaptive-t4.txt) | [CSV](affine-adaptive-t4.csv) |

These are procedural geometry results, not mesh assets. Default local/adaptive measurements preceded
adding the optional time argument; their algorithm and samples were the same. Timing comparisons are
within a run, not between the two logs.

## Oracle definition

A planar grid of side 4, a normalized-cube unit sphere, and an open unit cylinder of height 4. The plane
oracle is the exact offset. The curved oracles are the closed-form **smooth** surface logarithmic maps,
not exact maps on their triangulated approximations. The common tangent gauge uses the projected
signpost-zero direction and its right-handed smooth tangent frame; no rotation is fitted to results.

All non-source vertices, including boundaries, are measured. The returned source is explicitly zero.
Only the exactly antipodal sphere vertex and exactly opposite cylinder meridian are omitted because
the shortest log direction is nonunique. Nearby difficult vertices are retained. The raw outputs list
excluded/nonfinite counts. Errors are Euclidean distances in the source gauge. Strict regressions count
any increase; significant regressions use an absolute 1e-10 roundoff allowance. Even with that allowance
the gate fails. Radius bands partition [0, maximum analytical radius] into four equal parts.

## Findings (commands above)

The default plane maps have errors at roundoff, but neither strategy dominates on the sphere/cylinder.
In the sphere's last band [3pi/4, pi], **all 216 measured vertices regress** at default time.

| Variant / time | Sphere significant regressions / 1536 | Cylinder significant regressions / 526 | Sphere last-band mean / max error |
|---|---:|---:|---:|
| Legacy / 1 | baseline | baseline | .069995 / .230964 |
| Local / 1 | 456 | 68 | .281252 / 1.810962 |
| Adaptive / 1 | 920 | 68 | .425389 / 1.902318 |
| Local / .0625 | 668 | 0 | .463242 / 1.167787 |
| Local / .25 | 620 | 12 | .313480 / 1.040400 |
| Local / 4 | 508 | 180 | .585894 / 2.382786 |
| Adaptive / .0625 | 736 | 0 | .447950 / .976416 |
| Adaptive / .25 | 788 | 12 | .305628 / 1.099566 |
| Adaptive / 4 | 1220 | 180 | .854236 / 2.478868 |

The sweep tests a high-ROI hypothesis without new infrastructure: smaller time might reduce cut-locus
blending, while larger time might reduce mesh-direction bias. Smaller time removes significant cylinder
regressions in this fixture, but does not resolve the sphere criterion. All plane/sphere/cylinder max and
mean errors **per radius band**, regression counts, worst vertex and error delta are retained in each log;
all individual errors are retained in its CSV. No broader parameter search or default tuning followed.

Warm queries, median of five runs after warm-up, milliseconds; factorization excluded (same commands):

| Run | Plane legacy / candidate | Sphere legacy / candidate | Cylinder legacy / candidate |
|---|---:|---:|---:|
| Local / 1 | 2.9038 / .8555 | 2.5375 / 2.2480 | .8111 / .3869 |
| Adaptive / 1 | 3.5543 / .6435 | 5.6004 / 3.1197 | 1.4821 / .3704 |

These are exploratory run-local observations, not an accepted performance result or a release claim.

## Direct potpourri3d comparison and paper check

The owner requested a direct check against [potpourri3d](https://github.com/nmwsharp/potpourri3d#mesh-vector-heat).
Its MIT license was checked before use. The installed precompiled package is potpourri3d 1.4.0 with
NumPy 2.5.4 and SciPy 1.18.1. This is an executable reference, not another independent algorithm:
potpourri3d binds geometry-central. Its `VectorHeat` strategy is **not** this library's Legacy strategy.

Exact executed comparison (PowerShell, session-local dependencies and mesh exports):

```powershell
$root = 'C:\Users\Harmen\.copilot\session-state\20fc9bdf-7df0-4f72-84e1-972051f09ee0\files'
dotnet run --project benchmarks\IntrinsicBenchmark -c Release -- --affine-export "$root\potpourri-fixtures"
& "$root\reference-venv\Scripts\python.exe" benchmarks\IntrinsicBenchmark\compare_potpourri.py "$root\potpourri-fixtures" benchmarks\IntrinsicBenchmark\affine-gate\potpourri-points.csv
```

To reproduce on another machine, create a virtual environment and install `potpourri3d==1.4.0`,
`numpy==2.5.4`, `scipy==1.18.1`; substitute its interpreter and a scratch export directory outside the
repository. The exporter uses the **same C# fixture constructors and vertex/triangle arrays**, not a
similar Python mesh. Mesh exports are not committed. Both solvers run with t coefficient 1, on both
input connectivity and intrinsic Delaunay connectivity. Source gauges are aligned from exposed tangent
frames projected into the analytical tangent plane, without fitting computed log maps.

[Complete per-band console output](potpourri-output.txt), [per-vertex CSV](potpourri-points.csv),
[aggregate summary](potpourri-summary.txt). The reference Python oracle matches the earlier C# legacy
error values to at most 6.32e-16 (comparison of the CSVs, reported in the summary).

Default intrinsic sphere, far band [3pi/4, pi], from the comparison command:

| Method | Far-band mean / max error | All-sphere regressions against C# Legacy / 1536 |
|---|---:|---:|
| C# Legacy | .069995 / .230964 | baseline |
| C# AffineLocal | .281252 / 1.810962 | 456 |
| potpourri3d AffineLocal | .277929 / 1.796062 | 460 |
| C# AffineAdaptive | .425389 / 1.902318 | 920 |
| potpourri3d AffineAdaptive | .419085 / 1.887103 | 920 |

Both reference affine variants also regress at **all 216** far-band sphere samples. Maximum direct
C#/reference map difference on defined intrinsic sphere samples is .015779 (local) / .016973 (adaptive),
much smaller than the far-band analytical errors, but not numerical identity. Cylinder affine reference
results also fail the original pointwise gate. Input-connectivity comparisons have larger sphere
differences; these are not claimed resolved or attributable solely to a specific operator difference.

**Reference sanity warning:** the comparison exits 1 after retaining every case because the installed
reference fails the planar exactness sanity check: max error .903953 for local and .177908 for adaptive,
where the C# maps are at roundoff. A separate [gauge-independent planar check](potpourri-planar-sanity.txt)
confirms these are distance errors on fresh reference instances too, not fitted-frame or reuse artifacts.
It compares `abs(norm(solver.compute_log_map(source, strategy), axis=1) - norm(V-V[source], axis=1))`
and repeats after `VectorHeat`; fresh/reused differences are zero. This is an observed package result,
not a diagnosed upstream bug. It prevents claiming that the reference fully certifies the port.

The uploaded paper was read locally, without uploading or committing it. Its short-time discretization
and local/adaptive equations agree with the block-triangular derivation. More importantly, section 5,
page 12 warns that the distance approximations are inaccurate at the cut locus because the ground-truth
parameterization is discontinuous. This supports the bounded stop, not a relaxation of the owner's gate.
The executable reference **reproduces the important sphere failure**, so it does not reveal a small
port correction that would rescue the original acceptance. Preserve the prototype and evidence; do not
claim universal superiority or continue expanding scope automatically.

## Verification and open items

The independent reference assembles full homogeneous matrices and solves by dense pivoted elimination,
including boundary/interior sources and a refinement option. Adaptive reference construction uses angle
subtraction and an independently solved parallel section. Both variants have repeat/A–B–A checks, fresh
instance comparison and exact Legacy-dispatch comparisons. Existing legacy tests are unchanged.

Hand mutations were detected: reversing the local translation sign failed both local reference cases;
negating the adaptive reference frame failed both adaptive cases while the local cases passed. Each
mutation was restored and followed by a forced rebuild:

```powershell
dotnet build tests\geometry3Sharp.Tests -c Release -f net10.0 -t:Rebuild
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --no-build --filter 'FullyQualifiedName~IntrinsicGeodesicsAffineTests|FullyQualifiedName~IntrinsicGeodesicsTests.LogMap' --logger 'console;verbosity=normal'
```

The [final selected run](adaptive-restored-tests.txt) passed all eleven cases. Raw mutation failures are
retained for the [local sign](affine-mutation-output.txt) and [adaptive frame](adaptive-mutation-output.txt).
The earlier local mutation log uses the test method's original name, before parameterizing both variants.
Full pre-push local CI is recorded separately in [local-ci-summary.txt](local-ci-summary.txt).
All mandated steps ran. Library/addon builds, net48 builds, all benchmark builds, both package smoke tests,
and API-validated packs passed. The full library/CLI test steps were **not green**: culture-sensitive
remesher/CLI expectations and a generated-file freshness check also fail on untouched base `8364ac74`.
The meshing oracle's timing assertion failed in the full suite, but passed when isolated on both base
and this prototype. [Baseline commands and evidence](baseline-ci-summary.txt) distinguish those cases;
this is not a claim that the full suite was fixed. These unrelated/restricted areas were not changed.

Not complete: the accuracy acceptance itself; full Similarity.All()/bitwise scaling coverage;
source-free-component, underflow-growth and comprehensive mesh/options invalidation tests; demonstrated
inserted-vertex coverage (enabling refinement alone does not establish it); independent ready-PR review.
No parallel path was added. Full CI being green does not make the failed accuracy gate green.

The prototype is intentionally preserved in a **draft** PR for an explicit owner decision. No evidence
currently supports promising the original pointwise/cut-locus goal within another small change. A new
algorithm, broader analysis or altered acceptance requires a new brief and budget; do not merge this
experiment as an accepted implementation of brief 2.
