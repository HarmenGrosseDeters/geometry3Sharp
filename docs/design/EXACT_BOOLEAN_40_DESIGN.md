# Exact Boolean 4.0: design analysis (stage 0)

Status: stage 0, design and measurement only (owner decision 2026-11-17). No library code is changed. The measurement code is
benchmark code: `chain-exact` / `chain-exact-proc` in `benchmarks/BooleanComparison` (`ChainExact.cs`), the driver and
summary `benchmarks/BooleanComparison/chain_exact.py`, and the method `ours_exact` of the abc-chained class
(`data/abc_chained.py`, `.github/workflows/abc-chained.yml`).

What 4.0 is ([roadmap](../roadmap/exact-boolean.md), "Exact Boolean 4.0"; [pre-study](EXACT_BOOLEAN_30_PRESTUDY.md)
section 4, target T4): exact output with provenance. Triangles keep their source plane and vertices their construction over
the original double inputs, so chained booleans stay exact with one rounding at the end and coplanar faces stay mergeable.
Consumers: chained booleans with exact intermediate results (the use case), then tet meshing of boolean results.

## Summary and verdict

1. **The key claim holds, with conditions.** Under plane-based provenance the bit length of every vertex of a chain result is
   bounded by a constant that does not depend on the number of steps k: on the input grid at most 7B+14 bits per numerator
   and 6B+12 for the common denominator for closed inputs (10B+20 / 9B+18 with open inputs; B the bits of the input grid,
   about 54-58 here; in input coordinates plus the grid's scale exponent), for any k, if (C1) every face keeps its original
   source plane, (C2) every vertex is kept as an input double, an LPI of an original edge, or a TPI of three original planes,
   (C3) vertices with one or two incident planes are removed after each step (`ExactPlanarSimplify` does that today for new
   points with one closed, manifold fan) and (C4) the contact split of the assembly does not put new midpoints into the
   exact result ([section 2](#2-bit-length-bounds)). Without (C2), as nested constructions, the degree grows by a factor of
   5 to 9 per step.
2. **Measured: consistent with the bounds.** In the exact chain results of 480 procedural steps (k = 1..8) and 266 ABC
   steps (abc-chained, 3 steps, B = 37-59) no vertex is beyond the TPI(F, F, F) envelope. The largest numerator stays flat
   along the chain (generic procedural chains: 195 bits at step 1, 314 at step 2, then 369-378 from step 3 to step 8; ABC:
   at most 196 bits). The proxy is the one-pass n-ary boolean over the original operands, which gives the same solid as an
   exact chain ([section 4](#4-measurements-the-upper-bound-before-any-build)). By construction its points are single-level
   constructions over the originals, so it cannot exceed 10B+20 / 9B+18 anyway: it shows how large the values get, not that
   a step-by-step 4.0 meets (C2)-(C4). Those conditions are derived, not measured.
3. **Quality: a real gain on short chains, a regression on long contact chains.**
   - **ABC (abc-chained, 3 steps):** today 6 of 266 results are unclean, all at step 3. Exact intermediates with one final
     rounding make all 6 clean, and no result gets worse. Four become clean non-empty results. In the other two, iud p0008
     and p0029, the final rounding removes the whole thin exact solid (6612 and 43306 exact triangles; the slab rule of
     2026-11-16). Three of the 6 are the RC4b differences of the triage (iud p0020, p0022 and p0029). For p0020 and p0022
     the defect came from **intermediate** rounding, not from the final one; for p0029 the "fix" is the slab removal. The
     other three are two ucd step-3 intersections (rounding not clean) and iud p0008 (inside-out).
   - **Procedural chains, up to step 4:** the same picture. 3 results are fixed (unclean only chained), 4 are unclean either
     way, and none gets worse (of 240).
   - **Procedural contact chains, steps 5-8** (1-ulp copies, mirrors, face contacts): exact intermediates are **worse**.
     21 results become unclean and none is fixed (of 108). The exact chain carries every sub-ulp slab forward, and the final
     rounding then reaches its effort limit (RC4a) on all of them at once. Today each step's rounding removes or merges a
     slab where it appears.
   - **Generic chains** (no exact contacts): clean either way on all 240 results.
   - The RC4a cases of the triage (iud p0005 / p0021 step-3 union and intersection) are side operations, not the plan's
     operation, so this measurement does not cover them. Their plan operation (the difference) is empty and clean either
     way.
4. **What exact intermediates also give:** exact volumes and identities along the chain. Today's chained volume differs
   from the once-rounded exact one by a median of 1e-16 (generic, ABC) up to 4e-9 (contact chains) relative per step, and by
   up to 1.3e-3 (ABC) or 4.8e-4
   (procedural) where a step removed components. Fewer triangles on long generic chains: today's chained results have
   1.01-1.28 times the exact triangles (median per step), up to 1.76 at step 8, because coplanar faces stay mergeable. On
   ABC (median 1.00; exact results larger where they keep slabs, per-result ratios down to 0.38) and on contact chains
   (0.84-1.20) there is no such gain. A cost saving of at most the
   k − 1 roundings: 11-30 % of a step on generic chains, 23-81 % on contact chains and 3-44 % on ABC (upper bound, before
   the cost of implicit inputs, which is not measured).
5. **A cheap form exists already.** For a chain whose operations are known before the result is needed, the one-pass n-ary
   boolean (`ExactMeshBoolean(meshes, windingPredicate)`) *is* the exact chain with one rounding. A lazy `ExactMesh` as a
   CSG expression over double sources costs effort S (the cull by the expression is a separate exact-code step). On ABC the one pass for the step-3 result took 0.14-0.34 (median) of the
   summed chained time of steps 1-3, but up to 25x at step 2 of iud, where a chain shrinks early. These ratios carry an
   order bias: at k = 1, where both compute the same boolean, the one pass (always run second) takes 0.69-0.88 of the
   chained time. So the time case for stage 1 is not shown yet; it needs an order-balanced measurement. On the small procedural chains it took 0.3-0.8 (median per k), with a long tail of up
   to 8x on contact chains. It loses on long, dense trees (pre-study section 3, from n = 16 on).

**Verdict: go for stage 1 without the cull (lazy expression, effort S, low risk); no-go for the full 4.0 build (stages 2-4,
effort L) now.** The cull by the expression (stage 1c) is a separate exact-code step with its review, gated by an
order-balanced time measurement.
- **Why stage 1:** it captures the measured cleanness gain at low risk. It brings exact chain results and makes every
  unclean ABC result clean (6 of 266: 4 clean non-empty, 2 by removing a thin exact solid; among them the three RC4b iud
  differences), with no exact-code change. Its speed against a chain is not shown yet (summary item 5).
- **What the full build adds:** only materialized exact intermediates for a consumer that does not know its next operation
  (interactive or persistent modelling) and the tet mesher on exact input. Neither is a current request. Re-open it when
  such a consumer exists.
- **Known limitation:** on long contact chains (5 or more steps with 1-ulp copies) the single final rounding regresses
  until stage 1b, the slab removal before the repair (the RC4a lever, `SAFE_ROUNDING.md`). Stage 1 can expose that choice:
  round each step or once ([section 6](#6-stage-plan)).

## 1. Representation options

The arrangement already stores every new point as a construction over the input points
(`ExactMeshArrangement.PointKind`): `Input` (an input double), `EdgePlane` (an LPI: input edge crossing a plane),
`Segments` (two coplanar input edges crossing), `Planes` (a TPI of three planes; a plane is a triangle plane or, per edge,
the plane through the edge perpendicular to the triangle) and `Rational` (given exactly). Every input coordinate is converted
exactly to one integer grid (coordinate = integer · 2^ScaleExp, centered). The planes come from the input doubles
(`InputTriangle.P`, `EdgePlanes`). `merge_points` gives coincident points of different constructions one id. The exact
values are computed on demand as canonical rationals (`ExactPoint3`, or fixed width, `FIXED_WIDTH_EXACT_POINTS.md`).

| | (a) constructions over the original doubles | (b) plane-based (three input planes) | (c) rational / homogeneous coordinates |
|---|---|---|---|
| a vertex is | `Input`, `EdgePlane`, `Segments`, `Planes` with keys into the **original** meshes (the arrangement's kinds, the pre-study sketch) | an input double, or TPI(F1, F2, F3) of three original face planes (edge planes only on open boundaries) | canonical X/W, Y/W, Z/W (`ExactPoint3`) |
| a face is | a piece of an original triangle: source (mesh, triangle) | the same; each edge also has its carrier plane (the neighbour's source plane) | three rational corners; the plane computed from them |
| exactness | exact | exact | exact |
| bits along a chain | constant **if** step-k points are re-expressed over the originals (canonicalization, C2); as nested constructions (a step-k LPI of a step-(k−1) edge) the degree grows 5-9x per step | constant: every genuine vertex is a TPI of original planes (section 2.3) | the *values* of genuine vertices are bounded as in (b); planes recomputed from corners have unreduced coefficients of about 21B bits and need a gcd per triangle to get back to the original plane; values of points that are not genuine (diagonal crossings) grow if they survive a step |
| memory per vertex | kind 1 B + 3 keys 24 B + approximation 24 B + error 8 B, about 57 B; the original meshes stay alive | 3 plane ids 12 B + approximation and error 32 B, about 44 B; plus the source planes (about 9B+11 bits each, once per used source triangle) | measured 143-707 bits mean per vertex (section 4), as four `BigInteger`s about 180-250 B with object overhead; fixed width where it fits |
| arrangement input path | an operand's vertices become implicit input points; its triangles keep their source plane key; constructions from step-k edges must be mapped back to original keys | the same, and the edges carry planes, so a crossing with a crease edge is TPI(F, F', G) directly | rational input points: the grid conversion, the filters (inputs are no longer exact doubles) and every exact stage take rationals; no bookkeeping |
| exact predicates needed | today's, on implicit points whose arguments are original doubles, plus one nested form for diagonal crossings | the same | today's predicates with rational inputs of about 7B bits: new filters, exact stages about 8x wider |

Reading: (a) and (b) are the same idea at two granularities. (a) keeps today's point kinds, including LPIs and the
coplanar `Segments` points, which are smaller than a TPI. (b) normalizes every genuine vertex to three face planes, which
gives the smallest uniform bound and makes coplanarity a matter of plane ids. Both need the same canonicalization step at
the end of each boolean. (c) is the simplest input path but pays at every exact stage and in memory, and it relies on gcd
reduction to recover the planes. A practical 4.0 is (a) with (b)'s carrier planes on edges: the arrangement's own kinds,
plus a map from a step's points back to original keys.

Prior art ([section 7](#7-literature-check)): plane-based booleans (Sugihara and Iri 1989, Fortune 1997, Bernstein and
Fussell 2009, Campen and Kobbelt 2010, Nehring-Wirxel et al. 2021, EMBER 2022) quantize the input once to short integers
(about 26 bits in EMBER) so that fixed-width integers suffice and iterated CSG stays closed. We keep the original doubles
(B ≈ 54-58 here), so the same structure gives 7B+14 ≈ 400 bits instead of their fixed 256-bit kernels. Indirect
predicates (Attene 2020, Cherchi et al. 2020 / 2022) are what the arrangement already uses. Lévy 2025 computes the
co-refinement exactly and names the rounding to doubles as the open part (the same status as our SafeRounding).

## 2. Bit-length bounds

### 2.1 Setting

Input coordinates on the common grid: integers with |x| < 2^B. (The arrangement centers the grid, which only makes the
magnitudes smaller; the bounds below hold for the uncentered values.) A canonical rational (`ExactPoint3`: gcd 1, W > 0)
has at most the bits of any representation of the same point, so a bound on the value of one construction bounds the
stored value whatever construction produced it. "n bits" means |v| < 2^n. In input coordinates (x = grid · 2^s) add max(0, s)
to numerators and max(0, −s) to W. The measurement does this. Hadamard: |det M| ≤ ∏ of the row norms, and also ≤ ∏ of the
column norms; 3^{3/2} < 2^3.

### 2.2 Per construction

| construction | normal / offset bits | point numerators X | point denominator W | derivation |
|---|---|---|---|---|
| input vertex | | B (W a power of two) | | |
| face plane F of (p, q, r) | n_F = 2B+3, d_F = 3B+5 | | | u = q−p, v = r−p, \|u_i\|, \|v_i\| < 2^{B+1}; \|N_i\| = \|u_j v_k − u_k v_j\| < 2·2^{2B+2}; \|N·p\| < 3·2^{2B+3}·2^B < 2^{3B+5} |
| edge plane E of edge (p, q) of F | n_E = 3B+5, d_E = 4B+7 | | | E = (q−p) × N: \|E_i\| < 2·2^{B+1}·2^{2B+3}; \|E·p\| < 3·2^{3B+5}·2^B |
| LPI(input edge, F) (`EdgePlane`) | | 4B+8 | 3B+6 | W = N·(q−p) < 3·2^{2B+3}·2^{B+1}; X = W p + (d − N·p)(q−p): 2^{4B+6} + (2^{3B+5} + 3·2^{3B+3})·2^{B+1} = 2.75·2^{4B+6} |
| LPI(input edge, E) | | 5B+10 | 4B+8 | the same with n_E, d_E: 2^{5B+8} + 1.75·2^{5B+8} |
| two coplanar input edges (`Segments`) | | 3B+5 | 2B+3 | 2D: W = d1 × d2 < 2·2^{2B+2}; X = p1 W + ((p2−p1) × d2) d1 < 2^{3B+3} + 2^{2B+3}·2^{B+1} (the dropped coordinate likewise, the point lies on edge 1) |
| TPI(F, F, F) (`Planes`) | | **7B+14** | **6B+12** | W = det of the normals: rows ∏ √3·2^{2B+3} < 2^{6B+12}; X_i: columns (D, N_j, N_k): √3·2^{3B+5} · (√3·2^{2B+3})^2 < 2^{7B+14} |
| TPI with one edge plane (F, F, E) | | 10B+18 | 7B+14 | W rows: 2(2B+3) + (3B+5) + 3; X rows: \|row\| ≤ 2^{d}·(1 + 2^{1+2(n−d)})^{1/2}, and d − n ≥ B+2, so ∏ < 2^{Σd+1} = 2^{10B+18} |
| TPI (F, E, E) | | 10B+20 | 8B+16 | W rows; X columns: √3·2^{4B+7}·(√3·2^{3B+5})^2 |
| TPI (E, E, E) | | 10B+20 | 9B+18 | W rows 3(3B+5) + 3; X columns as above |

So every vertex construction of today's arrangement (`EdgePlane`, `Segments`, `Planes`) has X ≤ 10B+20 and W ≤ 9B+18
bits, and every point built from face planes only (the plane-based form) has X ≤ 7B+14 and W ≤ 6B+12. (`Rational` points,
the classification's centroids, are not vertices and not bounded by this.) The bounds of TPIs with edge planes are loose
where the point is geometrically a crossing of two input edges: the coplanar-overlap segments carry edge planes as their
lines (`Segment.Line`), and their crossings are `Planes(F, E, E)` keys of at most 3B+5 / 2B+3 bits in value. LPI(F) also
holds with 4B+7. With B = 54: 560 / 504 and 392 / 336 bits. The pre-study's measured
175-300 bits are below these (most vertices are input doubles or LPIs). The measurement classifies each exact vertex into
the nested envelopes double ⊂ LPI(F) (4B+8 / 3B+6) ⊂ TPI(F, F, F) ⊂ any construction (10B+20 / 9B+18). The envelopes are
value bounds, not kinds: an LPI with an edge plane falls into the TPI(F, F, F) envelope.

### 2.3 Per chain step: when the degree stays constant

**Claim.** Let R_k be the exact result of a chain of k regularized booleans of closed meshes, with every face a piece of an
original input triangle (C1). Then every vertex of R_k after removal of the vertices with one or two incident planes (C3) is
either an original input vertex, or a point on an original edge (an LPI), or the intersection of three original face planes
with independent normals. So its value obeys the bounds of 2.2 for every k.

*Proof sketch.* Faces: a boolean only cuts and selects pieces of its operands' faces, so by induction every face of R_k
lies in an original plane (C1). Vertices: take a vertex v that is neither an input vertex nor on an original edge. Group
the faces around v (per manifold fan; split pinches are separate fans) into maximal regions of one plane. If all incident
planes contain a common line L, then every edge between two consecutive regions lies in two distinct planes through L, so
on L. Then v has only two edge directions and one or two regions (grouped by geometric plane), and it is a vertex with
one or two incident planes. (C3) removes it,
and `ExactPlanarSimplify` does exactly that: it removes a new point inside a flat region, or on a straight edge between two
regions of different planes, if its fan is closed and manifold. Otherwise the incident planes share no line, so their
normals span R^3, and three of them with independent normals meet only at v: v = TPI(F1, F2, F3). Points on original edges
are LPIs, or TPI(F_left, F_right, G) where the edge is a crease. ∎

**Conditions** (where the claim fails, the bits are bounded per step but can grow along the chain):
- (C2) the representation must actually re-express a step-k vertex over original planes (canonicalization). Kept as a
  nested construction (an LPI of a step-(k−1) edge with a step-(k−1) plane), the degree grows. For points of h bits, a plane
  through three of them has 3x3 minors of 3h+3 bits, an LPI of a segment of two of them has 5h+6 bits and a TPI of three
  such planes 9h+12 bits. Starting from B = 54, three nested TPI levels give about 9^3·B ≈ 39 000 bits: this is the
  growth the pre-study meant by "thousands of bits per step".
- (C3) a vertex with two incident planes that the simplification cannot remove (a fan that is not closed or not manifold,
  e.g. at a contact) has no TPI form. Its value is that of whatever construction made it.
- (C4) the assembly splits edges where solids touch at exact midpoints (`SplitNonManifoldEdges`, in `ExactMeshBoolean`,
  with or without SafeRounding; `ResultExactVertices` holds them). A midpoint of two TPI(F, F, F) points has
  13B+27 / 12B+25 bits, of two LPIs 7B+15 / 6B+13, and midpoints of midpoints grow. An exact result must keep the contact
  without new midpoints (duplicated vertices at the TPI positions are enough). The measured exact results of section 4
  contain such midpoints on the contact chains, so (C4) is violated there and the value classes mix them with genuine
  vertices; none went beyond the TPI(F, F, F) envelope.
- Open inputs: a boundary edge has no second face plane. Its carrier is the original edge (points on it are LPIs) or its
  edge plane (TPIs with E, 10B+20 / 9B+18). Still constant along a chain, with a larger nested bound inside a step (below).

**Inside a step** the arrangement of R_{k−1} with X_k also has points that are not vertices of R_k. A crossing of X_k's
plane G with a crease edge of R_{k−1} is TPI(F, F', G) (constant). A crossing with a *diagonal* of R_{k−1}'s triangulation
(an edge inside one planar region) is a nested LPI(segment(P, Q), G) with P, Q TPI(F, F, F) points: π·P̂ = N_G·X_P − d_G
W_P < 3·2^{2B+3}·2^{7B+14} + 2^{3B+5}·2^{6B+12} = 2^{9B+19}, so X < 2·2^{9B+19}·2^{7B+14} = 2^{16B+34} and W < 2^{15B+32}.
Such a point lies on the line F ∩ G of two regions, so (C3) removes it after the step, unless a third region meets there,
in which case it is a TPI. The coplanar case, an input edge of X_k crossing a diagonal (P, Q) of R_{k−1} in a shared
plane, is a nested `Segments` point: with the homogeneous 2D line l = P̂ × Q̂, W < 2^{14B+29} and X < 2^{15B+31}, inside the
same bound. With open inputs the diagonal's ends can be TPIs with edge planes (10B+20 / 9B+18), and the nested bound grows
to 23B+48 / 22B+46 in the worst case (G an edge plane: π·P̂ < 3·2^{3B+5}·2^{10B+20} + 2^{4B+7}·2^{9B+18} = 2^{13B+27}). Either way the bound is one nesting level, the same at every step. Passing the operand's planar regions as
polygons, whose edges all have carrier planes, avoids diagonals and with them the nested form.

**Does the degree stay constant along a chain?** Yes, for closed results under (C1)-(C4) (derived). The measurement is
consistent with it (section 4.1) but does not test the step-wise conditions. The per-step points are bounded by the
one-nesting bound 16B+34 / 15B+32 (derived).

## 3. Where it plugs in

**ExactMeshArrangement input path.** Today: `inputCoords` (doubles), converted exactly to the grid (`ScaleExp`, `Center`
from all inputs), points of kind `Input`, planes computed from the corners when first needed (`InputTriangle.P`,
`EdgePlanes`). For an exact operand:
- the grid is set from the **original** sources of all operands (an exact operand registers its source meshes), so that
  keys of original points and planes are valid in this arrangement;
- an operand vertex enters as an implicit point with its original key (`EdgePlane`, `Segments`, `Planes` over original
  keys; `merge_points` already unifies coincident constructions);
- an operand triangle takes its source plane key instead of computing a plane from its corners (C1). Its edge planes
  (perpendicular through the edge) are not defined over originals for a triangle with implicit corners. They are used in
  exact stages of coincident constructions (`EdgePlanes`) and, on the main coplanar path, as the `Line` carriers of every
  coplanar-overlap segment, whose crossings are `Planes(F, E, E)` keys. Both must be replaced by the edge's carrier plane
  (the neighbour's source plane, or the original edge) or by a nested form;
- new keys for "a crossing of a diagonal" (the nested LPI above), with its own filter;
- `PlaneOf(key)` addresses planes of the triangles in the arrangement. A source-plane table (planes of original triangles
  that are not part of this arrangement's surface) is needed.

**Predicates and filters.** Reused as they are: plane side of an implicit point against an original plane (the planes stay
built from doubles), orient2d / incircle of implicit points in the CDT (the points inside a triangle are implicit
today). New, each with an error-bound derivation and the exact-code review:
1. the filter of the nested diagonal crossing: its approximation from the approximations of P and Q (each with its
   bound) and the double plane, and the forward error bound of π·P̂, as for the other kinds (Attene 2020's scheme);
2. candidate pair and tri-tri tests with implicit corners: today's prefilter takes double corners (`TRITRI_PREFILTER.md`),
   with implicit corners it needs boxes from approximation ± error and sign tests of implicit corners against planes
   (existing kind of test, new bound for the box);
3. classification: an exact ray against a triangle with implicit corners (`ExactRayTriangle` takes doubles). The
   alternative without new predicates is to classify a piece against R_{k−1} through the winding numbers of the
   **original** operands (R_{k−1} = f(w_0..w_{k−1}), the one-pass predicate): today's exact code on double meshes, at a cost
   that grows with k;
4. `ExactPlanarSimplify` groups by normalized integer planes of the source triangles. With source plane ids it needs no
   change, and coplanar faces of different steps merge exactly (today they do not after rounding: the 1.76x triangles).

Every new bound gets its derivation in the stage note and the stronger-model review (CLAUDE.md, exact code).

**Final SafeRounding.** Unchanged: `MeshRoundingRepair` takes the exact result (it does so today from the arrangement's
points). The measurement shows that its cost and its effort limit become the bottleneck on contact chains (section 4).

**API shape (additive, compatible with 1.0.324; `NotCleanReason` / `MeshRepairMetrics` semantics unchanged).** As the
pre-study sketch, in two forms:

```csharp
public sealed class ExactMesh                              // new
{
    // stage 1 (lazy): a CSG expression over double sources; materialized by one n-ary ExactMeshBoolean
    public static ExactMesh FromDMesh(DMesh3 m);
    public ExactMesh Union(ExactMesh b); public ExactMesh Difference(ExactMesh b); public ExactMesh Intersection(ExactMesh b);
    public DMesh3 Round(Action<ExactMeshBoolean> configure = null);   // today's SafeRounding result, one rounding
    public double ExactVolumeSign();                                  // optional, exact volume sign (RC3 code)
    // stage 2+ (materialized provenance): the same type, with the exact result stored
    public int TriangleCount, VertexCount;
    public ExactPoint3 GetVertex(int v);                    // value on demand
    public int SourcePlane(int t);                          // (source mesh, triangle)
}
// ExactMeshBoolean (new members only): public bool ExactOutput; public ExactMesh ExactResult;
//   public ExactMeshBoolean(ExactMesh a, ExactMesh b, Operations op);
```

## 4. Measurements: the upper bound before any build

**Method.** A 4.0 step does not exist yet, so the exact chain is measured by what it must compute. Regularized booleans
act cell by cell on the arrangement of all operands: a regular closed set that is a union of closed 3-cells is determined
by its cells, and each operation is a set operation on the cells. So the k-th result of an exact chain is the one-pass
n-ary `ExactMeshBoolean` over the original operands X0..Xk with the composed winding predicate (closed operands, integer
winding numbers, inside = w > 0). Its genuine vertices (three or more incident planes) are those of any exact chain result,
and all its vertices are constructions over the original doubles. Vertices with two planes, contact midpoints and the
triangulation depend on the construction and can differ from a step-by-step 4.0; the solid cannot. Per step (`chain-exact`):
- **chained**: today, `ExactMeshBoolean(R_{k−1} rounded, X_k)`, default settings;
- **onepass**: the one-pass boolean with SafeRounding (exact intermediates, one final rounding);
- **exact**: the same unrounded with `ComputeExactVertices` (vertices, triangles, bits; each vertex against the envelopes of
  2.2, with B and s over all operands of the chain, which only loosens the check).

"Clean" here is `NotCleanReason(result, false)` in {"", "empty"} on the unwelded in-memory result, and `RoundingClean`.
That is the same test for both variants, but not the abc-chained eval (which welds the OFF result, splits pinches and adds
a self-intersection test), so the counts are not comparable to the `ours` rows of abc-chained; the chain also passes the
in-memory result between steps, not an OFF file. "Unclean only chained" means intermediate rounding made it, "unclean in both" means the final rounding
(RC4), and "unclean only one pass" means exact intermediates made it worse.

### 4.1 Procedural chains (k = 1..8, local)

`python3 benchmarks/BooleanComparison/chain_exact.py proc rows.jsonl --seeds 30 --steps 8`, then `chain_exact.py summary`,
master ed7114e0 plus this branch's benchmark code, 4 cores, single runs after a warm-up boolean (a second full run gave the
same results, counts and bits identical). Rows on the data branch `runs/exact-boolean-40`, folder `proc-b2c5d747`. Shapes as in the RC4 triage probe
(normalized-cube sphere, capped cylinder, rotated grid box, float32 coordinates).
- **generic**: shapes at random rotations, scales and offsets (no exact contacts), operations union, union, difference,
  intersection, repeating;
- **mixed**: exact placements against X0 (`ContactTransforms`), cycling partial-overlap, ulp-shift (a copy of X0 one ulp
  off), face-contact, mirror; operations union, difference, union, intersection, repeating.

| family | step | chained clean | one pass clean | unclean only chained | unclean in both | unclean only one pass | tris chained / exact (median of ratios, max) | median ms chained (rounding) | max X / W bits | mean bits / vertex | vertices: double coords / ≤LPI / ≤TPI(F,F,F) / ≤any / beyond |
|---|---|---|---|---|---|---|---|---|---|---|---|
| generic | 1 | 30/30 | 30/30 | 0 | 0 | 0 | 1.00 (1.00) | 25 (4) | 195 / 196 | 283 | 8739/1765/0/0/0 |
| generic | 2 | 30/30 | 30/30 | 0 | 0 | 0 | 1.01 (1.21) | 20 (4) | 314 / 315 | 361 | 11039/4232/30/0/0 |
| generic | 4 | 30/30 | 30/30 | 0 | 0 | 0 | 1.21 (1.50) | 22 (3) | 375 / 375 | 646 | 4449/6318/178/0/0 |
| generic | 6 | 30/30 | 30/30 | 0 | 0 | 0 | 1.14 (1.50) | 23 (7) | 377 / 378 | 491 | 11498/8361/292/0/0 |
| generic | 8 | 30/30 | 30/30 | 0 | 0 | 0 | 1.28 (1.76) | 27 (3) | 376 / 377 | 684 | 3037/4647/216/0/0 |
| mixed | 1 | 30/30 | 30/30 | 0 | 0 | 0 | 1.00 (1.00) | 22 (5) | 113 / 114 | 143 | 10698/1745/0/0/0 |
| mixed | 2 | 30/30 | 30/30 | 0 | 0 | 0 | 0.92 (1.03) | 188 (122) | 210 / 212 | 258 | 11631/4542/0/0/0 |
| mixed | 3 | 30/30 | 30/30 | 0 | 0 | 0 | 0.94 (1.04) | 14 (6) | 210 / 212 | 204 | 17014/5346/0/0/0 |
| mixed | 4 | 23/30 | 26/30 | 3 | 4 | 0 | 0.84 (1.19) | 118 (95) | 216 / 218 | 408 | 2018/25476/0/0/0 |
| mixed | 5 | 25/27 | 19/27 | 0 | 2 | 6 | 0.95 (1.15) | 12 (3) | 209 / 211 | 343 | 7646/23905/0/0/0 |
| mixed | 6 | 27/27 | 22/27 | 0 | 0 | 5 | 0.99 (1.33) | 59 (29) | 212 / 215 | 374 | 7330/16061/0/0/0 |
| mixed | 7 | 27/27 | 22/27 | 0 | 0 | 5 | 1.00 (1.30) | 19 (9) | 212 / 215 | 219 | 12939/16926/0/0/0 |
| mixed | 8 | 27/27 | 22/27 | 0 | 0 | 5 | 1.20 (1.74) | 51 (18) | 212 / 214 | 434 | 1310/20812/0/0/0 |

(Generic steps 3, 5 and 7 are like their neighbours: max bits 369-377, all clean. B = 54-58: TPI(F, F, F) bounds 392-420 /
336-360 bits plus the scale term. The full table is printed by `chain_exact.py summary`.)

Totals over 468 results computed both ways (240 generic, 228 mixed): chained clean 459, one pass clean 441. Unclean only chained: 3 (the step-4
intersections with the mirror copy of seeds 5, 19 and 22: intermediate rounding). Unclean in both: 6 (the final rounding).
Unclean only one pass: 21 (seeds 3, 6, 9, 12, 24, 27, 30, steps 5-8). Of the 27 unclean one-pass results, 23 reached the
repair's evaluation limit (today's chain: 3 of its 9), and 17 have degenerate triangles left. In three mixed chains (seeds 7, 15, 16) the step-4
intersection is a thin solid that both roundings remove (an empty, clean result), so today's chain ends there. The exact
chain goes on with the thin solid, and steps 5-8 are not compared.

**Why exact intermediates are worse here.** A ulp-shift copy makes 1-ulp slabs between X0 and its copy. Today the step
that creates them rounds them away: it removes them as components (`removed comps`, e.g. 48 and 130 in seeds 3 and 6,
step 6) or merges them, and later steps start from a clean double mesh. The exact chain keeps every slab exactly (they are
part of the exact solid), and every later final rounding must repair all of them at once: the unclean one-pass results have a median of 4259 exact
triangles, against 2274 for today's results of the same steps (a scratch count over the rows). The final rounding then hits the evaluation limit or leaves degenerate triangles. So the single final
rounding is not easier than k roundings; with exact contacts it is harder.

**Size and drift.** The largest numerator is flat along the chain (generic: 195 → 314 → 369-378 bits at steps 1, 2, 3-8;
mixed: 113 → 209-218). No vertex is beyond the TPI(F, F, F) envelope of 2.2. The generic chains have vertices in the
TPI(F, F, F) envelope from step 2 on (up to 374 per step summed); the mixed chains have none beyond the LPI envelope (value
classes, not construction kinds). Mean bits per vertex 143-684 (four integers, against 192 for three doubles), growing with
the share of implicit vertices (vertices with double coordinates: 83 % at step 1, 38 % at step 8 in the generic chains;
those are not checked against a bound, their bits are in the maxima). On generic chains today's results carry 1.01-1.28x
the exact triangles (median of the per-result ratios per step from step 2), up to 1.76x at step 8: rounded coplanar faces
are no longer exactly coplanar. On the contact chains the exact results are larger (0.84-1.00x at steps 2-7), as they keep
the slabs. Volume: today's chained result against the once-rounded one pass (`chain_exact.py summary`, last column):
median 1e-16 per step on generic chains (max 6.6e-11), 1e-12 to 4e-9 on the contact chains (max 4.8e-4 where a step
removed components).

**Time.** Today's step on these small inputs: median 12-188 ms, of which the rounding is 11-30 % (generic) and 23-81 %
(mixed). A 4.0 step saves the rounding of k − 1 of k steps, at most that share, minus the unmeasured overhead of implicit
inputs (new filters, wider exact stages). The one pass for the final result (stage 1) against the summed chained steps
1..k: median 0.48-0.76 (generic, k = 2..8, p90 up to 1.13, max 1.90) and 0.29-0.41 (mixed, p90 up to 3.08, max 8.16 where
the one pass rounds the accumulated slabs). At k = 1, the same boolean both ways, the ratio is already 0.40-0.45: these are
single runs on 1-3k triangles where a fixed per-call cost dominates, so the ratios are rough; the ABC rows are the
realistic sizes.

### 4.2 abc-chained

Workflow `abc-chained.yml` with `methods=ours_exact` (GitHub-hosted runner, one job), 30 chains per variant (ucd, dui, iud),
three steps each, only the plan's operation per step (abc-chained also computes the two other operations of a step for its
identities; those are not part of a chain). Run [44130237776](https://github.com/HarmenGrosseDeters/geometry3Sharp/actions/runs/44130237776)
(commit a233ad78, which classified with the looser envelope 12B+22; the maxima below are also below 10B+20), rows on
`runs/abc-chained`, folder `run-44130237776`; table by `chain_exact.py summary`. 90 chains, 266 steps with both variants
(iud p0000 and p0013: the step-1 intersection is empty in both variants; the chained harness does not chain on an empty
result, the one pass goes on, with 7300 and 23198 triangles at step 2).
Timings are from the rerun [44145542419](https://github.com/HarmenGrosseDeters/geometry3Sharp/actions/runs/44145542419)
(commit d2a9a8e0, with a warm-up boolean before each chain; folder `run-44145542419`). Every result, count and bit length is
identical to the first run (all 270 rows, apart from the timings and a field the second commit dropped).

| variant | step | op | chained clean | one pass clean | unclean only chained / both / only one pass | median tris chained / exact | median ms chained (rounding) / one pass | max X / W bits | mean bits / vertex | vertices: double coords / ≤LPI / ≤TPI(F,F,F) / beyond | volume deviation median (max) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| ucd | 1 | union | 30/30 | 30/30 | 0 / 0 / 0 | 29107 / 29107 | 233 (66) / 166 | 106 / 111 | 102 | 450445/2730/0/0 | 0.0e+00 (0.0e+00) |
| ucd | 2 | difference | 30/30 | 30/30 | 0 / 0 / 0 | 28228 / 29094 | 291 (68) / 336 | 122 / 124 | 106 | 428306/40361/0/0 | 0.0e+00 (1.2e-13) |
| ucd | 3 | intersection | 28/30 | 30/30 | 2 / 0 / 0 | 15378 / 15473 | 1126 (35) / 246 | 122 / 124 | 105 | 219217/35901/0/0 | 5.0e-16 (1.3e-03) |
| dui | 1 | difference | 30/30 | 30/30 | 0 / 0 / 0 | 19430 / 19430 | 174 (56) / 126 | 104 / 105 | 102 | 265611/4129/0/0 | 0.0e+00 (0.0e+00) |
| dui | 2 | union | 30/30 | 30/30 | 0 / 0 / 0 | 27843 / 34430 | 224 (71) / 352 | 189 / 192 | 118 | 472379/47540/0/0 | 3.6e-16 (1.8e-11) |
| dui | 3 | intersection | 30/30 | 30/30 | 0 / 0 / 0 | 20086 / 20353 | 1488 (104) / 243 | 189 / 192 | 105 | 317579/4506/0/0 | 2.3e-15 (1.4e-09) |
| iud | 1 | intersection | 30/30 | 30/30 | 0 / 0 / 0 | 1590 / 1590 | 102 (24) / 96 | 114 / 115 | 127 | 57422/15085/0/0 | 0.0e+00 (0.0e+00) |
| iud | 2 | union | 28/28 | 28/28 | 0 / 0 / 0 | 18531 / 21934 | 172 (60) / 1116 | 192 / 196 | 118 | 325922/25823/0/0 | 1.2e-15 (1.3e-09) |
| iud | 3 | difference | 24/28 | 28/28 | 4 / 0 / 0 | 10543 / 15817 | 1163 (510) / 879 | 143 / 145 | 111 | 191762/143679/0/0 | 3.7e-16 (4.6e-09) |

- **Cleanness.** Today's chain: 260 of 266 clean. One pass (exact intermediates, one rounding): 266 of 266. All six
  unclean results are fixed and none is new:
  - **iud p0020, p0022, p0029 (step-3 difference):** the RC4b cases of the triage. Today: degenerate triangles, degenerate
    triangles at the evaluation limit, and inside-out at the evaluation limit (24 triangles left). One pass: clean, clean,
    and empty (the exact result is a thin solid of 43306 triangles that the final rounding removes, as decided for slabs on
    2026-11-16).
  - **iud p0008 (step-3 difference):** inside-out today (34 triangles). One pass: empty and clean (the exact result has
    6612 triangles; the rounding removes it).
  - **ucd p0001 and p0022 (step-3 intersection):** `RoundingClean` false today, clean exact.

  So on these chains the unclean results come from **intermediate** rounding (for p0008 and p0029 together with the slab
  removal of the final rounding): the step-2 union rounds R ∪ C, where C is a
  1-ulp copy or the mirror of A, and step 3 then subtracts or intersects A against a surface that is near A but not on it.
  With exact intermediates R2's faces lie exactly in A's planes, and the coplanar handling resolves them.
- **Bits.** At most 192 / 196 bits (X / W) (B up to 59); no vertex beyond the LPI envelope (value classes). 57-99 % of the
  vertices have double coordinates (57 % in the iud step-3 slabs).
- **Size.** Median per-result ratio chained / exact 1.00 at every step. Exact results are larger where they keep slabs
  (dui steps 2-3, iud step 3; per-result ratios down to 0.38). The table's medians are over different populations at iud
  step 3 (today's include 6 empty results).
- **Time.** Today's step: median 102-1488 ms, of which the rounding is 3-44 %. The one pass for the step-k result against
  today's summed steps 1..k:
  - **k = 1 (the same boolean both ways, the order baseline):** median 0.69-0.88. The one pass always runs second on the
    same meshes, so every ratio carries this bias.
  - **k = 3:** median 0.14-0.34 (p90 0.49-0.89, max 0.96). A hypothesis, not profiled: today's step 3 (median 1126-1488 ms)
    works on a rounded R2 whose faces are near A's but not on them, while the exact faces lie in A's planes.
  - **k = 2 on iud (A ∩ B, then ∪ C with C a copy of A):** median 1.15, p90 9.1, max 25. The one pass builds the arrangement
    of A with its 1-ulp copy C everywhere, while today's chain first shrinks A ∩ B to a median of 1590 triangles.

  This is the pre-study's finding again: the one pass loses where a chain shrinks early. A step-by-step 4.0 (stages 2-3)
  would not have that cost; stage 1c, the cull by the expression, is the lever (section 6).


### 4.3 What quality gain is realistic

- **Cleanness:**
  - **ABC chains:** every unclean result of the 3-step chains is fixed (6 of 266, 2.3 %), among them three of the RC4b cases.
  - **Procedural chains up to step 4:** 3 fixed, none worse.
  - **Generic chains:** nothing to gain, all clean either way.
  - **Long contact chains (steps 5-8):** worse as long as the final rounding is today's. Exact intermediates move the slabs
    from "rounded away at the step where they appear" to "all at once at the end" (21 of 108 newly unclean). The slab
    removal before the repair (SAFE_ROUNDING.md, the thin-component rule with RC1) is the lever for that regression.
- **Exactness:** volume identities exact along the chain (today up to 4.8e-4 relative after a removal). Coplanar merging
  keeps the exact results smaller on generic chains (today's carry 1.01-1.28x their triangles, median, up to 1.76x), not on
  contact chains. Fixes today's "unclean only chained" class (3 of 228 contact results, 0 of 240 generic).
- **Time:** at most the rounding share of k − 1 steps (11-81 % of a step on these inputs, the pre-study measured 29 % on
  Thingi models), before the overhead of implicit inputs.

## 5. Honest limits

- The final rounding keeps RC4a (the effort limit on 1-ulp slabs) and RC4b (the drift bound) as limits. An exact chain
  ends with today's rounding, and on long contact chains it gives it more to do, not less (section 4.1). The RC4b cases
  measured here (iud p0020 / p0022 / p0029) came from intermediate rounding and are fixed (section 4.2). The RC4a cases of
  the triage are side operations and not measured; a single boolean's RC4a / RC4b are unchanged.
- Consumers that need doubles (STL, the remesher, the tet mesher, every `DMesh3` API) gain only inside a chain: one
  rounding instead of k. A single boolean gains nothing.
- The tet mesher on exact input needs predicates on implicit points everywhere (`Orient3dLPI` / `TPI` exist, the rest does
  not), a project of its own.
- The bounds are worst-case value bounds. Exact fallbacks on 400-560-bit operands are several times the cost of today's
  (about 200 bits), and their share on implicit inputs is not measured: the main cost risk of a build.
- The one-pass measurement is a proxy for the solid and the vertex values, not for the triangulation or the time of a
  step-by-step 4.0.
- Memory: provenance keeps all source meshes of a chain alive; rationals cost 180-250 B per vertex against 24 B.

## 6. Stage plan

| stage | content | effort | risk | go / no-go gate | owner decisions |
|---|---|---|---|---|---|
| 0 | this note: options, bounds, measurement | done | | **go for stage 1 (without the cull); no-go for stages 2-4 now** (sections 4-5) | whether exact chain results are wanted as an API (stage 1) |
| 1 | lazy `ExactMesh`: a CSG expression over double sources, `Round()` = one n-ary `ExactMeshBoolean` (exists); invariance over `Similarity.All()`, reuse and determinism tests; an order-balanced benchmark against chained | S (3-5 days) | low: no exact code change; known regression: on contact chains of 5+ steps `Round()` is less often clean than a chain (21 newly unclean of 108) until 1b | on abc-chained no newly unclean result against today's chain (measured: 0 of 266, 6 made clean); exact volumes; the long-chain regression and the measured time ratios documented | the API name and shape (additive); whether `Round` may differ from the chained result (it does: exact, other triangles); whether stage 1 may ship before 1b |
| 1b | prerequisite for the long contact chains: thin-slab removal before the repair (the RC4a lever; RC1 rule) | M | medium: changes rounding outputs | the mixed chains of 4.1: one pass clean at least as often as chained | changes SafeRounding's outputs (owner decision) |
| 1c | the operation-aware cull (`CullOutsideA` / `cull_outside`, today only for the built-in Difference / Intersection) generalized to the expression's active zone (Rossignac and Voelcker 1989), so that an early intersection shrinks the one pass as it shrinks a chain | S-M | medium: exact code (`mesh_ops/ExactMesh*`), it decides with exact rays which pieces are dropped; the exact-code review | order-balanced, on abc-chained and the procedural chains: the median of one pass / summed chained time ≤ 1 and its p90 ≤ 2 per k (measured without the cull and order bias: iud k = 2 p90 9.1, mixed p90 up to 3.1); results identical with and without the cull | |
| 2 | materialized provenance: exact result stored as (a) + carrier planes; canonicalization after each step; arrangement input path from provenance (source-plane table, grid from sources) | M-L (3-5 weeks) | high: new keys in the exact code, new filters | per-step time ≤ today's step without its rounding × 1.5 on abc-chained; all values within the bounds of 2.2 (test); no new unclean result | only if a consumer needs exact intermediates without knowing the next operation (interactive or persistent modelling) |
| 3 | nested diagonal crossings (or polygon input) and classification on implicit operands (winding of the original operands, or a new ray predicate) | M | high: new error bounds, exact review and mutation gate | the incremental mutation run above its threshold; every new bound reviewed | |
| 4 | tet meshing of exact results | L (own project) | high | | separate decision |

## 7. Literature check

Searched 2026-11-17 (plane-based iterated booleans, bounded-degree exact constructions, exact output of mesh booleans).
Papers only, no code read.
- K. Sugihara, M. Iri, *A solid modelling system free from topological inconsistency*, J. Inf. Process. 12(4), 1989:
  plane-based solids with integer planes.
- S. Fortune, *Polyhedral modelling with multiprecision integer arithmetic*, CAD 29(2), 123-133, 1997,
  doi:10.1016/S0010-4485(96)00041-3: face planes as the primary geometry, vertices implicit, bounded precision per
  predicate (abstract and summaries read).
- G. Bernstein, D. Fussell, *Fast, exact, linear booleans*, SGP 2009: plane-based polygons with fixed-precision plane
  coefficients; closed under booleans.
- M. Campen, L. Kobbelt, *Exact and robust (self-)intersections for polygonal meshes*, CGF 29(2), 2010: plane-based BSP in an
  octree.
- J. Nehring-Wirxel, P. Trettner, L. Kobbelt, *Fast exact booleans for iterated CSG using octree-embedded BSPs*, CAD 2021,
  arXiv 2103.02486: plane-based with integer inputs and 256-bit arithmetic, for iterated CSG (abstract read).
- P. Trettner, J. Nehring-Wirxel, L. Kobbelt, *EMBER: exact mesh booleans via efficient & robust local arrangements*, ACM TOG
  41(4), 2022: plane-based, quantized input, output stays plane-based (earlier notes, prior-art survey).
- M. Attene, *Indirect predicates for geometric constructions*, CAD 2020; G. Cherchi, M. Livesu, R. Scateni, M. Attene,
  *Fast and robust mesh arrangements using floating-point arithmetic*, ACM TOG 39(6), 2020; G. Cherchi, F. Pellacini,
  M. Attene, M. Livesu, *Interactive and robust mesh booleans*, ACM TOG 41(6), 2022, arXiv 2205.14151: implicit points
  LPI / TPI with filters over the double inputs (the arrangement's basis).
- B. Lévy, *Exact predicates, exact constructions and combinatorics for mesh CSG*, arXiv 2405.12949 (v2 2025), ACM TOG: exact
  co-refinement with expansion and multi-precision kernels; the conversion to doubles named as the open part.
- trueform, arXiv 2607.15905 (2026): reports that exact expansions grow to tens of thousands of components in degenerate
  compositions (a vendor paper, only its summary of Lévy's kernels read).

No work was found that bounds the bits of iterated booleans over unquantized double inputs, or that measures whether exact
intermediates make the final rounding easier. The quantized plane-based systems avoid the question by never rounding
between steps and fixing the input precision.
