# Curvature and heat-method convergence oracles

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

### Where the maximum nodal error sits (re-checked 2026-11-25)

A scratch check, not committed, shows three things.
- **The operator matches its formula.** It recomputed the vector at every vertex from the mixed-area cotangent formula of Meyer et al.
  (their eq. 8 with the mixed area), independently of `MeshCurvature`. The library matches it to 9e-14 at every level up to 129 vertices per
  cube edge.
- **The growing maximum sits on the cube seams.** It is always at a valence-4 vertex on a seam of the normalized cube, where two grid
  families of neighbouring faces meet. The eight valence-3 corners have zero error by symmetry.

| Vertices per cube edge | RMS nodal error | Maximum on seams | Maximum on face interiors |
|---:|---:|---:|---:|
| 5 | 0.21489 | 0.93235 | 0.061415 |
| 17 | 0.063021 | 1.1234 | 0.014802 |
| 65 | 0.016505 | 1.1786 | 0.0034927 |
| 129 | 0.0083278 | 1.1890 | 0.0018365 |

- **The midpoint icosphere (`IcosphereFixture`) converges everywhere.** Its maximum nodal error halves at each subdivision: 0.0334, 0.0167,
  0.00835, 0.00417 and 0.00209 for levels 2 to 6.

So the seam error levels off at about 1.19 (60 % of |2x|): an O(1) consistency error of the pointwise cotangent operator on a stencil without
the needed symmetry. It is not a defect of the implementation, and not an artefact of the probe's normalisation (the RMS and the face-interior
maximum fall as O(h)). This is the known behaviour: pointwise convergence of the cotangent mean-curvature normal needs regular (eg valence-6,
symmetric) neighbourhoods (G. Xu, *Discrete Laplace-Beltrami operators and their convergence*, CAGD 21, 2004; Wardetzky, *Convergence of the
cotangent formula: an overview*, 2008). In general it holds only in the weak sense of Hildebrandt, Polthier and Wardetzky's Corollary 4. A
pointwise oracle for this operator must use a mesh family with regular stencils, or exclude the seams.

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

Keep the probe as a baseline and defer a rate assertion for PR-tier tests. The monotone maximum error over these levels gives only a family-specific observation, and the finest RMS values already rule out a monotone RMS assertion on these inputs. The analytic reference and error reduction each cost one traversal of the vertices after the heat solve.

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

## Scalar cotangent Laplacian

