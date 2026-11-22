using System;
using System.Collections.Generic;
using System.Linq;
using g3;

// The procedural adversarial corpus: every case has the operands as given to the pipeline and the "intent" operands (clean,
// closed, outward) that define what the result should be. No mesh files.
sealed class Case
{
    public string Class, Name;
    public ExactMeshBoolean.Operations Op;
    public Func<DMesh3[]> Given;
    public Func<DMesh3[]> Intent;       // null: the given meshes are the intent
    public double ExpectVolume = double.NaN;    // in the canonical frame, where it is known in closed form
    public string Note = "";
}

sealed class Frame
{
    public string Name;
    public Func<Vector3d, Vector3d> Map;
    public bool Soup;       // the given meshes are unwelded (three vertices per triangle), like an STL read
    public bool Exact;      // the map is exact in doubles (identity or a power of two), so closed-form volumes hold
    public double VolumeScale = 1;

    public DMesh3 Apply(DMesh3 m, bool given)
    {
        var c = new DMesh3(m, false, MeshComponents.None);
        foreach (int v in c.VertexIndices())
            c.SetVertex(v, Map(c.GetVertex(v)));
        return given && Soup ? Corpus.Soup(c) : c;
    }
}

static class Corpus
{
    // ---- builders ----
    public static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var m = new DMesh3();
        for (int i = 0; i < 8; ++i)
            m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
        int[] t = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 7, 3, 2, 6, 7, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
        for (int i = 0; i < 36; i += 3)
            m.AppendTriangle(t[i], t[i + 1], t[i + 2]);
        return m;
    }

    public static DMesh3 Cube(double lo, double hi) { return Box(lo, lo, lo, hi, hi, hi); }

    public static DMesh3 FineBox(Vector3d c, Vector3d e, int n) { return new GridBox3Generator() { Box = new Box3d(c, e), EdgeVertices = n }.Generate().MakeDMesh(); }

    public static DMesh3 Sphere(Vector3d c, double r, int n)
    {
        var m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        MeshTransforms.Translate(m, c);
        return m;
    }

    public static DMesh3 Cylinder(Vector3d c, double r, double h, int slices)
    {
        var m = new CappedCylinderGenerator() { BaseRadius = (float)r, TopRadius = (float)r, Height = (float)h, Slices = slices }.Generate().MakeDMesh();
        MeshTransforms.Translate(m, c);
        return m;
    }

    public static DMesh3 Torus(Vector3d c, double R, double r, int nu, int nv)
    {
        var m = new DMesh3();
        for (int i = 0; i < nu; ++i)
            for (int j = 0; j < nv; ++j) {
                double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                m.AppendVertex(c + new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), r * Math.Sin(v), (R + r * Math.Cos(v)) * Math.Sin(u)));
            }
        Func<int, int, int> id = (i, j) => (i % nu) * nv + (j % nv);
        for (int i = 0; i < nu; ++i)
            for (int j = 0; j < nv; ++j) {
                int a = id(i, j), b = id(i + 1, j), cc = id(i + 1, j + 1), d = id(i, j + 1);
                m.AppendTriangle(a, d, cc); m.AppendTriangle(a, cc, b);
            }
        return m;
    }

    public static DMesh3 Soup(DMesh3 m)
    {
        var s = new DMesh3();
        foreach (int t in m.TriangleIndices()) {
            Index3i tv = m.GetTriangle(t);
            int a = s.AppendVertex(m.GetVertex(tv.a)), b = s.AppendVertex(m.GetVertex(tv.b)), c = s.AppendVertex(m.GetVertex(tv.c));
            s.AppendTriangle(a, b, c);
        }
        return s;
    }

    // several meshes as the components of one (vertices are not shared between them)
    public static DMesh3 Combine(params DMesh3[] parts)
    {
        var m = new DMesh3();
        foreach (DMesh3 p in parts) {
            var map = new Dictionary<int, int>();
            foreach (int v in p.VertexIndices())
                map[v] = m.AppendVertex(p.GetVertex(v));
            foreach (int t in p.TriangleIndices()) {
                Index3i tv = p.GetTriangle(t);
                m.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]);
            }
        }
        return m;
    }

    public static DMesh3 Invert(DMesh3 m)
    {
        var c = new DMesh3(m, false, MeshComponents.None);
        c.ReverseOrientation();
        return c;
    }

    public static DMesh3 FlipTriangles(DMesh3 m, params int[] tids)
    {
        var c = Soup(m);        // (a soup: single triangles can be reversed without touching the neighbours)
        foreach (int t in tids)
            c.ReverseTriOrientation(t);
        return c;
    }

    public static DMesh3 Without(DMesh3 m, Func<int, Vector3d, bool> remove)
    {
        var c = new DMesh3(m, false, MeshComponents.None);
        foreach (int t in m.TriangleIndices())
            if (remove(t, m.GetTriCentroid(t)))
                c.RemoveTriangle(t, true, false);
        return c;
    }

    public static DMesh3 WithTriangle(DMesh3 m, Vector3d a, Vector3d b, Vector3d c)
    {
        var r = new DMesh3(m, false, MeshComponents.None);
        r.AppendTriangle(r.AppendVertex(a), r.AppendVertex(b), r.AppendVertex(c));
        return r;
    }

    // the cube [0,2]^3 as a soup whose top face is split at the midpoint of one edge while the side face along that edge is
    // not: a T-junction. Watertight as a point set, not as a mesh
    public static DMesh3 TJunctionCube()
    {
        DMesh3 b = Cube(0, 2);
        var s = new DMesh3();
        Action<Vector3d, Vector3d, Vector3d> tri = (p, q, r) => s.AppendTriangle(s.AppendVertex(p), s.AppendVertex(q), s.AppendVertex(r));
        foreach (int t in b.TriangleIndices()) {
            Index3i tv = b.GetTriangle(t);
            if (tv.a == 4 && tv.b == 5 && tv.c == 7) {
                Vector3d mid = 0.5 * (b.GetVertex(4) + b.GetVertex(5));
                tri(b.GetVertex(4), mid, b.GetVertex(7));
                tri(mid, b.GetVertex(5), b.GetVertex(7));
            } else
                tri(b.GetVertex(tv.a), b.GetVertex(tv.b), b.GetVertex(tv.c));
        }
        return s;
    }

    static readonly ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union, I = ExactMeshBoolean.Operations.Intersection, D = ExactMeshBoolean.Operations.Difference;

    public static List<Case> Cases()
    {
        var L = new List<Case>();
        Action<string, string, ExactMeshBoolean.Operations, Func<DMesh3[]>, Func<DMesh3[]>, double, string> add = (cls, name, op, given, intent, vol, note) =>
            L.Add(new Case { Class = cls, Name = name, Op = op, Given = given, Intent = intent, ExpectVolume = vol, Note = note ?? "" });
        double nan = double.NaN;
        Func<DMesh3> A = () => Cube(0, 2);
        Func<DMesh3> Hollow = () => Combine(Cube(0, 4), Invert(Cube(1, 3)));

        // ---- base: clean closed operands in general position ----
        add("base", "box U box", U, () => new[] { A(), Cube(1, 3) }, null, 15, null);
        add("base", "box I box", I, () => new[] { A(), Cube(1, 3) }, null, 1, null);
        add("base", "box D box", D, () => new[] { A(), Cube(1, 3) }, null, 7, null);
        add("base", "sphere U sphere", U, () => new[] { Sphere(Vector3d.Zero, 1, 12), Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 12) }, null, nan, null);
        add("base", "sphere D cylinder", D, () => new[] { Sphere(Vector3d.Zero, 1, 12), Cylinder(new Vector3d(0.2, -1.5, 0.1), 0.4, 3, 24) }, null, nan, null);
        add("base", "torus U cylinder", U, () => new[] { Torus(Vector3d.Zero, 1, 0.3, 32, 12), Cylinder(new Vector3d(1, -0.8, 0), 0.25, 1.6, 24) }, null, nan, null);
        add("base", "box U sphere U cylinder", U, () => new[] { A(), Sphere(new Vector3d(2, 2, 2), 1, 12), Cylinder(new Vector3d(0, 1, 1), 0.5, 3, 24) }, null, nan, null);

        // ---- nested shells and cavities ----
        add("nested", "hollow U box through the wall", U, () => new[] { Hollow(), Box(-1, 1.5, 1.5, 2, 2.5, 2.5) }, null, 56 + 1 + 1, null);
        add("nested", "hollow D box through the wall", D, () => new[] { Hollow(), Box(-1, 1.5, 1.5, 2, 2.5, 2.5) }, null, 56 - 1, null);
        add("nested", "hollow I box through the wall", I, () => new[] { Hollow(), Box(-1, 1.5, 1.5, 2, 2.5, 2.5) }, null, 1, null);
        add("nested", "hollow with an island D box", D, () => new[] { Combine(Cube(0, 8), Invert(Cube(1, 7)), Cube(3, 5)), Box(-1, 3.5, 3.5, 9, 4.5, 4.5) }, null,
            512 - 216 + 8 - (1 + 1 + 2), null);
        add("nested", "box inside the cavity of a hollow, U", U, () => new[] { Hollow(), Cube(1.5, 2.5) }, null, 56 + 1, null);
        add("nested", "box D box inside it (makes a cavity)", D, () => new[] { Cube(0, 4), Cube(1, 3) }, null, 56, null);
        add("nested", "box D box inside, flush with a face (pocket)", D, () => new[] { Cube(0, 4), Box(1, 1, 1, 3, 3, 4) }, null, 64 - 12, null);
        add("nested", "cavity wall given outward, U far box", U, () => new[] { Combine(Cube(0, 4), Cube(1, 3)), Box(6, 0, 0, 7, 1, 1) },
            () => new[] { Hollow(), Box(6, 0, 0, 7, 1, 1) }, 57, "intent: a hollow box; given: both shells outward (winding 2 in the cavity)");
        add("nested", "hollow whose cavity touches the outer wall, D box", D, () => new[] { Combine(Cube(0, 4), Invert(Box(0, 1, 1, 3, 3, 3))), Box(3.5, -1, -1, 5, 5, 5) }, null,
            64 - 12 - 8, "the cavity is open to the outside through a face at x = 0: a pocket written as two shells");

        // ---- touching and coplanar ----
        add("touch", "face touch U", U, () => new[] { A(), Box(2, 0, 0, 4, 2, 2) }, null, 16, null);
        add("touch", "face touch I (empty)", I, () => new[] { A(), Box(2, 0, 0, 4, 2, 2) }, null, 0, null);
        add("touch", "partial face overlap U", U, () => new[] { A(), Box(2, 1, 1, 4, 3, 3) }, null, 16, null);
        add("touch", "edge touch U", U, () => new[] { A(), Box(2, 2, 0, 4, 4, 2) }, null, 16, null);
        add("touch", "vertex touch U", U, () => new[] { A(), Cube(2, 4) }, null, 16, null);
        add("touch", "identical U", U, () => new[] { A(), A() }, null, 8, null);
        add("touch", "identical D (empty)", D, () => new[] { A(), A() }, null, 0, null);
        add("touch", "identical I", I, () => new[] { A(), A() }, null, 8, null);
        add("touch", "flush cut D (coplanar faces)", D, () => new[] { A(), Box(1, 0, 0, 2, 2, 2) }, null, 4, null);
        add("touch", "three stacked U", U, () => new[] { A(), Box(0, 0, 2, 2, 2, 4), Box(0, 0, 4, 2, 2, 6) }, null, 24, null);
        add("touch", "one ulp apart U", U, () => new[] { Cube(1, 3), Box(Math.BitIncrement(1.0), 1, 1, Math.BitIncrement(3.0), 3, 3) }, null, nan, null);
        add("touch", "a subnormal coordinate (5e-324), U", U, () => new[] { A(), Box(Math.BitIncrement(0.0), 0, 0, Math.BitIncrement(2.0), 2, 2) }, null, nan,
            "the second box starts at the smallest positive double: not invariant under 2^-40 (it becomes 0 there)");
        add("touch", "one ulp apart D (a one-ulp slab)", D, () => new[] { A(), Box(-1, -1, -1, Math.BitDecrement(2.0), 3, 3) }, null, nan,
            "the true result is a slab one ulp thick");
        add("touch", "fine boxes, partial coplanar overlap U", U, () => new[] { FineBox(new Vector3d(1, 1, 1), new Vector3d(1, 1, 1), 9), FineBox(new Vector3d(3, 1.5, 1.25), new Vector3d(1, 1, 1), 9) }, null, 16, null);

        // ---- thin and near-degenerate ----
        double h30 = Math.ScaleB(1, -30), h45 = Math.ScaleB(1, -45);
        add("thin", "I: overlap slab 2^-30", I, () => new[] { A(), Box(2 - h30, 0, 0, 4, 2, 2) }, null, 4 * h30, null);
        add("thin", "I: overlap slab 2^-45", I, () => new[] { A(), Box(2 - h45, 0, 0, 4, 2, 2) }, null, 4 * h45, null);
        add("thin", "D: wall 2^-30 left", D, () => new[] { A(), Box(h30, -1, -1, 3, 3, 3) }, null, 4 * h30, null);
        add("thin", "D: wall 2^-45 left", D, () => new[] { A(), Box(h45, -1, -1, 3, 3, 3) }, null, 4 * h45, null);
        add("thin", "U: needle 2^-20 through a box", U, () => new[] { A(), Box(-1, 1, 1, 3, 1 + Math.ScaleB(1, -20), 1 + Math.ScaleB(1, -20)) }, null, 8 + 2 * Math.ScaleB(1, -40), null);
        add("thin", "U: plate 2^-40 beside a box", U, () => new[] { A(), Box(3, 0, 0, 5, 2, Math.ScaleB(1, -40)) }, null, 8 + 4 * Math.ScaleB(1, -40), null);
        add("thin", "D: tool overshoots by 2^-30 (sliver cut off a face)", D, () => new[] { A(), Box(2 - h30, -1, -1, 3, 3, 3) }, null, 8 - 4 * h30, null);
        add("thin", "small solid beside a large one, U (1 : 2^20)", U, () => new[] { Cube(0, 1024), Box(1100, 0, 0, 1100 + Math.ScaleB(1, -10), Math.ScaleB(1, -10), Math.ScaleB(1, -10)) }, null,
            1073741824.0 + Math.ScaleB(1, -30), null);

        // ---- open inputs ----
        Func<DMesh3[]> boxPair = () => new[] { A(), Cube(1, 3) };
        add("open", "one triangle missing, U", U, () => new[] { Without(A(), (t, c) => t == 2), Cube(1, 3) }, boxPair, 15, "the hole is outside the other operand");
        add("open", "one face missing, U", U, () => new[] { Without(A(), (t, c) => c.z > 1.99), Cube(1, 3) }, boxPair, 15, null);
        add("open", "face missing inside the other operand, U", U, () => new[] { A(), Without(Cube(1, 3), (t, c) => c.z < 1.01) }, boxPair, 15, "the hole lies inside the first operand: the union does not depend on it");
        add("open", "sphere with a one-triangle hole, D", D, () => new[] { Without(Sphere(Vector3d.Zero, 1, 12), (t, c) => t == 5), Cylinder(new Vector3d(0.2, -1.5, 0.1), 0.4, 3, 24) },
            () => new[] { Sphere(Vector3d.Zero, 1, 12), Cylinder(new Vector3d(0.2, -1.5, 0.1), 0.4, 3, 24) }, nan, null);
        add("open", "cutting sheet as D tool", D, () => new[] { Cube(0, 4), Without(Box(-1, -1, 2, 5, 5, 9), (t, c) => c.z > 2.01) },
            () => new[] { Cube(0, 4), Box(-1, -1, 2, 5, 5, 9) }, 32, "the tool is the plane z = 2 (normal down): the intent is the lower half");
        add("open", "T-junction cube (watertight point set), U", U, () => new[] { TJunctionCube(), Cube(1, 3) }, boxPair, 15, null);
        add("open", "duplicated face, U", U, () => new[] { Combine(A(), Without(A(), (t, c) => c.z < 1.99)), Cube(1, 3) }, boxPair, 15, "the top face is in the mesh twice");
        add("open", "debris triangle far away, U", U, () => new[] { WithTriangle(A(), new Vector3d(9, 9, 9), new Vector3d(9.5, 9, 9), new Vector3d(9, 9.5, 9)), Cube(1, 3) }, boxPair, 15, null);
        add("open", "zero-area triangle on the surface, U", U, () => new[] { WithTriangle(A(), new Vector3d(0.5, 0.5, 2), new Vector3d(1, 1, 2), new Vector3d(1.5, 1.5, 2)), Cube(1, 3) }, boxPair, 15, null);

        // ---- non-manifold and self-overlapping operands ----
        add("nonmanifold", "operand = two boxes sharing an edge, U box", U, () => new[] { Combine(A(), Box(2, 2, 0, 4, 4, 2)), Box(1, 1, 0.5, 3, 3, 1.5) }, null, 16 + 2, null);
        add("nonmanifold", "operand = two boxes sharing a vertex, D box", D, () => new[] { Combine(A(), Cube(2, 4)), Cube(1, 3) }, null, 14, null);
        add("nonmanifold", "operand = two overlapping boxes (self-intersecting), U", U, () => new[] { Combine(A(), Cube(1, 3)), Box(5, 0, 0, 6, 1, 1) },
            () => new[] { A(), Cube(1, 3), Box(5, 0, 0, 6, 1, 1) }, 16, null);
        add("nonmanifold", "operand = two overlapping boxes (self-intersecting), D", D, () => new[] { Cube(0, 4), Combine(Cube(1, 2.5), Cube(2, 3)) },
            () => new[] { Cube(0, 4), Cube(1, 2.5), Cube(2, 3) }, 64 - (3.375 + 1 - 0.125), null);
        add("nonmanifold", "operand = the same box twice (winding 2), D", D, () => new[] { Combine(Cube(0, 4), Cube(0, 4)), Cube(1, 3) },
            () => new[] { Cube(0, 4), Cube(1, 3) }, 56, null);
        add("nonmanifold", "operand = two boxes sharing a face (inner wall), U", U, () => new[] { Combine(A(), Box(2, 0, 0, 4, 2, 2)), Box(1, 1, 1, 3, 3, 3) }, null, 16 + 6, null);

        // ---- orientation ----
        add("orientation", "inside-out box U box", U, () => new[] { Invert(A()), Cube(1, 3) }, boxPair, 15, null);
        add("orientation", "inside-out tool, D", D, () => new[] { A(), Invert(Cube(1, 3)) }, boxPair, 7, null);
        add("orientation", "two components, one inside-out, U", U, () => new[] { Combine(A(), Invert(Box(5, 0, 0, 7, 2, 2))), Cube(1, 3) },
            () => new[] { Combine(A(), Box(5, 0, 0, 7, 2, 2)), Cube(1, 3) }, 23, null);
        add("orientation", "one triangle flipped, U", U, () => new[] { FlipTriangles(A(), 3), Cube(1, 3) }, boxPair, 15, null);
        add("orientation", "half of the faces flipped, U", U, () => new[] { FlipTriangles(A(), 0, 1, 4, 5, 8, 9), Cube(1, 3) }, boxPair, 15, null);
        add("orientation", "inside-out hollow (both shells reversed), D", D, () => new[] { Invert(Hollow()), Box(-1, 1.5, 1.5, 2, 2.5, 2.5) },
            () => new[] { Hollow(), Box(-1, 1.5, 1.5, 2, 2.5, 2.5) }, 55, null);
        add("orientation", "self-intersecting operand with a cavity given outward, U", U, () => new[] { Combine(Cube(0, 4), Cube(1, 3), Box(3.5, 1, 1, 5, 2, 2)), Box(8, 0, 0, 9, 1, 1) },
            () => new[] { Combine(Cube(0, 4), Invert(Cube(1, 3))), Box(3.5, 1, 1, 5, 2, 2), Box(8, 0, 0, 9, 1, 1) }, 56 + 1 + 1,
            "not embedded (a box pokes through the wall), so ByNesting cannot orient the cavity (nesting per component)");
        return L;
    }

    // a fixed generic rotation (axis (1, 2, 3), angle 0.7)
    static Vector3d rotate(Vector3d p)
    {
        double ax = 1 / Math.Sqrt(14), ay = 2 / Math.Sqrt(14), az = 3 / Math.Sqrt(14);
        double c = Math.Cos(0.7), s = Math.Sin(0.7), k = 1 - c;
        return new Vector3d(
            (c + ax * ax * k) * p.x + (ax * ay * k - az * s) * p.y + (ax * az * k + ay * s) * p.z,
            (ay * ax * k + az * s) * p.x + (c + ay * ay * k) * p.y + (ay * az * k - ax * s) * p.z,
            (az * ax * k - ay * s) * p.x + (az * ay * k + ax * s) * p.y + (c + az * az * k) * p.z);
    }

    public static Vector3d Rotate(Vector3d p) { return rotate(p); }
    public static readonly Vector3d Utm = new Vector3d(500000, 5500000, 100);
    public static Vector3d Float32(Vector3d p) { return new Vector3d((float)p.x, (float)p.y, (float)p.z); }

    public static List<Frame> Frames()
    {
        double big = Math.ScaleB(1, 40), small = Math.ScaleB(1, -40);
        return new List<Frame> {
            new Frame { Name = "id", Map = p => p, Exact = true },
            new Frame { Name = "soup", Map = p => p, Exact = true, Soup = true },
            new Frame { Name = "2^40", Map = p => big * p, Exact = true, VolumeScale = big * big * big },
            new Frame { Name = "2^-40", Map = p => small * p, Exact = true, VolumeScale = small * small * small },
            new Frame { Name = "utm", Map = p => p + Utm },
            new Frame { Name = "1e12", Map = p => p + new Vector3d(1e12, -1e12, 1e12) },
            new Frame { Name = "rot", Map = p => rotate(p) },
            new Frame { Name = "rot+utm", Map = p => rotate(p) + Utm },
            new Frame { Name = "rot,f32", Map = p => Float32(rotate(p)), Soup = true },
            new Frame { Name = "x16+utm,f32", Map = p => Float32(16 * rotate(p) + Utm), Soup = true },
        };
    }
}
