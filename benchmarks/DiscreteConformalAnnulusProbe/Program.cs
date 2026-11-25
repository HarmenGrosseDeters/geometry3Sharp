// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

static DMesh3 MakeCylinder(
    int sides,
    int strips,
    double height,
    double topRadius = 1.0,
    double waistScale = 1.0,
    double twistRad = 0.0,
    double bendAngleRad = 0.0,
    double zJitterFrac = 0.0,
    double radialJitterFrac = 0.0,
    double sharedAngleJitter = 0.0,
    int seed = 42)
{
    var mesh = new DMesh3();
    var rng = new Random(seed);
    double dz = height / strips;
    double bendR = bendAngleRad > 1e-9 ? height / bendAngleRad : 0.0;

    for (int ring = 0; ring <= strips; ++ring) {
        double t = (double)ring / strips;
        double rBase = (1.0 - t) * 1.0 + t * topRadius;
        double waist = 1.0 - (1.0 - waistScale) * Math.Sin(Math.PI * t);
        double rRing = rBase * waist;
        for (int i = 0; i < sides; ++i) {
            double angle = 2.0 * Math.PI * i / sides + t * twistRad + sharedAngleJitter * Math.Sin(3 * i + 1);
            double z = dz * ring;
            if (zJitterFrac > 0)
                z += (2.0 * rng.NextDouble() - 1.0) * zJitterFrac * dz;
            double r = rRing;
            if (radialJitterFrac > 0)
                r *= 1.0 + (2.0 * rng.NextDouble() - 1.0) * radialJitterFrac;

            Vector3d p;
            if (bendAngleRad > 1e-9) {
                double phi = z / bendR;
                double cx = bendR - bendR * Math.Cos(phi);
                double cz = bendR * Math.Sin(phi);
                Vector3d ex = new Vector3d(Math.Cos(phi), 0, Math.Sin(phi));
                Vector3d ey = new Vector3d(0, 1, 0);
                p = new Vector3d(cx, 0, cz) + r * Math.Cos(angle) * ex + r * Math.Sin(angle) * ey;
            } else {
                p = new Vector3d(r * Math.Cos(angle), r * Math.Sin(angle), z);
            }
            mesh.AppendVertex(p);
        }
    }
    for (int ring = 0; ring < strips; ++ring) {
        for (int i = 0; i < sides; ++i) {
            int a = ring * sides + i;
            int b = ring * sides + (i + 1) % sides;
            mesh.AppendTriangle(a, b, b + sides);
            mesh.AppendTriangle(a, b + sides, a + sides);
        }
    }
    return mesh;
}

static DMesh3 MakeHighCurvatureCone(int sides, int rings, double height, double jitter = 0.0, int seed = 7)
{
    var mesh = new DMesh3();
    var rng = new Random(seed);
    int apex = mesh.AppendVertex(new Vector3d(0, 0, height));
    for (int r = 1; r <= rings; ++r) {
        double t = (double)r / rings;
        for (int i = 0; i < sides; ++i) {
            double a = 2.0 * Math.PI * i / sides;
            double rad = t * (1.0 + (jitter > 0 ? (2.0 * rng.NextDouble() - 1.0) * jitter : 0));
            double z = height * (1.0 - t) + (jitter > 0 ? (2.0 * rng.NextDouble() - 1.0) * jitter * height / rings : 0);
            mesh.AppendVertex(new Vector3d(rad * Math.Cos(a), rad * Math.Sin(a), z));
        }
    }
    for (int i = 0; i < sides; ++i) {
        int b = 1 + i;
        int c = 1 + (i + 1) % sides;
        mesh.AppendTriangle(apex, b, c);
    }
    for (int r = 1; r < rings; ++r) {
        int base0 = 1 + (r - 1) * sides;
        int base1 = 1 + r * sides;
        for (int i = 0; i < sides; ++i) {
            int a = base0 + i, b = base0 + (i + 1) % sides;
            int c = base1 + (i + 1) % sides, d = base1 + i;
            mesh.AppendTriangle(a, d, c);
            mesh.AppendTriangle(a, c, b);
        }
    }
    return mesh;
}

