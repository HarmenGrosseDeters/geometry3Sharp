# Timed two-pass endpoint budget trial

This trial measures an in-process weighted guard at the existing first-merge checkpoint. It uses the arrangement's canonical point IDs, masks, exact projected orientation, cancellation checks and budget reporting. The weighted path is enabled explicitly in a scratch build. The current opt-in unweighted guard is the A/B reference in the same executable. [Cost study and bounds](ENDPOINT_COST_STUDY.md)

## Trial design

The raw scan tests the upper ceiling of one piece per active source plus twice the number of raw endpoint references. When that permits a certificate, the first pass deduplicates canonical endpoints per source and packs the IDs and source spans into two reusable-for-the-second-pass buffers. It stops for any unweighted certificate. Otherwise, if the unweighted count plus all retained endpoints cannot clear the cap, it goes straight to full prediction. The second pass checks exact orientations in source order and stops after the first source span whose lower bound exceeds the cap or whose upper bound is at or below it. Within a source, point order cannot affect either decision. A noncertifying scan continues through the ordinary Boolean computation. [Z3 stopping rules](../../tools/Z3Pilot/endpoint_prefix.py)

The timers start before `ExactMeshBoolean.Compute` and include the raw scan, endpoint storage, classification, GC, and any fallback work within that call. Both modes are warmed, including the weighted classification path. Pair order alternates. Reference predictions for runs where both guards refuse early are obtained outside the pair timers. Every result must agree with the exact prediction, and an accepted candidate must return the same indexed mesh as the reference. An early certificate must be above the cap and at or below the exact prediction.

`weighted.raw`, `weighted.count`, and `weighted.classify` are nested within `merge.endpoint_budget`. These laps report integer milliseconds and approximate process-wide allocation deltas. The end-to-end allocation columns use the precise cumulative managed byte counter. The flattened point buffer uses an `int` index and keeps all counted IDs live until classification ends; larger production workloads require an explicit capacity and allocation policy.

## Reproduce

