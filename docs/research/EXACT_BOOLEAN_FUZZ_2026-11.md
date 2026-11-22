# Metamorphic and differential fuzzing of the exact Boolean (2026-11)

Status: research spike (tests and this note, no library change), 2026-11-22, on master `57623890`.
Harness: `tests/geometry3Sharp.Tests/Harness/BooleanFuzzCases.cs` (generators), `BooleanFuzzCheck.cs` (relations),
`BooleanFuzzExact.cs` (the exact comparison); tests: `ExactBooleanFuzzTests` (PR-tier smoke, Nightly campaign).
Builds on [METAMORPHIC_BUG_HUNT_2026-11.md](METAMORPHIC_BUG_HUNT_2026-11.md) (chains of operations, double volumes with
tolerances, 17 Boolean pairs) and [design/BOOLEAN_PROPERTY_TESTS.md](../design/BOOLEAN_PROPERTY_TESTS.md) (integer boxes and
tetrahedra with an analytic oracle). Neither generates the near-degenerate cases on purpose, and neither compares exactly
outside the integer grid.

**Question.** Does `ExactMeshBoolean` (with `ExactMeshArrangement` under it, and `SolidBooleanPipeline` as a consumer) break an
algebraic law, an invariance or its documented result contract on small solids built to sit on its tie decisions?

## 1. Result in five lines

1. **No defect of the exact Boolean.** 587 680 Boolean runs on 10 100 generated cases of the final harness (and 3200 cases of
   the exploration before), 1 392 542 relations checked: every exact relation holds as an equality of rational numbers, in
   every family, including the rotated near-degenerate ones.
2. **One defect of the test harness, fixed here:** the certifying checker (`BooleanCertificate.Cover`) ran out of memory after
   100 s on a result of 4 triangles (section 4). The library computed that Boolean in 1 ms.
3. **Three documented behaviours fired first and are not defects** (section 5): a thin closed component that the rounding
   turned inside out while `RoundingClean` is true; `SolidBooleanPipeline` refusing with `RoundingNotClean` where the default
   mode reports a clean result; triangles the certificate cannot cover within its Delta.
4. The exact comparison is new and reusable: a **surface fingerprint** (per exact plane, signed area and first moment, in
   `BigInteger` rationals) turns the set identities into equalities without a tolerance, for any input coordinates (section 2).
5. Yield: everything that fired did so in the first 3200 cases (400 seeds per family); 10 100 more gave nothing. Section 6
   says where a further hunt could still pay and where not.

## 2. Method

### 2.1 The exact comparison

`ExactMeshBoolean` with `ComputeExactVertices` and without `SafeRounding` gives the exact rational coordinates of the result's
vertices. A triangulated oriented surface is a 2-chain; `SurfaceFingerprint` reduces it to, per supporting plane (the
primitive integer normal with a fixed sign, and the offset), the signed area in units of that normal and the signed first
moment (area times the sum of the corners). Properties:

- It does not depend on the triangulation, the vertex and triangle order, or duplicated vertices; a two-sided sheet cancels.
- It is additive. The boundary operator is linear, so identities of indicator functions become identities of fingerprints:
  `∂(A ∪ B) + ∂(A ∩ B) = ∂A + ∂B`, `∂(A − B) = ∂A − ∂(A ∩ B)`, `∂((U − A) ∩ (U − B)) = ∂U − ∂(A ∪ B)`.
- Six times the volume is the sum over the planes of offset times signed area, so equal fingerprints have equal exact volumes;
  the volume identity `vol(A ∪ B) + vol(A ∩ B) = vol(A) + vol(B)` is implied, exactly.
- It is not a complete invariant (a region may move within its plane with its area and centroid kept), but it has no false
  alarm: every difference it shows is a difference of the two surfaces.

"The solid of A" is the exact result of `A ∪ A` (positive winding number), so the identities also hold for inputs with
winding numbers above 1 (nested shells of the same orientation, overlapping parts). Where the generator knows that the
winding number is 0 or 1, the mesh itself must be the boundary of its solid: `∂(A ∪ A) = A`.

### 2.2 Generators (`BooleanFuzzCases`)

