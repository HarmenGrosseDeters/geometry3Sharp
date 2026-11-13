# Metamorphic relations beyond `Similarity.All()`: catalogue, value ranking, plan

Status: study (tier S, docs only), decisions of the owner recorded in section 9 (2026-11-13). No test or library code is
changed by this note.

**Question.** Which metamorphic relations (properties between the outputs of related inputs) can our harness check
beyond invariance under `Similarity.All()`, which public operations do they apply to, what do they cost, and which
should come first?

**Result in four lines.**
1. Boolean algebra, representation changes (permutation, conforming subdivision) and 2^k scale are already covered
   well. The weakest spots are operations *outside* `ExactMeshBoolean` and relations that are *not* a similarity.
2. The cheapest large gain is an **exact symmetry group**: the 48 axis permutations and sign flips map doubles to
   doubles without rounding, so they allow a bit-for-bit comparison that the 31-degree rotation of `All()` cannot
   (it is toleranced only). It also adds reflections, which only single tests use.
3. The second gain is a **cost relation**: three of the 18 closed issues (#636, #712, #664) are "runs to the limit /
   takes 5-52 s on a transformed copy", found only by fuzz timeouts. No test states "work is invariant under the
   transformation".
4. Recommendation: build the five relations of section 7, in this order: R1 octahedral symmetry, R2 cost invariance,
   R3 refinement invariance of queries and measurements, R4 Boolean algebra beyond pairs, R5 idempotence and
   partition additivity. About 3 PRs of tier M, none touches `ExactMesh*`.

Honesty note on the evidence (section 5): only 18 issues are closed in the repo, and none of them is clearly a bug
that *only* a new relation would have found. The ranking therefore rests on the bug lists in the repo's own findings
documents (ROBUSTNESS_SWEEP R1-R36, `RepairCampaignFindings`, `ExactBooleanPropertyFindings`) and on classes of
fault, and is an estimate, not a measurement.

## 1. What exists today (read from the test tree, not re-run)

| Family | Relations already checked | Where |
|---|---|---|
| All ops with an invariance test (117 files use `Similarity.All`) | Result under translation (UTM), scale 1e-3 / 1e3 / 2^-10 / 2^10, one rotation (31 deg about z, 17 about x), and a mix; exact (bit for bit) for 2^k via `Similarity.IsExact`, toleranced from `Similarity.Resolution` otherwise. `All2D()` for plane inputs. | `Harness/Invariance.cs` |
| Permutation | Vertex ids, triangle order, start corner (`Invariance.Permuted`) | `Invariance.cs` |
| Reuse, determinism | Compute twice on one instance; parallel = serial; repeated runs bit-identical; `GeometricDifference` where ids follow the thread schedule | `DeterminismTests`, `*ReuseTests`, per-op |
| `ExactMeshBoolean` | Analytic box oracle; vol(AuB)+vol(AnB)=vol A+vol B (exact, dyadic); (A-B) u (AnB) = A computed; commutativity of u, n; invariance under triangle order, vertex ids, corner rotation, conforming subdivision, slivers (`Rep` enum) on operand A, B or both; tetrahedra family; reuse and repeated runs; CsCheck campaigns with parameter-level shrinking and replay (net10.0) | `ExactBooleanMetamorphicTests`, `ExactBooleanPropertyTests.CsCheck.cs`, `Harness/BooleanOracle.cs`, [BOOLEAN_PROPERTY_TESTS.md](BOOLEAN_PROPERTY_TESTS.md) |
| `ExactMeshBoolean` n-ary | `AtLeast` k of n spheres = chained pairwise; n-ary = chained; `TwoMeshes_SameAsPairwise`; far components | `ExactMeshBooleanNaryTests*`, `ExactBooleanFarComponentTests` |
| `MeshRobustRepair`, `MeshRoundingRepair` | Apply() true implies `NotCleanReason == ""`; no defect the input lacks; displacement <= MaxDrift; 2^k bit for bit; parallel = serial; reuse; a second Apply() on a clean result is clean (cleanliness, not bit identity) | `RepairCampaignTests.CsCheck.cs`, `RepairCampaignFindings.cs` |
| Alpha wrap | Guarantees (closed, manifold, outward, no self-intersection, on the offset, input inside); repeat on one instance and on a new one; 2^k scaled input bit for bit | `AlphaWrapCampaignTests.CsCheck.cs` |
| Reducer, Remesher | Valid, closed stays closed, Euler characteristic and components, no bowtie, MaxDeviation (also on a second call), PreventFoldOver, sizes 2^-30 .. 2^6 | `RemeshReduceCampaignTests.CsCheck.cs` |
| MarchingCubes | Enclosure on random SDF compositions; four root modes agree; parallel = serial; field/bounds/cell scaled by 2^k bit for bit | `MarchingCubesCampaignTests.CsCheck.cs` |
| Terrain slope design | Never steeper (exact check); catch-line oracles; library cut/fill volumes vs `TerrainVolumeDifference`; second Compute equal; 2^k bit for bit | `TerrainSlopeCampaignTests.CsCheck.cs` |
| `DMesh3` edits | Model-based: after every random edit the mesh equals a plain model; Euler characteristic and connectivity kept by topology-preserving edits; all similarities give the same topology and mapped positions | `DMesh3PropertyTests.cs` |
| Mesh edit ops | Voxel surface area/volume = exposed faces; insert polygon covers exactly the polygon; region operator round trip keeps the rest; trim loop | `MeshEditOpsPropertyTests.cs` |
| ConvexHull2 | Hull = exact monotone chain; invariant under permutation and 2^k scale | `ConvexHull2PropertyTests.cs` |
| Self-intersection pairs | Pair set = brute force over all pairs (lattice soups) | `SelfIntersectionPairsProperty.CsCheck.cs` |
| 2D mesher | An independent oracle (`benchmarks/Meshing2Benchmark/Oracle`, exact reference path and fast path, itself tested by corruption of valid meshes); golden masters; `All2D()` invariance | `Meshing2/*` |
| Readers, writers | Seeded blind mutation of valid files: no throw, time and allocation budget, "Ok means valid"; round trips (`SerializationRoundTripTests`, `TextWriterRoundTripTests`, format tests) | `ReaderFuzzTests`, `*RoundTrip*` |
| CoreMath | Differential vs a reference; campaign | `CoreMath*Tests` |

Harness pieces to reuse: `Similarity`, `Invariance.Transformed / MappedBack / Permuted / Difference /
GeometricDifference`, `CampaignRunner` (CsCheck, replay, `G3_PROPERTY_*`), `BooleanOracle` (`SameSolid`,
`Change(rep)`, `Dyadic`), `DegenerateFamilies`, `MeshMinimizer` / `DeltaDebug`, `Timed`.

### Gaps seen in the tree (grep results)
- Mirroring appears only in single cases: terrain `Fill_IsTheMirroredCut` (z -> -z, bit for bit), glTF negative node
  scale, and `BoundsAndMirrorFixesTests` (a mirror fix in the AABB tree); no operation family is run over reflections.
  Every `Similarity` in `All()` is a proper rotation (determinant +1); the one rotation is irrational, so rotation
  invariance is never bit for bit.
- No absorption, idempotence on a *separately built copy*, De Morgan-style or distributive Boolean identity; no
  associativity of three operands (n-ary vs chained is checked, not `(AuB)uC = Au(BuC)`).
- No cost relation: time and output size on a transformed copy are not compared; the only guard is the global
  per-test time limit.
- Repair idempotence checks cleanliness, not that a clean mesh comes back unchanged.
- Conforming subdivision is applied only to Boolean operands; no other operation (queries, measurements, repair)
  gets a refined copy of its input.

## 2. Prior art

Marked **[V]** = confirmed by a search in this session, **[M]** = recalled from memory, to be checked before a
citation goes into a paper or `docs/SOURCES.md`.

- Chen, Kuo, Liu, Poon, Towey, Tse, Zhou, "Metamorphic Testing: A Review of Challenges and Opportunities", ACM
  Computing Surveys 51(1), art. 4, 2018, DOI 10.1145/3143561 **[V]**. The central practical problem it names is
  *finding good relations* (identification); this note is a relation identification for one code base.
- Segura, Fraser, Sanchez, Ruiz-Cortes, "A Survey on Metamorphic Testing", IEEE TSE 42(9), 805-824, 2016, DOI
  10.1109/TSE.2016.2532875 **[V]**; it reports 119 papers (1998-2015) and that most relations are simple
  (permutation, scaling, addition, subset). Authors' first names are from memory **[M]**.
- Chen, Cheung, Yiu, "Metamorphic testing: a new approach for generating next test cases", HKUST technical report
  HKUST-CS98-01, 1998 **[M]**: the origin of the method.
- Donaldson, Evrard, Lascu, Thomson, "Automated testing of graphics shader compilers", OOPSLA 2017 (GraphicsFuzz)
  **[M]**; the idea: semantics-preserving transformations of a shader (dead code, identity arithmetic) must keep the
  rendered image. The CACM 2020 article named in the task could not be confirmed by the search **[unverified]**.
  Transfer: *equivalence modulo inputs* (Le, Afshari, Su, PLDI 2014 **[M]**), i.e. change the program where the input
  does not look. For us: add geometry where the operation must not look (far components, interior refinement).
- Claessen, Hughes, "QuickCheck: a lightweight tool for random testing of Haskell programs", ICFP 2000 **[M]**:
  properties, generators, shrinking. CsCheck (Anthony Lloyd, Apache-2.0; already used, pinned 4.9.1 in
  `docs/SOURCES.md`) is the .NET equivalent.
- Kanewala, Bieman, "Testing scientific software: a systematic literature review", Information and Software
  Technology 56(10), 2014 **[M]**: reports metamorphic testing as the main answer to the oracle problem in scientific
  code. Geometry is the same situation: we have no oracle for most outputs.
- Geometry and meshes: the search found no paper that applies metamorphic testing to mesh Booleans. It found a
  metamorphic-plus-classifier approach for mesh simplification from HKU (hub.hku.hk, title and authors not read
  **[unverified]**) and vendor comparisons of Boolean libraries on degenerate inputs (not scholarly, not used).
  Search terms: "metamorphic testing" + computational geometry / mesh Boolean / polygon mesh processing. So the
  geometry-specific list here is our own; section 3 gives the reasoning for each relation.
- Robust-predicate literature gives the *exactness* argument used here: IEEE operations `+ - * /` on doubles are
  exact for sign flips, negation and (barring overflow) 2^k scaling, so a comparison can be bitwise (Shewchuk 1997,
  "Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates" **[M]**). The repo states the
  same in its libm rule (`+ - * /`, `Sqrt`, `Floor`, `Round`, `Abs`, `Min`/`Max` are IEEE-exact everywhere).

How others test (for the "how others solve it" line owed to the owner, AGENT_WORKFLOW section 2): GraphicsFuzz and EMI
modify the program, we modify the geometry; QuickCheck-style libraries generate and shrink; scientific-software
practice uses a few simple relations (permute, scale, add a no-op component) first. That is the order of this plan.

## 3. Catalogue of relations

Notation: `op` an operation, `x` an input, `T` a transformation, `~` the comparison. **Exact** = bit for bit up to
the ordering that the operation does not promise (compare as a set of oriented triangles by coordinates, or use
`Invariance.GeometricDifference`); **tol** = within `Similarity.Resolution` or the operation's documented bound.
Cost: S = under a day, one helper plus parametrised tests; M = a generator or a new oracle; L = a new oracle or a
research question. Value: expected faults found per cost, see section 5.

### 3.1 Symmetry and relabelling

| ID | Relation | Applies to | Comparison | Cost | Value |
|---|---|---|---|---|---|
| S1 | **Octahedral symmetry**: apply one of the 48 signed axis permutations `P` (24 rotations, 24 reflections) to the input: `op(P x) = P op(x)`. Reflections reverse triangle orientation, so the mesh is re-wound (swap two corners) to keep outward normals. | Everything geometric. Exact for ops with no axis-dependent choice (Boolean, predicates, hull, rounding repair, winding number, MC vertices); tol or set-compare where the op prefers an axis (AABB tree split axis, Morton order, sweep direction, remesher tie breaks). | exact (set compare) or tol; classify per op | S | high |
| S2 | Vertex / triangle relabelling, corner rotation | all | exact | exists | - |
| S3 | **Orientation flip of the whole input**: reverse every triangle of a closed mesh. For a solid operation the "inside" swaps: Boolean of two reversed solids is the reversed complement-semantics result; for the exact boolean this is documented behaviour to be read from `ExactBooleanOrientationTests` first. For measurements: volume changes sign, area unchanged. | measurements, MeshQueries (winding number sign), repair (orientation repair should reorient back), Boolean | exact | S | medium |
| S4 | **Operand swap** `op(A,B)` vs `op(B,A)` | u, n commutative (exists); `-` is not commutative: `A-B` vs `B-A` partition (exists as inclusion-exclusion) | exact | exists | - |
| S5 | **Duplicate the input**: two identical, separately built copies of a component as one operand (a doubled surface). For Resolve: the result is the same as for one copy or reported. | Resolve, repair, Boolean preparation | exact/status | M | medium-low (contract questions, `ExactBooleanInputPreparationTests` has cases) |

### 3.2 Splitting without moving the surface (refinement)

| ID | Relation | Applies to | Comparison | Cost | Value |
|---|---|---|---|---|---|
| F1 | **Triangle split, edge split, vertex insertion on an edge or in a face** (1-to-3, 1-to-4 conforming): the surface as a point set is unchanged, so volume, area (tol, exact when dyadic), bounds, winding number at any query point, nearest-point *distance*, `IsInside`, signed distance, Hausdorff and `MeshDeviation` (= 0 against the unsplit mesh) must agree. | `MeshMeasurements`, `MeshQueries`, `DMeshAABBTree3` (distance, ray hit, winding, `FastWindingNumber`), `MeshSignedDistanceGrid`, `MeshWindingNumberGrid`, `MeshHausdorffDistance`, `MeshRepairMetrics.SelfIntersectionPairs` (a split must not create pairs beyond shared elements), `MeshRobustRepair` (result is the same solid up to MaxDrift), MC/DC of an SDF built from the mesh | tol on queries (the split point is rounded unless dyadic; use dyadic midpoints for exactness) | S-M | high: ops that were never given a refined copy |
| F2 | Same for Boolean operands: **exists** (`Rep.Subdivision`, `Rep.Slivers`). | Boolean | - | exists | - |
| F3 | **Coarsening of a flat region** (collapse interior vertices of a coplanar fan) is the inverse; for ops with a documented envelope (`Reducer`, `MeshEnvelope`): `op(coarse)` stays within MaxDeviation of `op(fine)`. | Reducer, Remesher | tol | M | low-medium |
| F4 | **Insert a coplanar, interior vertex** (a T-junction-free split of a face into a fan): a *negative* test for ops that count vertices (genus, Euler characteristic must not change). | measurements, `MeshValidation` | exact | S | low |

### 3.3 Boolean algebra (regularized, solids)

Present: commutativity of u and n, inclusion-exclusion on volume, `(A-B) u (AnB) = A`. Candidate additions, all
comparisons by `BooleanOracle.SameSolid` (box oracle) or volume plus sampled winding numbers for non-box solids:

| ID | Relation | Notes | Cost | Value |
|---|---|---|---|---|
| B1 | **Idempotence on a separately built copy**: `A u A' = A`, `A n A' = A`, `A - A' = empty`, where `A'` is the same solid built with other triangulation (subdivision, other diagonal) so every face is coplanar with a face of the other operand | strongest degeneracy stress: all faces coincide. The current "identical" family uses the same triangulation. | S | high |
| B2 | **Absorption**: `(A u B) n A = A`, `(A n B) u A = A` | two chained operations, so the first result is input of the second: this checks that a Boolean *output* is a valid *input* (output of op1 touches A along entire faces) | S | high |
| B3 | **Subset**: `A n B subset A` via `(A n B) - A = empty` and `(A n B) n A = A n B` | | S | medium |
| B4 | **Associativity, distributivity, De Morgan by difference** for three solids: `(AuB)uC = Au(BuC)`, `An(BuC) = (AnB)u(AnC)`, `A-(BuC) = (A-B)-C`, `A-(BnC) = (A-B)u(A-C)` | needs a triple generator (nested/touching/overlapping families squared); the cost is the generator, not the checks. Complement is not available (no infinite solid); difference replaces it. | M | medium-high |
| B5 | **n-ary against its definition**: `AtLeast(1) = union`, `AtLeast(n) = intersection`, `AtLeast(k)` by inclusion-exclusion on volume, order of operands irrelevant | partly exists (`AtLeastKOfN_Spheres_MatchesChainedPairwise`) | S | medium |
| B6 | **Union is the sum when disjoint**, translate B apart by a power of two until separated: result volume additive, result equals the disjoint concatenation | an upper-bound check on touching handling | S | medium |
| B7 | **Translate one operand along a grid axis through a touching configuration** (distance -h, 0, +h with h = 2^-k): the result changes *continuously* in volume (no jump larger than the overlap, regularized contact gives zero volume at 0). A monotonicity relation at the combinatorial boundary: a contact case must lie between its neighbours. | reveals mishandling of touching, the area where historical findings sit | M | medium-high |
| B8 | **Idempotence of the output rounding**: `Round(Round(R)) = Round(R)` for the SafeRounding result `R` (no further edit on a clean rounded mesh); `MeshRoundingRepair.Apply` on its own output returns no change and keeps drift 0 | related R22, R23 (repeated differences) | S | medium-high |

### 3.4 Idempotence, monotonicity, additivity

| ID | Relation | Applies to | Comparison | Cost | Value |
|---|---|---|---|---|---|
| I1 | **Repair is idempotent**: `Apply(Apply(x))` returns the first result bit for bit and a clean input is returned unchanged (today: only "still clean") | `MeshRobustRepair`, `MeshRoundingRepair`, `MeshAutoRepair`, `ExactMeshBoolean.Resolve`, `MeshWindingNumberSolidify` (up to its resolution) | exact for rounding/resolve; set for others | S | medium-high |
| I2 | **Reduce/Remesh a fixed point**: remesh of an already-remeshed mesh at the same target changes little (area/volume within tol); not a bit-identity claim | Remesher, Reducer | tol | S | low (the ops are heuristic; flaky risk) |
| M1 | **Monotone refinement**: a finer Marching Cubes cell size / smaller alpha / smaller `MaxDeviation` / larger grid never *increases* the measured error (Hausdorff to the reference, volume error to the analytic solid) beyond a stated slack; `Reducer` with a larger triangle target has deviation not above the smaller target. | MC, DC, alpha wrap, SDF grid, Reducer, terrain TIN (smaller error bound gives more points, monotone) | tol with slack | M | medium (needs an analytic reference, which the campaigns have) |
| M2 | **Monotone offset**: `Offset(d1) subset Offset(d2)` for d1 < d2 by winding numbers at sample points; alpha wrap: wrap at alpha a1 contains the input; larger offset contains the smaller | `MeshSignedDistanceGrid` offsets, `MeshAlphaWrap`, `MeshEnvelope` | sampled | M | medium |
| A1 | **Additivity**: vol/area of a plane cut: `vol(below) + vol(above) = vol` and the cap area equals the section area; `MeshPlaneCut` of a mesh at two parallel planes; union of the halves is the original (B-level check) | `MeshPlaneCut`, `MeshTrimLoop` | exact when dyadic | S | medium (R5 was `MeshPlaneCut` losing the cap at tiny scale; the scale relation found it) |
| A2 | **Terrain antisymmetry and additivity**: `Volume(T1,T2)` cut = fill of `(T2,T1)`; cut - fill = integral difference; three surfaces `T1,T2,T3`: `V(T1,T3) = V(T1,T2)+V(T2,T3)` for net volume; cell-sum = total (exists) | `TerrainVolumeDifference`, `TerrainOffset` | tol (the sum is exact up to rounding by design) | S | medium |
| A3 | **Partition**: the output with `MaxMeshElementCount = L`, concatenated, equals the unlimited output (set of faces); a bound property found #647 directly, the relation checks the content too | `VoxelSurfaceGenerator`, `PartitionedReduction`, batch sizes ("CandidateSearchInBatches_SameResult" exists) | exact | S | medium-low |

### 3.5 Locality ("equivalence modulo inputs")

| ID | Relation | Applies to | Comparison | Cost | Value |
|---|---|---|---|---|---|
| L1 | **Far component**: add a component C far away (distance D >> size); `op(A u C)` = `op(A)` plus `op(C)` on the respective parts. Exists for the Boolean and the repair F1 cases (`RepairCampaignFarPartExamples`, `ExactBooleanFarComponentTests`); not for Remesher, Reducer, QualityMesh2, MC, alpha wrap, UV atlas. | all area ops that treat components independently | exact where the op is local, else tol | M | medium-high: this is the class that found F1/F6/F7 in repair |
| L2 | **Detail outside the bounds of the query**: change geometry that cannot reach the result (a pocket far from a box-box Boolean, a point far away in `QualityMesh2` domains with a gap) | QualityMesh2 (#638: a point 1e-286 of the extent away), alpha wrap | exact | M | medium |
| L3 | **Translation by an integer lattice vector** (2D mesher, #723 "reproduces after a translation by (-331000, -5585000)"): the output changes by the same translation up to the documented rounding | QualityMesh2, ConstrainedDelaunayMesh2, Clipper2 adapter | tol | S (partly `Utm`) | low (already in `All2D`) |

### 3.6 Scale

| ID | Relation | Notes |
|---|---|---|
| Z1 | 2^k scale, bit for bit | exists, 117 files |
| Z2 | **Non-power-of-two scale** (1e-3, 1e3): toleranced via `Resolution` | exists |
| Z3 | **Cost under scale and rotation**: see section 3.7 | new |
| Z4 | **Scale of one operand only** (disparity): `ExactBooleanScaleDisparityTests` exists; extend to repair / alpha wrap with a far part | exists / partial |

### 3.7 Cost (resource metamorphic relations)

| ID | Relation | Applies to | Comparison | Cost | Value |
|---|---|---|---|---|---|
| C1 | **Output size and work counters under the transformation**: `size(op(T x)) <= c * size(op(x))` (vertices, triangles, Steiner points, iterations, attempts) for T in the 48 symmetries, `Rotated`, `Utm`, 2^k. For 2^k and the symmetries the sizes are *equal*. | QualityMesh2 (#664: 400 000 vs a few hundred), repair fallbacks (solidify, wrap resolution), Remesher, Reducer, MC | integer compare, c = 2 for non-exact T | S-M (needs counters where the result has none; the 2D mesher reports `added_vertices`, other ops give vertex and triangle counts of the output) | high |
| C2 | **Time ratio** `t(T x) <= 20 t(x) + 50 ms`, measured in the nightly tier only | ops where no counter exists (`MeshRobustRepair` #712: 5-52 s for 4-20 vertices) | wall clock, generous | S | medium (flaky unless generous and nightly) |

### 3.8 Reader / writer

| ID | Relation | Applies to | Comparison | Cost | Value |
|---|---|---|---|---|---|
| W1 | **write-read-write is a fixed point**: `write(read(write(m)))` equals `write(m)` byte for byte (text formats with round-trip doubles); for float32 formats the second write is the fixed point of the first | OBJ, OFF, PLY (ascii, binary), STL, 3MF, glTF/GLB, `.g3mesh`, XYZ | bytes | S | low-medium |
| W2 | **Diamond across formats**: `m -> OBJ -> PLY -> 3MF -> OBJ`; double formats exact, float formats within the float32 resolution | all pairs | tol | S | low-medium |
| W3 | **Transformed mesh through a writer**: `read(write(T m)) = T read(write(m))` for the `Similarity` set (exists as `MeshRoundTrip_AllSimilarities_StoredBitsAndMappedBackPositions` for `.g3mesh`; the float formats lose UTM precision by design, a documented relation) | writers | bits / tol | exists | - |
| W4 | **Truncation prefix**: a truncated file reads either an error or the *prefix* of the faces (monotone), never a different mesh | readers | | S | low (the fuzz covers crashes) |

### 3.9 Differential relations (not metamorphic, listed to mark the border)
Exact vs fast predicate, serial vs parallel, fast vs reference path of the 2D oracle, `MeshRobustRepair` vs the exact
resolve on the same input: these are differential tests, already the repo's strongest tool. They are out of scope.

## 4. Where the relations fit in the harness

- **S1** needs `Similarity`-like objects for the 48 symmetries. They must not go into `Similarity.All()` (90+ tests
  would change meaning, and 24 of them reverse orientation). Add `Symmetry.All48()` and `Symmetry.Rotations24()` with
  `Apply(Vector3d)`, `Apply(DMesh3)` (re-winds reflections) and `Inverse`, next to `Invariance.cs`.
- Every test in `Invariance.cs` takes a `Func<DMesh3, DMesh3>` and a comparer; a `Symmetry` can reuse them as they
  are because a symmetry maps doubles to doubles exactly, so `Difference` (bitwise) applies after mapping back.
- F1 needs one helper, `Refine.Dyadic(mesh, seed)`, that splits triangles at dyadic points (exact), alongside the
  `Rep.Subdivision` code of `BooleanOracle.Change`; move that code into `Invariance` so both use it.
- B1-B4 need only `BooleanOracle` plus a triple generator in `ExactBooleanPropertyTests.CsCheck.cs`.
- C1 needs a counter per operation; a `Func<int>` selector per op is enough.

## 5. Value estimate: what the past record says

### 5.1 Closed issues (all 18; `gh api search/issues`, state closed)

Only 18 issues are closed, 15 of them `bugfix`. Mapping each to the relation that would have caught it *without
knowing the input*:

| # | Defect | Found by | Would a relation in this note have caught it? |
|---|---|---|---|
| 723, 637 | QualityMesh2 `AngleBelowBound` without a forced angle | fuzz / corpus run; 723 "reproduces after a translation" | Not by a relation: an oracle-versus-mesher disagreement. L3 and S1 on the minimized input would pin it, not find it. |
| 718 | `CoreMath.Exp(2^-53)` | review of a port | No relation (differential vs reference exists); algebraic identities `Exp(Log(x)) ~ x` are weak at one ulp. |
| 712 | `MeshRobustRepair` 5-52 s on 4-20 vertices, magnitude 2^-100 .. 2^97 | fuzz timeouts | **C1/C2** (cost under 2^k), the same input at magnitude 1 takes milliseconds. |
| 710 | `ExactMeshBoolean` difference throws, magnitude 3.7e-223, one corner at 1.8e-284 | fuzz | **Generator range** (extreme magnitudes with near-degenerate corners in the CsCheck box generator), not a new relation. 2^k scale of a *working* case does not reach it. |
| 664 | QualityMesh2 near-contact fan under `rotated` runs to the Steiner limit | invariance test (`Similarity.All()`) | Already caught by `All()`; **C1** states it as a rule instead of a test cap (the #661 cap of 3 000 vertices is a workaround). S1 adds the exact variant. |
| 647 | `VoxelSurfaceGenerator` exceeds `MaxMeshElementCount` | seeded property | Caught by the bound property; **A3** adds content check. |
| 645 | `ConvexHull2` throws NotImplemented for offered modes | property test | No (contract). |
| 644 | `ConvexHull2` wrong for extent <= 2^-52 | property test over 2^k | Already by 2^k scale. |
| 638 | QualityMesh2 livelock, point 1e-286 of the extent away | fuzz | **L2** (a far point must not change local meshing). |
| 636 | `MeshRobustRepair` takes 9 s on 6 vertices + tetrahedron | measured | **C2**. |
| 628, 620 | `PlanarComplex` nesting and hole area fail for close circles under simplification and translation | probe over `Similarity.All()` | Caught by `All()`; **F1-like** relation "refining the circle sampling keeps the nesting" is the missing monotone relation (M1). |
| 676, 671, 630, 625, 621 | flaky CI, tool refactor, local tasks | - | Not applicable. |

Reading: three of 13 real defects (712, 636, 664) are cost explosions, which no existing relation states; two (710,
638) are about generator range; four (644, 664, 628, 620) were found by the similarity harness itself, so the
harness works. No closed issue is a bug that only mirror symmetry or Boolean absorption would have found. That is
the weakness of the evidence for R3-R5 and the reason the plan below keeps them cheap.

### 5.2 Wider record (ROBUSTNESS_SWEEP R1-R36, findings classes in the tests)
Of 36 sweep findings, the relation that found each: scale 2^k or 1e-3 about 17 (R1, R2, R3, R5, R8, R10, R14, R15,
R17, R26, R28-R30 and others), non-finite input about 9 (R9, R11-R13, R20, R24, R27, R32-R36), nondeterminism 3 (R18,
R19, plus the first-hour findings), translation to UTM 4 (R3, R4, R22, R23), plus a few that were reclassified or
documented. Counts are rough, read from the table's symptom column. The campaigns added F1-F7 (repair: scale at
extreme magnitudes, a lost far part) and the Boolean Finding 1 (SafeRounding contracts a legal tiny triangle),
the latter found by the *conforming subdivision / slivers* relation: **a non-similarity relation already earned its
cost once**.

Two readings follow. (1) Scale and non-finite input found the bulk, and both are exhausted by now for the swept
operations; the next faults sit in classes the harness does not state. (2) Bug classes visible in the records that
no relation covers: asymmetry (cell index saturation for negative coordinates, R4, only appeared for particular
coordinates), accumulated rounding across *chained* operations (R22, R23: repeated differences, whose relation is
chaining and idempotence, B2 and B8), cost (C1), and refinement (F1).

## 6. Cost and tier

- PR tier (every push): relations that cost milliseconds per case on small procedural inputs, parametrised over a
  handful of cases (S1 with a subset of operations; F1; B1-B3; I1). They must obey the 5 s rule and carry the three
  CLAUDE.md companions (invariance via `Similarity.All()` where the relation is itself a transformation, reuse,
  determinism).
- CsCheck campaign tier (`Category=Campaign`, default 5-20 samples in CI, `G3_PROPERTY_SAMPLES=2000` locally): the
  random versions (B4 triples, B7 contact sweeps, L1 with random far parts, C1 over random scenes).
- Nightly (`[Trait("Category","Nightly")]`): only the wall-clock check C2 and an all-48-symmetries x all-operations
  sweep (about 48 x 25 operations; at 0.1 s each, 2 min, which is borderline for PR but fine nightly). The nightly
  rule in CLAUDE.md requires "a check whose run is the check, above 10 s"; C2 is justified by timing noise, the sweep
  is not (make it PR tier with a symmetry subset and the full group nightly, or accept the 2 min split by operation).
- Exact code: none of these needs a change of `math/Exact*` or `mesh_ops/ExactMesh*`; tests only. Mutation shards
  are unaffected. `New tests on base` will report them *passing* on base unless they find a bug (a test-only PR
  without a `bugfix` label is fine).

## 7. The plan: five relations, in order

| # | Relation | Class and file | Tier | Est. size | Why first |
|---|---|---|---|---|---|
| 1 | **R1 = S1 octahedral symmetry** (48 elements; reflections re-wound) on `ExactMeshBoolean`, `MeshRoundingRepair`, `MeshRobustRepair`, `DMeshAABBTree3` queries, `MeshMeasurements`, `ConvexHull2`, `QualityMesh2` (2D: 8 symmetries of the square), `MarchingCubes`, `Reducer` (tol). New `Harness/Symmetry.cs`, new `SymmetryInvarianceTests.cs` with an op table like `RobustnessSweepTests.Ops` | PR (subset of 6 symmetries: identity, one 90-degree rotation, one reflection per axis; full 48 as a campaign) | M, about 1 PR | Zero new oracles. Exact comparison. Covers handedness, sign and axis bias, which no test states (reflection exists only in the terrain mirror test and two single cases). |
| 2 | **R2 = C1 cost invariance** (+ C2 nightly) on `QualityMesh2` (Steiner points), `MeshRobustRepair` (stage, attempts, final vertex count), `Remesher`/`Reducer` (result count), `MarchingCubes`, `MeshAlphaWrap` for the symmetries, `Rotated`, 2^k and `Utm` | PR for counters (exact equality for 2^k and symmetries, factor 2 for others); C2 nightly | S-M | 3 of 13 real closed defects were this. Counters make it deterministic. |
| 3 | **R3 = F1 refinement invariance** for queries and measurements (dyadic split of triangles, edges and faces; compare volume, area, winding, distance, `SelfIntersectionPairs`, repair result) | PR; class `RefinementInvarianceTests`; helper moved from `BooleanOracle.Change` | S-M | Reuses the Boolean's best relation for all other operations; slivers from splits stress the same predicates that have bitten before (Finding 1). |
| 4 | **R4 = B1, B2, B4 + B8 Boolean beyond pairs**: idempotence on a re-triangulated copy, absorption, associativity / distributivity / difference identities with a triple generator, output rounding idempotence | PR for B1-B3 and B8 as deterministic families in `ExactBooleanMetamorphicTests`; B4 and B7 as CsCheck campaigns in `ExactBooleanPropertyTests.CsCheck.cs` | M | Chained operations and fully coplanar operands are where the exact pipeline is least covered (output of op 1 as input of op 2). Test-only: `ExactMesh*` locked for local work during Exact Boolean 3.0; this PR touches tests only. |
| 5 | **R5 = I1 idempotence + A1/A2 additivity + L1 far component for the non-Boolean ops** (repair fixed point bit for bit, plane-cut additivity, terrain antisymmetry, far part for Remesher/Reducer/QualityMesh2/MC) | PR for I1, A1, A2; campaign for L1 | S-M, 2 small PRs | Cheap breadth. L1 repeats the idea that found F1/F6/F7 in repair. |

Not recommended now: M1/M2 (needs analytic references; the campaigns already hold most), W1-W4 (readers have a
fuzz harness and round-trip tests; low yield), B6, F3, I2, S5.

**One plan.** R1 → R2 → R3 in the first wave (independent, each one PR, tier M, reviewed by one fresh agent), R4 and
R5 in the second wave. Do R1 first: it finds out in a day whether reflection and exact rotations show anything at
all; if the first sweep over all 25 `RobustnessSweepTests.Ops` is clean, the expected value of R3-R5 stays the same and
the plan continues, but the exact-symmetry coverage is then a regression net, not a bug finder, and should stay
cheap (subset in the PR tier).

## 8. Risks

1. **False positives from legitimate non-uniqueness.** Remesher, Reducer, alpha wrap and the 2D mesher make
   tie-breaks that depend on axis order, so S1 may report differences that are not defects. Mitigation: classify each
   operation as `ExactGeometry`, `ExactUpToOrder` or `Toleranced` in the op table before running, as
   `Invariance.GeometricDifference` already does; add an op only with its class.
2. **Reflection and orientation.** A reflected mesh is inside-out unless re-wound. Operations that infer
   orientation from the signed volume (repair, Boolean preparation) may flip it back, and the comparison must then
   use the oriented-triangle set of the re-wound result. Test the helper with a `BoxOracle`-style self-check first,
   as `BoxOracle_AcceptsHandBuiltResults_AndRejectsWrongOnes` does.
3. **Circular oracles.** B1-B4 use `ExactMeshBoolean` on both sides of an identity; a bug that is consistent
   under the identity (a wrong but algebraic rule) passes. Mitigation: always include the box oracle or sampled
   winding numbers (independent code) for the final comparison; the existing `SameSolid` is independent.
4. **Test-time and CI cost.** 48 symmetries x 25 operations must stay under the 5 s rule; use a subset in PR tier.
5. **Wall-clock flakiness** (#676, #671 were load flakes on net48). C2 only nightly, factor 20, and only on
   linux net10.0; C1 uses counters.
6. **Counters are not public API for every op.** Where a count is absent, use result sizes (vertices, triangles) and
   do not add public API (API compatibility with 1.0.324 is enforced; added members are allowed, changed ones are not).
7. **Evidence is thin** (section 5): the plan may find nothing. The value is then a regression net, which is also
   what a growing exact pipeline (Exact Boolean 3.0) needs; the cost stays low because no oracle is built.
8. **Literature gaps.** The geometry-specific part has no published precedent that the search found; citations
   marked **[M]** and **[unverified]** must be checked before they go into `docs/SOURCES.md` (no code is ported from
   any of them, so no licence issue arises; CsCheck is Apache-2.0 and already listed).
9. **No probe was run.** The container has no .NET SDK (`dotnet: command not found`); installing it
   (docs/QA.md section 5) was judged not worth it for a study whose first deliverable would be a throwaway test.
   The one measurement worth taking first is R1 on the 25 sweep operations (task 1 of the plan).

## 9. Decisions of the owner (2026-11-13)

All as recommended:
1. Plan accepted. R1 (octahedral symmetry) and R2 (cost invariance) first, as one test-only feature PR; R3-R5 later,
   each as its own PR.
2. The symmetries live in their own `Harness/Symmetry.cs`; `Similarity.All()` stays unchanged.
3. A subset of the 48 symmetries in the PR tier, all 48 in the nightly job.
4. Cost relation: equality under the 2^k scales and the symmetries, at most a factor of 2 otherwise; a nightly
   wall-clock check with a factor of 20.
5. The triple-Boolean generator (B4) waits until Exact Boolean 3.0 is complete.
6. By-design differences (tie-breaks) are listed as classified `Toleranced` rows with the reason, not as
   `KnownLimitation_` tests.
7. The [M] citations are verified before they go into `docs/SOURCES.md`. If R1 finds real defects they become
   `bugfix` issues and are not fixed in the R1/R2 PR.
