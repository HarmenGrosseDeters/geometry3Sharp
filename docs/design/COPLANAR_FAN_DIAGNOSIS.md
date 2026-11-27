# Coplanar fan overlays (#1630): re-measurement, per-stage diagnosis, upper bound and options

Status: diagnosis and options, nothing built (2026-11-27). Refs #1630. This note re-measures the case independently and adds
what [COPLANAR_PROPAGATION.md](COPLANAR_PROPAGATION.md) (the design of 2026-11-25) does not have: a reproduction from an exact
rational circle, counts for every stage, a second (realistic) case, an upper bound for the whole union, and a choice between
options. The design of the walk itself stays in that document; this one decides what to build first.

## 1. Reproduction

Procedural, no mesh files, platform independent (no `sin`/`cos`):

- A capped cylinder, radius 0.5, height 1. The ring points are the rational parametrisation of the circle,
  `(x, z) = r (1 - t^2, 2 t) / (1 + t^2)` for `t = k / K`, `k = -K .. K - 1`, mirrored for the left half and sorted by angle: `4K`
  ring points. The caps are fans from the cap centres. (The generator's float angles of the issue give the same counts: 466,608
  pieces at 1920 triangles against 464,688 here.)
- **Case S (the issue):** a copy shifted along x by 2 ulps of the x extent, so every shifted coordinate is exact, united with the
  original (`ExactMeshBoolean`, `Union`, defaults).
- **Case R (realistic):** the same copy shifted by 0.25 (a second cylinder on a parallel axis that shares the cap planes). The
  union is a true union (3852 triangles at 1920 inputs), not the identity.

Both caps of each cylinder are fans of n triangles; the fans of the two cylinders overlay into Theta(n^2) pieces (every wedge
of one fan crosses a few wedges of the other near the apex in S, a constant fraction in R).

## 2. Measurements (Release, net10.0, 4 cores, one run each after a warm-up; counts exact, times noisy by 10 to 20 %)

Case S, per doubling of the ring (n points per circle, 2n side + 2n cap triangles per cylinder):

| ring n | input tris | pieces | patches | union ms | stacks ms | winding ms | simplify ms | coplanar tests | per piece | winding plane sides | per piece |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 32 | 128 | 2,352 | 548 | 469 | 63 | 34 | 34 | 25,432 | 10.8 | 55,232 | 23.5 |
| 60 | 240 | 7,728 | 1,864 | 640 | 158 | 42 | 67 | 155,744 | 20.2 | 395,128 | 51.1 |
| 120 | 480 | 29,808 | 7,324 | 2,051 | 630 | 265 | 354 | 1,197,064 | 40.2 | 3,307,360 | 111.0 |
| 240 | 960 | 117,168 | 29,044 | 7,612 | 3,241 | 623 | 1,556 | 9,392,504 | 80.2 | 27,049,880 | 230.9 |
| 480 | 1,920 | 464,688 | 115,684 | 46,278 | 21,970 | 4,127 | 8,823 | 74,426,584 | 160.2 | 218,783,132 | 470.8 |

Growth per doubling (last three steps): pieces x3.9, x3.9, x4.0 (quadratic); coplanar tests x7.7, x7.8, x7.9 and winding plane
sides x8.4, x8.2, x8.1 (cubic: the per piece count doubles with n); union x3.2, x3.7, x6.1.

Stage times at ring 480 (ms, of 46,278): intersections 1,149; merge 2,944 (of which `merge.crossings` 2,744); triangulation
6,558; classification 26,654 (stacks 21,970, winding 4,127, rest 557); assembly 8,988 (simplify 8,429, order 556); rounding 118.
At ring 240: 428; 453; 1,107; 3,629; 1,557; 77. The other stages grow x5.9 to x6.9 at the last step, but per piece (triangulation
17.5, 15.4, 9.4, 14.1 microseconds at rings 60 to 480; merge 8, 6, 4, 6; simplify 9, 12, 13, 19) they are flat to mildly rising:
they are linear in the pieces, with the cache and allocation effects of 465,000 pieces, not a further factor n.

Case R (parallel axes, offset 0.25), same code:

