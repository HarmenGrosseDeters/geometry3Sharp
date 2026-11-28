# Exact Boolean early endpoint budget

The opt-in guard runs after the first point merge and before crossing preparation. [End-to-end measurements](../../benchmarks/SolidBooleanAudit/ENDPOINT_BUDGET.md)

## Scope and diagnostics

`ExactMeshBoolean.EnableEarlyPieceBudgetCheck` defaults to false. A positive `MaxPieces` still limits the number of arrangement triangles. The arrangement runs the early check after pair intersections and the first canonical point merge. It continues with its current preparation, merge, prediction and triangulation when the check cannot certify refusal. Keep the initial scope on direct Boolean calls. Call paths that do not forward the opt-in setting, including imprint, retain the existing budget behavior.

An early refusal reports `BudgetExceeded` and a separate `piece_lower_bound` count. The error reports that many pieces as a minimum. The `predicted_pieces` count is exact and is present only when a full prediction was computed for an opted-in early-budget run. Legacy callers retain their current diagnostic behavior. A cancelled scan reports cancellation. No output pieces are published on an early stop, and a reused instance clears the lower-bound diagnostic.

## Counting argument

- For each active, nondegenerate source triangle, collect canonical IDs of its known points and both ends of its intersection segments. Remove the three corner IDs and deduplicate within that triangle. Check that the source is eligible for triangulation and survives culling.
- Preparing the triangle retains existing points and adds the endpoints of its unique segments. Removing a duplicate segment with the same canonical ends preserves those ends. A collapsed segment contributes its coincident endpoint. The later merge can rename identities, so the proof requires that the first merge already unifies each equal pair of known coordinates. Region clipping can discard endpoints of embedded regions' flat-edge clips, so it always uses the full prediction path.
- For a triangulated disk with `i` extra interior vertices and `e` extra boundary vertices the exact prediction is `1 + 2*i + e`. A known subset of distinct non-corner vertices contributes a lower bound of one piece plus the subset size. Sum the lower bounds and refuse only when the sum is strictly greater than the budget. For noncoplanar pairs, the emitted endpoints lie in the overlap of the two closed plane sections. For coplanar pairs, the clip emits only points in the closed source triangle. Preparation retains both sets of endpoints.
- Before allocating deduplication sets, count raw point references and two references per segment. The sum plus the active source count is an upper bound on the endpoint certificate. If it is at or below the budget, skip endpoint deduplication and use the original path. The raw reference sum gives no reason to refuse.
- Subtract each raw contribution from the remaining positive budget. Stop the raw scan when a contribution first exceeds the remaining amount. This avoids overflow even if the full raw sum would exceed `Int64.MaxValue`. Each raw contribution fits in a `long` because the point and segment counts fit in `int`. A source contributes at most `1L + int.MaxValue` to the deduplicated lower bound and there are at most `int.MaxValue` source triangles, so their sum fits in `long`.

The existing arrangement follows Zhou et al.'s split, triangulate and classify method. The guard moves one budget decision earlier within that arrangement. [Arrangement sources](../sources/exact-arithmetic-and-booleans.md) · [Z3 arithmetic obligations](../../tools/Z3Pilot/endpoint_budget.py)

Run `python tools/Z3Pilot/endpoint_budget.py` to replay the local counting and geometry obligations. The models cover the strict budget comparison, the raw-reference filter, signed subtraction, and the section and edge interval tests. They require retained canonical endpoints. A separate satisfiable check shows that counting discarded candidates can reject a valid budget. [Solver results](../../tools/Z3Pilot/results/endpoint_budget_2026-11-27.json)

## Acceptance criteria

- Differential against the existing public guard and unlimited calls on accepted, equal-budget and rejected examples. Check exact output identity for accepted calls, refusal with a valid lower bound, and cancellation with no published output.
- Cover culling, triangulation masks, degenerate inputs, shared corners, duplicate endpoints, coplanar segments, endpoint aliases and triple crossings. Include parallel equality, reuse after refusal, and power-of-two frame invariance. The existing predictor uses Int32 per source. Comparison to that predictor assumes its per-source value fits. The early certificate uses Int64 and applies to the mathematical piece count.
- Measure end-to-end time and allocations in the same run order for the tight 70-cube rejection and the 20- and 35-cube accepted controls. A candidate should improve the tight rejection by at least 10% and keep passing-call time and allocation overhead within 5% on these controls. Include loose budgets and a disabled-budget control.
- Keep the crossing-prefix strategy as a benchmark comparator. Its exact-coordinate scans allocate substantially more on passing cases. Test that any future prefix design reduces that overhead before adding it to the runtime path.
