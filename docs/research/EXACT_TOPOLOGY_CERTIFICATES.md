# Solid Betti numbers as Boolean and repair test oracles

Tier M study for [PR #1424](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1424). The question is whether shell nesting adds a useful assertion to the existing Boolean and repair tests.

## What the paper contributes

Delfinado and Edelsbrunner count components, independent tunnels and voids of an embedded finite simplicial complex. Their incremental algorithm classifies simplex insertions by whether they create a cycle or kill one. The three-dimensional construction uses connectivity on the complex and on the complement, with a reverse traversal for the latter. The near-linear bound is in the size of the supplied complex. The Boolean and repair APIs return a boundary mesh. For disjoint boundary shells, the information needed for the solid Betti numbers can be recovered without building tetrahedra. [Delfinado and Edelsbrunner, 1995](https://pub.ista.ac.at/~edels/Papers/1995-03-IncrementalBettiNumbers.pdf)

Hatcher's Alexander duality (Corollary 3.45) identifies the second Betti number of the solid with the number of bounded complement components. Zhou et al. track winding labels on arrangement cells, including the unbounded cell. Here only the parity of nesting in the returned mesh is needed. `MeshTopologySummary` already supplies the sum of shell genera, `DMeshAABBTree3.WindingNumber` has an exact ray-crossing path for closed meshes, and the repair cleanliness check already uses geometric nesting to detect inverted components. `ContouringTopologyCertificate` concerns the topology of a contour relative to its field. [Hatcher](https://pi.math.cornell.edu/~hatcher/AT/AT.pdf) · [Zhou et al.](https://www.cs.columbia.edu/cg/mesh-arrangements/) · [Contouring design](../design/CONTOURING_TOPOLOGY.md)

## Derivation and preconditions

For the boundary of a bounded solid with pairwise disjoint, closed, embedded orientable shells, let `s` be the number of shells and `g` their total genus. The surface Betti numbers are `(s, 2g, s)`. Determine the depth of each shell by counting the other shells that contain one of its vertices. The vertex lies off every other shell, and the entire connected shell lies on the same side of each other shell. The unbounded region is empty. Crossing a shell alternates between material and void, so each even-depth shell bounds a new material component and each odd-depth shell bounds a new cavity. The material-component count is `b₀`. Alexander duality gives the cavity count as `b₂`. Doubling the solid along its boundary gives a closed orientable 3-manifold with Euler characteristic zero, so `χ(solid) = χ(boundary)/2 = s − g`. Since `b₀ + b₂ = s` and `b₃ = 0`, the solid's `b₁ = g`.

The guarantee is about the Betti numbers of that solid. The three counts leave cavity ownership, geometry, and winding orientation undetermined. For example, moving a cavity from one disconnected material part to another can preserve all three counts. Keep cleanliness and signed-orientation assertions alongside a nesting check. Touching or crossing shells and bowtie vertices fall outside the derivation.

## Probe and tests

The probe of this study was a benchmark project (`benchmarks/TopologyCertificateProbe`, PR #1449 at `959a48a8`). Its check now lives in the tests, and the project was removed. The helper [`SolidBetti`](../../tests/geometry3Sharp.Tests/Harness/SolidBetti.cs) computes the solid Betti numbers by nesting parity. It asserts the cheap parts of its precondition: the mesh is closed, has no bowtie vertex, and no two shells meet. [`SolidBettiTests`](../../tests/geometry3Sharp.Tests/SolidBettiTests.cs) checks it on procedural boxes: a cube with a cavity `(1, 0, 1)`, two disjoint boxes `(2, 0, 0)`, a part inside a cavity `(2, 0, 1)` and a Boolean through tunnel `(1, 1, 0)`. Its assertions sit in the cavity branches of `ExactMeshBooleanTests.DisjointAndNested` and `NestedShells_SameOrientation_AndInverted`, and in the forced repair (`PassThroughCleanParts = false`) of `RobustRepairPassThroughTests.CleanWholeInput_PartsWhoseBoxesMeet_ReturnedUnchanged`.

The probe called `ExactMeshBoolean` with safe rounding for the enclosed subtraction and the through-tunnel cut. It sent the enclosed result through `MeshRobustRepair` with clean pass-through enabled and disabled. The expected Betti numbers follow from the procedural constructions. The probe verified the returned meshes with the library's validity and cleanliness checks and checked the small fixtures for pairwise shell intersections before measuring nesting. It then compared three clean replacements for the expected cavity. The two disjoint boxes have volumes 24 and 2, so the replacement matches the cavity's total signed volume of 26.

The run below used Release on Linux x64 (.NET SDK 10.0.404, Intel Xeon Platinum 8573C). The timing is the median of seven batches of 1,000 calls after 1,000 warm-up calls per variant. It excludes the Boolean, repair and cleanliness checks. The baseline computes `MeshTopologySummary`; the extended variant computes the same summary and builds fresh winding trees for the nesting queries.

```text
repair pass-through enabled=True:PassThrough
repair pass-through enabled=False: Repair
case | triangles | surface Betti | solid Betti | total volume | signed shell volumes | surface matches cavity | solid matches cavity
Boolean cavity | 24 | (2, 0, 2) | (1, 0, 1) | 26 | [-1, 27] | True | True
Repair pass-through | 24 | (2, 0, 2) | (1, 0, 1) | 26 | [-1, 27] | True | True
Repair forced | 24 | (2, 0, 2) | (1, 0, 1) | 26 | [-1, 27] | True | True
Filled cavity | 12 | (1, 0, 1) | (1, 0, 0) | 27 | [27] | False | False
Disjoint solids | 24 | (2, 0, 2) | (2, 0, 0) | 26 | [2, 24] | True | False
Through tunnel | 32 | (1, 2, 1) | (1, 1, 0) | 24 | [24] | False | False
Point contact | edge components=2 | bowties=1 | clean check=bowties | reference surface Betti=(1, 0, 2)
Boolean cavity median us/check: summary=35.41, summary+nesting=164.60
```

The surface comparison rejects two of the three clean replacements. Adding nesting rejects all three. The disjoint replacement also preserves triangle count and total signed volume, so those assertions do not close the gap. Both real repair paths returned the expected cavity. For the non-manifold control, the two cube boundaries share exactly one mesh vertex and form a wedge of two spheres. Their abstract surface has Betti numbers `(1, 0, 2)`, while the summary reports two edge-connected components and flags the bowtie. The existing cleanliness check rejects that case.

As a check of the tests, replace `if (depth % 2 == 0)` with `if (depth >= 0)` in `SolidBetti`. This deliberately classifies all shells as outer shells. The cavity, nested-part and all three fixture assertions fail, for example `a - inner: solid Betti numbers (2, 0, 0), expected (1, 0, 1)`; the disjoint-boxes and tunnel cases still pass.

## Recommendation

- **Nested Boolean differences and shell resolution.** The cavity branches of `ExactMeshBooleanTests.DisjointAndNested` and `NestedShells_SameOrientation_AndInverted` assert overall volume and triangle count. The box replacement demonstrates why those assertions can miss a change from a cavity to separate parts. A test-local solid-Betti assertion would check the expected cavity even in fixtures that also check surface topology.
- **Existing centred-cube subtraction.** Keep its signed per-shell volume and fill-back assertions. The signed volumes alone reject all three replacements here. The study gives no reason to add the nesting check to that test.
- **Existing clean repair pass-through.** Keep the vertex-set, volume and pass-through assertions. The disjoint replacement changes the vertex positions and is already rejected. A forced-repair cavity fixture that only checks the result path, cleanliness or total volume gains a specific cavity-preservation assertion. Soup resolution needs an explicit per-fixture expectation because `NestedPartAsSoup_ResolvedAsSolidNotCavity` intentionally expects solid material.
- **Non-manifold repair outputs.** Park a general homology check. The point-contact fixture demonstrates a difference in the meaning of connectivity, but the current bowtie guard already rejects it.

A test helper restricted to known disjoint shells is enough for nested Boolean and forced-repair fixtures. The helper repeats the shell extraction already implicit in the summary and builds one winding tree per shell. It performs up to `s(s-1)` containment queries. The timing above measures reconstruction of those trees on the centred-cube fixture, so it gives a cost for small regression tests. A library-wide checker would need a separate workload and a way to avoid all-pairs queries on assemblies with many parts. This study found a test coverage gap on controlled replacements. It found no defect in the measured production outputs.

## Follow-up on cavity ownership

The global Betti numbers discard which material component encloses each cavity. A second procedural fixture isolates that gap after the Boolean and forced-repair paths. The stock is the union of [0,5]³ and its translation by (10,0,0). Subtract two unit cubes with minimum corners (1,1,1) and (3,1,1). The expected result has both cavities in the left stock. Two counterfactual outputs come from changing the cutter placement: one cavity in each stock, or both in the right stock. The strict Boolean constructs all three outputs. The probe also runs `MeshRobustRepair` with clean pass-through disabled on the expected output.

The probe checks cleanliness, then reports the production topology summary, the existing test-local `SolidBetti` result, total volume and the sorted signed shell volumes. It adds four winding queries at the centers of the candidate cavity locations, in x order (1.5, 3.5, 11.5, 13.5), all with y = z = 1.5. These points stay strictly inside material or a cavity in every arrangement. The exact closed-mesh winding path gives 1 for material and 0 for void on this oriented boundary.

Run `dotnet run -c Release --project benchmarks/TopologyOwnershipProbe`. The probe links the existing `SolidBetti` helper directly. Timings are medians of seven batches of 1,000 calls after 1,000 warm-up calls. They exclude Boolean execution, repair and cleanliness checks. The fresh-tree comparison includes winding-cache construction. The existing-tree comparison uses the warmed cache.

This Release run used Linux x64, .NET SDK 10.0.404 and an Intel Xeon Platinum 8573C.

```text
forced repair path: Repair
case | triangles | surface (shells, genus, euler) | solid Betti | signed volume | signed shell volumes | landmark material
expected Boolean | 48 | (4, 0, 8) | (2, 0, 2) | 248 | [-1, -1, 125, 125] | 0011
forced repair | 48 | (4, 0, 8) | (2, 0, 2) | 248 | [-1, -1, 125, 125] | 0011
one cavity each | 48 | (4, 0, 8) | (2, 0, 2) | 248 | [-1, -1, 125, 125] | 0101
both cavities moved | 48 | (4, 0, 8) | (2, 0, 2) | 248 | [-1, -1, 125, 125] | 1100
SolidBetti: 383.18 us/check (checksum 16000)
build + four winding queries: 41.90 us/check (checksum 16000)
four winding queries on existing tree: 2.45 us/check (checksum 16000)
```

All four outputs are clean and agree on triangle count, surface topology, solid Betti numbers, total volume and signed shell volumes. The unlabelled nesting forest distinguishes one cavity per stock from two cavities under one stock. The forest is isomorphic when both cavities move to the other, identical stock component. The ordered winding results distinguish both replacements, including that relocation. The forced repair preserves the expected pattern. All four examples have two material landmarks, so comparing the number of positive queries would discard the distinction again.

Use the ordered winding assertion in a fixed Boolean difference or forced-repair fixture when the contract says where a cavity must remain. Compose it with the cleanliness and solid Betti checks where those are already relevant. Transform the landmarks together with the test geometry when testing similarities. Four queries on this fixture take about 42 µs including a fresh tree, or 2.5 µs on the already-warm tree. The guarantee is occupancy at the chosen points. Cavity geometry elsewhere, unlabelled inputs, and general cavity correspondence remain outside this check.
