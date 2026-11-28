# Early endpoint piece budget

For the paired follow-up runs, phase cost analysis, and weighted scan comparison, see the [endpoint budget cost study](ENDPOINT_COST_STUDY.md).

The [input preparation and verification recipe](WEIGHTED_ENDPOINT_TRIAL.md#reproduce) reconstructs the file-backed operands and checks the hashes for the baseline and follow-up runs.

The `endpoint-budget` mode compares the public `ExactMeshBoolean.Compute` with `EnableEarlyPieceBudgetCheck` off and on. It warms both modes, alternates their run order, and measures complete calls, including input validation and output construction when they occur. File loading, hashing, and result checks are outside the timer. Each refused call must agree with the baseline status and, when certified early, expose a lower bound above the budget and at most the exact baseline prediction. Each accepted call must produce the same indexed mesh.

The budget argument accepts a number, `tight` (half the exact prediction), `near` (one below the exact prediction), or `exact`. The three derived modes use a separate reference call to get that prediction before timing. Caps derived from the prediction are stress controls. They do not describe the budget distribution of a production workload.

```sh
dotnet build benchmarks/SolidBooleanAudit -c Release
export DOTNET_PROCESSOR_COUNT=2
tool=benchmarks/SolidBooleanAudit/bin/Release/net10.0/SolidBooleanAudit.dll
dotnet "$tool" endpoint-budget cubes70 Union 300000 4 70
dotnet "$tool" endpoint-budget cubes70 Union 1000000 2 70
dotnet "$tool" endpoint-budget cubes20 Union 112800 2 20
dotnet "$tool" endpoint-budget cubes35 Union 567000 2 35
dotnet "$tool" endpoint-budget cubes20 Union 0 2 20
```

The cube schedule uses [ThingiCSG multi_rot_cube.scad](https://github.com/BrunoLevy/thingiCSG/blob/f362674851a06f783070ab8c09d7257c0d59e8f4/DATABASE/Basic/multi_rot_cube.scad). The other cases use prepared operands from the targeted benchmark. Small fixtures cover disjoint, nested, coplanar and rational-crossing inputs. ABC covers a tessellated thin CAD part and a finer part with fillets, each cut with its existing benchmark tool. EMBER cases 16 and 84 use Thingi10K meshes with the transforms from the published test cases. The Objaverse sample is a repaired room asset, cut with a box whose end is at 47% of its x extent. The JSONL records operand filenames and SHA-256 hashes. Supply the corresponding prepared files at runtime.

```sh
dotnet "$tool" endpoint-budget coplanar Union exact 2 /path/to/B04-identical-other-diagonal-a.obj /path/to/B04-identical-other-diagonal-b.obj
dotnet "$tool" endpoint-budget ABC-00000382 Difference near 2 /path/to/ABC-00000382-fine.obj /path/to/ABC-00000382-cutter.obj
dotnet "$tool" endpoint-budget EMBER-0084 Union tight 2 /path/to/EMBER-0084-a.obj /path/to/EMBER-0084-b.obj
dotnet "$tool" endpoint-budget OBJ-02543290 Difference exact 2 /path/to/OBJ-02543290-welded-repair.obj
```

[Targeted benchmark PR](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1711) · [EMBER transform source](https://github.com/elalish/manifold/tree/master/extras/ember_tests) · [Objaverse asset](https://sketchfab.com/3d-models/02543290444e41a3babfbb894e7d0909) · [Design and bounds](../../docs/design/EXACT_BOOLEAN_ENDPOINT_BUDGET.md)

## Results

The .NET 10 Release runs used two logical processors and base `43ad3470c`. Times are medians of the paired end-to-end runs from the commands above. The tight 70-cube case has four pairs. Most cases have two pairs, and short runtimes vary noticeably between orders.

| Case and cap | Exact pieces | Early certificate | Off ms | On ms |
|---|---:|---:|---:|---:|
| 70 cubes, 300,000 | 4,322,736 | 300,284 | 5,305.8 | 853.8 |
| 70 cubes, 1,000,000 | 4,322,736 | None | 4,734.9 | 4,729.3 |
| 20 cubes, exact | 112,800 | None | 2,168.8 | 1,743.8 |
| 35 cubes, exact | 567,000 | None | 10,727.2 | 10,962.2 |
| 20 cubes, budget disabled | 112,800 | None | 2,016.0 | 1,467.1 |
| ABC-00000177, tight | 1,018 | 510 | 6.7 | 5.5 |
| ABC-00000382, tight | 21,182 | 10,592 | 62.1 | 62.9 |
| EMBER-0016, tight | 28,754 | 14,380 | 260.8 | 286.1 |
| EMBER-0084, tight | 105,802 | 52,902 | 852.0 | 734.5 |
| OBJ-02543290, tight | 3,158 | 1,580 | 37.9 | 20.2 |

All nine additional cases certified early refusal at the deliberately tight cap. The three near-limit cases completed the exact prediction and agreed on refusal. All successful pairs returned identical indexed meshes and passed the output cleanliness check. The disabled-budget control follows the same path in both modes and shows how much timing can vary. The tight 70-cube case avoided crossing preparation and allocated a median 548 MB with the guard, down from 1,029 MB. The accepted 35-cube case allocated about 3,500 MB in both modes.

The measurable savings depend on how much work remains after the first merge and whether known endpoints can prove the requested cap is already exceeded. The near-limit and small cases give less opportunity to save time. The option defaults to off. [All 54 paired rows](results/endpoint-budget-2026-11-27.jsonl)

## Documented tail cases

The repository's [extreme runtime inventory](../../docs/perf/EXTREME_RUNTIME_BOOLEANS.md#31-measured-rows) supplies a separate stratum with classified bottlenecks. The early budget check can skip crossing preparation and the second point merge when it proves refusal. The existing exact budget already stops before triangulation, classification, and rounding. Historical uncapped timings therefore describe the workloads and their bottlenecks. The capped comparison isolates the additional savings of the new guard.

Use the existing `BooleanComparison export` rotation and translation. Its `--operands-only` switch writes the same `A.off` and `B.off` pair while skipping the uncapped resolves used to compute reference volumes. Download the raw STL files at runtime. The two benchmark tools use the same Release library.

```sh
set -e
dotnet build benchmarks/BooleanComparison -c Release
dotnet build benchmarks/SolidBooleanAudit -c Release
export DOTNET_PROCESSOR_COUNT=2 DOTNET_TieredPGO=0 DOTNET_GCHeapHardLimit=0x150000000
root=$(mktemp -d /tmp/endpoint-tail.XXXXXX)
exporter=benchmarks/BooleanComparison/bin/Release/net10.0/BooleanComparison.dll
audit=benchmarks/SolidBooleanAudit/bin/Release/net10.0/SolidBooleanAudit.dll
mkdir -p "$root/raw"
for id in 519511 1368057 117959 55928; do
  curl -fsSL "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/1e52df6/raw_meshes/$id.stl" -o "$root/raw/$id.stl"
  dotnet "$exporter" export "$root/raw/$id.stl" "$root/operands/$id" --operands-only
done
run_tail() {
  dotnet "$audit" endpoint-budget "Thingi10K-$1" "$2" "$3" 1 "$root/operands/$1/A.off" "$root/operands/$1/B.off" --allow-open
}
run_tail 519511 Union 500000
run_tail 1368057 Union 500000
run_tail 117959 Union 1000000
run_tail 55928 Difference 1000000
```

The inventory's 55928 difference spent about 380 s, mostly in classification and triangulation. Its 15.1 million pieces arise largely from self-intersections. The 117959 union spent about 114 s, mostly in classification. The 1368057 and 519511 union cases cover rounding and intersection-heavy profiles. [55928 analysis](../../docs/design/EXACT_BOOLEAN_TAIL_55928.md) · [Historical raw profiles](../ExtremeBooleans/results/inventory_2026-11-12.jsonl)

The current capped replays below are one pair per model on two logical processors, with open operands allowed and a 120-second cancellation deadline per call. The skipped baseline time is the sum of crossing preparation, the second canonical merge and full prediction on an early-stop row.

| Tail case | Cap | Exact prediction | Early certificate | Off / on wall s | Baseline phases skipped |
|---|---:|---:|---:|---:|---:|
| 519511 union | 500,000 | 1,147,764 | 500,001 | 4.72 / 5.61 | 0.96 s |
| 1368057 union | 500,000 | 1,014,150 | None | 3.11 / 2.74 | None |
| 117959 union | 1,000,000 | 2,585,382 | 1,000,001 | 9.83 / 6.04 | 0.13 s |
| 55928 difference | 1,000,000 | 15,120,914 | 1,000,086 | 27.86 / 5.80 | 22.05 s |

For 55928 the pair-search phases were nearly equal (2.94 and 2.99 s). Cumulative allocation fell from 13.90 GB to 3.22 GB. On 519511 the filtered run skipped later phases, but intersections took 4.79 s versus 3.15 s in the baseline, and its total wall time was higher. On 117959 the skipped phases accounted for only 0.13 s of the baseline. These single-pair measurements show where work was skipped and why elapsed time alone is a poor adoption criterion. [Raw tail replays with operand hashes](results/endpoint-budget-tail-2026-11-27.jsonl)

## Which budgets can the first merge certify?

The `endpoint-evidence` mode runs the public guard at cap one and inspects the retained first-merge snapshot offline. It counts all known endpoints per active source, removes canonical corner IDs, and deduplicates by canonical point ID. It uses the arrangement's signed orientation predicate to check each endpoint in projection and to distinguish boundary from interior. The weighted bound counts each known interior point twice, as the exact predictor does. This is an audit of retained data and projected membership. The local Z3 models cover arithmetic and exact section and clipping geometry, with the underlying constructions and projection taken as premises.

```sh
dotnet "$audit" endpoint-evidence cubes70 Union 70
dotnet "$audit" endpoint-evidence Thingi10K-55928 Difference "$root/operands/55928/A.off" "$root/operands/55928/B.off" --allow-open
dotnet "$audit" endpoint-budget cubes70 Union 348639 1 70
dotnet "$audit" endpoint-budget cubes70 Union 348640 1 70
```

For the other tail cases, use the operand pairs and operations in the table above.

| Case | Raw ceiling for current check | Current endpoint bound | Weighted endpoint bound | Full prediction |
|---|---:|---:|---:|---:|
| 70 cubes | 657,720 | 348,640 | 387,280 | 4,322,736 |
| 519511 union | 1,147,764 | 913,795 | 1,147,764 | 1,147,764 |
| 1368057 union | 673,817 | 412,228 | 441,872 | 1,014,150 |
| 117959 union | 2,585,366 | 2,573,319 | 2,585,382 | 2,585,382 |
| 55928 difference | 2,146,978 | 1,447,681 | 1,848,934 | 15,120,914 |

The live cube replay refused early at 348,639 and reached the full prediction at 348,640. At cap equality the bound does not prove refusal. Endpoint weighting would leave the decision unchanged at every cap tested in the original tail replays and at the one-million-piece cube cap. In the 55928 snapshot it would extend the certifiable range from caps strictly below 1,447,681 to caps strictly below 1,848,934. The 117959 row also shows why a weighted check needs its own raw-reference filter. Its weighted bound exceeds the unweighted filter's raw ceiling.

To test caps in the newly certifiable range, `endpoint-weighted` compares the offline certificate to the exact prediction from the current opt-in path at the target cap. It also reports the cap-one snapshot call and full reflective inspection times. Those timings describe an offline experiment. A production weighted check could stop at the first certifying prefix and would need an allocation and time comparison on accepted calls.

```sh
dotnet "$audit" endpoint-weighted Thingi10K-55928 Difference 1600000 "$root/operands/55928/A.off" "$root/operands/55928/B.off" --allow-open
dotnet "$audit" endpoint-weighted Thingi10K-519511 Union 1000000 "$root/operands/519511/A.off" "$root/operands/519511/B.off" --allow-open
dotnet "$audit" endpoint-weighted cubes20 Union 120000 20
```

These single replays use base `20116116e` with the endpoint changes and the same runtime settings as the capped tail replays. The current opt-in path reached the full prediction on every row. “Later phases” combines crossing preparation, the second merge and prediction in that call.

| Case | Cap | Offline weighted refusal | Current call | Later phases | Full offline inspection |
|---|---:|---|---:|---:|---:|
| 55928 difference | 1,600,000 | Certified | 31.10 s | 24.50 s | 1.09 s |
| 519511 union | 1,000,000 | Certified | 4.60 s | 0.95 s | 1.27 s |
| 20 cubes union | 120,000 | Accepted by the exact predictor | 1.92 s | 0.15 s | 0.09 s |

The 55928 case has much more work left to skip after the first merge. For 519511, full offline inspection took longer than the later phases, even though the weighted certificate matched the exact count. [Weighted replay with inputs, phases and timings](results/endpoint-weighted-2026-11-27.jsonl)

[Snapshot counts and input hashes](results/endpoint-evidence-2026-11-27.jsonl) · [Live boundary replay](results/endpoint-boundary-2026-11-27.jsonl) · [Z3 results](../../tools/Z3Pilot/results/endpoint_budget_2026-11-27.json)
