# Curvature and heat-distance convergence oracles

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

Keep the probe as a baseline and defer a rate assertion for PR-tier tests. The monotone maximum error over these levels gives only a family-specific observation, and the finest RMS values already rule out a monotone RMS assertion on these inputs. The analytic reference and error reduction each cost one traversal of the vertices after the heat solve. The cotangent Laplacian in isolation and vector heat remain open.

## Principal curvature on torus refinements

Rusinkiewicz (2004), §4 and Figure 8, uses an analytic torus to measure curvature estimation error under different tessellations and noise levels. Here the vertices lie on a smooth ring torus and the grid is refined. The paper reports direction-integrated normal-curvature error. This experiment measures the two principal values and the azimuthal principal direction separately to see which part of the estimate changes with refinement. [Paper](https://gfx.cs.princeton.edu/pubs/Rusinkiewicz_2004_ECA/curvpaper.pdf)

The probe uses a torus with major radius 1 and tube radius 0.35. At each vertex with distance ρ from the z-axis, the exact outward principal curvatures are `k1 = 1/0.35` around the tube and `k2 = (ρ - 1) / (0.35ρ)` along the azimuth. The azimuthal direction is `(-y, x, 0) / ρ`. The curvature gap is strictly positive on this torus, so the principal lines are well defined everywhere. The direction metric is the maximum acute angle to the azimuthal line, with both signs of the estimated vector treated equally.

The existing test samples a single 64 × 32 fixed-diagonal grid. The probe doubles both grid dimensions from 16 × 8 through 256 × 128 and runs the default `MeshPrincipalCurvature.Compute` at each level. A second triangulation uses the same vertices and alternates the quad diagonal in a checkerboard pattern. This controls for connectivity while keeping the sampled smooth surface and the reference values identical. Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe`.

| Diagonals | Grid | Mean edge length | k1 RMS | k1 max | k2 RMS | k2 max | Azimuth max (°) | Normal max (°) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| fixed | 16 × 8 | 0.377556 | 0.0912552 | 0.116358 | 0.166894 | 0.238346 | 1.20077 | 2.73607 |
| fixed | 32 × 16 | 0.190975 | 0.0242966 | 0.0313478 | 0.0427457 | 0.0604451 | 0.297693 | 0.698538 |
| fixed | 64 × 32 | 0.0957652 | 0.00615781 | 0.00795702 | 0.0106969 | 0.0150356 | 0.074211 | 0.175498 |
| fixed | 128 × 64 | 0.0479175 | 0.00154289 | 0.00199324 | 0.002668 | 0.00373954 | 0.0185386 | 0.0439276 |
| fixed | 256 × 128 | 0.0239631 | 0.000385702 | 0.000498123 | 0.00066576 | 0.000931894 | 0.00463376 | 0.0109852 |
| checkerboard | 16 × 8 | 0.377556 | 0.0922953 | 0.134095 | 0.179057 | 0.363096 | 4.45278e-14 | 2.73607 |
| checkerboard | 32 × 16 | 0.190975 | 0.0247452 | 0.0364747 | 0.0467422 | 0.091871 | 5.2935e-14 | 0.698538 |
| checkerboard | 64 × 32 | 0.0957652 | 0.0062955 | 0.0092377 | 0.0117768 | 0.0228936 | 1.36106e-13 | 0.175498 |
| checkerboard | 128 × 64 | 0.0479175 | 0.00158028 | 0.00231229 | 0.0029457 | 0.00571199 | 6.09062e-13 | 0.0439276 |
| checkerboard | 256 × 128 | 0.0239631 | 0.000395407 | 0.000578059 | 0.000736003 | 0.00142654 | 3.09435e-12 | 0.0109852 |

Both triangulations give empirical order 2.00 to 2.01 on the final refinement for the RMS and maximum errors of both principal values. The checkerboard has about 1.53 times the fixed layout's maximum k2 error on the finest mesh. Its azimuthal direction error stays below 3.1 × 10⁻¹² degrees at all levels. Reflection in the meridional plane through a vertex preserves the checkerboard stencil and reverses its azimuthal vector. In exact arithmetic, the fitted tensor at that vertex therefore has zero coupling between the meridional and azimuthal directions. The estimated normals can still tilt within the meridional plane. Both layouts show the same maximum normal-angle error to the reported precision, and the fixed layout's maximum principal direction error falls from 1.20° to 0.00463°.

### Principal-curvature decision

Propose a PR-tier regression using the fixed layout at 16 × 8, 32 × 16 and 64 × 32. Check validity, finite values and a threefold reduction per step in both maximum principal curvature errors. Check the absolute azimuthal angle on the finest test mesh against 0.1° to exercise direction estimation with a stencil that permits azimuthal error. The input has 2,688 vertices across the three grids. The experiment supports these thresholds for the specified torus family and gives no evidence of a defect. The study covers smooth samples on two structured stencils. A convergence guarantee for arbitrary triangulations or noisy samples remains outside this result.
