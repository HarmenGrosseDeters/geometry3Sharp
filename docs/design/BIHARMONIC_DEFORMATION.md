# Biharmonic displacement deformation (#886)

`BiharmonicMeshDeformer` moves a few handle vertices to given targets and lets the rest of the mesh follow with the
smoothest displacement field, in the sense of the squared cotangent Laplacian. It is a displacement solve with point
constraints. It is **not** the bounded biharmonic *weights* of the paper (no bounds, skinning weights, affine handle
frames or volumetric domains) and it is not ARAP (#896).

## Method

For each connected component, with displacement `u` per vertex and `u = target - rest` at each handle, minimize
`1/2 u^T L^T M^-1 L u` independently for x, y and z.

- `L` is the symmetric cotangent stiffness matrix of the rest triangles.
- `M` is diagonal, one third of each incident triangle's area per vertex (barycentric lumping). It stays positive on
  obtuse triangles, where the Voronoi mass of Meyer et al. does not, which is why it was chosen.
- With `Q = L^T M^-1 L`, the free rows give `Q_ff u_f = -Q_fc u_c`. `Q` is assembled as outer products of single rows of
  `L`, factored once by `SparseCholesky` (fill-reducing ordering inside) and solved for three right-hand sides.

Sources are in [SOURCES.md](../SOURCES.md): Jacobson et al. 2011 sections 3.1 and 3.4 for the energy, Pinkall and Polthier
for the cotangent matrix, Meyer et al. for the lumped mass, Botsch and Sorkine for the background. No libigl source was read.

## Behaviour

- The input mesh is not modified. The result is a copy with the handles imposed exactly (bit for bit); vertex normals
  are recomputed when present.
- Every connected component needs at least one handle (isolated vertices count), otherwise `UnanchoredComponent`.
- Each component is normalized (rest radius, and the largest handle displacement) before assembly, so the result does
  not depend on the unit or on tiny motions of a huge mesh. Scaling by a power of two gives the same result bit for bit
  (`Similarity.All()` test).
- A component where every vertex is a handle skips the solve (`Iterations = 0`, `Residual = NaN`), after an exact
  check that no triangle is degenerate.
- Translation of all handles by the same vector moves every vertex by it (to rounding). A rigid motion given on **all**
  vertices is reproduced exactly. A rigid rotation given on only a *subset* is not extended: the linear energy is not
  rotation-invariant, so the free vertices are not rotated rigidly with it. Use ARAP for that.
- `Status`: `Success`; `InvalidInput` (null or empty mesh, null handles, unknown handle id, non-finite coordinates or
  settings); `UnanchoredComponent`; `DegenerateTriangle` (exact zero-area test); `NumericalRange` (overflow or underflow
  of a weight, a product that underflows to zero, a handle difference lost to rounding, a non-finite intermediate);
  `LinearSolveFailed` (the factorization fails or the residual exceeds `RelativeTolerance`); `NonFiniteResult`;
  `ResourceLimit` (see below). `Mesh` is null unless `Success`.
- `Residual` is the largest free-vertex gradient of the energy divided by an absolute bound of its contributions. It is a
  backward-error check against `RelativeTolerance` (default 1e-8), **not** a bound on the position error: for an
  ill-conditioned system (a very fine mesh with few handles) the positions can be less accurate than the residual suggests.
- Terms below half an ulp of a matrix entry are rounded away like in any floating-point assembly. This matters for
  rotated meshes, where the cotangent of a right angle is rounding noise of about 1e-17 added to entries of order one.
  Genuine loss is still reported: a nonzero product that underflows to zero, an overflow, or `target - rest` losing an operand.
- Limits: `MaxMatrixContributions` (default 10^7 uncompressed outer-product entries, computed from the topology before any
  weight) bounds the assembly, not the fill of the Cholesky factor or the total memory. The solver is serial, one
  `Compute` at a time per instance. Cost is dominated by the factorization: about 0.26 s for 10k and 4.5 s for 100k
  vertices (benchmark below), and the factor is not reused between calls.

## Tests

`tests/geometry3Sharp.Tests/BiharmonicMeshDeformerTests.cs`, procedural meshes only: the independent unit-square case
(a split unit square with `z = 1` at vertex 1 and `0` at vertices 0 and 3 gives `3/4` at vertex 2; harmonic
interpolation would give a different value, so it distinguishes the operator), the `Similarity.All()` invariance on a patch and a
grid (power-of-two scales bit for bit), reuse on one instance, translation, all-vertex rigid motion, invalid, disconnected
and degenerate inputs, the extreme-range cases and the assembly limit. Hand mutations that fail them: inverse masses
replaced by 1, a flipped constraint sign, handles not imposed, the underflow guard removed.

## Benchmark

[`benchmarks/BiharmonicDeformationBenchmark`](../../benchmarks/BiharmonicDeformationBenchmark/README.md): a grid with
the left column fixed and the right column moved; 10k vertices about 0.26 s, 100k about 4.5 s warm.

## Parked: certified accuracy

An opt-in mode that returned a *proved* bound on the position error (interval arithmetic: a dense interval solver
up to 16 vertices, and a sparse spectral certificate with geometric path bounds up to 100k vertices) was developed in this PR
and then parked by the owner decision of 2026-11-16 (ship the core, certify later). It is preserved unchanged,
with its design notes (`BIHARMONIC_IMPLEMENTATION_PLAN.md`, `BIHARMONIC_CERTIFICATE_PROOF.md`), probes and results, on the
branch [`claude/biharmonic-certificate-parked`](https://github.com/HarmenGrosseDeters/geometry3Sharp/tree/claude/biharmonic-certificate-parked).
Its open gates are #980 (the aligned 100k grid does not certify, cold runs take 25 to 60 s) and #976 (a 20 s tail on
one 10k certificate). It is not part of the library and was never in a release, so adding it later is an addition to the API.
