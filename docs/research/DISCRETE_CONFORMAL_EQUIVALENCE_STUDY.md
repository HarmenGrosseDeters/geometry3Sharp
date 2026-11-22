# Does discrete conformal scaling resolve P1 annulus collapse?

**Partly.** On both open cylinders used in the parameterization tests, even after a small symmetry-breaking angular displacement, an explicit vertex scaling gives an embedded planar annulus. LSCM returns two bad faces in each case. These cases admit a closed-form solution of the metric equations. They do not exercise the paper's curvature optimizer or require any flips.

## Experiment

Run `dotnet run --project benchmarks/ConformalAnnulusProbe` from the repository root. The probe builds a unit-radius cylinder with equally spaced height bands, joined without a seam. In the perturbed case the first angular column moves by 0.00001 radians on every ring. The library supplies LSCM, the intrinsic edge lengths (mollification disabled), the Delaunay routine and the UV distortion check.

| Sides × bands | Band height | Perturbation (rad) | LSCM flipped + degenerate | Scaled flipped + degenerate | Max relative edge residual | Strict predicate failures | Default flips |
|---|---:|---:|---:|---:|---:|---:|---:|
| 8 × 3 | 0.6 | 0 | 2 | 0 | 6.661e-16 | 0 | 0 |
| 8 × 3 | 0.6 | 0.00001 | 2 | 0 | 6.661e-16 | 1 | 0 |
| 48 × 12 | 0.3 | 0 | 2 | 0 | 1.554e-15 | 172 | 0 |
| 48 × 12 | 0.3 | 0.00001 | 2 | 0 | 1.554e-15 | 171 | 0 |

Measurements used the .NET 10 SDK on Linux with the repository C# sources. A Release build with the repository analyzers reproduced all four rows. Residual is the absolute relative difference between each planar edge length and its scaled intrinsic length. Strict failures count input edges classified as non-Delaunay at zero tolerance.

## Why this scaling works

For band height h choose q = exp(-2 asinh(h/2)), so (1-q)²/q = h². Ring j maps to radius r = q^j at its original angles. Use the vertex scaling from Gillespie, Springborn and Crane, equation 1, with log scale log r. An edge with endpoint radii r and s scales by sqrt(rs). For unit-radius chord length c the squared planar distance is (r-s)² + rs c². Dividing by rs gives c² on one ring and h² + c² across a band, exactly the squared cylinder edge length. This also covers vertical edges with c = 0. Each sector is a convex trapezoid between two nested similar polygons. Their interiors are disjoint and triangulating them gives a globally embedded annulus.

## Flips, arithmetic and decision

Every side panel unfolds to a rectangle on the cylinder and maps to an isosceles trapezoid in the plane. Both are cyclic, so their diagonals are exact Delaunay ties. Along vertical and ring edges the opposite angles in the input rectangles are acute. This construction supplies an analytic tie oracle. The library sums two double cotangents and compares to minus a tolerance. With zero tolerance it can reject exact-model ties. The default tolerance accepts every input edge here. The trigonometric vertex coordinates themselves are rounded, so these counts do not distinguish input rounding from predicate evaluation error.

Section 3.3.4 of the paper updates a diagonal by the sum of products of opposite side lengths divided by the old diagonal. Rational arithmetic could evaluate this exactly for rational input lengths. Rounded square roots and scaled lengths are a different input model. The library's Euclidean intrinsic flip preserves the current Euclidean metric. The paper's Ptolemy flip agrees with it on cyclic quadrilaterals but has different semantics off that locus. No executed flip in this experiment validates a Ptolemy implementation.

**Recommendation — park a general solver.** Keep this inexpensive analytic case as a reference. The existence guarantee here follows from the explicit sector construction. General annuli would require choosing boundary conditions, controlling holonomy, solving for scale factors, transporting connectivity across flips and checking global embedding. That is a separate medium-to-large implementation and validation effort. This probe gives no guarantee for those charts.
