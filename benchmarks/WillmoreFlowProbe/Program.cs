using System;
using System.Diagnostics;
using g3;
using gs;

// Unit tube radius. The two angles and winding match the MeshCurvatureTests torus.
static DMesh3 Torus(double major, int nu, int nv, double warp = 0)
{
    var mesh = new DMesh3();
    for (int i = 0; i < nu; ++i) {
        double u = 2 * Math.PI * i / nu;
        u += warp * Math.Sin(u);
        for (int j = 0; j < nv; ++j) {
            double v = 2 * Math.PI * j / nv, rho = major + Math.Cos(v);
            mesh.AppendVertex(new Vector3d(rho * Math.Cos(u), rho * Math.Sin(u), Math.Sin(v)));
        }
    }
    for (int i = 0; i < nu; ++i) {
        for (int j = 0; j < nv; ++j) {
            int a = i * nv + j, b = ((i + 1) % nu) * nv + j;
            int c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, d);
        }
    }
    return mesh;
}

static double Energy(DMesh3 mesh)
{
    var curvature = MeshCurvature.Compute(mesh);
    if (curvature.ValidVertices != mesh.VertexCount)
        throw new InvalidOperationException("Invalid curvature sample.");
    double sum = 0;
    foreach (int v in mesh.VertexIndices())
        sum += curvature.MixedArea[v] * curvature.Mean[v] * curvature.Mean[v];
    if (!double.IsFinite(sum)) throw new InvalidOperationException("Invalid bending energy.");
    return sum;
}

static void CheckTorus(DMesh3 mesh)
{
    var topology = MeshTopologySummary.Compute(mesh);
    string defect = MeshRobustRepair.NotCleanReason(mesh, checkSelfIntersections: true);
    if (defect.Length != 0 || topology.Components != 1 || topology.Genus != 1)
        throw new InvalidOperationException($"Invalid torus: {defect}, genus {topology.Genus}.");
}

if (Array.IndexOf(args, "--acceptance") >= 0) {
    bool timings = Array.IndexOf(args, "--timings") >= 0;
    if (timings) CompareAcceptance(0.8, "budget", report: false);
    Console.WriteLine("warp policy proposed-R accepted-R previous moved full smooth accepted energy min-quality");
    foreach (double warp in new[] { 0.0, 0.8 })
        foreach (string policy in new[] { "baseline", "strict", "budget", "smooth-only" }) {
            var clock = Stopwatch.StartNew();
            CompareAcceptance(warp, policy);
            if (timings)
                Console.WriteLine($"elapsed-ms {warp:G2} {policy} {clock.Elapsed.TotalMilliseconds:F2}");
        }
    return;
}

foreach (int nv in new[] { 12, 24, 48 }) {
    foreach (double major in new[] { Math.Sqrt(2), 3.0 }) {
        double measured = Energy(Torus(major, 2 * nv, nv));
        double exact = Math.PI * Math.PI * major * major / Math.Sqrt(major * major - 1);
        Console.WriteLine($"oracle {nv} {major:G10} {measured:G10} {exact:G10} {Math.Abs(measured-exact):G8}");
    }
}

// Descend the measured energy on the one-parameter structured family.
// Apply each accepted change to a second mesh whose connectivity evolves.
Console.WriteLine("motion guard step radius delta structured-energy before after vertices pairs rejected");
foreach (bool motion in new[] { false, true })
    foreach (bool guard in new[] { false, true })
        RunPasses(motion, guard);

