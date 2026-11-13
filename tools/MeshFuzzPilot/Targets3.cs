using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace MeshFuzzPilot;

/// <summary>
/// The third wave of targets (QA 2.0 T13, gap list): MarchingCubes, TerrainGreedyTIN and the upstream editing operations MeshMeshCut,
/// MeshInsertPolygon and MeshTrimLoop. As for the earlier waves, every claim is a function of plain results, so that the self-test can hand
/// it a contradiction. The operations run on the power-of-two normalized copy of the case's mesh A (box diagonal in [1, 2)); the options are
/// the four raw bytes after the queries (ParamByte).
/// </summary>
public static partial class Program
{
    // the editing operations and the terrain judge a mesh up to 2^16 extents from the origin: farther, the coordinates' own rounding (the grid
    // of doubles there is coarser than the cells the operations make) changes the input, a limit of the representation, not of the operation
    static bool FarFromOrigin(AxisAlignedBox3d box)
    {
        double far = Math.Max(Math.Max(Math.Abs(box.Min.x), Math.Abs(box.Max.x)), Math.Max(Math.Max(Math.Abs(box.Min.y), Math.Abs(box.Max.y)), Math.Max(Math.Abs(box.Min.z), Math.Abs(box.Max.z))));
        return far > Pow2(16) * Diagonal(box);
    }

    // ---- MarchingCubes ----

