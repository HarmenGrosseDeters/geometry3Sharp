# Stage 3B library brief: pure quads by the sign-aware flow (opt-in), with exact fold detection and fallback

Status: brief for the implementing session (owner direction 2026-11-18: option A plus mandatory detection and fallback; B deferred).
Evidence: this directory, `results_round3_*.txt`, `results_round4_gate.txt`, README rounds 3-4. Sources: Huang, Zhou, Niessner,
Shewchuk, Guibas, "QuadriFlow", CGF 37(5) 2018 (sections 3.2-3.6, from the paper only); Ahuja, Magnanti, Orlin, "Network Flows",
9.7 (successive shortest paths); Bommes et al., "Integer-Grid Maps", TOG 32(4) 2013 (the frame-aligned orientation constraints that
the sign-aware cost softens). Add all three to `docs/SOURCES.md`. Never read QuadriFlow, Instant Meshes, LEMON or MiniSat code.

## 1. API (opt-in, no default change, compatible with 1.0.324)

Only additions to `gs.QuadRemesher` (new public members are not an API break; the package validation must stay clean):

```csharp
public bool PureQuads = false;                 // off: stage 2 output, bit for bit as today
public PureQuadStatus PureQuadStatus;          // NotRequested when PureQuads is false
public int InvertedTriangles, FlippedQuads;    // the detection counters of the last pure-quad attempt
public double FlowSeconds, RepairSeconds;      // stage times (as FieldSeconds etc.)
public enum PureQuadStatus { NotRequested, Success, OrientationFolds, FlippedFaces, NotManifold, FlowInfeasible, BudgetExceeded }
```

`QuadRemeshStatus` is unchanged (no new member): a pure-quad failure is not a remeshing failure.

## 2. Fallback contract (never silent)

- `PureQuads = true` and every check passes: `Result` is the pure-quad mesh, `PureQuadStatus = Success`.
- Any check fails: `Result` is the stage 2 quad-dominant mesh, identical to `PureQuads = false`; `Status = Success`;
  `PureQuadStatus` names the first failed check, the counters say how bad. Document this in the XML doc of `PureQuads`.
- Checks, in order: flow full (else `FlowInfeasible`); exact detection below (`OrientationFolds`); the local repair stayed in its
  deterministic budget (else `BudgetExceeded`); after extraction `PolyMeshMetrics`: manifold, 100 % quads, no unpaired triangle
  (`NotManifold`); geometric quad normals against the cluster normals, the spike's flipped-quad test (`FlippedFaces`).

## 3. Exact detection

After the subdivision to |d|_inf <= 1, the greedy pass and the local SAT, every triangle's offsets are small integers in its own
frame; `det(D0, D1)` is an `int` (|value| <= 2). `InvertedTriangles = count(det < 0)`; > 0 means fallback. This is exact (no floating
point), so it cannot miss a fold that is in the integer map. Measured (round 4, 54 runs of option A on 9 procedural shapes x 6
variations, plus 9 runs of the round 2 cost): every structurally bad result (not manifold, < 100 % quads, flipped quads) had
`InvertedTriangles > 0`; undetected bad: 0 of 63. Not proven in general: the flipped-quad, manifold and quad-share checks stay as a
second gate, because the position solve and the extraction are floating point / combinatorial steps after the integer map.

## 4. What moves into the library

New internal file `mesh_ops/QuadFlowRepair.cs` (internal class, `InternalsVisibleTo` for the tests), called by `QuadRemesher` after
the position field:
1. Integer offsets per edge from the position field (spike `BuildOffsets`, `MiddlePoint`) with the real offsets r.
2. Triangle frames and transitions (`BuildTriangles`), the BFS equation rotation, balanced / fixed variables, the greedy B = 0.
3. The min-cost flow: successive shortest paths with potentials (`Mcf.cs`), **cost mode 2 only**: per unit step the marginal
   |d - r| cost (two tiers, +1), times 100 when the step grows |d| against the sign of r (any |r| > 0). Capacity H from 2, +2 until
   full. Put the solver in `solvers/` as an internal class with its own unit tests (tiny networks, against a brute force).
