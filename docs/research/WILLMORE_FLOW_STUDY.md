# Helfrich bending energy on ring tori

**Decision.** Build an energy regression fixture for the membrane study in #1424. The existing `MeshCurvature.Compute` distinguishes three torus shapes in their predicted energy order at every resolution tested. A subsequent fairing experiment can use this energy to measure progress between steps. Choosing a stable time step still requires a flow experiment.

## Continuous prediction

Helfrich models the local bending energy density by κ(2H−C₀)²/2 + κ̄K, with bending modulus κ, spontaneous curvature C₀, mean curvature H and Gaussian curvature K. Set κ=1, C₀=0 and omit tension and volume constraints. On a closed torus the integrated Gaussian term vanishes by Gauss–Bonnet. The remaining energy is twice the Willmore integral. [Helfrich (1973), original paper](https://doi.org/10.1515/znc-1973-11-1209)

For a circular tube of radius r around a circle of radius R, write q=R/r>1. The principal curvatures, up to a common orientation sign, are 1/r and cos(v)/(R+r cos(v)). The area element is r(R+r cos(v)) du dv. Substitution gives E = π ∫ (q+2 cos(v))²/(q+cos(v)) dv over one turn. Polynomial division leaves a cosine term with zero integral and q²/(q+cos(v)), giving **E(q)=2π²q²/√(q²−1)**. Within the ring-torus family the minimum is 4π² at q=√2. A uniform change of length scale cancels between curvature squared and area.

## Operator measurements

Linux x64, .NET 10 Release, library revision `3c4d93ac`. Periodic n-by-n angle grids, fixed quad diagonal, 2n² triangles. Sum twice the mixed vertex area times the squared scalar mean curvature at every vertex. The table reports the signed percentage error against the analytic energy at r=1.

| Samples per angle | q=1.2 | q=√2 | q=2 |
|---:|---:|---:|---:|
| 16 | −4.9330% | −2.8030% | −1.6977% |
| 32 | −1.3095% | −0.7295% | −0.4410% |
| 64 | −0.3327% | −0.1847% | −0.1118% |

At n=64 the energies in column order were 42.708909, 39.405487 and 45.534788. The narrower inner neck at q=1.2 gave the largest relative error. Doubling the final angular resolution reduced each error by approximately a factor of four. All vertices passed the curvature validity check. Repeating all nine cases at r=8 reproduced the energies to twelve significant digits. The largest absolute summed angle deficit among the eighteen runs was 1.04e−12 or less, consistent with Euler characteristic zero.

**Proposed use and cost.** Allow half an engineering day for a regression fixture. At n=32 all relative errors were below 1.4%; the q=√2 case alone needs 2,048 triangles. Check the energy ordering, scale invariance and agreement with the smooth limit under refinement. Generation and curvature accumulation are linear in face count on this fixed-valence mesh. The formula gives an exact smooth reference. The accuracy observed here applies to the stated discretization. Flow stability, energy decrease after remeshing and the effects of nonzero spontaneous curvature remain open.

## Reproduce

Reference the net10.0 build of geometry3Sharp from a temporary .NET 10 console project and run the following Program.cs with `dotnet run -c Release`. Output columns are n, q, r, energy, signed relative error and summed angle deficit.

```csharp
using System;
using System.Linq;
using g3;
using gs;

foreach (int n in new[] {16, 32, 64})
foreach (double q in new[] {1.2, Math.Sqrt(2), 2.0})
foreach (double r in new[] {1.0, 8.0}) {
    var mesh = new DMesh3();
    int Id(int i, int j) => (i % n) * n + j % n;
    for (int i = 0; i < n; ++i)
        for (int j = 0; j < n; ++j) {
            double u = 2*Math.PI*i/n, v = 2*Math.PI*j/n;
            double rho = r * (q + Math.Cos(v));
            mesh.AppendVertex(new Vector3d(rho*Math.Cos(u), rho*Math.Sin(u), r*Math.Sin(v)));
        }
    for (int i = 0; i < n; ++i)
        for (int j = 0; j < n; ++j) {
            int a = Id(i,j), b = Id(i+1,j), c = Id(i+1,j+1), d = Id(i,j+1);
            if (mesh.AppendTriangle(a,b,c) < 0 || mesh.AppendTriangle(a,c,d) < 0)
                throw new Exception("Invalid face");
        }
    var curvature = MeshCurvature.Compute(mesh);
    if (curvature.ValidVertices != n*n) throw new Exception("Invalid curvature");
    double energy = mesh.VertexIndices().Sum(i => 2*curvature.MixedArea[i]*
        curvature.Mean[i]*curvature.Mean[i]);
    double exact = 2*Math.PI*Math.PI*q*q/Math.Sqrt(q*q-1);
    double defect = mesh.VertexIndices().Sum(i => curvature.AngleDeficit[i]);
    Console.WriteLine($"{n} {q:G8} {r} {energy:G12} {energy/exact-1:G6} {defect:G5}");
}
```
