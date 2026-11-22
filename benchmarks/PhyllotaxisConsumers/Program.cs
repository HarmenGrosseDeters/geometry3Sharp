using g3;
using System.Text.Json;

var data = JsonSerializer.Deserialize<Input>(Console.In.ReadToEnd());
var disc = data.Disc.Select(p => new Vector2d(p[0], p[1])).ToArray();
var result = new DelaunayMesh2().Compute(disc);
if (result.Status != Triangulation2Status.Success || !result.Mesh.IsValid(out _))
    throw new Exception("Disc triangulation failed verification");
if (Enumerable.Range(0, disc.Length).Any(i => !result.Mesh.IncidentTriangles(i).Any()))
    throw new Exception("Unused disc site");
// The fourth site crosses the unit circle by one representable step.
foreach (double radius in new[] { Math.BitDecrement(1d), 1d, Math.BitIncrement(1d) }) {
    Vector2d[] quartet = [new(1, 0), new(0, 1), new(-1, 0), new(0, -radius)];
    var quad = new DelaunayMesh2().Compute(quartet).Mesh;
    bool horizontal = quad.Edges().Any(e => e.a == 0 && e.b == 2);
    if (!quad.IsValid(out _) || quad.TriangleCount != 2 || horizontal != (radius >= 1))
        throw new Exception("Incorrect near-cocircular diagonal or tie");
}
var edges = result.Mesh.Edges().Select(e => disc[e.a].Distance(disc[e.b])).ToArray();
foreach (bool guard in new[] { false, true })
    MeasureRemesh(data, guard);
Console.WriteLine($"discTriangles={result.Mesh.TriangleCount} shortestEdge={edges.Min():F5}");

static void MeasureRemesh(Input data, bool guard) {
    var mesh = new DMesh3();
    foreach (var p in data.Sphere) mesh.AppendVertex(new Vector3d(p[0], p[1], p[2]));
    foreach (var f in data.Faces)
        if (mesh.AppendTriangle(f[0], f[1], f[2]) < 0) throw new Exception("Invalid hull");
    var remesher = new Remesher(mesh) { PreventNormalFlips = guard, EnableParallelSmooth = false, EnableParallelProjection = false };
    remesher.SetTargetEdgeLength(Math.Sqrt(8*Math.PI/(Math.Sqrt(3)*data.Sphere.Length)));
    remesher.SetProjectionTarget(new UnitSphere());
    for (int pass = 0; pass <= 5; pass++) {
        if (pass > 0) remesher.BasicRemeshPass();
        if (!mesh.CheckValidity() || !mesh.IsClosed() || mesh.VertexCount-mesh.EdgeCount+mesh.TriangleCount != 2)
            throw new Exception("Invalid sphere topology");
        int inverted = 0;
        foreach (var id in mesh.TriangleIndices()) {
            var face = mesh.GetTriangle(id);
            if (!(mesh.GetTriNormal(id).Dot(mesh.GetVertex(face.a)) > 0))
                inverted++;
        }
        var lengths = mesh.EdgeIndices().Select(i => {
            var e = mesh.GetEdgeV(i);
            return mesh.GetVertex(e.a).Distance(mesh.GetVertex(e.b));
        }).ToArray();
        {
            double mean = lengths.Average();
            double cv = Math.Sqrt(lengths.Average(x => Math.Pow(x/mean-1, 2)));
            double drift = mesh.VertexIndices().Max(i => Math.Abs(mesh.GetVertex(i).Length-1));
            Console.WriteLine($"guard={guard} pass={pass} inverted={inverted} vertices={mesh.VertexCount} edgeCV={cv:F4} radiusDrift={drift:G3}");
        }
    }
}
record Input(double[][] Disc, double[][] Sphere, int[][] Faces);
class UnitSphere : IProjectionTarget {
    public Vector3d Project(Vector3d point, int identifier = -1) => point.Normalized;
}
