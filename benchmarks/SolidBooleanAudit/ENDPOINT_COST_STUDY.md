# Exact Boolean endpoint budget cost study

The opt-in budget check can save crossing preparation, the second canonical point merge, and full piece prediction. It runs after pair intersections and the first point merge. At the caps in the refusal trials, the original predictor also stops before triangulation. Input size or final piece count alone cannot explain the speedup. The early check has to certify the requested cap using the endpoints available at the first merge, and the phases that follow must cost enough to pay for the check. [Endpoint budget design](../../docs/design/EXACT_BOOLEAN_ENDPOINT_BUDGET.md)

## Two questions for each workload

**Can the snapshot certify refusal?** Let \(S\) be the number of active source triangles, \(N\) the number of retained, distinct non-corner endpoints counted per source, and \(I\) the number of those endpoints in the source interior. The two certificates are \(U=S+N\) and \(W=S+N+I\). The exact predictor also sees vertices created by crossing preparation. A positive cap \(C\) is refused early only when the applicable certificate is strictly greater than \(C\). The same mesh pair can therefore exercise three paths as the cap changes: refusal by counting alone, refusal that needs weighting, and full prediction.

**How much work is left to save?** Let \(D\) be the sum of the original call's measured crossing, second-merge, and prediction phases, and \(G\) the time in the opt-in call's endpoint check. On a certified refusal, \(D-G\) is the measured phase budget for savings. On a fallback, the direct contribution is \(-G\). Subtract the corresponding phase estimate from the paired wall-time improvement to get a residual. The residual includes differences in common phases, phase rounding, GC and any effects on subsequent work. Runtime allocation numbers are cumulative managed bytes. A cost estimate needs repeated, paired measurements and should report the residual alongside the local phases.

The arrangement follows the split and classify structure of Zhou et al. EMBER also exploits local arrangements to limit geometric work. Here the model concerns a decision before the current arrangement is complete, and the existing arrangement's `Orient` predicate supplies the offline endpoint labels. The scope inherits the retention premise of the current guard and its region-clipping exclusion. [Geometry sources](../../docs/sources/exact-arithmetic-and-booleans.md) · [Existing clipping and prediction checks](../../tools/Z3Pilot/endpoint_budget.py)

## What Z3 checks

For a two-pass scan, assume the first pass has computed \(U\). After orientation has confirmed \(i\) interior points, let \(r\) points be unclassified. The completed weighted count lies in \([U+i,U+i+r]\). The classifier proves refusal once the lower bound is above the cap. It gives up on the endpoint certificate once the upper bound is at or below the cap and the caller still has to obtain the exact prediction. Each classification batch shrinks the interval while retaining the true count. The solver also checks that the raw-reference ceiling is safe and that a one-pass weighted source prefix is a lower bound. Satisfiable cases show why equality cannot justify refusal and why a straddling interval can resolve either way.

The model assumes correctly classified, retained vertices. It uses integer counting and gives no latency estimate. [Solver](../../tools/Z3Pilot/endpoint_prefix.py) · [Solver results](../../tools/Z3Pilot/results/endpoint_prefix_2026-11-27.json)

## Benchmark setup

The A/B calls run the original and opt-in public paths, four pairs per cap, alternating which path runs first. The driver checks status, exact predictions or valid lower bounds, and exact indexed-mesh agreement on success. Each cap runs as a separate process. The experiment uses .NET SDK 10.0.404, Linux x86-64 and a two-CPU quota. The library under test is the draft PR's tree. Obtain the hashed real operands with the existing tail recipe. [Operand recipe](ENDPOINT_BUDGET.md#documented-tail-cases)

```sh
dotnet build benchmarks/SolidBooleanAudit -c Release
export DOTNET_PROCESSOR_COUNT=2 DOTNET_TieredPGO=0 DOTNET_GCHeapHardLimit=0x150000000
audit=benchmarks/SolidBooleanAudit/bin/Release/net10.0/SolidBooleanAudit.dll
tail_inputs() {
  local mode=$1 id=$2 operation=$3 caps=$4
  shift 4
  dotnet "$audit" "$mode" "Thingi10K-$id" "$operation" "$caps" "$@" \
    "$root/operands/$id/A.off" "$root/operands/$id/B.off" --allow-open
}
dotnet "$audit" endpoint-budget cubes70 Union 348639 4 70
dotnet "$audit" endpoint-budget cubes70 Union 348640 4 70
tail_inputs endpoint-budget 55928 Difference 1000000 4
tail_inputs endpoint-budget 519511 Union 500000 4
tail_inputs endpoint-budget 1368057 Union 500000 4
dotnet "$audit" endpoint-budget cubes20 Union 112800 4 20
```

The offline `endpoint-prefix` mode reads a complete first-merge snapshot and simulates two schedules in source order. One pass counts and classifies each source in turn, stopping at the first certifying source. Two passes count the whole unweighted snapshot first, then classify sources until the weighted interval settles. Both can use the same doubled raw-reference ceiling. Each reported classification count is a number of point labels consumed at source boundaries. The reader checks all three edges per point for membership. It separately counts the one to three predicates each point would need if classification stopped at the first boundary edge, as the exact predictor does. These are operation counts, and two-pass classification counts exclude the full pass to deduplicate and count endpoints.