Wardetzky formulates the cotangent stiffness matrix as a discrete Dirichlet pairing and discusses convergence of Laplace–Beltrami operators in a weak formulation. Theorem 3.4 concerns polyhedral surfaces represented as normal graphs and their lifted operators. The two measurements here are the stiffness energy of one interpolated smooth function and the maximum error of a barycentric-mass pointwise estimate. [Paper](https://ddg.math.uni-goettingen.de/pub/convergence_cotan.pdf)

The C# probe uses `IntrinsicTriangulation.CotanLaplacian()` and `VertexAreas()` on input connectivity with zero mollification. The smoother and deformer named in the plan use graph weights, so they would exercise a different operator. For vertex samples `f`, the probe compares `fᵀ L f` with the exact integral of `|∇f|²`. It also compares `(Lf)[i] / area[i]` with the positive-sign smooth Laplacian at interior vertices. All sphere vertices enter the pointwise comparison. Boundary rows on the square are included in the energy pairing and omitted from the pointwise comparison.

- On the unit square, `f = x²` has exact energy 4/3 and positive-sign Laplacian −2. The stiffness row for each interior vertex has cardinal weights 1 and diagonal weights 0 in both meshes. At grid spacing h it gives `Lf = −2h²`. The fixed-diagonal mesh has six right triangles per interior vertex and barycentric area h². The checkerboard has four or eight triangles per vertex, producing areas 2h²/3 and 4h²/3. Its pointwise estimates are therefore −3 and −1.5 at every refinement. In both layouts the linear interpolant is constant in y and has derivative `(2i + 1)h` on strip i. Its energy is lower than 4/3 by exactly h²/3.
- On the unit sphere, `f = z²` has positive-sign Laplacian `6z² − 2`. Its energy is `4 ∫ (z² − z⁴) dA = 32π/15`.

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe`. The resolution is the number of subdivisions per side of the square or per cube edge.

| Mesh | Subdivisions | Absolute energy error | Maximum pointwise error |
|---|---:|---:|---:|
| square-fixed | 8 | 0.0052083333 | 5.7287508e-14 |
| square-checkerboard | 8 | 0.0052083333 | 1 |
| cube-sphere | 8 | 0.11792834 | 0.48268416 |
| square-fixed | 16 | 0.0013020833 | 2.2781776e-13 |
| square-checkerboard | 16 | 0.0013020833 | 1 |
| cube-sphere | 16 | 0.02966036 | 0.57541133 |
| square-fixed | 32 | 0.00032552083 | 9.0993879e-13 |
| square-checkerboard | 32 | 0.00032552083 | 1 |
| cube-sphere | 32 | 0.0074271653 | 0.62233803 |
| square-fixed | 64 | 8.1380209e-05 | 3.6384229e-12 |
| square-checkerboard | 64 | 8.1380208e-05 | 1 |
| cube-sphere | 64 | 0.0018575625 | 0.64506165 |

The square energy errors match the exact h²/3 formula to rounding precision. The fixed layout reproduces the quadratic pointwise Laplacian to rounding precision. The checkerboard error stays at 1 even though every triangle remains right isosceles and its energy converges at second order. Changing the diagonal pattern leaves this stiffness action unchanged and changes the area used to normalize it.

The cube-sphere energy error falls from 0.11793 to 0.001858, with a final empirical order of 2.00 relative to the inverse subdivision count. Its maximum pointwise error increases from 0.483 to 0.645. At every measured level the maximum occurs at the same projected cube corner, `(1, −1, −1) / √3`, where the exact Laplacian is zero. This is a measurement for one scalar field and mesh family. The planar calculation gives an exact counterexample to a general pointwise convergence requirement for this mass normalization.

### Cotangent Laplacian decision

Propose a PR-tier test of the stiffness energy using the two squares at 8, 16 and 32 subdivisions. Compare the measured energy errors against h²/3. For a curved check, use the 8, 16 and 32 cube-spheres and require at least a threefold energy-error decrease per step. Matrix assembly and the energy evaluation are linear in mesh size on these fixtures. The checkerboard explains why a general pointwise rate assertion would reject a mathematically expected result. This study provides no evidence of a defect in the assembled stiffness matrix.

## Vector heat parallel transport

Sharp, Soliman and Crane report approximately first-order convergence of angular error for vector heat transport on their refinement experiment. They also report larger discrepancies near the cut locus. This probe measures the library's default intrinsic Delaunay path on the same cube-sphere family as the scalar heat experiment. The existing great-circle transport test checks a single sphere resolution. As a control, the probe repeats each solve with `IntrinsicDelaunay = false` on the same vertices and source. The input-connectivity path still clamps negative cotangent weights in the heat operators. [Paper, §7.3 and Fig. 13](https://arxiv.org/abs/1805.09170)

The source is the north pole s, and the input is the unit x-axis vector u, converted to the source signpost coordinates. At a unit destination p, transport along the unique short great-circle arc rotates the source tangent into the destination tangent. The analytic result is `u − (u · p)(s + p)/(1 + s · p)`. This follows by holding the component perpendicular to the great-circle plane fixed and rotating the component within that plane by the colatitude. The formula also returns u at the source.

Convert the estimated vector to world coordinates with `ToWorld` and project it onto the smooth tangent plane at p. Measure the unsigned angle between the projected estimate and the analytic vector. The angle distinguishes opposite vectors. Report unweighted vertex RMS and maximum angles for colatitudes at most 2.4 radians and for the band from 2.4 to 3.0 radians, exclusive at the lower end. The fixed regions separate the main sample from a band closer to the cut locus and exclude the antipode, where the transport is not unique. The probe also reports the largest departure from unit length in the signpost coordinates and the ratio of the actual diffusion time to the solver's mean edge length squared. Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe`.

| Connectivity | Vertices per cube edge | Mean input edge length | Cap RMS (°) | Cap max (°) | Far RMS (°) | Far max (°) |
|---|---:|---:|---:|---:|---:|---:|
| delaunay | 5 | 0.408789 | 0.180441 | 0.490797 | 0.0414808 | 0.0655371 |
| input | 5 | 0.408789 | 1.65144 | 6.29108 | 0.607527 | 0.890169 |
| delaunay | 9 | 0.20717 | 0.273496 | 0.843217 | 0.773448 | 1.29488 |
| input | 9 | 0.20717 | 1.14052 | 4.99062 | 1.84305 | 3.26294 |
| delaunay | 17 | 0.10394 | 0.672729 | 2.14894 | 2.45463 | 4.14182 |
| input | 17 | 0.10394 | 0.851401 | 3.94823 | 1.49785 | 2.78197 |
| delaunay | 33 | 0.0520145 | 1.02673 | 3.92394 | 4.75966 | 9.53327 |
| input | 33 | 0.0520145 | 0.780319 | 3.44977 | 1.26005 | 2.61623 |
| delaunay | 65 | 0.0260129 | 1.23684 | 5.30356 | 7.43347 | 19.1399 |
| input | 65 | 0.0260129 | 0.791387 | 4.05571 | 1.50278 | 3.51947 |

The default path gives increasing angular error on every measured refinement in both regions. The cap RMS rises from 0.180° to 1.237°, and the far maximum rises from 0.0655° to 19.14°. The sampled cap grows from 89 to 21,341 vertices and the far band from 8 to 3,100. On input connectivity, the cap RMS initially decreases from 1.651° to 0.780° before rising slightly to 0.791°. The input connectivity also has lower far-band error on the finest mesh. The length error stays below 3.34 × 10⁻¹⁶ in both modes. The diffusion ratio is 1 for all ten solves, so the automatic increase in diffusion time was inactive.

### Vector heat decision

Keep both configurations in the benchmark and defer a first-order rate assertion. The default path misses the empirical trend from the paper on this cube-sphere family. The control shows that the trend depends on the triangulation path, which changes the stencil, weights, and connection coefficients together. The contribution of each change and the asymptotic rates remain open. The existing planar and single-resolution sphere tests still pass.

## Diffusion-time control for scalar and vector heat

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-time`. On the unit cube-spheres with 9, 17, 33 and 65 vertices per edge, the probe keeps the mesh, source, intrinsic Delaunay settings and analytic references fixed. It varies only the public `TimeFactor` on both operators. Using the mean input edge length h, the three schedules are the default t = h², t = 4h², and t = 0.5h. The linear schedule is expressed in unit-sphere coordinates. It sends t to zero more slowly while increasing the diffusion length in edge units, √t/h. The reported vector time ratios matched the requested factors to the printed precision, so the automatic increase in diffusion time was inactive.

Scalar RMS and maximum errors are over all vertices and use great-circle distance in radians. Vector RMS errors use the same cap and far band as the transport experiment. The measured default rows match the earlier probes.

| Schedule | Vertices per cube edge | TimeFactor | Vector cap RMS (°) | Vector far RMS (°) | Scalar RMS | Scalar max |
|---|---:|---:|---:|---:|---:|---:|
| default | 9 | 1 | 0.273496 | 0.773448 | 0.0215733 | 0.0725219 |
| fourfold | 9 | 4 | 0.0520047 | 0.0864041 | 0.0107102 | 0.0511612 |
| linear | 9 | 2.41348 | 0.0530798 | 0.052518 | 0.012892 | 0.0558015 |
| default | 17 | 1 | 0.672729 | 2.45463 | 0.0175334 | 0.0499383 |
| fourfold | 17 | 4 | 0.0701504 | 0.226369 | 0.00562192 | 0.0279809 |
| linear | 17 | 4.81047 | 0.046719 | 0.151661 | 0.00502529 | 0.026728 |
| default | 33 | 1 | 1.02673 | 4.75966 | 0.0172995 | 0.0393091 |
| fourfold | 33 | 4 | 0.191823 | 0.670994 | 0.00448337 | 0.0175919 |
| linear | 33 | 9.6127 | 0.0485887 | 0.152675 | 0.00222784 | 0.0131012 |
| default | 65 | 1 | 1.23684 | 7.43347 | 0.0178775 | 0.0344755 |
| fourfold | 65 | 4 | 0.291222 | 1.35921 | 0.00438556 | 0.0127203 |
| linear | 65 | 19.2213 | 0.0376905 | 0.128427 | 0.00107573 | 0.00651013 |

A fixed fourfold increase reduces both kinds of error on the fine grids, while vector RMS still increases under refinement. With t = 0.5h, scalar RMS falls from 0.0129 to 0.00108 and roughly halves on each of the last two steps. On the finest mesh, vector cap RMS is 0.0377° and far RMS is 0.128°, compared with 1.237° and 7.433° at the default. Vector cap RMS remains nonmonotone with the linear schedule, and far RMS initially increases. These are finite-resolution measurements on one mesh family and one source.

The within-mesh control shows that the default errors are sensitive to the diffusion schedule even with intrinsic connectivity fixed. It leaves open the contribution of stencil anisotropy, the connection discretization and the short-time solver. Keep the alternate schedule as a benchmark control and defer a default change or vector-rate assertion. A scale-invariant policy would require a geometric length scale besides h and validation on other mesh families and near cut loci.

## Icosahedral sphere control

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-icosphere`. The probe repeats the three diffusion schedules on icosahedral sphere refinements with 320, 1,280, 5,120 and 20,480 triangles. Each step divides every triangle into four, projecting edge midpoints onto the unit sphere, as in the `MeshCurvatureTests` fixture. The source is the same original icosahedron vertex at all levels. The initial vector is the normalized projection of the x-axis into its smooth tangent plane. The great-circle references and angular regions are defined relative to this source. Compared with the cube-sphere, this control changes the triangulation family and the source neighborhood.

The reported vector diffusion ratio equalled the requested factor at every level. The table uses the same metrics and units as the cube-sphere time sweep.

| Schedule | Subdivisions | Mean edge length | Vector cap RMS (°) | Vector far RMS (°) | Scalar RMS | Scalar max |
|---|---:|---:|---:|---:|---:|---:|
| default | 2 | 0.299332 | 0.1545 | 0.0151453 | 0.0455219 | 0.0965698 |
| fourfold | 2 | 0.299332 | 0.0773911 | 0.0151453 | 0.0445002 | 0.0928904 |
| linear | 2 | 0.299332 | 0.117253 | 0.0151453 | 0.0449676 | 0.0946439 |
| default | 3 | 0.15073 | 0.0759457 | 0.1746 | 0.0231983 | 0.0506752 |
| fourfold | 3 | 0.15073 | 0.0349435 | 0.0237292 | 0.0212584 | 0.045568 |
| linear | 3 | 0.15073 | 0.0386942 | 0.0306851 | 0.021386 | 0.0459306 |
| default | 4 | 0.0754991 | 0.0478458 | 0.226306 | 0.0133661 | 0.0292058 |
| fourfold | 4 | 0.0754991 | 0.0169712 | 0.0454842 | 0.0107798 | 0.0233749 |
| linear | 4 | 0.0754991 | 0.012909 | 0.0228159 | 0.0104532 | 0.0225678 |
| default | 5 | 0.0377664 | 0.0592679 | 0.23571 | 0.00885611 | 0.0188706 |
| fourfold | 5 | 0.0377664 | 0.0124484 | 0.0580412 | 0.00585723 | 0.0127068 |
| linear | 5 | 0.0377664 | 0.00445335 | 0.0145343 | 0.00518083 | 0.011183 |

The default scalar RMS decreases at each icosahedral refinement, from 0.0455 to 0.00886 radians. The cube-sphere default approaches 0.018 radians on its last two grids. The icosahedral cap vector RMS mostly decreases at the default time and rises slightly on the last step. The far-band RMS increases from 0.0151° to 0.236°. At the finest icosahedral level, t = 0.5h gives 0.00445° cap RMS, 0.0145° far RMS and 0.00518 radians scalar RMS. The same time schedule improves fine-grid errors on both families.

The scalar plateau is specific to the tested cube-sphere setup. The default far-band vector error growth appears on both families, with different magnitudes. A global default change still needs evidence across other meshes and sources. This comparison leaves the individual contributions of the triangulation, source neighborhood and discrete heat kernel open.

## Changing the source on the same mesh

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-sources`. The source sweep holds each refined mesh, the default intrinsic Delaunay setting and t = h² fixed. On the cube-sphere it compares the north pole (a cube face center) with the projected (1, 1, 1) cube corner. On the icosahedral sphere it compares original vertex 0 with the projected midpoint of edge (0, 11), which is present at every tested subdivision. The same tangent-vector convention and angular bands are evaluated relative to each source. Changing the source changes both its local stencil and the paths to the destinations, so this experiment measures source-location sensitivity.

All 16 solves used the requested vector diffusion time. The table uses the same units and error metrics as the time sweeps. Cube levels are vertices per edge and icosahedral levels are subdivision counts.

| Source | Level | Mean edge length | Vector cap RMS (°) | Vector far RMS (°) | Scalar RMS | Scalar max |
|---|---:|---:|---:|---:|---:|---:|
| cube-pole | 9 | 0.20717 | 0.273496 | 0.773448 | 0.0215733 | 0.0725219 |
| cube-corner | 9 | 0.20717 | 0.499228 | 0.748727 | 0.0340947 | 0.0914859 |
| cube-pole | 17 | 0.10394 | 0.672729 | 2.45463 | 0.0175334 | 0.0499383 |
| cube-corner | 17 | 0.10394 | 0.157999 | 0.799504 | 0.0274582 | 0.0636434 |
| cube-pole | 33 | 0.0520145 | 1.02673 | 4.75966 | 0.0172995 | 0.0393091 |
| cube-corner | 33 | 0.0520145 | 0.350567 | 0.474006 | 0.0249258 | 0.0518426 |
| cube-pole | 65 | 0.0260129 | 1.23684 | 7.43347 | 0.0178775 | 0.0344755 |
| cube-corner | 65 | 0.0260129 | 0.597588 | 1.06813 | 0.0239289 | 0.0464366 |
| ico-corner | 2 | 0.299332 | 0.1545 | 0.0151453 | 0.0455219 | 0.0965698 |
| ico-midpoint | 2 | 0.299332 | 0.324552 | 1.25035 | 0.0392471 | 0.089192 |
| ico-corner | 3 | 0.15073 | 0.0759457 | 0.1746 | 0.0231983 | 0.0506752 |
| ico-midpoint | 3 | 0.15073 | 0.162053 | 0.805936 | 0.0215386 | 0.0499449 |
| ico-corner | 4 | 0.0754991 | 0.0478458 | 0.226306 | 0.0133661 | 0.0292058 |
| ico-midpoint | 4 | 0.0754991 | 0.0976322 | 0.255205 | 0.0146705 | 0.0328499 |
| ico-corner | 5 | 0.0377664 | 0.0592679 | 0.23571 | 0.00885611 | 0.0188706 |
| ico-midpoint | 5 | 0.0377664 | 0.129991 | 0.492421 | 0.0118599 | 0.0250549 |

At the finest cube level, the far-band vector RMS drops from 7.43° at the pole to 1.07° at the corner. The pole error increases on every step, while the corner error is nonmonotone. The corner's scalar RMS is higher at every tested level, yet it decreases across the sweep. On the icosahedral sphere, the far-band RMS increases with refinement from the original vertex. From the midpoint it first decreases from 1.25° to 0.255°, then rebounds to 0.492°.

Source location changes both the magnitude and the refinement trend at the default time. A convergence test for either mesh family should cover multiple sources and measure distance and transport independently. These cases identify source dependence for fixed connectivity. The contributions of the source stencil, the changed distribution of sampled destinations and the discrete propagation paths remain open.

## Tangent-coordinate conversion control

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-frames`. This repeats the four sources and all refinements from the source sweep. At each sampled destination, encode the analytic great-circle transport with `FromWorld`, decode it with `ToWorld`, and project it back onto the smooth tangent plane. The angular error of this round trip measures the change that the tangent-plane and coordinate conversions can produce for the exact reference direction.

A second comparison takes the angular discrepancy between the decoded vector and the explicit projection of the analytic vector onto the mesh tangent plane from `GetTangentBasis`. This checks the coordinate round trip before the final smooth-plane projection. The usual vector heat solve is measured on the same vertices for comparison. Each reported maximum includes both angular bands.

| Finest mesh source | Transport cap RMS (°) | Transport far RMS (°) | Round-trip cap RMS (°) | Round-trip far RMS (°) | Round-trip max (°) |
|---|---:|---:|---:|---:|---:|
| cube-pole | 1.23684 | 7.43347 | 1.2055e-05 | 1.46694e-07 | 0.000348943 |
| cube-corner | 0.597588 | 1.06813 | 1.02931e-05 | 2.74964e-06 | 0.000311944 |
| ico-corner | 0.0592679 | 0.23571 | 9.891e-06 | 1.15139e-05 | 4.86756e-05 |
| ico-midpoint | 0.129991 | 0.492421 | 9.81845e-06 | 9.06861e-06 | 4.85901e-05 |

Across the 16 runs, the largest discrepancy from direct mesh-plane projection was 1.50 × 10⁻¹³ degrees. The round-trip maximum decreased on each refinement for all four sources. For the finest cube-pole case, the far-band round-trip RMS was 1.47 × 10⁻⁷ degrees, while the transport RMS was 7.43 degrees.

These results rule out destination coordinate conversion and the final tangent-plane projection as the sole explanation for the observed growth. The analytic field already round-trips accurately through those steps. The experiment leaves the discrete edge rotations, their aggregation in the connection Laplacian and the heat resolvent itself open.

## Single-edge transport control

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-edges`. The probe visits each edge once in the input and intrinsic Delaunay triangulations of both sphere families. It measures edges with positive cotangent weights because the heat operator clamps the others to zero. For a halfedge from i to j, the rotation acting from j to i is `Signpost(h) − ReverseSignpost(h) + π`, as in the connection assembly. The benchmark uses the public signposts and applies that rotation directly in tangent coordinates.

For each edge, the probe transports two smooth vectors from j: the unit tangent to the short great-circle arc toward i and a vector angled 45° from it. It compares the decoded result, projected onto the smooth tangent plane at i, with analytic transport along the great circle. The RMS is unweighted across edges and the two vectors. Edges whose endpoints were unconnected in the input mesh are also counted and measured separately. The smooth arc is the comparison reference even for a flipped edge whose polyhedral path differs.

| Mesh | Level | Connectivity | Edge RMS (°) | Edge max (°) | Inserted edges | Inserted RMS (°) |
|---|---:|---|---:|---:|---:|---:|
| cube | 9 | input | 0.0323564 | 0.148616 | 0 | - |
| cube | 9 | delaunay | 0.0327289 | 0.157513 | 192 | 0.0345313 |
| cube | 17 | input | 0.00573551 | 0.0529321 | 0 | - |
| cube | 17 | delaunay | 0.00598449 | 0.0529321 | 768 | 0.00709962 |
| cube | 33 | input | 0.00097769 | 0.0166374 | 0 | - |
| cube | 33 | delaunay | 0.00105166 | 0.0166374 | 3072 | 0.00136257 |
| cube | 65 | input | 0.000166815 | 0.00478541 | 0 | - |
| cube | 65 | delaunay | 0.000183834 | 0.00478541 | 12288 | 0.000252258 |
| ico | 2 | delaunay | 0.0761433 | 0.167299 | 0 | - |
| ico | 3 | delaunay | 0.0177126 | 0.0527956 | 0 | - |
| ico | 4 | delaunay | 0.00347959 | 0.0132814 | 0 | - |
| ico | 5 | delaunay | 0.000644404 | 0.00332574 | 0 | - |

The measured icosahedral input and Delaunay rows agree at every level. Every cube-sphere Delaunay refinement has smaller single-edge RMS and maximum error than the previous level. On the finest cube-sphere the Delaunay edge RMS is 0.000184° and the maximum is 0.00479°. The inserted edges have a higher RMS than the average over all active Delaunay edges and their RMS still decreases on every step, reaching 0.000252°.

The single-edge error decreases even on this family where the far-band error of full vector heat grows at the default time. This control measures a local consistency property for two input directions per edge. It leaves open how correlated edge errors accumulate and how the weighted diffusion combines paths. A one-edge error threshold cannot serve as an end-to-end convergence oracle on these fixtures.

## Accumulation along a shortest edge path

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-paths`. The probe uses the same intrinsic Delaunay sphere meshes and one source per family from the time sweeps. It runs `DijkstraGraphDistance` over the positive-weight edges, using intrinsic edge lengths as the graph costs. The library's graph routine uses single-precision costs and selects one predecessor at each vertex. The probe follows that same predecessor tree in both transport calculations.

At each edge, one calculation applies the signpost rotation in the mesh tangent coordinates. The other transports the smooth vector along the short unit-sphere arc between the same two vertices. Both start with the tangent vector of the previous controls. At each destination, the projected signpost vector is compared to the composed analytic vector. This is the rotation error along the chosen path. Comparing the composed analytic vector with transport along the direct great-circle arc gives the path error. On a sphere, the latter measures the holonomy of the loop formed by the two routes, modulo a full turn. The earlier angle cutoffs exclude the antipode and define the same cap and far-band samples. This experiment uses shortest edge paths as a control for path geometry. The full heat solve combines paths with its own weights.

| Mesh | Level | Path cap RMS (°) | Path far RMS (°) | Rotation cap RMS (°) | Rotation far RMS (°) |
|---|---:|---:|---:|---:|---:|
| cube | 9 | 2.04721 | 7.41346 | 0.0738128 | 0.0955596 |
| cube | 17 | 1.93298 | 10.5183 | 0.0214706 | 0.0284808 |
| cube | 33 | 1.94152 | 10.6242 | 0.00581227 | 0.00770385 |
| cube | 65 | 1.93121 | 10.9685 | 0.00150956 | 0.00200858 |
| ico | 2 | 13.7809 | 31.7163 | 0.0951262 | 0.0568844 |
| ico | 3 | 12.8295 | 22.7217 | 0.0520729 | 0.0214664 |
| ico | 4 | 12.5973 | 19.3759 | 0.0268398 | 0.0124363 |
| ico | 5 | 12.5901 | 18.6423 | 0.0131329 | 0.00546394 |

On the cube-sphere, the far-band path error rises to 10.97° while the rotation error along that same path decreases to 0.00201°. On the icosahedral sphere, the far-band path error decreases but remains 18.64° on the finest mesh. Its rotation error decreases to 0.00546°. On both meshes, the angle from composing smooth transport along the chosen route is much larger than the discrepancy caused by replacing its rotations with signposts.

The shortest edge path is a deliberately constrained control. In particular, the finest icosahedral heat solve previously measured only 0.236° far-band RMS, far below 18.64° for this control. These measurements show that route geometry alone can dominate transport error in an edge-path method. They leave the effective weights and cancellations of the heat resolvent open. The path probe provides no reason to replace vector heat with graph-path transport.

## Replacing the connection rotations inside the heat solve

The [unit-sphere control patch](../../benchmarks/CurvatureWeakProbe/sphere-connection-control.patch) changes the rotation used by `VectorHeatMethod` on each edge. It decodes the head's local vector (1, 0), projects it onto the smooth sphere tangent plane, transports it analytically along the short great-circle arc, and encodes the result at the tail. The encoded angle fixes the replacement 2D rotation. This calibrates one reference direction per edge. The signpost-to-world map is piecewise linear in angle, so one rotation need not match analytic transport for every direction. Connectivity, weights, vertex mass, right-hand side, factorization and output conversions stay the same.

Run the baseline from the repository root, then apply the patch in a disposable worktree. The patch assumes unit-sphere vertices and is intended only for the two commands below.

```sh
dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-sources
dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-time
git worktree add --detach ../sphere-connection-control HEAD
git -C ../sphere-connection-control apply "$PWD/benchmarks/CurvatureWeakProbe/sphere-connection-control.patch"
cd ../sphere-connection-control
dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-sources
dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-time
```

The default-time source sweep kept the scalar distance columns and vector diffusion ratios identical in all 16 cases. The finest vector results are below.

| Finest mesh source | Baseline cap RMS (°) | Control cap RMS (°) | Baseline far RMS (°) | Control far RMS (°) |
|---|---:|---:|---:|---:|
| cube-pole | 1.23684 | 1.23776 | 7.43347 | 7.43436 |
| cube-corner | 0.597588 | 0.599334 | 1.06813 | 1.06865 |
| ico-corner | 0.0592679 | 0.0586036 | 0.23571 | 0.234973 |
| ico-midpoint | 0.129991 | 0.129518 | 0.492421 | 0.491892 |

The large cube-pole far-band error persists at t = h² with the replacement rotations. At t = 0.5h the finest cube-pole far-band RMS is 0.12843° with signposts and 0.12903° with the replacement, compared with 7.43347° and 7.43436° at t = h². Thus sensitivity to diffusion time persists after replacing the edge rotations by this smooth reference calibration.

For these sphere meshes, the implicit vector heat system can also be read as a sum over walks. Write it as `(A − tW_R)y = b`, where A is the diagonal mass plus weighted degree term and each off-diagonal block of W_R is the nonnegative cotangent weight times its edge rotation. The maximum row sum of the corresponding scalar matrix `A⁻¹tW` is strictly below one because vertex masses are positive. Expanding the inverse as a Neumann series gives one term per walk length. Individual terms multiply the scalar transition factors and compose the rotations along the walk. Changing t changes those factors even with rotations fixed. The experiment does not measure the directional distribution of those terms or identify a particular stencil feature as the source of the error.

## Equal-distance scalar heat contributions at the equator

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-equator`. On each cube-sphere, the positive x-axis meets a face center and the direction (1, 1, 0) meets a cube seam. Both vertices lie exactly π/2 radians from the north pole. The continuous unit-sphere scalar heat response at this distance is independent of azimuth.

The probe calls `VectorHeatMethod.ExtendScalar` with value one at the face center and zero at the seam. For the scalar inverse heat matrix K and pole p, the result is K[p, face] / (K[p, face] + K[p, seam]). Swapping the values measures the complementary share independently. Symmetry of K also makes the ratio of shares the ratio of the two responses to one pole impulse. This samples the total scalar weight of all walks to each endpoint. The probe checks that both shares are positive, sum to one within tolerance and use the requested diffusion time. A second pair compares the x and y face centers at the same distance.

| Vertices per edge | Face share, default | Face share, linear schedule | Face/seam log ratio, default | Face/seam log ratio, linear schedule |
|---:|---:|---:|---:|---:|
| 9 | 0.496224 | 0.491580 | −0.015106 | −0.033683 |
| 17 | 0.522082 | 0.495379 | 0.088387 | −0.018486 |
| 33 | 0.575858 | 0.498031 | 0.305791 | −0.007875 |
| 65 | 0.679779 | 0.499484 | 0.752756 | −0.002065 |

At 65 vertices per edge, the face-center response is 2.123 times the seam response at the default time. The linear schedule brings that ratio to 0.99794. The x/y face-center ratio is one to the eight printed significant digits throughout. The default operator thus shows a growing directional difference for this equatorial pair across the sampled resolutions, while the linear schedule approaches equal contributions. These are relative amplitudes. The comparison samples two endpoints and sums over all routes to each one. It leaves open how rotations along different walks to a *single* far-band endpoint combine to produce its vector error.

## Cancellation among walks to the same vertex

For a single unit tangent impulse at the north pole, let y be the vector heat solution in local coordinates and φ the scalar response to a unit impulse at that same pole. The expansion over walks above gives φ as the sum of their positive weights and y as the weighted sum of transported unit vectors. Thus R = ‖y‖/φ is the length of their weighted mean at one destination, also called the mean resultant length. R = 1 means all positive-weight directions agree. Smaller R measures more cancellation. The mean direction can still deviate from analytic great-circle transport when R is close to one.

The [diagnostic patch](../../benchmarks/CurvatureWeakProbe/sphere-walk-resultant.patch) returns y/φ before direction normalization and adds the probe command in a disposable worktree. It uses the same scalar and vector solves as the production transport. From the repository root run

```sh
git worktree add --detach ../sphere-walk-resultant HEAD
git -C ../sphere-walk-resultant apply "$PWD/benchmarks/CurvatureWeakProbe/sphere-walk-resultant.patch"
cd ../sphere-walk-resultant
dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-resultant
```

Each grid contains the same off-axis destination, the cube generator's sphere mapping of (1/2, 1/4, −1). The initial vector points along the x-axis. At the fixed vertex the probe records R and the angle between the mean direction and analytic transport. Over the earlier far band it reports the unweighted vertex mean of R and the RMS angle. The probe checks the requested diffusion time and bounds on R at each sampled vertex.

| Vertices per edge | Schedule | Fixed R | Fixed angle (°) | Far vertices | Far mean R | Far RMS angle (°) |
|---:|---|---:|---:|---:|---:|---:|
| 9 | default | 0.38277 | 1.27798 | 44 | 0.44258 | 0.77345 |
| 9 | linear | 0.23344 | 0.07923 | 44 | 0.29534 | 0.05252 |
| 17 | default | 0.63743 | 3.74898 | 192 | 0.69725 | 2.45463 |
| 17 | linear | 0.34741 | 0.22748 | 192 | 0.44514 | 0.15166 |
| 33 | default | 0.81271 | 7.65999 | 772 | 0.84186 | 4.75966 |
| 33 | linear | 0.48111 | 0.23130 | 772 | 0.56536 | 0.15268 |
| 65 | default | 0.91302 | 11.69874 | 3100 | 0.91945 | 7.43347 |
| 65 | linear | 0.61070 | 0.19525 | 3100 | 0.66937 | 0.12843 |

At the fixed destination under default time, R rises from 0.383 to 0.913 as the angular error grows from 1.28° to 11.70°. The transported vectors increasingly agree around a biased direction. On the finest grid the linear schedule gives more cancellation (R = 0.611) and a smaller error (0.195°) at that same point. All eight far-band RMS angles reproduce the production time sweep to its reported precision. Greater agreement among walks is therefore insufficient as an accuracy indicator on this fixture. These first moments do not identify which routes account for the directional bias.

## Grouping walks by their final edge

At the same off-axis target i, group the walks by their last step from a neighbor j. Write m = y/φ for the local mean vector measured above and Dᵢ for the scalar system diagonal (vertex mass plus t times the sum of incident weights). Because i is away from the source, its scalar and vector system rows give

```text
pⱼ = t wᵢⱼ φⱼ / (Dᵢ φᵢ)
sumⱼ pⱼ = 1
mᵢ = sumⱼ pⱼ Rᵢⱼ mⱼ
```

Here Rᵢⱼ is the rotation used in the assembled connection matrix. The probe reconstructs both rows, then repeats the vector sum with each neighbor's mean direction replaced by analytic pole-to-neighbor transport encoded in its local frame. Each neighbor retains its measured mean length, its scalar arrival share and its final-edge rotation. The control asks how much directional error the final step produces when supplied with those reference directions. Walks can revisit the target, so a last-edge group can contain earlier visits as well.

To reproduce from the repository root, apply the [arrival control](../../benchmarks/CurvatureWeakProbe/sphere-arrival-control.patch) on top of the preceding resultant patch.

```sh
git worktree add --detach ../sphere-arrival-control HEAD
cd ../sphere-arrival-control
git apply benchmarks/CurvatureWeakProbe/sphere-walk-resultant.patch
git apply benchmarks/CurvatureWeakProbe/sphere-arrival-control.patch
dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-arrivals
```

| Vertices per edge | Schedule | Largest arrival share | Measured angle (°) | Angle with reference directions at neighbors (°) |
|---:|---|---:|---:|---:|
| 9 | default | 0.33028 | 1.27798 | 0.72921 |
| 9 | linear | 0.28361 | 0.07923 | 0.03795 |
| 17 | default | 0.37693 | 3.74898 | 0.78557 |
| 17 | linear | 0.28750 | 0.22748 | 0.04751 |
| 33 | default | 0.40476 | 7.65999 | 0.57019 |
| 33 | linear | 0.28662 | 0.23130 | 0.01331 |
| 65 | default | 0.42271 | 11.69874 | 0.37105 |
| 65 | linear | 0.28367 | 0.19525 | 0.00299 |

All eight reconstructions agree with the measured angles to the printed precision. The largest scalar or vector row residual is below 5 × 10⁻¹⁶. At the finest resolution one of the six incoming edges carries 42.27% of the scalar weight at default time and the largest share under the linear schedule is 28.37%. With the original weights and mean lengths, supplying analytic neighbor directions reduces the default error from 11.699° to 0.371° and the linear-schedule error from 0.195° to 0.003°. Thus at this destination the large default error depends on the directions already present in the neighboring field. This local substitution neither determines where that bias first develops nor yields an additive split of angular errors.

## Spatial profile from the pole to the off-axis target

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-profile`. Divide the short great-circle arc from the north pole to the previous target into eight equal intervals. Select the nearest vertex to each station on the 17-vertex-per-edge cube-sphere, then sample those same physical vertices on the 33 and 65 grids. The largest angular offset from an intended station is 2.78°. The table uses the actual colatitudes. Each value is the signed angle from analytic pole-to-vertex transport to the computed direction projected onto the smooth tangent plane. Positive angles follow the right-hand rule about the outward normal.

| Colatitude (°) | Default 17 (°) | Default 33 (°) | Default 65 (°) | Linear 65 (°) |
|---:|---:|---:|---:|---:|
| 0 | 0 | 0 | 0 | 0 |
| 18.30399 | 0.00065 | 0.00056 | 0.00038 | 0.00006 |
| 41.78529 | 0.03297 | 0.01858 | 0.01473 | 0.00197 |
| 60.35664 | 0.15081 | 0.12668 | 0.12628 | 0.00877 |
| 80.51052 | 0.48554 | 0.51739 | 0.53560 | 0.02932 |
| 99.48948 | 0.92040 | 1.17149 | 1.26349 | 0.05375 |
| 119.64336 | 1.51499 | 2.29911 | 2.71940 | 0.08268 |
| 138.21471 | 2.39204 | 4.26649 | 5.62083 | 0.12448 |
| 157.13005 | 3.74898 | 7.65999 | 11.69874 | 0.19525 |

The endpoint reproduces the previous control on all three grids under both schedules. The default error decreases with refinement at the 41.79° station (0.03297° to 0.01473°), but increases at each sampled station from 80.51° onward. On the finest grid the signed error grows from 0.53560° at 80.51° to 2.71940° at 119.64°. Its largest sampled increase is 6.07791°, between 138.21° and the target at 157.13°. With the linear schedule the corresponding increment is 0.07076°. These differences describe the spatial variation in the solved field along one sampled meridian. They do not measure the contribution of walks traversing a particular segment.

## Dependence on longitude at fixed colatitude

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-azimuth`. Keep the north-pole source and its x-directed input fixed. The probe evaluates the signed transport error above at every equatorial vertex and every vertex with the z-coordinate of the off-axis target (within 1e-12 on the unit sphere). This yields 64, 128 and 256 equatorial samples and eight target-latitude samples per resolution. Comparisons across longitude are at the same spherical distance from the source, without interpolation. The eight far samples are symmetry-related copies of one off-axis point and give no coverage between those longitudes.

| Vertices per edge | Schedule | Equator max absolute error (°) | Target latitude max absolute error (°) |
|---:|---|---:|---:|
| 17 | default | 0.73326 | 3.75274 |
| 33 | default | 0.82569 | 7.66080 |
| 65 | default | 0.85324 | 11.69891 |
| 17 | linear | 0.07082 | 0.23099 |
| 33 | linear | 0.06800 | 0.23210 |
| 65 | linear | 0.04423 | 0.19544 |

On the finest default equator, the signed error is within 0.000011° of zero at longitudes 0° and 45°, yet reaches +0.85283° at 23.44°. Comparing the entire sampled equator with itself after a quarter turn gives a maximum absolute difference of 0.00238° or less. Reflecting longitude and comparing with the sign reversed gives a maximum difference below 0.00166°. The signed field therefore has an approximately repeating quarter-turn pattern with opposite errors across the symmetry plane.

At colatitude 157.13°, the finest default errors at longitudes +25.82° and −25.82° are +11.69874° and −11.69856°. The other six copies follow the same quarter-turn and reflection pattern to within 0.00036°. The previously measured endpoint bias recurs at symmetry-related destinations and changes sign under reflection. These comparisons identify a directional pattern on the cube-sphere but leave the individual walk contributions unresolved.

## Fourfold harmonic of the equatorial error

The quarter-turn and reflection measurements suggest a specific signed angular profile, A sin(4λ), where λ is longitude from the x-axis and A is in degrees. This fixes the phase using the cube axes and fits just one coefficient per grid and time schedule. Higher multiples of four can share these symmetries, so the residual measures how far the sampled profile departs from this single sine wave.

To reproduce the table, use the equator rows of `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-azimuth`. Sort by increasing longitude as printed (−180° to 180°) and convert longitude to radians. Give each sample weight w equal to half the sum of its two neighboring longitude gaps, including the gap across the wrap. This approximates uniform angular weighting on the irregularly spaced samples. With signed error e and s = sin(4λ), compute

```text
A = Σ(w s e) / Σ(w s²)
residual RMS = √[Σ(w (e − A s)²) / Σw]
share of squared error described = 1 − Σ(w (e − A s)²) / Σ(w e²)
```

For the last column, fit A on zero-based even positions in the sorted ring and evaluate residual RMS on odd positions, retaining the original weights. The interleaved samples come from the same solved field.

| Vertices per edge | Schedule | A (°) | Residual RMS (°) | Share of squared error (%) | Odd-position RMS (°) |
|---:|---|---:|---:|---:|---:|
| 17 | default | 0.696655 | 0.044113 | 99.206 | 0.024630 |
| 33 | default | 0.830570 | 0.017127 | 99.915 | 0.012643 |
| 65 | default | 0.875739 | 0.041030 | 99.563 | 0.041069 |
| 17 | linear | 0.049546 | 0.018769 | 77.742 | 0.012772 |
| 33 | linear | 0.060369 | 0.008141 | 96.493 | 0.007340 |
| 65 | linear | 0.041683 | 0.003062 | 98.933 | 0.002968 |

At 65 vertices per edge the linear schedule reduces the fitted amplitude by a factor of 21.0. For default time the residual remains 0.041° RMS both on the full ring and at the interleaved evaluation points. The fit describes the spatial shape of the equatorial error on these meshes. The eight symmetry-related samples at the far target latitude do not distinguish this harmonic from other shapes between the sampled longitudes.

## Longitude profile at the far target latitude

Run `dotnet run -c Release --project benchmarks/CurvatureWeakProbe -- --heat-latitude`. At the target's 157.13° colatitude, sample 256 equally spaced longitudes around the circle. For each direction, cast a ray from the origin and use its barycentric coordinates in the intersected input triangle to interpolate the **signed vertex errors** defined above. Every contributing vertex error is within (−90°, 90°), where this scalar interpolation is unambiguous. At all eight vertices on the circle, a separate ray query must reproduce the directly measured error within 1e-8°. Between them the values depend on the interpolation and the mesh resolution. The 256 longitudes sample one interpolated field per solve.

For a ring of N samples with longitude λ in radians and signed error e in degrees, compute the amplitude of each sine term as `Aₘ = (2/N) Σ e sin(mλ)`. Uniform spacing makes the m = 4 and m = 8 terms orthogonal. The table gives the RMS residual after fitting the fourfold sine alone, its share of total squared error, and the RMS residual after adding the eightfold sine. Both phases are fixed by the cube axes.

| Vertices per edge | Schedule | A₄ (°) | Fourfold residual RMS (°) | Share (%) | A₈ (°) | Two-term residual RMS (°) |
|---:|---|---:|---:|---:|---:|---:|
| 17 | default | 3.557512 | 0.129601 | 99.735 | −0.026833 | 0.128204 |
| 33 | default | 7.510203 | 0.431302 | 99.345 | −0.598977 | 0.081454 |
| 65 | default | 11.463937 | 1.490145 | 96.731 | −2.052221 | 0.338716 |
| 17 | linear | 0.220217 | 0.008521 | 99.701 | 0.006788 | 0.007040 |
| 33 | linear | 0.234865 | 0.002652 | 99.974 | 0.002986 | 0.001605 |
| 65 | linear | 0.200461 | 0.001175 | 99.993 | 0.001570 | 0.000384 |

On the finest default grid the fourfold component dominates at both latitudes. Its amplitude rises from 0.876° at the equator to 11.464° at the far latitude, while its share drops from 99.563% to 96.731%. At the far latitude, adding the 45°-period term reduces the remaining RMS from 1.490° to 0.339°. With the linear schedule the fourfold component alone leaves 0.00118° RMS. This measures the angular shape of the solved field at finite resolution. It does not attribute the eightfold component to a particular family of diffusion walks.