| ring n | pieces | union ms | stacks ms | winding ms | simplify ms | tests per piece | plane sides per piece |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 60 | 6,064 | 613 | 61 | 44 | 47 | 10.3 | 8.2 |
| 120 | 22,480 | 469 | 135 | 78 | 47 | 18.6 | 17.3 |
| 240 | 86,448 | 2,265 | 751 | 577 | 217 | 35.1 | 35.7 |
| 480 | 339,504 | 5,908 | 1,994 | 1,258 | 1,014 | 67.9 | 72.4 |

Same structure, a smaller constant: stacks and winding are 55 % of the union at ring 480 and the tests per piece still double
per doubling.

**What every piece looks like.** In both cases every piece that has a stack lies in exactly two triangles: mean stack size 2.00,
maximum 2 (458,904 pieces at ring 480 in S). The true answer per piece is a source and one other triangle, while the scan tests
n/3 (S) or n/7 (R) candidates per piece, and every candidate really touches the source (the box filter removes under 2 %).

## 3. Diagnosis

1. **Pieces: Theta(n^2), inherent.** The arrangement of two fans from different apexes has Theta(n^2) cells. The arrangement
   code finds them (candidate pairs 351,368 and intersecting pairs 120,490 at ring 480) and the stages after it are linear in
   them. No change of an exact stage reduces this; only a different output (merging pieces below the resolution of doubles)
   would, and that is not an exact operation. So the floor of any exact fix is linear in the pieces.
2. **Stacks: cubic, because the test is per piece against the whole list.** `coplanar_stack` runs the exact test against every
   triangle of `src.Coplanar` (n/3 survive the box filter, all of them touch the source), for a stack of size 2. 47 % of the
   union at ring 480 (21,970 of 46,278 ms), 43 % at 240, 31 % at 120.
3. **Winding: cubic, because the patches multiply.** Pieces with different stacks do not join into one patch: patches = pieces / 4.
   Each patch shoots an exact ray that meets O(n) wedges: 1,891 plane sides per patch at ring 480. 9 % of the union at 480, 8 % at
   240, 13 % at 120.
4. **Everything else is linear in the pieces** (about 45 microseconds per piece in S, cache effects included): triangulation 14 %,
   simplify 18 %, merge 6 %, intersections 2.5 % at ring 480.

## 4. Upper bound (best case if the two cubic stages were linear in the pieces)

A walk with O(1) re-tests per piece (stack size 2: one edge crossing changes one membership) costs the seed scans, the sum of
`|src.Coplanar|` over the sources (O(pieces): about 4 n^2 = 0.9 M tests at ring 480 for the four cap fans, against 74 M), plus 1 to 3 microseconds per piece; winding
regions replace patches (about one per cap source: O(n) rays of O(n) sides). Estimate = today's other stages + 3 microseconds
per piece + 10 microseconds per winding region:

| case | ring | today ms | stages that stay ms | with the walk (estimate) ms | saving |
|---|---:|---:|---:|---:|---:|
| S | 120 | 2,051 | 1,156 | about 1,250 | 39 % |
| S | 240 | 7,612 | 3,748 | about 4,200 | 45 % |
| S | 480 | 46,278 | 20,181 | about 21,700 | 53 % |
| R | 240 | 2,265 | 937 | about 1,050 | 54 % |
| R | 480 | 5,908 | 2,656 | about 2,950 | 50 % |

The saving is far above 10 %, so the stop rule does not apply. It applies only where the fans are small (rings under 32:
stacks + winding under 20 % of the union) and to inputs with no large coplanar cluster, which are not changed at all (the
walk is switched on by a size threshold, [COPLANAR_PROPAGATION.md](COPLANAR_PROPAGATION.md) section 5).

**The floor is above the 20 s rule.** At 1,920 + 1,920 triangles case S stays at about 22 s after the walk, because the pieces
(465,000) cost about 45 microseconds each in the stages that are not touched. #1630 can then be closed on its growth
(at most x4.5 per doubling instead of x7 to x8), not on the absolute time at 1,920 triangles; reaching under 20 s would need
the per piece cost of triangulation and simplify to fall by 10 %.

## 5. Prior art (papers only; no code read, nothing from CGAL, libigl or other GPL / MPL code)

- Zhou, Grinspun, Zorin, Jacobson, *Mesh Arrangements for Solid Geometry*, ACM TOG 35(4), 2016: winding numbers are labels of
  cells, propagated across patches by adjacency, and a ray is cast only to seed a connected component. The walk below is the same
  propagation restricted to the coplanar cells of one source triangle.