```sh
dotnet "$audit" endpoint-prefix cubes70 Union 348639,348640,368000,387279,387280,1000000 70
tail_inputs endpoint-prefix 55928 Difference 1000000,1600000,1848933,1848934,2000000
tail_inputs endpoint-prefix 519511 Union 500000,1000000,1147763,1147764
tail_inputs endpoint-prefix 1368057 Union 412228,420000,441871,441872,500000
dotnet "$audit" endpoint-prefix cubes20 Union 112800 20
python tools/Z3Pilot/endpoint_prefix.py
```

## Public A/B results

All values below are medians in milliseconds. Gain is the median of the four paired differences, so it can differ from subtracting the two displayed medians. The local estimate is calculated per pair before taking the median. Each row has four pairs. [Raw A/B runs](results/endpoint-cost-ab-2026-11-27.jsonl)

| Case and cap | Early refusal | Original | Opt-in | Paired gain | Local estimate | Residual |
|---|:---:|---:|---:|---:|---:|---:|
| 70 cubes, 348,639 | Yes | 4,994 | 999 | 4,167 | 3,883 | 213 |
| 70 cubes, 348,640 | No | 5,509 | 5,003 | 422 | −12 | 449 |
| 55928 difference, 1,000,000 | Yes | 34,390 | 7,188 | 27,058 | 27,930 | −1,243 |
| 519511 union, 500,000 | Yes | 5,803 | 3,745 | 1,888 | 1,155 | 922 |
| 1368057 union, 500,000 | No | 2,907 | 3,334 | −370 | −12 | −357 |
| 20 cubes, 112,800 | No | 1,648 | 1,387 | 268 | 0 | 268 |

- **Same geometry, adjacent caps.** All four 70-cube pairs at 348,639 stopped early. At 348,640 all four reached the full exact prediction. At equality the endpoint check cost a median 12 ms and skipped no phases. Its apparent 422 ms wall gain belongs to the residual. The 20-cube control accepted and returned the same indexed mesh in each pair. The raw gate and timer granularity make a recorded 0 ms compatible with a small cost.
- **55928 difference.** The paired gains ranged from 21.77 to 29.88 s. Of the 28.02 s median later-phase cost, the second canonical merge alone accounted for a median 15.07 s. Cumulative managed allocation fell from 13.90 to 3.22 GB. The full prediction was 15,120,914 pieces and the complete weighted first-merge certificate was 1,848,934. This large gap identifies work created after the first merge.
- **519511 union.** The median later-phase cost was 1.17 s, with the second merge recorded as 0 ms. The intersection stage favored the opt-in run by a median 0.89 s even though that stage precedes the gate. The earlier single pair favored the original run overall. The skip is reproducible at this cap and the wall-time margin is sensitive to common-phase variation.
- **1368057 union.** At 500,000 both paths reached the exact predictor. The guard phase was 12 ms at the median. Two pairs favored each mode. The lower cap in the prefix study tests a weighted certificate on the same geometry without asserting a time improvement.

## Weighted prefix results

Each row below gives the number of projected edge predicates consumed at source boundaries. The complete snapshot supplies the reference. No weight is required when the unweighted count already proves refusal. [All 21 cap replays with operand hashes](results/endpoint-cost-prefix-2026-11-27.jsonl)

| Case and cap | Weighted refusal | Full classification | One pass | Two passes |
|---|:---:|---:|---:|---:|
| 70 cubes, 348,640 | Yes | 685,980 | 618,258 | 4,540 |
| 70 cubes, 1,000,000 | No | 685,980 | 685,980 | 0 |
| 55928 difference, 1,600,000 | Yes | 3,086,631 | 2,626,319 | 1,229,102 |
| 55928 difference, 1,848,933 | Yes | 3,086,631 | 3,086,631 | 3,086,604 |
| 55928 difference, 1,848,934 | No | 3,086,631 | 3,086,631 | 3,086,631 |
| 519511 union, 1,000,000 | Yes | 1,529,817 | 1,315,458 | 561,358 |
| 1368057 union, 420,000 | Yes | 488,978 | 464,088 | 325,190 |
| 1368057 union, 500,000 | No | 488,978 | 488,978 | 289,604 |
| 20 cubes, 112,800 | No | 54,120 | 0 | 0 |

Near \(U\), knowledge of the complete unweighted count makes a large difference. At 348,640 on the 70-cube input the two-pass schedule only needs the first interior point. It reaches one after classifying 2,270 endpoints. At the million-piece cap, \(S+2N\) is already below the cap, so the two-pass schedule needs no orientations after counting. The 20-cube control resolves from raw references before the point scan.

At 1.6 million on 55928 and one million on 519511, the two-pass schedule uses about 40% and 37% of the full classification predicates, respectively. At one piece below the weighted bound, it needs effectively all of them. At 1368057's 500,000 cap the shrinking upper bound still avoids 41% of classification, even though both schedules fall through to the predictor. These differences arise from both the cap's position within the certified interval and the distribution of interior endpoints in source order.

## Performance implications

55928 at 1.6 million is the strongest candidate for a timed two-pass implementation. The earlier same-cap run measured 24.50 s in phases that a weighted refusal could avoid, and the simulated schedule consumes 1.23 million edge predicates. 519511 at one million has a much smaller measured budget of 0.95 s. The next comparison needs the full counting scan, any allocation or deduplication needed to reuse the point set, and the native point predicates in its timer. It also needs the weighted equality and accepted controls, since these can pay for classification and then still run the full predictor. Retain the current opt-in path as the reference and compare allocations as well as wall time. [Earlier same-cap replays](ENDPOINT_BUDGET.md#which-budgets-can-the-first-merge-certify)
