# Mean-curvature and heat-distance oracles on the unit sphere

Hildebrandt, Polthier and Wardetzky, *On the Convergence of Metric and Geometric Properties of Polyhedral Surfaces*, Corollary 4, establishes convergence of the mean-curvature functional in the dual Sobolev norm under totally normal convergence. Example 1 gives a counterexample to general strong convergence for the paper's finite-element representative. The repository uses mixed vertex areas for its nodal representative. The common object is the area gradient integrated against a test field.

## Weak mean-curvature experiment

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe` on .NET 10. Existing MeshCurvature tests check scalar curvature on an icosphere. Here a closed unit cube-sphere is refined by doubling the subdivisions per cube edge. At each vertex x, sample the smooth field F = (x₁³, x₂³, x₃³). Sum MixedArea times MeanCurvatureNormal dotted with F. This pairs the discrete area gradient with the piecewise linear interpolant of F. Using the library's outward sign, the smooth twice-mean-curvature vector is 2x. Each coordinate's fourth moment integrated over the unit sphere is 4π/5, so the reference integral is 24π/5. The second measurement compares nodal vectors directly with 2x.

| Vertices per cube edge | Maximum mesh edge h | Absolute pairing error | Maximum nodal vector error |
|---:|---:|---:|---:|
| 5 | 0.549179 | 0.464867 | 0.932352 |
| 9 | 0.289015 | 0.114240 | 1.059330 |
| 17 | 0.148579 | 0.028481 | 1.123446 |
| 33 | 0.075386 | 0.007116 | 1.158989 |
| 65 | 0.037979 | 0.001779 | 1.178591 |

All vertices were valid. The last refinement gives an empirical order of 2.02 from log(error ratio) / log(h ratio). This is a single smooth test field, not a measurement of the full dual norm. Corollary 4 does not prescribe this empirical order. The maximum nodal error increased at every level. This observation neither proves asymptotic divergence nor violates the theorem.

### Mean-curvature decision

Build a small regression oracle using the 9, 17 and 33 resolutions. Require all vertices valid, a positive outward pairing, and at least a threefold error reduction on each of these two steps. That threshold gives margin around the measured fourfold reduction and is specific to this field and mesh generator. Cost is linear in the triangle count per mesh. This oracle checks the integrated area gradient: a compensating change to the mixed area and its reciprocal in the nodal vector would cancel, so it cannot validate either factor separately.

No defect issue is warranted by the mean-curvature measurement.

## Heat-distance experiment

The same command also runs the default `MeshGeodesicDistance` on each cube-sphere with a single source at the north pole. It checks that the source distance is exactly zero and every reported distance is finite and nonnegative. For a source direction s and vertex direction x, the smooth reference distance is the great-circle angle `atan2(|s × x|, s · x)`. This form also applies at the source and antipode. The probe reports the vertexwise RMS and maximum absolute error, the mean input edge length and the number of intrinsic flips.

[Crane, Weischedel and Wardetzky (2013), §4.2 and Fig. 19](https://arxiv.org/abs/1204.6216), report numerical evidence for linear convergence of the heat distance with respect to mean edge length. The finite-mesh error against the smooth sphere includes both the change in surface geometry and the error of the heat method on that geometry.

| Vertices per cube edge | Mean edge length | RMS error (rad) | Maximum error (rad) | Intrinsic flips |
|---:|---:|---:|---:|---:|
| 5 | 0.40878923 | 0.03856735 | 0.12152804 | 48 |
| 9 | 0.20716988 | 0.02157331 | 0.07252190 | 192 |
| 17 | 0.10393986 | 0.01753345 | 0.04993832 | 768 |
| 33 | 0.05201454 | 0.01729950 | 0.03930911 | 3072 |
| 65 | 0.02601286 | 0.01787747 | 0.03447552 | 12288 |

The empirical maximum-error orders for successive refinements are 0.76, 0.54, 0.35 and 0.19. The maximum error falls at each step, while the RMS error increases slightly on the finest mesh. The default heat-distance implementation on this family does not reproduce the paper's empirical first-order trend. The five levels cannot establish an asymptotic rate or attribute the remaining error to a specific part of the implementation.

### Heat-distance decision

Keep the probe as a baseline and defer a rate assertion for PR-tier tests. The monotone maximum error over these levels gives only a family-specific observation, and the finest RMS values already rule out a monotone RMS assertion on these inputs. The analytic reference and error reduction each cost one traversal of the vertices after the heat solve. Principal curvature, the cotangent Laplacian in isolation and vector heat remain open.
