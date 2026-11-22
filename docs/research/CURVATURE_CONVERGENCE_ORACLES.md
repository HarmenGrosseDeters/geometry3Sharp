# When cylinder refinement preserves area and mean curvature

Hildebrandt, Polthier and Wardetzky (2006) study how normal convergence controls intrinsic geometry on polyhedral approximations. Their Schwarz lantern example gives an actionable test for plan #1424. The vertices lie on the smooth cylinder at every resolution, yet the mesh area can approach the wrong limit. The paper's curvature convergence uses a weak formulation. The maximum vertex-wise error below is a separate empirical measurement. [Paper, including theorem statements](https://page.mi.fu-berlin.de/polthier/articles/convergence/convergence.pdf)

## Two meshes with the same radial accuracy

Take the open lateral surface of a unit-radius, unit-height cylinder. Place n equally spaced vertices on each of m+1 rings. Rotate alternate rings by π/n and join neighbours into 2nm congruent triangles. The radial deviation is at most s = 1−cos(π/n) throughout both families. Each triangle has base 2 sin(π/n) and altitude √(m⁻²+s²). Thus the exact polyhedral area divided by the smooth area 2π is

    [sin(π/n)/(π/n)] √(1 + m²s²).

For m=n the relative area error is (π⁴/8−π²/6)/n² + O(n⁻⁴). For m=n² the area ratio approaches √(1+π⁴/4), about 5.04. The facet normal tilts away from the radial direction at the base midpoint by arctan(ms). This tilt vanishes in the first family and approaches arctan(π²/2) in the second. These rates and limits follow from the fixture geometry.

## C# run

Library revision `3c4d93ac`, .NET 10 Release, Linux x64. `MeshMeasurements.AreaT` sums triangle areas. `MeshCurvature.Compute` supplies signed scalar mean curvature with the half-trace convention. The smooth interior has |H| = 0.5. Every interior vertex passed the operator's validity check. Boundary rings are excluded from the curvature statistic.

| n | m | Area / 2π − 1 | Absolute relative error vs polyhedral formula | Max interior error in curvature magnitude |
|---:|---:|---:|---:|---:|
| 8 | 8 | 0.140966 | 1.78e−15 | 0.135259 |
| 16 | 16 | 0.039482 | 4.22e−15 | 0.043177 |
| 32 | 32 | 0.010177 | 2.57e−14 | 0.011596 |
| 8 | 64 | 3.846443 | 1.38e−14 | 0.479785 |
| 16 | 256 | 3.987395 | 1.78e−13 | 0.480156 |
| 32 | 1024 | 4.023143 | 1.17e−12 | 0.480247 |

On the balanced sequence, observed area orders log₂(error ratio) were 1.84 and 1.96, approaching the analytic order 2. The curvature orders were 1.65 and 1.90. On the flattened sequence the area routine still tracked the exact discrete area, while curvature magnitude error stayed near 0.48. These three resolutions support a curvature regression baseline for this cylinder. They establish no general pointwise rate for the cotangent operator.

**Recommendation.** Build the paired area fixture. Use the discrete formula for both families and check the limiting trend separately. An assertion that every refined mesh approaches 2π would misdiagnose a correct area routine. Keep vertex curvature as a diagnostic until additional mesh families establish an acceptance envelope. A follow-up test should take about half an engineering day. Both the mesh size and area traversal scale as O(nm). The two n=16 cases require 512 and 8,192 triangles. Runtime budgets remain unmeasured.

## Reproduction

Create a temporary .NET 10 console project outside the checkout, reference `geometry3Sharp_netstandard.csproj`, use the following Program.cs and run `dotnet run -c Release`. The last three output fields correspond to the last three table columns.

```csharp
using gs;
using System;
using System.Linq;
using g3;

foreach (int n in new[] {8,16,32}) {
    foreach (int m in new[] {n,n*n}) {
        var mesh = new DMesh3();
        int Id(int j,int i) => j*n+(i+n)%n;
        for (int j=0;j<=m;j++)
            for (int i=0;i<n;i++) {
                double a = (2*i+j%2)*Math.PI/n;
                mesh.AppendVertex(new Vector3d(Math.Cos(a),Math.Sin(a),(double)j/m));
            }
        for (int j=0;j<m;j++)
            for (int i=0;i<n;i++) {
                int k = i+j%2;
                if (mesh.AppendTriangle(Id(j,i),Id(j,i+1),Id(j+1,k))<0 ||
                    mesh.AppendTriangle(Id(j,i+1),Id(j+1,k+1),Id(j+1,k))<0)
                    throw new Exception("Invalid strip");
            }
        double area = MeshMeasurements.AreaT(mesh,mesh.TriangleIndices());
        double angle = Math.PI/n;
        double sag = 1-Math.Cos(angle);
        double exact = 2*n*Math.Sin(angle)*Math.Sqrt(1+m*m*sag*sag);
        var curv = MeshCurvature.Compute(mesh);
        int[] interior = mesh.VertexIndices().Where(v=>v>=n && v<m*n).ToArray();
        if (interior.Any(v=>!curv.Valid[v])) throw new Exception("Invalid interior");
        double error = interior.Max(v=>Math.Abs(Math.Abs(curv.Mean[v])-.5));
        Console.WriteLine($"{n} {m} {area/(2*Math.PI)-1:G7} {Math.Abs(area/exact-1):G4} {error:G7}");
    }
}
```