A case is a family name and a seed; nothing is read from a file. The closed solids have 4 to about 100 triangles, the open
meshes 1 to 12. `shells` draws from 5 x 5 fixed kinds (a fifth with a random box), so its cases repeat; the other families give
at least 36 different cases in 40 seeds.

| Family | What it builds | The tie it sits on |
|---|---|---|
| `coplanar` | two boxes on a dyadic grid, B's bounds drawn from a short list around A's; one optionally subdivided; both optionally sheared by a dyadic matrix | overlapping, touching and equal faces in a common plane, with different triangulations, also not axis-aligned |
| `shared` | two tetrahedra on a coarse integer grid with a common face, edge or vertex; a vertex or edge of B exactly in a face or on an edge of A; the same tetrahedron with another vertex order | coincident vertices and edges, vertex-in-face, edge-in-face |
| `ulp` | a `coplanar` or `shared` case with coordinates moved by 1 to 3 units in the last place: each on its own, one axis of all vertices, or one coordinate of one vertex | faces crossing at angles of 1e-16: every filter fails, the exact predicates decide |
| `slab` | a box and a slab of thickness 2^-1 to 2^-49 at or through its top face, sheared by 2^-4 to 2^-45 or rotated by 2^-3 to 2^-39 rad | flat crossings, pieces thinner than the rounding bound |
| `shells` | a box with a cavity, a cavity with an island, a cavity with a face in the outer wall, a nested box of the same orientation (winding number 2), an overlapping part; B across the shells, equal to the cavity, or inside it | nested and coincident shells, winding numbers 0 to 2, coincident faces of opposite orientation |
| `rotated` | a case of another family under one random similarity (rotation, scale 0.001 to 1000, translation up to UTM size) | every exact coincidence becomes a near one, in all directions at once |
| `generic` | random tetrahedra and rotated boxes | the baseline: general position |
| `open` | a closed box and a box without a face or a triangle, a single triangle, or a sheet across the box | the generalized-winding path |

`Generators_HitTheHardCasesTheyAreFor` checks that the families give what they are for (of 40 seeds: at least 30 with a common
plane, 20 with a common vertex, 30 with coordinates a few units in the last place apart, 15 slabs thinner than 1e-6).

### 2.3 Relations (`BooleanFuzzCheck`)

About 80 Boolean runs per closed case.