static DMesh3 MakeTwoHolePlate(int nx, int ny, double bumpHeight)
{
    // Grid [0..nx] x [0..ny] with two rectangular holes removed and a smooth z-bump.
    var mesh = new DMesh3();
    int[,] vid = new int[nx + 1, ny + 1];
    for (int i = 0; i <= nx; ++i) {
        for (int j = 0; j <= ny; ++j) {
            double x = (double)i / nx * 2.0 - 1.0;
            double y = (double)j / ny * 2.0 - 1.0;
            double z = bumpHeight * Math.Exp(-2.5 * (x * x + y * y));
            vid[i, j] = mesh.AppendVertex(new Vector3d(x, y, z));
        }
    }
    bool IsHoleCell(int i, int j)
    {
        bool hole1 = i >= nx / 5 && i < 2 * nx / 5 && j >= ny / 3 && j < 2 * ny / 3;
        bool hole2 = i >= 3 * nx / 5 && i < 4 * nx / 5 && j >= ny / 3 && j < 2 * ny / 3;
        return hole1 || hole2;
    }
    for (int i = 0; i < nx; ++i) {
        for (int j = 0; j < ny; ++j) {
            if (IsHoleCell(i, j))
                continue;
            int a = vid[i, j], b = vid[i + 1, j], c = vid[i + 1, j + 1], d = vid[i, j + 1];
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, d);
        }
    }
    // Remove unreferenced vertices inside holes.
    mesh = new DMesh3(mesh, true);
    foreach (int v in mesh.VertexIndices().ToArray()) {
        if (mesh.GetVtxEdgeCount(v) == 0)
            mesh.RemoveVertex(v, false, false);
    }
    return new DMesh3(mesh, true);
}