    /// <summary> the union of up to four spheres (centres and radii scaled by the same power of two: every operation is exact under it) </summary>
    sealed class SphereUnion : ImplicitFunction3d
    {
        readonly Vector3d[] centers; readonly double[] radii;
        public SphereUnion(Vector3d[] centers, double[] radii, double k)
        {
            this.centers = centers.Select(p => k * p).ToArray();
            this.radii = radii.Select(r => k * r).ToArray();
        }
        public double Value(ref Vector3d p)
        {
            double f = double.MaxValue;
            for (int i = 0; i < centers.Length; ++i) {
                Vector3d d = p - centers[i];
                f = Math.Min(f, Math.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z) - radii[i]);
            }
            return f;
        }
    }

    /// <summary>
    /// MarchingCubes on the union of spheres at up to four corners of A (the root mode, the cell count and the radii from the bytes). The
    /// documented exceptions of LinearEdges (InvalidOperationException, ArgumentException, QA: the mode never repairs a violation of its
    /// preconditions) are allowed there, nothing else throws. A result is finite and valid; the serial run, a second run, and the run on the
    /// copy scaled by 2^40 (field, bounds and cell size too) give the same mesh bit for bit
    /// </summary>
    public static void CheckMarchingCubes(string what, MarchingCubes.RootfindingModes mode, DMesh3? result, Exception? error, DMesh3? serial, Exception? serialError,
        DMesh3? again, DMesh3? scaled, Exception? scaledError, double scale)
    {
        if (error != null) {
            Exception inner = error is AggregateException ae && ae.InnerExceptions.Count > 0 ? ae.InnerExceptions[0] : error;
            if (mode == MarchingCubes.RootfindingModes.LinearEdges && (inner is InvalidOperationException || inner is ArgumentException))
                return;     // the documented refusal of a field outside the preconditions of the mode
            throw new Finding($"{what}: Generate() threw {inner.GetType().Name} ({inner.Message.Split('\n')[0]}) in mode {mode}");
        }
        if (result == null)
            throw new Finding($"{what}: no mesh and no exception");
        if (!AllFinite(result))
            throw new Finding($"{what}: a vertex of the marching cubes mesh is not finite");
        if (!result.CheckValidity(true, FailMode.ReturnOnly))
            throw new Finding($"{what}: the marching cubes mesh is not valid");
        if (serialError != null || serial == null || !SameMesh(result, serial, 1))
            throw new Finding($"{what}: parallel and serial give different meshes{(serialError != null ? " (serial threw " + serialError.GetType().Name + ")" : "")}");
        if (again == null || !SameMesh(result, again, 1))
            throw new Finding($"{what}: two runs give different meshes");
        if (scaledError != null || scaled == null || !SameMesh(result, scaled, 1 / scale))
            throw new Finding($"{what}: the 2^40 scaled run is not the scaled mesh{(scaledError != null ? " (it threw " + scaledError.GetType().Name + ": " + scaledError.Message + ")" : "")}");
    }

    static void RunMarchingCubes(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        DMesh3 m = s.Norm;
        if (FarFromOrigin(m.GetBounds()))
            return; // far from the origin (> 2^16 extents) is the representation limit, QA 2.5a
        Vector3d[] centers = m.VertexIndices().Select(v => m.GetVertex(v)).Distinct().Take(4).ToArray();
        if (centers.Length == 0)
            return;
        int p0 = ParamByte(c, 0), p1 = ParamByte(c, 1), p2 = ParamByte(c, 2);
        double diag = Diagonal(m.GetBounds());
        double[] radii = Enumerable.Range(0, centers.Length).Select(i => diag * (0.1 + 0.5 * ((p0 >> (2 * i)) & 3) / 3.0)).ToArray();
        var mode = (MarchingCubes.RootfindingModes)(p1 & 3);
        int cells = 6 + p2 % 9;
        AxisAlignedBox3d box = m.GetBounds();
        var bounds = new AxisAlignedBox3d(box.Min - 0.6 * diag * Vector3d.One, box.Max + 0.6 * diag * Vector3d.One);
        double cube = bounds.MaxDim / cells;
        (DMesh3?, Exception?) Gen(double k, bool parallel)
        {
            try {
                var mc = new MarchingCubes() {
                    Implicit = new SphereUnion(centers, radii, k), Bounds = new AxisAlignedBox3d(k * bounds.Min, k * bounds.Max), CubeSize = k * cube,
                    RootMode = mode, RootModeSteps = 5, ParallelCompute = parallel, IsoValue = 0
                };
                mc.Generate();
                return (mc.Mesh, null);
            } catch (Exception e) {
                return (null, e);
            }
        }
        double scale = Pow2(40);
        var (a, ea) = Gen(1, true);
        var (serial, es) = Gen(1, false);
        var (again, _) = Gen(1, true);
        var (scaled, esc) = Gen(scale, true);
        CheckMarchingCubes(what, mode, a, ea, serial, es, again, scaled, esc, scale);
    }

    // ---- TerrainGreedyTIN ----

    /// <summary>
    /// TerrainGreedyTIN on a height grid whose heights are the z coordinates of A's corners (grid size, MaxError, RemoveVertices and a vertex
    /// limit from the bytes): Compute() is true; the TIN is valid and finite, its triangles are counter-clockwise in xy and tile the grid's
    /// rectangle (the areas add up), every grid node is within AchievedError of the TIN (recomputed), AchievedError is at most MaxError unless
    /// the vertex limit stopped the insertion, and a second run and the run on the copy scaled by 2^40 give the same TIN bit for bit
    /// </summary>
    public static void CheckTerrain(string what, bool ok, DMesh3? result, double achieved, double maxError, bool vertexLimited, double areaRatio,
        int clockwiseTriangles, double recomputedError, double tolerance, DMesh3? again, DMesh3? scaled, double scale)
    {
        if (!ok || result == null)
            throw new Finding($"{what}: Compute() is false for a valid grid");
        if (!AllFinite(result))
            throw new Finding($"{what}: a vertex of the TIN is not finite");
        if (!result.CheckValidity(true, FailMode.ReturnOnly))
            throw new Finding($"{what}: the TIN is not valid");
        if (clockwiseTriangles > 0)
            throw new Finding($"{what}: {clockwiseTriangles} triangles are not counter-clockwise in xy");
        if (!(Math.Abs(areaRatio - 1) <= 1e-9))
            throw new Finding($"{what}: the triangles cover {areaRatio} of the grid's rectangle");
        if (!(recomputedError <= achieved + tolerance))
            throw new Finding($"{what}: a grid node is {recomputedError} from the TIN, AchievedError says {achieved}");
        if (!vertexLimited && !(achieved <= maxError + tolerance))
            throw new Finding($"{what}: AchievedError {achieved} is above MaxError {maxError}");
        if (again == null || !SameMesh(result, again, 1))
            throw new Finding($"{what}: two runs give different TINs");
        if (scaled == null || !SameMesh(result, scaled, 1 / scale))
            throw new Finding($"{what}: the 2^40 scaled run is not the scaled TIN");
    }

    static void RunTerrain(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        DMesh3 m = s.Norm;
        Vector3d[] verts = m.VertexIndices().Select(v => m.GetVertex(v)).ToArray();
        if (verts.Length == 0)
            return;
        int p0 = ParamByte(c, 0), p1 = ParamByte(c, 1), p2 = ParamByte(c, 2), p3 = ParamByte(c, 3);
        int nx = 2 + p0 % 9, ny = 2 + p1 % 9;
        double diag = Diagonal(m.GetBounds()), cell = diag / nx;
        double x0 = verts[0].x, y0 = verts[0].y;
        // the grid nodes x0 + i cell must be representable: far from the origin compared with the cell (more than 2^16 extents) they collapse
        // (a limit of the representation, as for the Clipper2 target), so there the target is not judged
        if (FarFromOrigin(m.GetBounds()))
            return;
        var z = new double[nx * ny];
        for (int j = 0; j < ny; ++j)
            for (int i = 0; i < nx; ++i)
                z[i + nx * j] = verts[(i * 7 + j * 13) % verts.Length].z;
        double maxError = diag * (p2 / 255.0) * 0.5;
        bool remove = (p3 & 1) != 0, limited = (p3 & 2) != 0;
        int maxVertices = limited ? 4 + (p3 >> 2) % 20 : int.MaxValue;
        TerrainGreedyTIN Gen(double k, out bool ok)
        {
            var g = new TerrainGreedyTIN(z.Select(v => k * v).ToArray(), nx, ny, k * x0, k * y0, k * cell) { MaxError = k * maxError, RemoveVertices = remove, MaxVertices = maxVertices };
            ok = g.Compute();
            return g;
        }
        TerrainGreedyTIN first = Gen(1, out bool ok1);
        TerrainGreedyTIN second = Gen(1, out _);
        double scale = Pow2(40);
        TerrainGreedyTIN big = Gen(scale, out _);
        double areaRatio = 0, recomputed = 0;
        int clockwise = 0;
        if (ok1 && first.Result != null) {
            double total = 0;
            foreach (int t in first.Result.TriangleIndices()) {
                Index3i f = first.Result.GetTriangle(t);
                Vector3d a = first.Result.GetVertex(f.a), b = first.Result.GetVertex(f.b), d = first.Result.GetVertex(f.c);
                double cross = (b.x - a.x) * (d.y - a.y) - (b.y - a.y) * (d.x - a.x);
                if (!(cross > 0)) ++clockwise;
                total += 0.5 * cross;
            }
            areaRatio = total / ((nx - 1) * cell * ((ny - 1) * cell));
            // every grid node against the TIN: the triangle that contains it (barycentric, with a small slack for the node's rounding)
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    double px = x0 + i * cell, py = y0 + j * cell, best = double.MaxValue;
                    foreach (int t in first.Result.TriangleIndices()) {
                        Index3i f = first.Result.GetTriangle(t);
                        Vector3d a = first.Result.GetVertex(f.a), b = first.Result.GetVertex(f.b), d = first.Result.GetVertex(f.c);
                        double den = (b.y - d.y) * (a.x - d.x) + (d.x - b.x) * (a.y - d.y);
                        if (!(den != 0)) continue;
                        double l1 = ((b.y - d.y) * (px - d.x) + (d.x - b.x) * (py - d.y)) / den, l2 = ((d.y - a.y) * (px - d.x) + (a.x - d.x) * (py - d.y)) / den, l3 = 1 - l1 - l2;
                        if (l1 < -1e-9 || l2 < -1e-9 || l3 < -1e-9) continue;
                        best = Math.Min(best, Math.Abs(l1 * a.z + l2 * b.z + l3 * d.z - z[i + nx * j]));
                    }
                    recomputed = Math.Max(recomputed, best == double.MaxValue ? 0 : best);
                }
        }
        CheckTerrain(what, ok1, first.Result, first.AchievedError, maxError, limited, areaRatio, clockwise, recomputed, 1e-9 * Math.Max(diag, 1), second.Result, big.Result, scale);
    }

    // ---- MeshMeshCut ----

    /// <summary>
    /// MeshMeshCut (A cut by B, B scaled with A to the normalized size): Compute() does not throw; the cut mesh is finite and valid, has not
    /// lost a triangle, covers the same area as before (cutting only splits triangles; up to the snap tolerance), and a second run on a copy
    /// gives the same mesh bit for bit
    /// </summary>
    public static void CheckMeshCut(string what, bool valid, bool finite, int trianglesBefore, int trianglesAfter, double areaBefore, double areaAfter, bool same)
    {
        if (!finite)
            throw new Finding($"{what}: a vertex of the cut mesh is not finite");
        if (!valid)
            throw new Finding($"{what}: the cut mesh is not valid");
        if (trianglesAfter < trianglesBefore)
            throw new Finding($"{what}: the cut mesh has {trianglesAfter} triangles, the target had {trianglesBefore}");
        if (!(Math.Abs(areaAfter - areaBefore) <= 1e-4 * areaBefore))
            throw new Finding($"{what}: the area changed from {areaBefore} to {areaAfter}");
        if (!same)
            throw new Finding($"{what}: two runs give different meshes");
    }

    static double TotalArea(DMesh3 m) => m.TriangleIndices().Sum(t => m.GetTriArea(t));

    static void RunMeshCut(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        DMesh3 cut = ScaledBy(c.B, s.Scale);
        if (!AllFinite(cut) || cut.TriangleCount == 0 || s.Norm.TriangleCount == 0 || FarFromOrigin(s.Norm.GetBounds()) || FarFromOrigin(cut.GetBounds()))
            return;
        // (the snap tolerance of the cut, 1e-5, is absolute: A is of size 1 to 2, and B must not be smaller than 1e-5 of it, or larger than 2^20 of it:
        // a cutting mesh of a size the snap and the boxes of the trees do not resolve is a separate class)
        double db = Diagonal(cut.GetBounds());
        // (and a target of some area: the snap tolerance is absolute, so a sliver mesh of area 1e-11 moves its vertices by a visible share of it)
        if (!(TotalArea(s.Norm) >= 1e-3))
            return;
        if (!(db >= Pow2(-16) && db <= Pow2(20)))
            return;
        DMesh3 Cut(out DMesh3 target)
        {
            target = new DMesh3(s.Norm);
            new MeshMeshCut() { Target = target, CutMesh = cut }.Compute();
            return target;
        }
        DMesh3 first, second;
        try {
            first = Cut(out _);
            second = Cut(out _);
        } catch (Exception e) when (e.Message.Contains("find_intersections: found quantity 0") && Environment.GetEnvironmentVariable("MESH_FUZZ_STRICT") != "1") {
            return;     // the known class (FINDINGS.md): DMeshAABBTree3.find_intersections throws for an intersection of quantity 0 (coplanar triangles)
        }
        CheckMeshCut(what, first.CheckValidity(true, FailMode.ReturnOnly), AllFinite(first), s.Norm.TriangleCount, first.TriangleCount, TotalArea(s.Norm), TotalArea(first), SameMesh(first, second, 1));
    }

    // ---- MeshInsertPolygon ----

    /// <summary>
    /// MeshInsertPolygon of a polygon (the convex hull of A's xy, halved about its centre, with a hole from B's hull when it fits) into a grid
    /// mesh over A's box (z = 0, grid size from the bytes): when Insert() returns true the mesh is valid, covers the same area, the triangles
    /// of InteriorTriangles cover the polygon's area, and every triangle's centroid is inside the polygon exactly if the triangle is
    /// selected. The exception "could not find seed triangle!" is a known open class (gated; MESH_FUZZ_STRICT=1 judges it)
    /// </summary>
    public static void CheckInsertPolygon(string what, bool valid, double areaBefore, double areaAfter, double interiorArea, double polygonArea, int misclassified)
    {
        if (!valid)
            throw new Finding($"{what}: the mesh is not valid after Insert()");
        if (!(Math.Abs(areaAfter - areaBefore) <= 1e-9 * areaBefore))
            throw new Finding($"{what}: the area changed from {areaBefore} to {areaAfter}");
        if (!(Math.Abs(interiorArea - polygonArea) <= 1e-6 * areaBefore))
            throw new Finding($"{what}: the interior triangles cover {interiorArea}, the polygon {polygonArea}");
        if (misclassified > 0)
            throw new Finding($"{what}: {misclassified} triangles are selected or not against the centroid test");
    }

    static void RunInsertPolygon(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        DMesh3 m = s.Norm;
        if (FarFromOrigin(m.GetBounds()))
            return;
        var xy = m.VertexIndices().Select(v => m.GetVertex(v).xy).Distinct().ToList();
        if (xy.Count < 3)
            return;
        ConvexHull2 hull = new ConvexHull2(xy, 1e-9, QueryNumberType.QT_DOUBLE);
        if (hull.Dimension != 2)
            return;
        Polygon2d outer = hull.GetHullPolygon();
        Vector2d center = outer.Bounds.Center;
        for (int i = 0; i < outer.VertexCount; ++i)
            outer[i] = center + 0.5 * (outer[i] - center);
        if (outer.IsClockwise)
            outer.Reverse();
        // a polygon of the operation's contract: no vertex within 1e-6 of the previous one (the hull of nearly equal points has such), at least three
        var cleaned = new List<Vector2d>();
        foreach (Vector2d v in outer.Vertices)
            if (cleaned.Count == 0 || cleaned[cleaned.Count - 1].Distance(v) > 1e-6)
                cleaned.Add(v);
        while (cleaned.Count > 1 && cleaned[0].Distance(cleaned[cleaned.Count - 1]) <= 1e-6)
            cleaned.RemoveAt(cleaned.Count - 1);
        if (cleaned.Count < 3)
            return;
        outer = new Polygon2d(cleaned);
        if (!(outer.Area > 1e-6))
            return;
        var polygon = new GeneralPolygon2d(outer);
        DMesh3 nb = ScaledBy(c.B, s.Scale);
        var bxy = nb.VertexIndices().Select(v => nb.GetVertex(v).xy).Distinct().ToList();
        if (bxy.Count >= 3 && AllFinite(nb)) {
            var bh = new ConvexHull2(bxy, 1e-9, QueryNumberType.QT_DOUBLE);
            if (bh.Dimension == 2) {
                Polygon2d hole = bh.GetHullPolygon();
                Vector2d hc = hole.Bounds.Center;
                double size = Math.Max(hole.Bounds.Width, hole.Bounds.Height);
                for (int i = 0; i < hole.VertexCount; ++i)
                    hole[i] = center + 0.15 * (hole[i] - hc) * (outer.Bounds.Width / size);
                if (!hole.IsClockwise)
                    hole.Reverse();
                // (a hole inside the outer ring with room: its vertices at least 1e-2 of the outer ring's width from the ring and its edges longer than 1e-3 of it)
                double room = 1e-2 * outer.Bounds.Width, minEdge = 1e-3 * outer.Bounds.Width;
                bool fits = outer.Contains(hole) && hole.Vertices.All(v => outer.DistanceSquared(v, out _, out _) >= room * room)
                    && hole.SegmentItr().All(sg => sg.Length >= minEdge) && hole.Area > 1e-9;
                if (fits)
                    polygon.AddHole(hole);
            }
        }
        int p0 = ParamByte(c, 0);
        int n = 3 + p0 % 7;
        AxisAlignedBox3d box = m.GetBounds();
        double diag = Diagonal(box), x0 = box.Min.x - 0.25 * diag, y0 = box.Min.y - 0.25 * diag, h = 1.5 * diag / n;
        var grid = new DMesh3();
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i)
                grid.AppendVertex(new Vector3d(x0 + i * h, y0 + j * h, 0));
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, d = a + n + 1, e = d + 1;
                grid.AppendTriangle(a, b, e); grid.AppendTriangle(a, e, d);
            }
        double before = TotalArea(grid);
        var insert = new MeshInsertPolygon() { Mesh = grid, Polygon = polygon };
        bool ok;
        try {
            ok = insert.Insert();
        } catch (Exception e) when (e.Message.Contains("could not find seed triangle") && Environment.GetEnvironmentVariable("MESH_FUZZ_STRICT") != "1") {
            return;     // the known class (FINDINGS.md): the polygon does not cross the grid so that a seed triangle exists
        }
        if (!ok)
            return;
        if (Environment.GetEnvironmentVariable("MESH_FUZZ_TRACE") == "1")
            Console.Error.WriteLine("outer: " + string.Join(" ", polygon.Outer.Vertices.Select(p => $"({p.x:R},{p.y:R})")) + $" holes {polygon.Holes.Count}; grid {n} cells of {h:R} from ({x0:R},{y0:R})");
        var inside = new HashSet<int>(insert.InteriorTriangles);
        int misclassified = 0;
        foreach (int t in grid.TriangleIndices()) {
            // (a sliver along the polygon's edge, created by the insertion, has its centroid on the boundary: not judged)
            Vector2d g = grid.GetTriCentroid(t).xy;
            if (polygon.Outer.DistanceSquared(g, out _, out _) <= 1e-18 * h * h || polygon.Holes.Any(ho => ho.DistanceSquared(g, out _, out _) <= 1e-18 * h * h))
                continue;
            if (polygon.Contains(g) != inside.Contains(t)) {
                ++misclassified;
                if (Environment.GetEnvironmentVariable("MESH_FUZZ_TRACE") == "1") {
                    Index3i f = grid.GetTriangle(t);
                    Console.Error.WriteLine($"triangle {t} ({grid.GetVertex(f.a).xy.x:R},{grid.GetVertex(f.a).xy.y:R}) ({grid.GetVertex(f.b).xy.x:R},{grid.GetVertex(f.b).xy.y:R}) ({grid.GetVertex(f.c).xy.x:R},{grid.GetVertex(f.c).xy.y:R}) area {grid.GetTriArea(t):R} selected {inside.Contains(t)}; centroid {grid.GetTriCentroid(t).xy.x:R},{grid.GetTriCentroid(t).xy.y:R} distance to polygon {polygon.Outer.DistanceSquared(grid.GetTriCentroid(t).xy, out _, out _)}");
                }
            }
        }
        CheckInsertPolygon(what, grid.CheckValidity(true, FailMode.ReturnOnly), before, TotalArea(grid), inside.Where(t => grid.IsTriangle(t)).Sum(t => grid.GetTriArea(t)), polygon.Area, misclassified);
    }

    // ---- MeshTrimLoop ----

    /// <summary>
    /// MeshTrimLoop of a sphere (centre and size from A's box, 8 to 24 edge vertices) along a circle of latitude (height from the bytes), the
    /// seed on either side: Trim() does not throw; when it returns true the mesh is valid and finite, has one boundary loop, is smaller than
    /// before, and no vertex lies on the removed side beyond the border ring (3 mean edge lengths from the cut)
    /// </summary>
    public static void CheckTrimLoop(string what, bool valid, bool finite, int boundaryLoops, double areaBefore, double areaAfter, int verticesOnRemovedSide)
    {
        if (!finite)
            throw new Finding($"{what}: a vertex of the trimmed mesh is not finite");
        if (!valid)
            throw new Finding($"{what}: the trimmed mesh is not valid");
        if (boundaryLoops != 1)
            throw new Finding($"{what}: Trim() is true, but the mesh has {boundaryLoops} boundary loops");
        if (!(areaAfter < areaBefore))
            throw new Finding($"{what}: the area did not shrink ({areaBefore} to {areaAfter})");
        if (verticesOnRemovedSide > 0)
            throw new Finding($"{what}: {verticesOnRemovedSide} vertices lie on the removed side of the cut");
    }

    static void RunTrimLoop(Case c, string what)
    {
        Setup? s = MakeSetup(c);
        if (s == null)
            return;
        int p0 = ParamByte(c, 0), p1 = ParamByte(c, 1), p2 = ParamByte(c, 2);
        AxisAlignedBox3d box = s.Norm.GetBounds();
        if (FarFromOrigin(box))
            return;
        double r = 0.5 * Diagonal(box);
        Vector3d center = box.Center;
        DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = 8 + p0 % 17 }.Generate().MakeDMesh();
        foreach (int v in sphere.VertexIndices().ToArray())
            sphere.SetVertex(v, sphere.GetVertex(v) + center);
        double t = (p1 - 127.5) / 127.5 * 0.7;         // the latitude: cut height t r above the centre
        bool seedUp = (p2 & 1) == 0;
        double ring = r * Math.Sqrt(1 - t * t), cutY = center.y + t * r;
        var pts = new List<Vector3d>();
        for (int i = 0; i < 48; ++i) {
            double a = 2 * Math.PI * i / 48;
            pts.Add(new Vector3d(center.x + ring * Math.Cos(a), cutY, center.z + ring * Math.Sin(a)));
        }
        double before = TotalArea(sphere);
        double edge = MeanEdge(sphere);
        if (Environment.GetEnvironmentVariable("MESH_FUZZ_TRACE") == "1")
            Console.Error.WriteLine($"sphere: centre ({center.x:R},{center.y:R},{center.z:R}) radius {r:R} edge vertices {8 + p0 % 17}; circle at y {cutY:R} radius {ring:R}; seed {(seedUp ? "above" : "below")}");
        var trim = new MeshTrimLoop(sphere, new DCurve3(pts, true), new Vector3d(center.x, center.y + (seedUp ? r : -r), center.z));
        bool trimmed;
        try {
            trimmed = trim.Trim();
        } catch (ArgumentOutOfRangeException e) when (e.StackTrace != null && e.StackTrace.Contains("MeshTrimLoop.Trim") && Environment.GetEnvironmentVariable("MESH_FUZZ_STRICT") != "1") {
            return;     // the known class (FINDINGS.md): Trim() reads loops[0] of a mesh that has no boundary loop left
        }
        if (!trimmed)
            return;
        int loops = 0;
        var bl = new MeshBoundaryLoops(sphere);
        if (bl.Compute())
            loops = bl.Count;
        int wrong = 0;
        foreach (int v in sphere.VertexIndices()) {
            double y = sphere.GetVertex(v).y;
            if (seedUp ? y > cutY + 3 * edge : y < cutY - 3 * edge) ++wrong;
        }
        // (the seed side is removed: the vertices left lie on the other side; "wrong" counts vertices still on the seed side beyond the ring)
        CheckTrimLoop(what, sphere.CheckValidity(true, FailMode.ReturnOnly), AllFinite(sphere), loops, before, TotalArea(sphere), wrong);
    }
}
