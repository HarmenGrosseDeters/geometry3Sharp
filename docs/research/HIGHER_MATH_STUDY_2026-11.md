# Higher mathematics for the central algorithms: three ideas, measured upper bounds (2026-11-25)

A study of three ideas from higher mathematics, approved by the owner on 2026-11-25. It follows the style of
[MESHING2D_HP6_HP7_SPIKE_2026-11.md](MESHING2D_HP6_HP7_SPIKE_2026-11.md): prior art, building blocks, a measured upper bound,
and options. **No library code was changed.** The probes are in [benchmarks/HigherMathStudy](../../benchmarks/HigherMathStudy/README.md).
Every number below names its mode there, and the raw outputs are in its `results/` folder. All inputs are procedural. The
numbers are single runs on a Linux container with 4 cores (Xeon 2.1 GHz) and .NET 10.0.115. The owner decides on every option.

## Summary

| Idea | Upper bound (measured) | Stop rule | Leaning |
|---|---|---|---|
| 1. Valuation certificate for the exact Booleans (V and χ by inclusion-exclusion) | It catches nothing that the existing checks (closedness, C1 of the test certificate) do not already catch. The identity follows from the pieces tiling the input surfaces, so a mislabelled piece or a whole mislabelled component passes it (7 defects injected, section 1.4). It costs one extra Boolean plus 1.0-1.8 times the operation for the exact volumes. It says nothing about the rounding step, which is where #1488's case lies | no gain in power over the existing checks: stop as a certificate | no library mode. Optionally a tolerance-free test oracle for the exact path (option 1b) |
| 2. Multi-modular zero tests instead of BigInteger | BigInteger is **1.7 %** of the exact Boolean's busy time on the degenerate stratum and **0.9 %** on general position. The exact stage of `orient3d` that returns 0, the only part a modular test could take over, is **0.35 %**. The bound is below 2.1 % | below 10 %: stop | no build. The next candidates are listed in section 2.5 |
| 3. Soup orientation as Z2 synchronization (spectral) | **0 percentage points** on #1548 (k = 5, 3, 2): a weld by position plus the library's `MeshRepairOrientation` is already exact (1.000). On frustrated graphs the converged spectral method equals BFS and is worse than the library (0.484 vs 0.880, 0.917 vs 0.958), at 45 times BFS's time | 0 / negative: stop the spectral design | no Z2 solver. The lever is the links, not the solver: radial-neighbour links at non-manifold edges remove every wrong link (BFS 1.000 on both frustrated cases, +12 and +4 points over the library; section 3.5) |