static void Demand(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

Console.WriteLine("=== Part 1: Full CETM + Ptolemy Intrinsic Delaunay Solver vs LSCM and Spectral ===");
Console.WriteLine("case | mode | LSCM (flips+deg) | Spectral (status/flips) | CETM conv/iters | init+solve flips | maxAngleRes | rotHol(rad) | transHol(rel) | inputUV flips+deg | bndContacts | meanConf | nonEucl iters/tris | armijo+fallback | ms");

var cases = new (string Label, DMesh3 Mesh, DiscreteConformalSolver.BoundaryMode Mode)[] {
    ("P1 cyl 8x3 h=1.8", MakeCylinder(8, 3, 1.8), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("P1 cyl 8x3 h=3.6", MakeCylinder(8, 3, 3.6), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("P1 cyl 24x6 h=1.8", MakeCylinder(24, 6, 1.8), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("P1 cyl 48x12 h=3.6", MakeCylinder(48, 12, 3.6), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("z-jit10% 8x3 h=1.8", MakeCylinder(8, 3, 1.8, zJitterFrac: 0.10, seed: 42), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("z-jit10% 8x3 h=3.6", MakeCylinder(8, 3, 3.6, zJitterFrac: 0.10, seed: 42), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("z-jit10% 24x6 h=1.8", MakeCylinder(24, 6, 1.8, zJitterFrac: 0.10, seed: 42), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("z-jit10% 24x6 h=3.6", MakeCylinder(24, 6, 3.6, zJitterFrac: 0.10, seed: 42), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("3D-jit 24x6 h=1.8", MakeCylinder(24, 6, 1.8, zJitterFrac: 0.10, radialJitterFrac: 0.05, sharedAngleJitter: 0.05, seed: 42), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("frustum 24x6 r=0.5", MakeCylinder(24, 6, 1.8, topRadius: 0.5), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("pinched waist 24x8", MakeCylinder(24, 8, 2.4, waistScale: 0.45), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("twisted cyl 16x6", MakeCylinder(16, 6, 2.4, twistRad: 1.5 * Math.PI), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("bent banana 16x8 70deg", MakeCylinder(16, 8, 2.4, bendAngleRad: 70.0 * Math.PI / 180.0), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("bent banana (fixed u_b=0)", MakeCylinder(16, 8, 2.4, bendAngleRad: 70.0 * Math.PI / 180.0), DiscreteConformalSolver.BoundaryMode.FixedBoundaryScale),
    ("sharp cone h=4 (disk)", MakeHighCurvatureCone(24, 8, 4.0, jitter: 0.05), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    ("sharp cone (fixed u_b=0)", MakeHighCurvatureCone(24, 8, 4.0, jitter: 0.05), DiscreteConformalSolver.BoundaryMode.FixedBoundaryScale),
    ("2-hole bumped plate", MakeTwoHolePlate(20, 12, 0.5), DiscreteConformalSolver.BoundaryMode.UniformLoopTurning),
    // Second boundary-curvature choice (turning by input arc length) on the cases the uniform turning leaves open,
    // plus two symmetric controls.
    ("P1 cyl 24x6 h=1.8", MakeCylinder(24, 6, 1.8), DiscreteConformalSolver.BoundaryMode.ArcLengthLoopTurning),
    ("pinched waist 24x8", MakeCylinder(24, 8, 2.4, waistScale: 0.45), DiscreteConformalSolver.BoundaryMode.ArcLengthLoopTurning),
    ("z-jit10% 8x3 h=1.8", MakeCylinder(8, 3, 1.8, zJitterFrac: 0.10, seed: 42), DiscreteConformalSolver.BoundaryMode.ArcLengthLoopTurning),
    ("z-jit10% 8x3 h=3.6", MakeCylinder(8, 3, 3.6, zJitterFrac: 0.10, seed: 42), DiscreteConformalSolver.BoundaryMode.ArcLengthLoopTurning),
    ("z-jit10% 24x6 h=1.8", MakeCylinder(24, 6, 1.8, zJitterFrac: 0.10, seed: 42), DiscreteConformalSolver.BoundaryMode.ArcLengthLoopTurning),
    ("z-jit10% 24x6 h=3.6", MakeCylinder(24, 6, 3.6, zJitterFrac: 0.10, seed: 42), DiscreteConformalSolver.BoundaryMode.ArcLengthLoopTurning),
    ("3D-jit 24x6 h=1.8", MakeCylinder(24, 6, 1.8, zJitterFrac: 0.10, radialJitterFrac: 0.05, sharedAngleJitter: 0.05, seed: 42), DiscreteConformalSolver.BoundaryMode.ArcLengthLoopTurning),
    ("bent banana 16x8 70deg", MakeCylinder(16, 8, 2.4, bendAngleRad: 70.0 * Math.PI / 180.0), DiscreteConformalSolver.BoundaryMode.ArcLengthLoopTurning),
    ("2-hole bumped plate", MakeTwoHolePlate(20, 12, 0.5), DiscreteConformalSolver.BoundaryMode.ArcLengthLoopTurning),
};

int totalSignDisagreements = 0;
int totalNonEuclideanIterates = 0;
int totalNonEuclideanTris = 0;
int totalSolveFlips = 0;
int totalArmijo = 0, totalFallback = 0, totalNegInterior = 0, totalNegBoundary = 0;
double mostNegInterior = 0, mostNegBoundary = 0;

foreach (var (label, mesh, mode) in cases) {
    var lscm = new LSCMParameterization(mesh).Compute();
    string lscmStr = lscm.Distortion == null
        ? lscm.Status.ToString()
        : $"{lscm.Status}({lscm.Distortion.FlippedTriangles}+{lscm.Distortion.DegenerateUVTriangles})";

    var specOp = new SpectralConformalParameterization(mesh);
    var spec = specOp.Compute();
    string specStr = spec.Distortion == null
        ? (specOp.NearlyDoubleEigenvalue ? "Refused(double)" : spec.Status.ToString())
        : $"{spec.Status}({spec.Distortion.FlippedTriangles}+{spec.Distortion.DegenerateUVTriangles})";

    var solver = DiscreteConformalSolver.FromMesh(mesh);
    var r = solver.Solve(mode);

    totalSignDisagreements += r.CotanVsHyperbolicSignDisagreements;
    totalNonEuclideanIterates += r.AcceptedIteratesNonEuclideanBeforeFlip;
    totalNonEuclideanTris += r.NonEuclideanTrianglesAtAcceptedIterates;
    totalSolveFlips += r.SolveFlips;
    totalArmijo += r.ArmijoAcceptances;
    totalFallback += r.RoundoffFallbackAcceptances;
    totalNegInterior += r.NegativeInteriorHessianWeights;
    totalNegBoundary += r.NegativeBoundaryHessianWeights;
    mostNegInterior = Math.Min(mostNegInterior, r.MostNegativeInteriorHessianWeight);
    mostNegBoundary = Math.Min(mostNegBoundary, r.MostNegativeBoundaryHessianWeight);

    // Input-mesh check, measured the same way as LSCM: the seamless per-vertex UV evaluated on the INPUT triangles
    // (linear per triangle). After intrinsic flips the input edges are not straight in the flat metric, so this is
    // what a per-vertex UV export of the chart would give; with holonomy there is no single-valued vertex UV.
    string inputUvStr = "n/a (hol)";
    if (r.SeamlessVertexUV != null) {
        var uv = new Vector2d[mesh.MaxVertexID];
        for (int i = 0; i < r.SeamlessVertexUV.Length; ++i)
            uv[solver.InputVertexID(i)] = r.SeamlessVertexUV[i];
        var dist = ParameterizationDistortion.Compute(mesh, uv);
        inputUvStr = $"{dist.FlippedTriangles}+{dist.DegenerateUVTriangles}";
    }

    string modeTag = mode switch {
        DiscreteConformalSolver.BoundaryMode.UniformLoopTurning => "turn",
        DiscreteConformalSolver.BoundaryMode.ArcLengthLoopTurning => "turn-len",
        _ => "u_b=0",
    };
    string convStr = r.Converged ? $"yes/{r.NewtonIterations}" : $"NO/{r.NewtonIterations}";
    string bndStr = r.NonAdjacentBoundaryContacts < 0 ? "open(hol)" : r.NonAdjacentBoundaryContacts.ToString();
    Console.WriteLine(
        $"{label} | {modeTag} | {lscmStr} | {specStr} | {convStr} | {r.InitialFlips}+{r.SolveFlips} | " +
        $"{r.MaxAngleResidual:E2} | {r.MaxRotationalHolonomyRad:E2} | {r.MaxTranslationalHolonomyRel:E2} | " +
        $"{inputUvStr} | {bndStr} | {r.MeanIntrinsicConformalDistortion:F4} | " +
        $"{r.AcceptedIteratesNonEuclideanBeforeFlip}/{r.NonEuclideanTrianglesAtAcceptedIterates} | {r.ArmijoAcceptances}+{r.RoundoffFallbackAcceptances} | {r.ElapsedMs:F1}");

    // Layout guards: the unfolded intrinsic triangles are CCW by construction (ThirdVertexCCW), so the flipped
    // count only guards the layout code; non-degeneracy (relative) and the edge-length match are real checks.
    if (r.Converged) {
        Demand(r.FlippedIntrinsicTriangles == 0 && r.DegenerateIntrinsicTriangles == 0,
            $"{label}: converged CETM produced flipped/degenerate intrinsic triangles");
        Demand(r.MaxEdgeScaleResidual < 1e-11,
            $"{label}: edge scale residual {r.MaxEdgeScaleResidual} too large");
    }
}

Console.WriteLine($"Summary across all {cases.Length} solver runs: totalSolveFlips={totalSolveFlips}, " +
    $"acceptedIteratesNonEuclideanBeforeFlip={totalNonEuclideanIterates} (triangles {totalNonEuclideanTris}), " +
    $"armijo={totalArmijo}, roundoffFallback={totalFallback}, negativeHessianWeights (not clamped) interior={totalNegInterior} (min {mostNegInterior:E2}) boundary={totalNegBoundary} (min {mostNegBoundary:E2}), " +
    $"cotanVsHypDisagreements={totalSignDisagreements} (consistency check)");

Console.WriteLine();
Console.WriteLine("=== Part 2: Exact vs Double Delaunay Flip Predicates (InCircle, CotanSum, Exact Hyperbolic Polynomial) ===");
Console.WriteLine("diamond | exact InCircle | exact HypPoly | double HypPoly | double cotSum | library IsDelaunay | Euclidean len | Ptolemy len");
foreach (var (label, d, expectedSign) in new[] {
    ("cyclic", new Vector2d(1, 0), 0),
    ("near inside (2^-54)", new Vector2d(1, Math.ScaleB(1, -54)), 1),
    ("near outside (-2^-54)", new Vector2d(1, -Math.ScaleB(1, -54)), -1),
    ("near inside (2^-40)", new Vector2d(1, Math.ScaleB(1, -40)), 1),
    ("near outside (-2^-40)", new Vector2d(1, -Math.ScaleB(1, -40)), -1),
    ("clearly inside", new Vector2d(1, 0.25), 1),
    ("noncyclic", new Vector2d(1.25, 0), -1),
}) {
    Vector2d a = Vector2d.Zero, b = new Vector2d(1, 1), c = new Vector2d(0, 1);
    var mesh = new DMesh3();
    foreach (var p in new[] { a, b, c, d })
        mesh.AppendVertex(new Vector3d(p.x, p.y, 0));
    mesh.AppendTriangle(0, 1, 2);
    mesh.AppendTriangle(1, 0, 3);
    var intrinsic = new IntrinsicTriangulation(mesh, mollifyFactor: 0);
    int h = Enumerable.Range(0, intrinsic.HalfedgeCount)
        .Single(i => intrinsic.VertexID(intrinsic.Tail(i)) == 0 && intrinsic.VertexID(intrinsic.Head(i)) == 1);
    int exactInCircle = ExactPredicates.InCircle(a, b, c, d);
    Demand(exactInCircle == expectedSign, $"{label}: unexpected incircle sign {exactInCircle}");

    double la = a.Distance(c), lb = c.Distance(b), lc = b.Distance(d), ld = d.Distance(a), le = a.Distance(b);
    int exactHyp = DiscreteConformalSolver.ExactHyperbolicDelaunaySign(la, lb, lc, ld, le);
    double doubleHyp = (la * la + lb * lb - le * le) / (la * lb) + (lc * lc + ld * ld - le * le) / (lc * ld);
    double cot = intrinsic.OppositeCotan(h) + intrinsic.OppositeCotan(intrinsic.Twin(h));
    bool delaunay = intrinsic.IsDelaunay(h);
    double ptolemy = (la * lc + lb * ld) / le;
    Demand(intrinsic.FlipEdge(h), $"{label}: library refused convex diamond");
    double euclidean = intrinsic.Length(h);
    Console.WriteLine($"{label} | {exactInCircle} | {exactHyp} | {doubleHyp:G17} | {cot:G17} | {delaunay} | {euclidean:G17} | {ptolemy:G17}");
}
