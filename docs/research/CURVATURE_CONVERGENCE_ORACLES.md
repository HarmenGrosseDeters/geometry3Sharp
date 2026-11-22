# A weak mean-curvature oracle on the unit sphere

Hildebrandt, Polthier and Wardetzky, *On the Convergence of Metric and Geometric Properties of Polyhedral Surfaces*, Corollary 4, establishes convergence of the mean-curvature functional in the dual Sobolev norm under totally normal convergence. Example 1 gives a counterexample to general strong convergence for the paper's finite-element representative. The repository uses mixed vertex areas for its nodal representative. The common object is the area gradient integrated against a test field.

## Experiment

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe` on .NET 10. Existing MeshCurvature tests check scalar curvature on an icosphere. Here a closed unit cube-sphere is refined by doubling the subdivisions per cube edge. At each vertex x, sample the smooth field F = (x₁³, x₂³, x₃³). Sum MixedArea times MeanCurvatureNormal dotted with F. This pairs the discrete area gradient with the piecewise linear interpolant of F. Using the library's outward sign, the smooth twice-mean-curvature vector is 2x. Each coordinate's fourth moment integrated over the unit sphere is 4π/5, so the reference integral is 24π/5. The second measurement compares nodal vectors directly with 2x.

| Vertices per cube edge | Maximum mesh edge h | Absolute pairing error | Maximum nodal vector error |
|---:|---:|---:|---:|
| 5 | 0.549179 | 0.464867 | 0.932352 |
| 9 | 0.289015 | 0.114240 | 1.059330 |
| 17 | 0.148579 | 0.028481 | 1.123446 |
| 33 | 0.075386 | 0.007116 | 1.158989 |
| 65 | 0.037979 | 0.001779 | 1.178591 |

All vertices were valid. The last refinement gives an empirical order of 2.02 from log(error ratio) / log(h ratio). This is a single smooth test field, not a measurement of the full dual norm. Corollary 4 does not prescribe this empirical order. The maximum nodal error increased at every level. This observation neither proves asymptotic divergence nor violates the theorem.

## Decision

Build a small regression oracle using the 9, 17 and 33 resolutions. Require all vertices valid, a positive outward pairing, and at least a threefold error reduction on each of these two steps. That threshold gives margin around the measured fourfold reduction and is specific to this field and mesh generator. Cost is linear in the triangle count per mesh. This oracle checks the integrated area gradient: a compensating change to the mixed area and its reciprocal in the nodal vector would cancel, so it cannot validate either factor separately.

No defect issue is warranted by this measurement. The result covers MeshCurvature on one sphere family. Rates for the heat method, vector transport, principal curvatures and other surface families require their own operator-specific hypotheses and remain open in the broader plan.
