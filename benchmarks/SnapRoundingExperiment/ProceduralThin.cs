// Procedural cases with long thin triangles on tilted flat faces, for docs/research/ROUNDING_COST_2026-11.md: the kind of
// result on which SafeRounding's first search returns hundreds of box neighbours per query triangle (CAD parts, the
// rotated copy). No mesh files: everything is generated, and written only to the directory of a run.
//
//   gen <shape> <dir>     A = the shape, B = the copy of `BooleanComparison export` (rotated by 20 degrees about (1, 1, 0)
//                         through the centre, shifted by a quarter of the box); A.off and B.off with round-trip precision
//   shapes  cyl:<n>       a prism over a regular n-gon, radius 1, height 2: 2 n side triangles of 2 by 2 pi / n
//           gear:<n>      a prism over a gear outline of n teeth (4 corners per tooth), height 2
//           fins:<n>      a base plate with n thin fins (the union of n + 1 boxes): strips on the plate, thin faces on the fins
//           plate:<n>     a plate with n x n holes of 24 sides (the difference of a box and n^2 prisms)
//           sphere:<n>    the control: a cube sphere with n vertices per cube edge (no thin triangle)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using g3;

static class ProceduralThin
{
    public static int Gen(string shape, string dir)
    {
        DMesh3 a = Shape(shape);
        DMesh3 b = new DMesh3(a);
        AxisAlignedBox3d bounds = a.CachedBounds;
        MeshTransforms.Rotate(b, bounds.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
        MeshTransforms.Translate(b, 0.25 * new Vector3d(bounds.Width, bounds.Height, bounds.Depth));
        Directory.CreateDirectory(dir);
        WriteOFF(a, Path.Combine(dir, "A.off"));
        WriteOFF(b, Path.Combine(dir, "B.off"));
        Console.WriteLine("{\"shape\":\"" + shape + "\",\"triangles\":" + a.TriangleCount + ",\"closed\":" + (a.IsClosed() ? "true" : "false") + "}");
        return 0;
    }

    public static DMesh3 Shape(string shape)
    {
        string kind = shape.Substring(0, shape.IndexOf(':'));
        int n = int.Parse(shape.Substring(shape.IndexOf(':') + 1), CultureInfo.InvariantCulture);
        if (kind == "cyl") {
            var r = new double[n];
            for (int i = 0; i < n; ++i) r[i] = 1;
            return Prism(r, 2);
        }
        if (kind == "gear") {
            // per tooth: two corners at the root radius, two at the tip radius (flat flanks, flat tips)
            var r = new double[4 * n];
            for (int i = 0; i < 4 * n; ++i) r[i] = (i % 4) < 2 ? 0.8 : 1.0;
            return Prism(r, 2);
        }
        if (kind == "fins") {
            var parts = new List<DMesh3> { Box(0, 0, 0, 2, 2, 0.2) };
            double pitch = 2.0 / n;
            for (int i = 0; i < n; ++i)
                parts.Add(Box(i * pitch + 0.25 * pitch, 0.1, 0.1, i * pitch + 0.75 * pitch, 1.9, 1.0));
            return Run(new ExactMeshBoolean(parts, ExactMeshBoolean.Operations.Union));
        }
        if (kind == "plate") {
            var parts = new List<DMesh3> { Box(0, 0, 0, 2, 2, 0.2) };
            double pitch = 2.0 / n;
            var r = new double[24];
            for (int i = 0; i < 24; ++i) r[i] = 0.3 * pitch;
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    DMesh3 hole = Prism(r, 1);
                    MeshTransforms.Translate(hole, new Vector3d((i + 0.5) * pitch, (j + 0.5) * pitch, -0.4));
                    parts.Add(hole);
                }
            return Run(new ExactMeshBoolean(parts, ExactMeshBoolean.Operations.Difference));
        }
        if (kind == "sphere")
            return new Sphere3Generator_NormalizedCube() { EdgeVertices = n }.Generate().MakeDMesh();
        throw new ArgumentException("unknown shape " + shape);
    }

    static DMesh3 Run(ExactMeshBoolean b)
    {
        if (b.Compute() == false)
            throw new InvalidOperationException("the generator's boolean failed: " + b.Error);
        return b.Result;
    }

    // a prism of height h over the polygon with corner i at radius r[i] and angle 2 pi i / n (star-shaped about the
    // centre): the caps are fans from the centre, every side face two triangles over the whole height
    static DMesh3 Prism(double[] r, double h)
    {
        int n = r.Length;
        var m = new DMesh3();
        for (int k = 0; k < 2; ++k)
            for (int i = 0; i < n; ++i) {
                double t = 2 * Math.PI * i / n;
                m.AppendVertex(new Vector3d(r[i] * Math.Cos(t), r[i] * Math.Sin(t), k * h));
            }
        int c0 = m.AppendVertex(new Vector3d(0, 0, 0)), c1 = m.AppendVertex(new Vector3d(0, 0, h));
        for (int i = 0; i < n; ++i) {
            int j = (i + 1) % n;
            m.AppendTriangle(c0, j, i);
            m.AppendTriangle(c1, n + i, n + j);
            m.AppendTriangle(i, j, n + j);
            m.AppendTriangle(i, n + j, n + i);
        }
        return m;
    }

    static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var m = new DMesh3();
        for (int i = 0; i < 8; ++i)
            m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
        int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
        foreach (int[] q in quads) {
            m.AppendTriangle(q[0], q[1], q[2]);
            m.AppendTriangle(q[0], q[2], q[3]);
        }
        return m;
    }

    static void WriteOFF(DMesh3 mesh, string path)
    {
        CultureInfo ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("OFF\n").Append(mesh.VertexCount).Append(' ').Append(mesh.TriangleCount).Append(" 0\n");
        var map = new Dictionary<int, int>();
        foreach (int vid in mesh.VertexIndices()) {
            map[vid] = map.Count;
            Vector3d v = mesh.GetVertex(vid);
            sb.Append(v.x.ToString("R", ci)).Append(' ').Append(v.y.ToString("R", ci)).Append(' ').Append(v.z.ToString("R", ci)).Append('\n');
        }
        foreach (int tid in mesh.TriangleIndices()) {
            Index3i t = mesh.GetTriangle(tid);
            sb.Append("3 ").Append(map[t.a]).Append(' ').Append(map[t.b]).Append(' ').Append(map[t.c]).Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }
}
