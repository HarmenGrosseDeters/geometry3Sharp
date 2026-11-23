# Solid Betti numbers as Boolean and repair test oracles

Tier M study for [PR #1424](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1424). The question is whether shell nesting adds a useful assertion to the existing Boolean and repair tests.

## What the paper contributes

Delfinado and Edelsbrunner count components, independent tunnels and voids of an embedded finite simplicial complex. Their incremental algorithm classifies simplex insertions by whether they create a cycle or kill one. The three-dimensional construction uses connectivity on the complex and on the complement, with a reverse traversal for the latter. The near-linear bound is in the size of the supplied complex. The Boolean and repair APIs return a boundary mesh. For disjoint boundary shells, the information needed for the solid Betti numbers can be recovered without building tetrahedra. [Delfinado and Edelsbrunner, 1995](https://pub.ista.ac.at/~edels/Papers/1995-03-IncrementalBettiNumbers.pdf)

Hatcher's Alexander duality (Corollary 3.45) identifies the second Betti number of the solid with the number of bounded complement components. Zhou et al. track winding labels on arrangement cells, including the unbounded cell. Here only the parity of nesting in the returned mesh is needed. `MeshTopologySummary` already supplies the sum of shell genera, `DMeshAABBTree3.WindingNumber` has an exact ray-crossing path for closed meshes, and the repair cleanliness check already uses geometric nesting to detect inverted components. `ContouringTopologyCertificate` concerns the topology of a contour relative to its field. [Hatcher](https://pi.math.cornell.edu/~hatcher/AT/AT.pdf) · [Zhou et al.](https://www.cs.columbia.edu/cg/mesh-arrangements/) · [Contouring design](../design/CONTOURING_TOPOLOGY.md)

## Derivation and preconditions

For the boundary of a bounded solid with pairwise disjoint, closed, embedded orientable shells, let `s` be the number of shells and `g` their total genus. The surface Betti numbers are `(s, 2g, s)`. Determine the depth of each shell by counting the other shells that contain one of its vertices. The vertex lies off every other shell, and the entire connected shell lies on the same side of each other shell. The unbounded region is empty. Crossing a shell alternates between material and void, so each even-depth shell bounds a new material component and each odd-depth shell bounds a new cavity. The material-component count is `b₀`. Alexander duality gives the cavity count as `b₂`. Doubling the solid along its boundary gives a closed orientable 3-manifold with Euler characteristic zero, so `χ(solid) = χ(boundary)/2 = s − g`. Since `b₀ + b₂ = s` and `b₃ = 0`, the solid's `b₁ = g`.

The guarantee is about the Betti numbers of that solid. The three counts leave cavity ownership, geometry, and winding orientation undetermined. For example, moving a cavity from one disconnected material part to another can preserve all three counts. Keep cleanliness and signed-orientation assertions alongside a nesting check. Touching or crossing shells and bowtie vertices fall outside the derivation.

## Probe

The [C# probe](../../benchmarks/TopologyCertificateProbe/Program.cs) calls `ExactMeshBoolean` with safe rounding for the enclosed subtraction and the through-tunnel cut. It sends the enclosed result through `MeshRobustRepair` with clean pass-through enabled and disabled. The expected Betti numbers follow from the procedural constructions. The probe verifies the returned meshes with the library's validity and cleanliness checks and checks the small fixtures for pairwise shell intersections before measuring nesting. It then compares three clean replacements for the expected cavity. The two disjoint boxes have volumes 24 and 2, so the replacement matches the cavity's total signed volume of 26.

Run from the repository root with the .NET 10 SDK.

```sh
LC_ALL=C dotnet run --project benchmarks/TopologyCertificateProbe -c Release
```

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

As a check on the probe, replace `if (depth % 2 == 0)` with `if (depth >= 0)` and rerun. This deliberately classifies all shells as outer shells. The probe fails on the first Boolean output with `expected solid Betti (1, 0, 1), got (2, 0, 0)`.

## Recommendation

- **Nested Boolean differences and shell resolution.** The cavity branches of `ExactMeshBooleanTests.DisjointAndNested` and `NestedShells_SameOrientation_AndInverted` assert overall volume and triangle count. The box replacement demonstrates why those assertions can miss a change from a cavity to separate parts. A test-local solid-Betti assertion would check the expected cavity even in fixtures that also check surface topology.
- **Existing centred-cube subtraction.** Keep its signed per-shell volume and fill-back assertions. The signed volumes alone reject all three replacements here. The study gives no reason to add the nesting check to that test.
- **Existing clean repair pass-through.** Keep the vertex-set, volume and pass-through assertions. The disjoint replacement changes the vertex positions and is already rejected. A forced-repair cavity fixture that only checks the result path, cleanliness or total volume gains a specific cavity-preservation assertion. Soup resolution needs an explicit per-fixture expectation because `NestedPartAsSoup_ResolvedAsSolidNotCavity` intentionally expects solid material.
- **Non-manifold repair outputs.** Park a general homology check. The point-contact fixture demonstrates a difference in the meaning of connectivity, but the current bowtie guard already rejects it.

A test helper restricted to known disjoint shells is enough for nested Boolean and forced-repair fixtures. The probe repeats the shell extraction already implicit in the summary and builds one winding tree per shell. It performs up to `s(s-1)` containment queries. The timing above measures reconstruction of those trees on the centred-cube fixture, so it gives a cost for small regression tests. A library-wide checker would need a separate workload and a way to avoid all-pairs queries on assemblies with many parts. This study found a test coverage gap on controlled replacements. It found no defect in the measured production outputs.