Use a scratch worktree at the study revision. The patch adds the trial hook, comparator and a scratch-only analyzer registration. The production branch requires no patch to run the existing endpoint benchmarks. Use the same generated operands and inspect the hashes in the output. [Thingi10K tail recipe](ENDPOINT_BUDGET.md#documented-tail-cases) · [Prepared fixtures and external corpora](ENDPOINT_BUDGET.md)

```sh
audit_dir="$PWD/benchmarks/SolidBooleanAudit"
git fetch origin pull/1738/head
git worktree add --detach /tmp/g3-weighted-trial c9bf2264f
cd /tmp/g3-weighted-trial
git apply "$audit_dir/weighted-endpoint-trial.patch"
dotnet build benchmarks/SolidBooleanAudit -c Release -m:1
audit="$PWD/benchmarks/SolidBooleanAudit/bin/Release/net10.0/SolidBooleanAudit.dll"
bash "$audit_dir/run-weighted-trial.sh" stage1 "$(command -v dotnet)" "$audit" "$tail_root" "$fixtures" "$derived" > stage1.jsonl
bash "$audit_dir/run-weighted-trial.sh" stage2 "$(command -v dotnet)" "$audit" "$tail_root" "$fixtures" "$derived" > stage2.jsonl
python "$audit_dir/summarize-weighted-trial.py" stage1.jsonl stage2.jsonl
```

The first stage covers a weighted-only refusal on each of two documented Thingi10K cases, equality with the complete weighted certificate on 70 rotated cubes, and acceptance with both weighted passes exercised on the alternate-diagonal coplanar fixture. The second stage includes common refusals, raw-gated and scanned fallbacks, a second synthetic boundary, two other Thingi10K cases, rational crossings, a fine ABC CAD cut, EMBER intersection and union, and a repaired Objaverse asset.

## Stage 1

The trials ran on Linux x86-64 with .NET SDK 10.0.404, workstation GC, and a two-CPU quota. The table gives medians per mode. Times are for complete calls and allocation is cumulative managed memory. [24 raw pairs with operand hashes and phase counters](results/endpoint-weighted-trial-stage1-2026-11-27.jsonl)

| Case and cap | Pairs | Current guard | Weighted guard | Current / weighted allocated MB | Result |
|---|---:|---:|---:|---:|---|
| Alternate-diagonal cubes, 48 | 12 | 1.44 ms | 1.51 ms | 0.80 / 0.81 | Both accepted, same indexed mesh |
| 70 rotated cubes, 387,280 | 4 | 4.15 s | 4.48 s | 1,044.12 / 1,047.95 | Equality with weighted lower bound, both reached prediction |
| 55928 difference, 1,600,000 | 4 | 28.68 s | 5.62 s | 13,960.72 / 3,260.42 | Weighted-only refusal |
| 519511 union, 1,000,000 | 4 | 5.97 s | 5.07 s | 2,195.06 / 1,679.11 | Weighted-only refusal |

On 55928, the weighted guard took a median 306 ms and the median paired wall-time gain was 21.68 s. On 519511, the weighted guard took 423 ms and the median paired gain was 1.02 s. At weighted equality on the 70-cube input, the guard still spent a median 237 ms classifying endpoints before the full predictor ran. The accepted coplanar control executed both weighted passes in every pair. Its added allocation was about 3 KB per call.

## Stage 2

Times are medians for complete calls in milliseconds. The extra guard column is the median paired difference between the time in each mode's endpoint budget phase. [58 raw pairs with operand hashes and phase counters](results/endpoint-weighted-trial-stage2-2026-11-27.jsonl)

| Case and cap | Pairs | Current guard ms | Weighted guard ms | Extra guard ms | Result |
|---|---:|---:|---:|---:|---|
| 20 rotated cubes, 20,000 | 4 | 82.25 | 106.49 | 1.0 | Both guards certified |
| 20 rotated cubes, 112,800 | 4 | 1,970.45 | 1,958.63 | 0.0 | Both accepted, weighted raw scan skipped counting |
| 70 rotated cubes, 348,640 | 4 | 5,336.68 | 869.04 | 8.5 | Weighted-only refusal |
| 1368057 union, 420,000 | 4 | 2,936.99 | 1,860.64 | 163.0 | Weighted-only refusal |
| 1368057 union, 500,000 | 4 | 3,356.89 | 3,026.11 | 124.0 | Both reached prediction |
| 117959 union, 2,573,320 | 4 | 6,796.71 | 7,256.05 | 1.5 | Weighted-only refusal |
| Rational difference, 18 | 12 | 0.64 | 0.59 | 0.0 | Both accepted |
| ABC 00000382 difference, 21,182 | 6 | 113.76 | 102.12 | 4.5 | Both accepted |
| EMBER 0016 intersection, 28,754 | 6 | 389.65 | 419.05 | 15.5 | Both accepted |
| EMBER 0084 union, 105,802 | 4 | 1,597.32 | 1,515.86 | 65.5 | Both accepted |
| Objaverse 02543290 difference, 3,158 | 6 | 32.73 | 34.16 | 2.0 | Both accepted |

Across both stages all 82 pairs agreed with the exact budget decision. All 50 accepted pairs produced identical indexed meshes. In 20 pairs the weighted guard alone certified refusal. All four 117959 pairs certified, yet the baseline phases they bypassed took a median of only 98 ms, about 1.4% of the current call. The current call was faster at the end-to-end median in that case. At 500,000 on 1368057 and on the accepted CAD and EMBER cases, an apparent wall-time gain cannot be credited to skipping prediction because both modes ran it.

## Decision

This trial supports a targeted follow-up. The gains on 55928, 1368057 at 420,000, and the 70-cube cap at the unweighted bound are large enough to justify investigating a bounded weighted fallback. 117959 had little work left to save, while weighted equality on 70 cubes added about 242 ms to the guard and could not skip prediction. A candidate should preserve the cheap unweighted refusal path, bound the extra storage and classification work, and retain the equality and accepted controls for the next timing comparison.