static void RunPasses(bool motion, bool guard)
{
    double radius = 3.0;
    double Structured(double r) => Energy(Torus(r, 48, 24));
    var moving = Torus(radius, 48, 24);
    CheckTorus(moving);
    var surface = new RingSurface { Major = radius };
    var remesher = new Remesher(moving) { EnableParallelSmooth = false, EnableParallelProjection = false };
    remesher.PreventSelfIntersections = guard;
    remesher.SetProjectionTarget(surface);
    remesher.SetTargetEdgeLength(0.25);
    for (int step = 1; step <= (motion ? 4 : 1); ++step) {
        double delta = 0;
        if (motion) {
            const double epsilon = 1e-4;
            double slope = (Structured(radius + epsilon) - Structured(radius - epsilon)) / (2 * epsilon);
            delta = -Math.Sign(slope) * 0.75;
            double previous = Structured(radius);
            while (radius + delta <= 1 || Structured(radius + delta) >= previous) {
                delta *= 0.5;
                if (Math.Abs(delta) < 1e-8) throw new InvalidOperationException("Descent stalled.");
            }
        }
        foreach (int v in moving.VertexIndices()) {
            Vector3d p = moving.GetVertex(v);
            moving.SetVertex(v, p + delta * new Vector3d(p.x, p.y, 0).Normalized);
        }
        radius += delta;
        surface.Major = radius;
        CheckTorus(moving);
        double before = Energy(moving);
        remesher.BasicRemeshPass();
        int pairs = MeshRepairMetrics.SelfIntersections(moving);
        Console.WriteLine($"{motion} {guard} {step} {radius:G10} {delta:G10} {Structured(radius):G10} {before:G10} " +
            $"{Energy(moving):G10} {moving.VertexCount} {pairs} {remesher.SelfIntersectionsRejected}");
        if (pairs > 0 && !guard) break;
        CheckTorus(moving);
    }
}

// Replay the four radius proposals measured above for a paired policy comparison.
static void CompareAcceptance(double warp, string policy, bool report = true)
{
    double radius = 3;
    var current = Torus(radius, 48, 24, warp);
    foreach (double next in new[] { 2.25, 1.5, 1.40625, 1.400390625 }) {
        var moved = new DMesh3(current);
        foreach (int v in moved.VertexIndices()) {
            var p = moved.GetVertex(v);
            moved.SetVertex(v, p + (next - radius) * new Vector3d(p.x, p.y, 0).Normalized);
        }
        CheckTorus(moved);
        double previous = Energy(current), before = Energy(moved);
        var selected = moved;
        double selectedEnergy = before, quality = WorstQuality(moved);
        double full = double.NaN, smooth = double.NaN;
        string choice = "move";
        if (policy != "baseline") {
            foreach (bool connectivity in new[] { true, false }) {
                if (connectivity && policy == "smooth-only") continue;
                // Each proposal starts from the same clean moved mesh; rejection is lossless.
                var trial = new DMesh3(moved);
                var remesher = new Remesher(trial) {
                    PreventSelfIntersections = true, EnableParallelSmooth = false, EnableParallelProjection = false,
                    EnableFlips = connectivity, EnableSplits = connectivity, EnableCollapses = connectivity
                };
                remesher.SetProjectionTarget(new RingSurface { Major = next });
                remesher.SetTargetEdgeLength(0.25);
                remesher.BasicRemeshPass();
                CheckTorus(trial);
                double energy = Energy(trial), trialQuality = WorstQuality(trial);
                if (connectivity) full = energy;
                else smooth = energy;
                double ceiling = policy == "strict" ? Math.Min(previous, before) : previous;
                if (energy <= ceiling && trialQuality > quality) {
                    selected = trial;
                    selectedEnergy = energy;
                    quality = trialQuality;
                    choice = connectivity ? "full" : "smooth";
                    break;
                }
            }
        }
        if (selectedEnergy > previous) {
            selected = current;
            selectedEnergy = previous;
            quality = WorstQuality(current);
            choice = "hold";
        } else {
            radius = next;
        }
        current = selected;
        if (report) Console.WriteLine($"{warp:G2} {policy} {next:G10} {radius:G10} " +
            $"{previous:G10} {before:G10} {full:G10} {smooth:G10} {choice} {selectedEnergy:G10} {quality:G9}");
    }
}

static double WorstQuality(DMesh3 mesh)
{
    double minimum = 1;
    foreach (int t in mesh.TriangleIndices()) {
        var f = mesh.GetTriangle(t);
        Vector3d a = mesh.GetVertex(f.a), b = mesh.GetVertex(f.b), c = mesh.GetVertex(f.c);
        double q = MeshRefinerBase.TriangleQuality(ref a, ref b, ref c);
        minimum = Math.Min(minimum, q);
    }
    return minimum;
}

internal sealed class RingSurface : IProjectionTarget
{
    public double Major;
    public Vector3d Project(Vector3d point, int identifier = -1)
    {
        Vector3d center = Major * new Vector3d(point.x, point.y, 0).Normalized;
        return center + (point - center).Normalized;
    }
}