4. Subdivision to |d|_inf <= 1, the greedy vertex moves, the local SAT with the spike's DPLL (rings 1-6, decision budget), as in the
   spike. The budget is a decision count, not a clock (determinism).
5. The tangent-plane least-squares position solve (`SparseCholesky`), extraction (collapse d = 0, pair across hypotenuses, cancel
   2-gon pairs), **and projection of the cluster positions onto the input** (nearest triangle): without it the sphere's one-sided
   Hausdorff is 1.1 s (gate 0.5 s), with it 0.08 s.

Stays out: hard sign constraint (`cost=3`, unmeasured, too slow), `Diag.cs` (boundary-winding diagnosis), cost modes 0/1, `smartsplit`, `strict`, the reweighting rounds (3-5x time for a
partial gain), any CDCL or global fold removal (option B, deferred), the 9 extra shapes of `Program.cs` (tests use their own small
procedural inputs).

Determinism rules for the port: no `Dictionary`/`HashSet` iteration where order matters (the spike iterates both); the Dijkstra
priority queue must break ties by node index (`PriorityQueue` is not stable); everything serial, integers in the flow and the SAT.

## 5. Tests (each <= 5 s; small inputs, s about 2x the spike's)

- Failing on old (label not needed, a feature): `PureQuads_Sphere_IsAllQuadsAndFoldFree` (100 % quads, manifold, irregular <=
  singularities + 10 % of stage 2, `InvertedTriangles == 0`, one-sided Hausdorff <= 0.5 s), same for the torus.
- Fallback: an internal hook that disables the sign penalty (cost mode 1) on the sphere gives `OrientationFolds`, and `Result`
  equals the `PureQuads = false` result face for face. A capped cylinder (measured to fold) as a second, natural fallback case.
- Off by default: `PureQuads = false` gives the same `Result` as before, bit for bit (guards the default).
- Invariance over `Similarity.All()` (pattern of `Result_IsInvariantUnderSimilarities`): 2^k scales give the same pure-quad mesh bit
  for bit, scaled (offsets are computed in units of s, so the integer data must be identical; assert the d arrays too); rotations:
  same status class, face count within 10 %, Hausdorff gate.
- Reuse: `Compute()` twice on one instance, same result and counters.
- Determinism: `Parallel = true` vs `false` (the position field is parallel) and three repeated runs: identical result and counters.
- Solver unit tests: min-cost flow on hand networks; DPLL against brute force (port `dpllselftest`, a few hundred instances).
- A mutation check by hand: break the sign penalty, the det sign, the projection; the tests above must fail.

## 6. Open gates (QUAD_REMESHING.md stage 3), from round 4

Option A over 54 runs: **23 clean, 31 fallback (57 %), 0 undetected bad.** Clean per shape (of 6): torus-align10 6, filleted box 5,
genus 2 5, torus 3, sphere 2, bumpy 1, noisy sphere 1, capsule 0, capped cylinder 0. 15 of the 31 fallbacks have <= 12 inverted
triangles (near misses); capsule, capped cylinder and noisy sphere fold heavily (up to 207).

| gate (stage 3) | clean rows |
|---|---|
| 100 % quads, manifold | 23 / 23 (by the classification) |
| irregular <= singularities + 10 % of stage 2 | 23 / 23 (often exactly the singularity count) |
| Hausdorff <= 0.5 s (with projection) | 23 / 23 |
| corner p95 <= 15 deg | 18 / 23 (stage 2 itself: 17 / 23 on the same rows) |
| time <= 2 x stage 2 | **0 / 23** (median 10x, min 3.2x; flow 0.1-4.5 s, SAT up to 29 s on fallbacks) |

So the time gate stays open, and the fallback rate makes the option useful only as an opt-in. Speed: the paper's multi-resolution
MCF (collapse of d* = 0 edges, section 3.3) and one arc per variable with convex cost instead of one per capacity unit; the SAT
needs a deterministic decision budget so fallbacks stay cheap (the spike spent 18-29 s on capsule / noisy sphere). The library PR
should add the speed work only as far as the 5 s test rule needs it; the 2x gate is an owner decision (keep, relax, or defer).