| Relation of the brief | How it is checked | Exact? |
|---|---|---|
| Operand order | `A ∪ B = B ∪ A`, `A ∩ B = B ∩ A` | fingerprints |
| Set identities and volume | `∂(A ∪ B) + ∂(A ∩ B) = ∂A + ∂B`; `∂(A − B) = ∂A − ∂(A ∩ B)`; the same for `B − A`; `0 ≤ vol(A ∩ B) ≤ vol A, vol B ≤ vol(A ∪ B)` | fingerprints (the volume identity is implied) |
| Complement | in a universe box U: `A − B = A ∩ (U − B)`; `(U − A) ∩ (U − B) = U − (A ∪ B)`; `U − ((U − A) ∩ (U − B)) = A ∪ B`; U as a third operand of a winding predicate | fingerprints, as long as the intermediate result is exactly representable in doubles (`U − X` has only vertices of U and X unless X self-intersects); otherwise the step is counted, not checked |
| Idempotence | `A ∩ A = A ∪ A`, `A − A = ∅`, `∂(A ∪ A) = A` where the winding number is 0 or 1; `A ∪ B ∪ A`, `A ∩ B ∩ A`, `A − B − B` | fingerprints |
| Reordering, Seed | vertices and triangles permuted and corners rotated; another `Seed` | fingerprints |
| Internal paths | another hash salt with one shard; without the coplanar pre-decision and the early-out (`CullOutsideA`); without `SimplifyFlatRegions`; the operation as a `WindingPredicate` | fingerprints |
| `Similarity.All()` | 2^-10 and 2^10: the default mode's mesh bit for bit. The five that round (UTM, 1e-3, 1e3, rotated, rotated + 1e-3 + UTM): the transformed inputs are other inputs, so inclusion-exclusion is checked on them again exactly, the volumes agree with the untransformed ones within the resolution of the transformed coordinates, and the default mode's result is a closed surface | bits / fingerprints / tolerance |
| Result contract | default mode (`SafeRounding`): closed, every directed edge once with its reverse; valid `DMesh3`; clean (`BooleanOracle.CleanReason`: manifold, oriented, no self-intersections by `MeshRepairMetrics`' exact triangle tests, contacts at duplicated positions allowed) where `RoundingClean` says so; the exact volume within the rounding bound (4 times the bound the run applied, `MeshRoundingRepair.MaxLength`, times the inputs' area: it sees a moved or lost large part, not a lost component smaller than that, which is the certificate's job); a second `Compute()` gives the same mesh. Exact mode: closed, exact winding numbers reported for every input, and every vertex is its exact point rounded | independent code |
| Differential | `BooleanCertificate` (the certifying checker; it must apply, and outside `ulp`, `slab` and `rotated` cover every triangle); solid-angle winding numbers of result and inputs at 48 random points per operation; `SolidBooleanPipeline` on the same inputs (closed, the volume of the Boolean; outside the three sliver families it must succeed) | independent code |
| Open inputs | every operation in both orders succeeds with a finite, valid mesh; `ExactWinding` reports the open input; a second `Compute()` and the scale 2^10 give the same mesh, another `Seed` the same surface | bits; the fingerprint of the doubles for the Seed |

The brief names `ExactMeshSelfIntersection`; the library has no class of that name. The self-intersection check used is
`MeshRepairMetrics.SelfIntersectionPairs` (exact, `ExactTriangleIntersection`) through `MeshRobustRepair.FindDefects`.

### 2.4 The checker is checked

`Checker_SeesAWrongResult` replaces the result of one run by a wrong one (a vertex moved by 1/8, a result stretched by 1.5, the
surface reversed, a triangle removed, a crossing box added, an empty or a foreign result, a vertex that is not its exact point,
an input reported as open) and requires that the relation of that run fails: 37 faults over all groups;
`Checker_OpenCase_SeesAWrongResult` does the same for the six relations of the open case, `Checker_SeesAWrongPipelineResult` for
the pipeline's two, and `Checker_WithoutAFault_EveryGroupHolds` runs every group without a fault. Not mutated here: the
certificate's verdict (its clauses have their own mutation tests in `BooleanCertificateTests`). The fingerprint, the bit
comparison and the closed-surface check have their own tests (a difference of one unit in the last place of one coordinate
shows; a triangle and its translate within its plane differ by the first moment alone). The exemptions of the checker are
functions with their own tests, each with a known-wrong case just outside it
(`CleanCheck_ExemptsOnlyThinInsideOutComponents`: a reversed plate 4 times the bound thick fails;
`PipelineRefusals_OnlyTheDocumentedOnesPass`). Slivers in a result, triangles the certificate cannot cover and pipeline refusals
are allowed only for `ulp`, `slab` and `rotated`, whose inputs are built to give them; in the other families each is a failure,
and none occurs.

## 3. Runs

The exploration ran the seeds 0 to 399 of every family (3200 cases) while the checker was built; sections 4 and 5 come from it.
The final harness then ran two disjoint seed ranges, on Linux x64, net10.0, 4 cores, Release:

```
dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~ExactBooleanFuzzTests.Campaign" --logger "console;verbosity=detailed"
FUZZ_FROM=5000 dotnet test ... (the same)
```

The Nightly seeds (from 1000); the output lines of the run, as a table:

| Family | Cases | Boolean runs | Relations | Failed | Results reported not clean (intersections / degenerate / false contacts / scale disparity) | Thin inside-out components | Complement steps not representable | Triangles the certificate could not cover | Pipeline refusals | Time |
|---|---|---|---|---|---|---|---|---|---|---|
| `coplanar` | 400 | 32 400 | 83 997 | 0 | 3 (3 / 0 / 0 / 0) | 1 | 0 | 0 | 0 | 56 s |
| `shared` | 800 | 64 364 | 166 690 | 0 | 2 (0 / 1 / 0 / 1) | 1 | 436 | 0 | 0 | 43 s |
| `ulp` | 400 | 32 133 | 83 181 | 0 | 12 (1 / 8 / 2 / 3) | 0 | 267 | 1155 | 3 | 78 s |
| `slab` | 400 | 32 306 | 83 420 | 0 | 298 (0 / 0 / 0 / 298) | 0 | 94 | 34 | 0 | 47 s |
| `shells` | 400 | 32 400 | 83 840 | 0 | 1 (1 / 0 / 0 / 0) | 0 | 0 | 0 | 0 | 49 s |
| `generic` | 400 | 32 221 | 83 463 | 0 | 0 | 0 | 179 | 0 | 0 | 61 s |
| `rotated` | 250 | 20 026 | 51 711 | 0 | 74 (3 / 2 / 0 / 69) | 0 | 204 | 682 | 2 | 139 s |
| `open` | 2000 | 48 000 | 60 000 | 0 | 0 | 0 | 0 | 0 | 0 | 48 s |
| total | 5050 | 293 850 | 696 302 | 0 | 390 | 2 | 1180 | 1871 | 5 | 8.7 min |

A second run of the same seeds gave the same counts in every column. The seeds from 5000: 5050 cases, 293 830 Boolean runs,
696 240 relations, 0 failed (383 results reported not clean, 5 thin inside-out components, 8 pipeline refusals).

The columns right of "Failed" are the documented exceptions of section 5: counted, not failed. A result "reported not clean"
has `RoundingClean` false with `Status` `Success`, which is the documented answer where the rounding cannot repair within its
bound; for these it is still required to be a closed surface with the exact volume within the bound. Almost all are scale
disparity: a slab thinner than the rounding bound is kept and counted (#985).

Tiers:

- **PR tier**: `ExactBooleanFuzzTests.Smoke`, seeds from 0, 3 to 24 cases per family, up to 2 s each here in a loaded run (limit 5 s).
- **Nightly**: `ExactBooleanFuzzTests.Campaign`, seeds from 1000, 250 to 2000 cases per family; the run is the check (a fuzzing
  campaign finds by volume): 43 to 139 s per family, 8.7 minutes for all here, of the nightly job's 90.
- Other seeds locally: `FUZZ_FROM=5000 FUZZ_COUNT=2000 dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter
  "FullyQualifiedName~ExactBooleanFuzzTests.Campaign"`.
- A failure prints the case as code (`FuzzCase.ToCode`: the coordinates as bit patterns). It is reduced by hand to a procedural
  repro and pinned as a test; no fuzzer output is stored as a file.

## 4. The harness defect: the certificate's coverage grid (fixed in this PR)

- **Found by**: `ulp` seeds 115, 147 and 325 of the exploration (the differential check).
- **Symptom**: `BooleanCertificate.Check()` threw `OutOfMemoryException` after 70 to 110 s. The Boolean itself took 1 ms.
- **Smallest input** (`Finding_CertificateOfASliverIntersection_Finishes`): the tetrahedra `(3,4,0) (4,4,0) (0,4,4) (3,3,3)` and
  `(3.5, 4, 3·2^-52) (3, 1, 2 + 3 ulp) (3, 1, 4 + 3 ulp) (3, 2, 3 + 3 ulp)`: a vertex of B lies on an edge of A but for 3 units
  in the last place. Their intersection is a sliver of 4 triangles with an extent below 1e-12.
- **Cause**: `Cover` sized its uniform grid by the unpadded extent of the triangles (1e-15 here) and then inserted boxes padded
  by Delta (1e-5 of the diagonal, at least): one box covered (pad / cell)^3 cells.
- **Fix**: the grid is built over the padded extent (one line). The pinned test takes 4 ms with it.
- A test-only defect: the certificate is not shipped. It matters because the campaigns of `BooleanCertificateTests` could hit it
  with a sliver result and then time out instead of certifying.

## 5. What fired and is not a defect

Each first firing was read against the class comments before it counted, as in the bug hunt note (its section 5).

1. **A thin inside-out component in a result with `RoundingClean`** (`coplanar` seed 54 under "rotated, scale 1e-3, utm";
   7 such components in the runs of section 3). The exact result has a closed sliver of positive volume (thickness 5e-13
   at coordinates of 5.7e6, where a unit in the last place is 9e-10); rounded to doubles its volume is negative. It intersects
   nothing and has no degenerate triangle, so `RoundingClean` is true, and `NotCleanReason` says "inside-out components".
   Documented in `MeshRoundingRepair.remove_tiny_components` (#1067): a result with nothing to repair keeps such components,
   and only tiny ones are removed, because "a thin one may be a real foil that merely has the wrong orientation, which is
   reported, not deleted". The checker allows exactly this (a negative-volume component that is thin by the rounding's bound)
   and fails on any other inside-out component. **Observation O1** (section 7): the reader of `RoundingClean` alone does not
   see it.
2. **`SolidBooleanPipeline` fails with `RoundingNotClean` where the default mode is clean** (`rotated` seed 150; 13 refusals in
   the runs of section 3). The pipeline runs the Boolean with `StrictRounding`, which uses the relative bound only; the default mode adds
   256 units in the last place, which is what cleans a result at UTM coordinates
   ([design/SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md), section 5). Documented on `StrictRounding`.
3. **Triangles the certificate cannot cover within Delta** (1871 in the Nightly seeds, all in `ulp`, `slab` and
   `rotated`). The certificate's class comment says so: "a triangle not covered after the splits is only counted". Slivers
   thinner than Delta are the cause. The checker counts them and fails only on a violation (C0 to C5).
4. **Two mistakes of the generator**, found by the relations and fixed: a zero moved by one of its own units in the last place
   (2^-1074) is outside the supported range of the rounding (2^-200 to 2^200); the Boolean said so (`OutsideRange`), and the
   2^-10 copy underflowed to another input. A cavity with a face in the outer wall is the boundary of its solid only while
   the two faces coincide exactly; rotated, they cross and `∂(A ∪ A) = A` does not hold, correctly.
5. **Found by the review of the harness, fixed**: the checker threw on a non-finite result of the open case instead of
   reporting it, printed repro code that did not compile for negative coordinates, and skipped a certificate that answered
   "not applicable" without a trace (now a failure: closed inputs always have one).

## 6. What the campaign does not cover, and where a further hunt could pay

- **Input classes of the class comment that are not generated**: inside-out inputs, flipped faces and the `Orientation`
  modes, degenerate input triangles, inputs with non-manifold edges, `StrictRounding` (only through the pipeline), and the
  value of `Contacts`. `ExactBooleanOrientationTests`, `ExactBooleanByDesignTests` and the issue cases cover them with fixed inputs.
- **Size.** All inputs are small. Paths that need many triangles (the shards of the arrangement, the crossing grid, the box
  tree, the per-patch carry of the classification) run with trivial sizes; `ExactBooleanMetamorphicTests`, the certificate
  corpus and `benchmarks/BooleanComparison` cover them with other oracles.
- **Open inputs.** Only stability relations are checked (the same mesh again, scaled, with another seed), because the
  generalized-winding contract promises no set identity. The antipodal and fast-winding paths are not compared.
- **The fingerprint is not complete.** A wrong result with the same per-plane area and centroid passes it; the winding samples
  and the certificate are the second line.
- **Rounding.** The default mode is checked against its documented bound, not against a tighter one: a drift inside the bound
  is not a finding here.
- **Where a hunt could still pay**: curved inputs with many near-tangent crossings (spheres against spheres with offsets of
  units in the last place), n-ary operations with more than three operands on these families, and the same families through
  `ExactMeshImprint`. The exact relations of section 2.1 carry over unchanged.

## 7. Observations for the coordinator (no change made)

- **O1**: `RoundingClean` can be true for a result that `MeshRobustRepair.NotCleanReason` calls not clean ("inside-out
  components", section 5.1). It is documented in the code of the rounding repair, not on `RoundingClean`. Options: say it in
  the comment of `RoundingClean` (docs only); or count such components in a new field. A change of `RoundingClean`'s meaning or
  a new field is the owner's call; nothing is built here.
- **O2**: the certificate's grid fix (section 4) changes a test oracle. It makes no check weaker: the grid only preselects
  candidates for the exact distance test.

## 8. Reproduce

```
dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~ExactBooleanFuzzTests&Category!=Nightly"
dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~ExactBooleanFuzzTests.Campaign" --logger "console;verbosity=detailed"
```

The second prints one line per family: the seeds, the Boolean runs, the relations and the counts of the documented exceptions.
