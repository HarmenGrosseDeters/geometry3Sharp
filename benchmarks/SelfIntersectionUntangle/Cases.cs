using System;
using System.Collections.Generic;
using g3;

// The procedural self-intersecting solids of the untangling spike. Every case is one closed, consistently oriented mesh whose
// only defect is that it intersects itself. Intent, where it is not null, is the clean mesh with the same connectivity
// that the defect was injected into (vertices displaced), so "distance to the intent" is defined for every method.
sealed class UCase
{
    public string Class, Name;
    public Func<DMesh3> Given;
    public Func<DMesh3> Intent;
    public string Note = "";
}

static class UCases
{
    static DMesh3 Move(DMesh3 m, Func<int, Vector3d, Vector3d> f)
    {
        var c = new DMesh3(m, false, MeshComponents.None);
        foreach (int v in c.VertexIndices())
            c.SetVertex(v, f(v, c.GetVertex(v)));
        return c;
    }

    static DMesh3 Boolean(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op)
    {
        var x = new ExactMeshBoolean(a, b, op);
        if (!x.Compute()) throw new Exception("corpus boolean failed: " + x.Status);
        return x.Result;
    }

    // a capped tube around a helix of radius R and the given pitch: neighbouring turns interpenetrate if pitch < 2 r
    public static DMesh3 HelixTube(double R, double r, double pitch, double turns, int nu, int nv)
    {
        var m = new DMesh3();
        for (int i = 0; i <= nu; ++i) {
            double u = 2 * Math.PI * turns * i / nu;
            var er = new Vector3d(Math.Cos(u), Math.Sin(u), 0);
            Vector3d c = R * er + new Vector3d(0, 0, pitch * u / (2 * Math.PI));
            for (int j = 0; j < nv; ++j) {
                double v = 2 * Math.PI * j / nv;
                m.AppendVertex(c + r * (Math.Cos(v) * er + Math.Sin(v) * Vector3d.AxisZ));
            }
        }
        for (int i = 0; i < nu; ++i)
            for (int j = 0; j < nv; ++j) {
                int a = i * nv + j, b = (i + 1) * nv + j, cc = (i + 1) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                m.AppendTriangle(a, b, cc); m.AppendTriangle(a, cc, d);
            }
        for (int end = 0; end < 2; ++end) {
            int row = end == 0 ? 0 : nu;
            Vector3d s = Vector3d.Zero;
            for (int j = 0; j < nv; ++j) s += m.GetVertex(row * nv + j);
            int cv = m.AppendVertex(s / nv);
            for (int j = 0; j < nv; ++j) {
                int a = row * nv + j, b = row * nv + (j + 1) % nv;
                if (end == 0) m.AppendTriangle(cv, a, b); else m.AppendTriangle(cv, b, a);
            }
        }
        if (MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x < 0) m.ReverseOrientation();
        return m;
    }

    static DMesh3 Sphere(int n) { return Corpus.Sphere(Vector3d.Zero, 1, n); }
    static DMesh3 Plate(int n, double h) { return Corpus.FineBox(new Vector3d(0, 0, 0), new Vector3d(1, 1, 0.5 * h), n); }

    // the top of the plate pushed down by depth in a smooth bump of the given radius around the centre
    static DMesh3 PushedPlate(int n, double h, double depth, double radius)
    {
        return Move(Plate(n, h), (v, p) => {
            if (p.z < 0) return p;
            double d = Math.Sqrt(p.x * p.x + p.y * p.y) / radius;
            return d >= 1 ? p : p - new Vector3d(0, 0, depth * 0.5 * (1 + Math.Cos(Math.PI * d)));
        });
    }

    // the cap of the unit sphere around +z pushed down by depth (smooth), through the opposite side if depth > 2
    static DMesh3 PushedSphere(int n, double depth, double capAngle)
    {
        return Move(Sphere(n), (v, p) => {
            double ang = Math.Acos(Math.Max(-1, Math.Min(1, p.z / p.Length))) / capAngle;
            return ang >= 1 ? p : p - new Vector3d(0, 0, depth * 0.5 * (1 + Math.Cos(Math.PI * ang)));
        });
    }