- Devillers, Pion, Teillaud, *Walking in a triangulation*, IJFCS 13(2), 2002: traversal across shared edges, here as a
  traversal rather than for point location.
- Stolfi, *Oriented Projective Geometry*, 1991: the exact line keys (Pluecker coordinates) of the design as the independent
  check of the edge labels.
- Cherchi, Pellacini, Attene, Livesu, *Interactive and Robust Mesh Booleans*, ACM TOG 2022 (already in
  `docs/sources/exact-arithmetic-and-booleans.md`): classification by patches and the same per piece ray, with the same
  coplanar caveat.

## 6. Options

| option | what | exact code touched | proof obligations | output | saving (S, ring 480) | risk |
|---|---|---|---|---|---:|---|
| **A** stack walk only | the walk of [COPLANAR_PROPAGATION.md](COPLANAR_PROPAGATION.md) section 2 for the stacks; today's per patch winding stays | `coplanar_stack`, labels of the output edges | completeness (A) of section 3 only | bit for bit the same (lists in the same order) | stacks 22.0 s -> about 1 s: 46.3 -> about 25 s (45 %) | medium |
| **B** the whole walk | A plus winding regions instead of patches | A plus `patch_winding` and the patch decision | (A) and the label obligation 3 (fold, fin) | bit for bit the same | 46.3 -> about 22 s (53 %) | high: obligation 3 is a new proof about missing labels |
| **C** budget only | `MaxPieces` exists; add a `predicted_pieces` warning to the status text of the 20 s rule | none | none | none | 0 | none, no fix |
| **D** merge pieces below the resolution | snap near-coincident pieces | arrangement | none exact | changes the output | large | rejected: not an exact operation, changes the meaning of the result |

Option A removes the largest single stage, has the smaller proof (the winding labels are not used), keeps every decision as it
is and can be built behind the same reference switch. Option B adds the winding stage (4.1 s of 46 s here, 1.3 s of 5.9 s in R)
for the extra proof of obligation 3; its saving is 9 % of the union at ring 480, 8 % at 240.

## 7. Recommendation

1. Build **A** first, behind `CoplanarWalk` (default on above the size threshold), with the reference switch, the identity
   tests, the growth test (`coplanar_tests` at most x4.5 per doubling at rings 60, 120, 240) and the mutation gate of the
   design. Before the build, a spike of the stacks stage alone on the S series (CLAUDE.md: upper bound first, done in section 4).
2. Decide **B** after A, on its own measurement: it is worth 8 to 13 % of the union and carries the new proof.
3. Take #1630's acceptance as the growth rate, not the absolute time at 1,920 triangles (section 4). Open a separate issue for
   the per piece cost of triangulation and simplify (45 microseconds per piece, rising from 83 to 100 microseconds per piece
   in total between rings 60 and 480).
4. No output change in any option except D, which is rejected.

## Appendix: the scratch reproduction (not committed)

```csharp
static DMesh3 Cyl(int K, double r = 0.5, double h = 1.0)
{
    var ring = new List<(double, double)>();
    for (int k = -K; k < K; ++k) { double t = (double)k / K; ring.Add((r * (1 - t * t) / (1 + t * t), r * 2 * t / (1 + t * t))); }
    for (int k = -K; k < K; ++k) { double t = (double)k / K; ring.Add((-r * (1 - t * t) / (1 + t * t), -r * 2 * t / (1 + t * t))); }
    ring = ring.OrderBy(p => Math.Atan2(p.Item2, p.Item1)).ToList();      // then drop duplicates (t = +-1 repeat the points on the z axis)
    // bottom ring 0..n-1, top ring n..2n-1, cap centres 2n and 2n+1; per i: (c0, i, j), (c1, n+j, n+i), (i, n+j, j), (i, n+i, n+j)
}
// B = A shifted in x: 2 * (BitIncrement(maxAbs) - maxAbs) for case S, 0.25 for case R
var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { RecordStacks = true };
eb.Compute();   // eb.PhaseMilliseconds["classification.stacks_us"], eb.Counts["coplanar_tests"], eb.Counts["pieces"], eb.StacksPerPiece
```
