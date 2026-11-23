// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using g3;
using gs;

static (DMesh3 Mesh, Vector2d[] Uv, double[] LogScale) Cylinder(int sides, int strips, double height, double jitter)
{
    var mesh = new DMesh3();
    var uv = new Vector2d[sides * (strips + 1)];
    var logScale = new double[uv.Length];
    double dz = height / strips;
    double step = -2 * Math.Asinh(dz / 2);
    for (int ring = 0; ring <= strips; ++ring) {
        double u = step * ring;
        for (int i = 0; i < sides; ++i) {
            double angle = 2 * Math.PI * i / sides + jitter * Math.Sin(3 * i + 1);
            var direction = new Vector2d(Math.Cos(angle), Math.Sin(angle));
            int v = mesh.AppendVertex(new Vector3d(direction.x, direction.y, dz * ring));
            uv[v] = Math.Exp(u) * direction;
            logScale[v] = u;
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
    return (mesh, uv, logScale);
}

static int BoundaryContacts(DMesh3 mesh, Vector2d[] uv)
{
    var edges = mesh.BoundaryEdgeIndices().Select(mesh.GetEdgeV).ToArray();
    int count = 0;
    for (int i = 0; i < edges.Length; ++i) {
        var e = edges[i];
        for (int j = i + 1; j < edges.Length; ++j) {
            var f = edges[j];
            if (e.a == f.a || e.a == f.b || e.b == f.a || e.b == f.b)
                continue;
            if (Intersects(uv[e.a], uv[e.b], uv[f.a], uv[f.b]))
                ++count;
        }
    }
    return count;
}

static bool Intersects(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
{
    static bool Between(Vector2d p, Vector2d x, Vector2d y) =>
        Math.Min(x.x, y.x) <= p.x && p.x <= Math.Max(x.x, y.x)
        && Math.Min(x.y, y.y) <= p.y && p.y <= Math.Max(x.y, y.y);
    int abc = ExactPredicates.Orient2d(a, b, c), abd = ExactPredicates.Orient2d(a, b, d);
    int cda = ExactPredicates.Orient2d(c, d, a), cdb = ExactPredicates.Orient2d(c, d, b);
    return (abc * abd < 0 && cda * cdb < 0)
        || (abc == 0 && Between(c, a, b)) || (abd == 0 && Between(d, a, b))
        || (cda == 0 && Between(a, c, d)) || (cdb == 0 && Between(b, c, d));
}

static double MaxEdgeResidual(DMesh3 mesh, Vector2d[] uv, double[] u)
{
    double max = 0;
    foreach (int eid in mesh.EdgeIndices()) {
        var e = mesh.GetEdgeV(eid);
        double expected = mesh.GetVertex(e.a).Distance(mesh.GetVertex(e.b)) * Math.Exp((u[e.a] + u[e.b]) / 2);
        max = Math.Max(max, Math.Abs(uv[e.a].Distance(uv[e.b]) / expected - 1));
    }
    return max;
}

static void Demand(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

Console.WriteLine("chart | method/status | flipped + degenerate | nonadjacent boundary contacts | mean conformal distortion");
foreach (var (label, sides, strips, height, jitter) in new[] {
    ("P1", 8, 3, 1.8, 0.0),
    ("tall", 8, 3, 3.6, 0.0),
    ("refined", 24, 6, 1.8, 0.0),
    ("shared angular jitter", 8, 3, 1.8, 0.01),
}) {
    var (mesh, uv, scale) = Cylinder(sides, strips, height, jitter);
    var intrinsic = new IntrinsicTriangulation(mesh, mollifyFactor: 0);
    int flips = intrinsic.FlipToDelaunay();
    Demand(flips == 0 && intrinsic.IsDelaunay(), $"{label}: expected the original cylinder metric to be Delaunay");
    var lscm = new LSCMParameterization(mesh).Compute();
    var distortion = lscm.Distortion;
    string contacts = lscm.UV == null ? "-" : BoundaryContacts(mesh, lscm.UV).ToString();
    Console.WriteLine($"{label} | LSCM/{lscm.Status} | {distortion?.FlippedTriangles} + {distortion?.DegenerateUVTriangles} | {contacts} | {distortion?.MeanConformal:G6}");
    var reference = ParameterizationDistortion.Compute(mesh, uv);
    int referenceContacts = BoundaryContacts(mesh, uv);
    double residual = MaxEdgeResidual(mesh, uv, scale);
    Console.WriteLine($"{label} | vertex scaling | {reference.FlippedTriangles} + {reference.DegenerateUVTriangles} | {referenceContacts} | {reference.MeanConformal:G6} | edge residual {residual:G6} | initial Delaunay flips {flips}");
    Demand(reference.Triangles == mesh.TriangleCount && reference.FlippedTriangles == 0
        && reference.DegenerateUVTriangles == 0 && referenceContacts == 0 && residual < 1e-12,
        $"{label}: invalid discrete conformal reference");
}

Console.WriteLine("diamond | exact incircle | cotangent sum | library Delaunay | Euclidean new length | Ptolemy new length");
foreach (var (label, d, expectedSign) in new[] {
    ("cyclic", new Vector2d(1, 0), 0),
    ("near inside", new Vector2d(1, Math.ScaleB(1, -54)), 1),
    ("near outside", new Vector2d(1, -Math.ScaleB(1, -54)), -1),
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
    int exact = ExactPredicates.InCircle(a, b, c, d);
    Demand(exact == expectedSign,
        $"{label}: unexpected incircle sign {exact}");
    double cot = intrinsic.OppositeCotan(h) + intrinsic.OppositeCotan(intrinsic.Twin(h));
    bool delaunay = intrinsic.IsDelaunay(h);
    double ptolemy = (a.Distance(c) * b.Distance(d) + a.Distance(d) * b.Distance(c)) / a.Distance(b);
    Demand(intrinsic.FlipEdge(h), $"{label}: library refused convex diamond");
    double euclidean = intrinsic.Length(h);
    Console.WriteLine($"{label} | {exact} | {cot:G17} | {delaunay} | {euclidean:G17} | {ptolemy:G17}");
    Demand(Math.Abs(euclidean - c.Distance(d)) < 1e-12, $"{label}: unexpected Euclidean flip length");
    if (label == "cyclic")
        Demand(exact == 0 && Math.Abs(euclidean - ptolemy) < 1e-12, "Cyclic flip should agree");
    if (label == "noncyclic")
        Demand(exact != 0 && Math.Abs(euclidean - ptolemy) > 1e-4, "Noncyclic flip should differ");
}