    // a share of the vertices moved tangentially by amplitude * (mean edge) in a seeded random direction
    static DMesh3 TangentNoise(DMesh3 m, double share, double amplitude, int seed)
    {
        var rnd = new Random(seed);
        double edge = 0;
        foreach (int e in m.EdgeIndices()) { Index2i ev = m.GetEdgeV(e); edge += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); }
        edge /= m.EdgeCount;
        return Move(m, (v, p) => {
            double pick = rnd.NextDouble(), a = rnd.NextDouble() * 2 * Math.PI;
            if (pick >= share) return p;
            Vector3d n = p.Normalized, t1 = n.Cross(Math.Abs(n.x) < 0.9 ? Vector3d.AxisX : Vector3d.AxisY).Normalized, t2 = n.Cross(t1);
            return p + amplitude * edge * (Math.Cos(a) * t1 + Math.Sin(a) * t2);
        });
    }

    static DMesh3 Pocket() { return Boolean(Corpus.Cube(0, 4), Corpus.Box(1, 1, 1, 3, 3, 5), ExactMeshBoolean.Operations.Difference); }
    static DMesh3 Boss() { return Boolean(Corpus.Cube(0, 4), Corpus.Box(1, 1, 3, 3, 3, 5), ExactMeshBoolean.Operations.Union); }
    static DMesh3 Bracket() { return Boolean(Corpus.Box(0, 0, 0, 4, 4, 1), Corpus.Box(0, 0, 0, 1, 4, 4), ExactMeshBoolean.Operations.Union); }

    public static List<UCase> Cases()
    {
        var L = new List<UCase>();
        Action<string, string, Func<DMesh3>, Func<DMesh3>, string> add = (cls, name, given, intent, note) =>
            L.Add(new UCase { Class = cls, Name = name, Given = given, Intent = intent, Note = note ?? "" });

        // ---- interpenetrating parts of one mesh ----
        add("parts", "two boxes overlapping by half (components)", () => Corpus.Combine(Corpus.Cube(0, 2), Corpus.Cube(1, 3)), null, null);
        add("parts", "two boxes overlapping by 1e-6 (components)", () => Corpus.Combine(Corpus.Cube(0, 2), Corpus.Box(0.3, 0.2, 2 - 1e-6, 1.7, 1.6, 3)),
            () => Corpus.Combine(Corpus.Cube(0, 2), Corpus.Box(0.3, 0.2, 2 + 1e-6, 1.7, 1.6, 3)), "intent: the boxes just apart");
        add("parts", "two spheres, penetration 0.05 r (components)", () => Corpus.Combine(Corpus.Sphere(Vector3d.Zero, 1, 12), Corpus.Sphere(new Vector3d(1.95, 0.1, 0.05), 1, 12)), null, null);
        add("parts", "two spheres, penetration 0.8 r (components)", () => Corpus.Combine(Corpus.Sphere(Vector3d.Zero, 1, 12), Corpus.Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 12)), null, null);
        add("parts", "helix tube, turns overlap by 0.1 r (connected)", () => HelixTube(1, 0.2, 0.38, 1.5, 96, 16), () => HelixTube(1, 0.2, 0.42, 1.5, 96, 16), "intent: the pitch that just clears");
        add("parts", "helix tube, turns overlap by 1.0 r (connected)", () => HelixTube(1, 0.2, 0.2, 1.5, 96, 16), () => HelixTube(1, 0.2, 0.42, 1.5, 96, 16), "intent: the pitch that just clears");

        // ---- pushed-through regions of a fine mesh ----
        add("pushed", "sphere cap pushed through the far side by 0.2 r", () => PushedSphere(16, 2.2, 0.5), () => Sphere(16), null);
        add("pushed", "plate 0.05 thick, bump pushed 0.06 (through by 0.01)", () => PushedPlate(17, 0.05, 0.06, 0.5), () => Plate(17, 0.05), null);
        add("pushed", "plate 0.05 thick, bump pushed 0.3 (deep)", () => PushedPlate(17, 0.05, 0.3, 0.5), () => Plate(17, 0.05), null);

        // ---- folded flaps ----
        add("flap", "sphere, one vertex moved 1.5 edges tangentially", () => TangentNoise(Sphere(12), 0.004, 1.5, 3), () => Sphere(12), null);
        add("flap", "sphere, 10 % of the vertices moved 0.8 edges", () => TangentNoise(Sphere(12), 0.1, 0.8, 5), () => Sphere(12), null);
        add("flap", "sphere, 10 % of the vertices moved 1.5 edges", () => TangentNoise(Sphere(12), 0.1, 1.5, 5), () => Sphere(12), null);
        add("flap", "box ridge folded back over the top face", () => Move(Corpus.FineBox(Vector3d.Zero, Vector3d.One, 9), (v, p) =>
            p.x > 0.999 && p.z > 0.999 && Math.Abs(p.y) < 0.999 ? new Vector3d(p.x - 0.5, p.y, p.z + 0.125) : p), () => Corpus.FineBox(Vector3d.Zero, Vector3d.One, 9), null);

        // ---- near-coplanar overlaps ----
        add("coplanar", "plate 0.05 thick, bump pushed 0.05 + 1e-7 (top meets bottom)", () => PushedPlate(17, 0.05, 0.05 + 1e-7, 0.5), () => Plate(17, 0.05), null);
        add("coplanar", "flat face, one vertex moved 1.5 cells in the face", () => Move(Corpus.FineBox(Vector3d.Zero, Vector3d.One, 9), (v, p) =>
            p.z > 0.999 && Math.Abs(p.x) < 1e-9 && Math.Abs(p.y) < 1e-9 ? new Vector3d(p.x + 0.375, p.y + 0.1, p.z) : p), () => Corpus.FineBox(Vector3d.Zero, Vector3d.One, 9), null);
        add("coplanar", "two boxes with coplanar faces overlapping (components)", () => Corpus.Combine(Corpus.Cube(0, 2), Corpus.Box(1, 0, 0, 3, 2, 2)), null, null);

        // ---- CAD-like coarse parts with a pushed-through face ----
        add("cad", "pocket floor pushed through the bottom by 0.25", () => Move(Pocket(), (v, p) => p.z == 1 ? new Vector3d(p.x, p.y, -0.25) : p), Pocket, null);
        add("cad", "pocket floor pushed through the bottom by 2", () => Move(Pocket(), (v, p) => p.z == 1 ? new Vector3d(p.x, p.y, -2) : p), Pocket, null);
        add("cad", "boss top edge pushed through the opposite wall", () => Move(Boss(), (v, p) => p.z == 5 && p.x == 3 ? new Vector3d(0.5, p.y, p.z) : p), Boss, null);
        add("cad", "box corner pushed through the opposite face", () => Move(Corpus.Cube(0, 2), (v, p) => p.x == 2 && p.y == 2 && p.z == 2 ? new Vector3d(0.5, 0.6, -0.5) : p), () => Corpus.Cube(0, 2), null);
        add("cad", "bracket: top of the low arm pushed below the bottom", () => Move(Bracket(), (v, p) => p.z == 1 && p.x > 1 ? new Vector3d(p.x, p.y, -0.2) : p), Bracket, null);
        return L;
    }
}