**How others solve it, in one line each.**
1. Inclusion-exclusion of volumes serves as a test oracle with a tolerance (this repository's metamorphic tests). No published
   certifying Boolean uses valuations as its certificate (the searches of [CERTIFYING_BOOLEANS_2026-11.md](CERTIFYING_BOOLEANS_2026-11.md)).
2. Modular sign evaluation is due to Brönnimann, Emiris, Pan and Pion (SoCG 1997). Libraries in practice use filters and then
   expansions or multiprecision, and modular arithmetic for determinant zero tests and resultants.
3. Patch-level greedy and graph methods are used (Borodin et al. 2004; this library's patch spanning forest). Takayama et al.
   (2014) report that the global, energy-based orientation of soups by generalized winding numbers is "fundamentally flawed" for
   handmade meshes. Z2 synchronization (Singer 2011; Cucuringu, arXiv 2015) is robust to *random* outliers, not to the *structured*
   ones that non-manifold edges produce.

## 1. Idea 1: an integral-geometry certificate for the exact Booleans

### 1.1 The question

Volume and the Euler characteristic are valuations: V(A∪B) + V(A∩B) = V(A) + V(B), and χ likewise (Hadwiger 1957). On the
exact, pre-rounding result both sides are exactly computable. The volume comes from the divergence theorem over the rational
corners; χ = V − E + F per shell, with χ(∂M) = 2χ(M). The questions:
- Can this certify a Boolean result with no tolerance?
- Which defects does it catch, and which does it miss?
- What does it cost relative to the operation?

### 1.2 Building blocks in the library

- `ExactMeshBoolean.ComputeExactVertices` gives the rational corners of `Result`. With `SafeRounding = false` they belong to
  exactly the triangles of `Result`. With the repair on, they are the corners from before the repair, for the vertex ids that
  remain (the field's XML comment).
- `ExactPoint3` (homogeneous BigInteger), `ExactPoint3.Decompose` (doubles to dyadics) and `Det3`.
- The test-only certificate `tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs` (C0-C5). Its C1 checks that the pieces lie
  in their source triangles and tile them.
- `ExactBooleanMetamorphicTests` already checks inclusion-exclusion of the volumes, *within the rounding tolerance* of the
  rounded results (line 151).

### 1.3 Why the identity is weaker than it looks

Write each result's volume by the divergence theorem as the flux through its pieces.
- Every piece of ∂A lies in exactly one of ∂(A∪B) (outside B) and ∂(A∩B) (inside B), and likewise for ∂B.
- Coplanar pieces with the same orientation go once to each result.
- So the flux of U plus the flux of I equals the flux of all pieces of ∂A and ∂B, which is V(A) + V(B).

This holds whatever label each piece gets. The identity is a conservation law of the tiling, not a check of the classification. The same
argument holds for χ when it is counted on the meshes: every vertex and edge of an intersection curve appears once in U and once
in I, as it does once in A and once in B. A misclassification, the main failure mode of a Boolean, therefore passes, and so
does an inside-out or swapped whole component.

### 1.4 Measured: which defects it catches

Mode `valuation`, `results/valuation.txt`. Five procedural cases (two spheres at n = 24 and 64, a box and a rotated box, coplanar
boxes with shared faces, two spheres where the box swallows one). The identity is exact 0 on all five unmodified results, and
χ(U) + χ(I) = χ(A) + χ(B) on all five. Seven defects were injected into the exact union:

| defect | caught? | note |
|---|---|---|
| one triangle dropped | caught (5 of 5) | closedness catches it too. With the origin as apex, the drop was missed on the coplanar boxes (a face in a plane through the apex contributes 0). The probe therefore uses a generic dyadic apex |
| one triangle reversed | caught (5 of 5) | closedness in the winding sense catches it too |
| one triangle moved from U to I (mislabelled piece) | **missed (5 of 5)** | by section 1.3. Closedness catches it, because the results open up |
| one exact corner moved by 2^-40 out of its plane | caught (5 of 5) | closedness does **not** catch this: it is the one defect class the identity adds over closedness. C1 of the test certificate catches it |
| one corner moved inside its flat fan (coplanar boxes) | **missed** | a volume-preserving geometric error. C1 catches it |
| the rounded results instead of the exact ones | drift of 2e-19 to 3e-17 (0 on the box case) | the identity is not exact after rounding, so it needs a tolerance there |
| a whole closed component moved between U and I | **missed**, and both results stay closed | the classic anchor-ray failure: neither the identity nor closedness sees it |

### 1.5 Measured: cost

From the same run. One row per case. The certificate costs the complementary Boolean plus four exact volumes.

| case | triangles A+B | union ms | intersection ms | exact volumes ms | certificate / operation |
|---|---|---|---|---|---|
| spheres n = 24 | 12 696 | 624 (includes JIT) | 111 | 485 | 1.0 |
| spheres n = 64 | 95 256 | 672 | 543 | 1194 | 2.6 |
| two spheres + box | 5 412 | 36 | 50 | 43 | 2.6 |

The exact volume needed care.
- A first version summed the rationals in a chain: 44 s on the 95k-triangle case.
- One common power of two for all corners, grouping by the odd part of the denominators, and a balanced (tree) summation
  brought it to 1.2 s.
- A modular evaluation of the identity (idea 2) would be faster again. But a certified zero needs a CRT bound of the order of
  the sum of all denominator bits, so the modular form is only a probabilistic check.

### 1.6 The rounding step and #1488

The latest review of #1488 (2026-11-25) found that `SolidBooleanPipeline`'s RoundingVolume check pools a rounding allowance over
the whole result. A thin component's allowance (length × area) can then hide a real loss in a thick one. Its suggested fix is
a bound per component. The valuation identity does not reach that case:
- It lives on the exact result, before the rounding.
- On the rounded results it differs from 0 by the drift, so it needs the same kind of tolerance again (row "rounded" above).

What the exact side *can* give #1488 is a tolerance-free "before" per component: the exact volume of each closed component of
the pre-rounding result, which this probe computes. The allowance for the rounding stays, per component, as the reviewer
proposes. That is a refinement of the reviewer's fix, not a certificate.

### 1.7 Options (idea 1)

| # | option | gain | effort | risk |
|---|---|---|---|---|
| 1a | a library "valuation certificate" mode | none over closedness + C1. It misses misclassification, component swaps and tangential errors, and it says nothing about rounding | M | false confidence: a "certified" result that is wrong |
| 1b | a tolerance-free test oracle: the exact identity on the pre-rounding result, in the fuzz and metamorphic tests next to the toleranced one | it catches exact-geometry errors below the rounding tolerance without the heavy C1 checker. It costs one extra Boolean plus about 1-2 times the operation in tests | S (test harness only) | low |
| 1c | exact per-component "before" volumes for #1488's RoundingVolume | the "before" side becomes exact. The allowance stays | S-M, in the owner's #1488 work | low |
| 1d | nothing | | | |

**Leaning:** 1d for the library. 1b only if a future change touches the exact corners (the construction of implicit points),
where it is a cheap extra guard. 1c is for the owner's #1488 session to weigh.

## 2. Idea 2: multi-modular arithmetic for the exact zero tests

### 2.1 The question

Can the exact zero, equality and degeneracy tests be evaluated modulo several 61/62-bit primes with a magnitude bound (CRT)
instead of BigInteger? Coplanarity, collinearity and equal plane directions are such tests. The signs still need the
existing filters.

### 2.2 Prior art (papers only)

- H. Brönnimann, I. Z. Emiris, V. Y. Pan, S. Pion, *Computing exact geometric predicates using modular arithmetic with single
  precision*, SoCG 1997 (abstract and fragments read):
  - The sign of an integer polynomial comes from residues modulo several primes in single precision, recombined by CRT, with a
    floating-point step for the sign.
  - The authors note that arithmetic filters still decide most cases first.
- The zero test alone is the easy half of modular evaluation. A residue that is not 0 proves "not zero". A proof of "zero"
  needs primes whose product exceeds twice the magnitude bound.
- The library already avoids BigInteger in its hot paths:
  - the allocation-free limb kernel `ExactLimbSigns` and `ExactLimbPrepared` (P2, AGENT_WORKFLOW section 3);
  - the fixed-width `orient3d_int` path;
  - Shewchuk's expansions in `AdaptivePredicates`.

### 2.3 Where the exact code spends time in BigInteger (the upper bound)

Mode `bigint` under `dotnet-trace` (thread-time sampling), folded by `fold_speedscope.py`; `results/bigint_profile.txt`. The
share is inclusive: it counts the time in the `System.Runtime.Numerics` frames and the GC they trigger below them. The
denominator is the busy time under `ExactMeshBoolean.Compute`.

| stratum (workload) | busy time | BigInteger arithmetic | BigInteger-keyed containers |
|---|---|---|---|
| degenerate: coplanar boxes, the 9 `DegenerateFamilies` at 2^-20 and 2^-40, each against a shifted copy and against itself, 5 repetitions | 16.2 s | **1.7 %** | 0.2 % |
| normal: two spheres in general position, 5 repetitions | 1.4 s | **0.9 %** | 0.3 % |

The callers on the degenerate stratum are `ExactPoint3.Create` (gcd canonicalization) at 0.7 %, `ExactMeshArrangement.same_plane`
at 0.5 %, `edge_plane_exact` at 0.3 %, and the rest below 0.1 % each.

The plane key of `ExactWindingCache` from PR #1546 is not on master and was not measured. On master, ExactWindingCache's
BigInteger code is the edge angle at the branch cut, a rare path, and it did not appear in the profile.

### 2.4 The zero share of the exact `orient3d` stage

Modular arithmetic can replace an exact evaluation only where the result is exactly 0. A nonzero result needs its sign, and the
sign comes from the expansions in any case.

The probe measured this with temporary counters in `orient3d_exact` (`results/orient3d_zero_counters.patch`, never committed;
`results/orient3d_zero.txt`). The degenerate stratum, single runs, without the first case, which carries the JIT:

| exact stage of `orient3d` | time | share of the Boolean time (4.7 s) |
|---|---|---|
| calls that return 0 | 16.6 ms | **0.35 %** |
| calls that return a sign | 767 ms | 16.2 % (mostly FoldedCluster k = 40: 468 ms of 2.2 s) |

The Stopwatch inflates both rows, so they are upper bounds. The total bound for idea 2 is therefore below 2.1 %
(BigInteger plus zero-result exact stages), far below the 10 % threshold. **Stop, no spike.**

### 2.5 The next candidates the measurement points to

The stop rule rejects the design, not the goal. These are the hotspots of the same profile (the innermost g3 frame, `--top`),
on the degenerate stratum:

| candidate | share | quick bound and remark |
|---|---|---|
| `orient3d` exact stage with a nonzero sign (expansions) on the slow tail | 16 % of the stratum; 21 % of FoldedCluster k = 40 | sign-bearing, so modular arithmetic cannot help. A tighter filter stage (a semi-static or interval stage before the expansions, cf. [SYMBOLIC_TAYLOR_ERROR_BOUNDS_2026-11.md](SYMBOLIC_TAYLOR_ERROR_BOUNDS_2026-11.md)'s stage 1.5) bounds at ≤ 16-21 % on that stratum. It clears 10 % on the slow tail only |
| `TriangleTriangleFilter.by_projection` | 11.9 % | already studied in [TRI_TRI_FILTER_UPPER_BOUND_2026-11.md](TRI_TRI_FILTER_UPPER_BOUND_2026-11.md) and in the hotspots table |
| `MeshRoundingRepair` (`improves`, `canonical_of`, `intersects`, `near`, `zip_strip`) | about 24 % together | the rounding repair on degenerate inputs. Its own study is [ROUNDING_COST_2026-11.md](ROUNDING_COST_2026-11.md) |

None of them is a "higher mathematics" lever. They are listed for the coordinator, not pursued here.

## 3. Idea 3: orientation of triangle soups as Z2 synchronization

### 3.1 The question

Orient a soup by synchronization over Z2:
- Unknown signs x_i, one per triangle.
- A signed graph with one measured relation s_ij per pair of adjacent triangles.
- The spectral relaxation: the leading eigenvector of the normalized signed adjacency (Singer 2011; Cucuringu 2015).

Unwelded soups get their graph from a weld at a tolerance. Context: issue #1548. `MeshRobustRepair` turns an unwelded
PolyCube(4,3,2) soup with every k-th triangle reversed into a clean but wrong solid. The owner fixes it locally with a greedy
weld and orientation. This study does not touch `MeshRobustRepair`.

### 3.2 Prior art (papers only)

- A. Singer, *Angular synchronization by eigenvectors and semidefinite programming*, ACHA 30(1), 2011 (abstract read). The
  eigenvector method is stable under many *random* outliers.
- M. Cucuringu, *Synchronization over Z2 and community detection in signed multiplex networks with constraints*,
  arXiv 1504.04061, 2015 (abstract read). Spectral and SDP for Z2. The SDP is the most robust and the most expensive.
- K. Takayama, A. Jacobson, L. Kavan, O. Sorkine-Hornung, *Consistently orienting facets in polygon meshes by minimizing the
  Dirichlet energy of generalized winding numbers*, arXiv 1406.5431, 2014 (abstract read). A binary flip per patch from a
  quadratic form, a close relative of Z2 synchronization. Its own abstract calls the approach "fundamentally flawed" for most
  handmade meshes.
- F. Borodin, G. Zachmann, R. Klein, *Consistent normal orientation for polygonal meshes*, CGI 2004 [from memory]. Patches
  and visibility-based global orientation, the scheme `MeshRepairOrientation` resembles.
- T. M. Murali, T. A. Funkhouser, *Consistent solid and boundary representations from arbitrary polygonal data*, I3D 1997
  [from memory]. Space partitioning and solidity labels instead of orientation propagation.

### 3.3 Building blocks in the library

- `MeshRepairOrientation.OrientComponents`: consistent patches, then a maximum spanning forest of the patches weighted by the
  number of seam edges. That is a greedy solution of the same signed-graph problem.
- `SolveGlobalOrientation`: area-weighted ray voting per component.
- `MeshAutoRepair` (weld, orient, fill) and `WindingContouring.OrientedCopy` (inverted components, then the signed volume).

### 3.4 Measured

Mode `orient`, `results/orient.txt` (`G3_Z2_ITERATIONS=3000`) and `results/orient_iterations.txt` (300, 3000 and 30000
iterations).
- The score is the share of the soup's area that ends up correctly oriented.
- "Wrong links" counts the measured relations s_ij that contradict the truth: the frustration of the graph.
- The BFS, Z2 and library columns all run on the same graph, a weld of the soup's corners within eps.
- The global sign is the signed volume per component for BFS and Z2, and the library's ray voting for the library.

| case (tris) | wrong links / links | BFS | Z2 spectral (converged) | library weld + `MeshRepairOrientation` | `MeshAutoRepair` (output tris) | `OrientedCopy` steps |
|---|---|---|---|---|---|---|
| #1548 PolyCube(4,3,2), every 5th (104) | 0 / 156 | 1 | 1 | 1 | 0.981 (134) | 0.798 |
| #1548, every 3rd (104) | 0 / 156 | 1 | 1 | 1 | 0.663 (200) | 0.663 |
| #1548, every 2nd (104) | 0 / 156 | 1 | 1 | 1 | 0.538 (108) | 0.500 |
| cube, jitter 1e-4, 30 % random flips, eps 1e-3 (104) | 0 / 156 | 1 | 1 | 1 | 0 (0: empty) | 0.750 |
| sphere n = 16, jitter 1e-4, 50 % random, eps 1e-3 (2700) | 0 / 4050 | 1 | 1 | 1 | 0 (0: empty) | 0.507 |
| cube, 10 % triangles dropped, every 3rd (90) | 0 / 118 | 1 | 1 | 1 | 0.544 (158) | 0.667 |
| checkerboard cells, non-manifold edges, every 3rd (192) | 84 / 456 | 0.484 | 0.484 | **0.880** (52 not appended) | 0.714 | 0.667 |
| two blocks, gap 5e-4 < eps 1e-3, every 3rd (96) | 32 / 208 | 0.917 | 0.917 | **0.958** (16 not appended) | 0.740 | 0.667 |
| cube, jitter 1e-3 = eps (welds fail, 71 components) (104) | 0 / 33 | 0.490 | 0.490 | **0.654** | 0 (0: empty) | 0.663 |

Times on the 2700-triangle sphere:
- BFS: 2-3 ms.
- Z2 spectral: 15 ms with 300 iterations, but **wrong (0.681)**. Power iteration has not converged, because the spectral gap of
  a mesh graph shrinks with its diameter. It takes 89-134 ms with 3000 iterations (correct) and 860 ms with 30000.
- The library's weld and orientation: 69-82 ms. `MeshAutoRepair`: 145-178 ms.

Findings:
- **On #1548 the upper bound of any new solver is 0 points.** The graph of an exactly coincident soup has no frustration, so
  BFS, Z2 and the library's orientation are all exact once the soup is welded by position.
- The failure of #1548 is upstream of the solver. `MeshAutoRepair` leaves the flips in place: at k = 3 its 0.663 is the share
  of the unflipped triangles. Its weld does not join edges that run the same way (the comment in `MeshAutoRepair.Apply`:
  "MergeEdges never joins edges that run the same way"), so a flipped triangle never becomes adjacent to its neighbours.
- At jitter 1e-4 (relative 2e-5) `MeshAutoRepair` returns an empty mesh on the cube and the sphere: its default weld tolerance
  is relative 1e-8. This is a side finding for the owner's #1548 work.
- On frustrated graphs the converged spectral solution equals BFS's and loses to the library's patch spanning forest. The
  wrong links of a non-manifold edge are structured, not random: two solids touching at an edge give four triangles, and the
  four non-adjacent pairs among them are measured wrong. A global relaxation averages them in rather than outvoting them.
- When the welds fail (jitter = eps), the graph carries almost no information (33 links, 71 components). No solver helps;
  only the global step (ray voting) does.

### 3.5 The next candidate the measurement points to: the links, not the solver

At an edge with more than two triangles, link only the neighbours in the radial order of the triangles around the edge. Each
such pair bounds one wedge, solid or empty, and correctly oriented triangles are consistent across it. This is the pairing
the exact Boolean uses at non-manifold edges.

Measured in the same run, last column of `results/orient.txt`:
- **0 wrong links in all nine cases.**
- BFS on that graph reaches **1.000** on the checkerboard (library 0.880) and on the two blocks (library 0.958).
- The other cases are unchanged; the failed-weld case stays at 0.490.

The probe's radial order uses doubles. A library version would take the exact radial order the Boolean already has
(`ExactMeshBoolean.radial_order`, used by `ExactMeshCells` to pair pieces at non-manifold edges).

### 3.6 Options (idea 3)

| # | option | gain | effort | risk |
|---|---|---|---|---|
| 3a | a Z2 spectral (or SDP) orientation solver | 0 on #1548. Negative on frustrated soups. 45 times BFS's time | M | wrong orientations on non-manifold input |
| 3b | the owner's local fix of #1548 (weld by position, then orient) | the measured #1548 variants go from 0.98 / 0.66 / 0.54 (`MeshAutoRepair`) to 1.000 | owner-local | as the owner judges |
| 3c | radial-neighbour links at non-manifold edges in `MeshRepairOrientation` (or in a weld-then-orient step) | +12 points (checkerboard) and +4 points (thin gap) on this probe's frustrated soups; 0 elsewhere | S-M (an exact radial order exists) | low. Needs tests on touching solids and on thin gaps below the weld tolerance |
| 3d | nothing | | | |

**Leaning:** 3a no. 3b is the owner's work in progress, and these numbers support it. 3c is a small follow-up worth a plan
issue once #1548 is fixed, because non-manifold touching edges are where the current orientation still loses area.

## 4. What was read and what was not

- Papers: the abstracts of Brönnimann et al. 1997, Singer 2011, Cucuringu 2015 and Takayama et al. 2014 (web search,
  2026-11-25). Hadwiger 1957 and the valuation property of χ as already cited in `docs/sources/meshing-and-repair.md`.
  Borodin et al. 2004 and Murali and Funkhouser 1997 from memory.
- No code from outside the repository was read. No CGAL, Triangle/Triangle.NET, libigl, GEOS, geometry-central, or GPL / LGPL /
  MPL code.
- Library code read: `ExactMeshBoolean` (exact vertices, status), `ExactPoint3`, `ExactPredicates.orient3d_exact`,
  `ExactWindingCache` (BigInteger sites), `MeshRepairOrientation`, `MeshAutoRepair.Apply`, `WindingContouring.OrientedCopy`,
  `MeshPostRepairCleanup.FindInvertedComponents`, and the #1488 review comment and issue #1548.
