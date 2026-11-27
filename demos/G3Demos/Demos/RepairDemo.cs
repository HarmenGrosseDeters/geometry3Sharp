using System.Diagnostics;
using g3;
using gs;

namespace G3Demos.Demos
{
    /// <summary> a clean procedural solid broken on purpose, then MeshRobustRepair with its defaults </summary>
    public sealed class RepairParams
    {
        public string Shape = "sphere";   // sphere | box | torus
        public int Resolution = 10;
        public bool Unweld = true;        // every triangle its own three vertices (a soup)
        public int FlipEvery = 0;         // 0 = none, else every k-th triangle reversed
        public int Holes = 0;             // triangles removed
        public int JitterExp = 0;         // 0 = none, else each soup vertex moved by up to 10^-JitterExp of the size
        public int DuplicatePercent = 0;  // share of triangles appended a second time
        public bool OverlappingShell = false; // a second copy of the solid, shifted so that the two shells cross
        public int Fins = 0;              // loose triangles that cross the surface
        public int Seed = 1;
        public bool MeasureLostArea = false;
        public string View = "both";      // before | after | both
    }

    public static class RepairDemo
    {
        static DMesh3 BaseShape(RepairParams p)
        {
            switch (p.Shape) {
                case "box": return Shapes.Box(Vector3d.Zero, new Vector3d(0.5, 0.4, 0.3), p.Resolution);
                case "torus": return Shapes.Torus(0.6, 0.25, 4 * p.Resolution, 2 * p.Resolution);
                default: return Shapes.Sphere(Vector3d.Zero, 0.5, p.Resolution);
            }
        }

        /// <summary> the clean solid(s) and the broken soup made from them; knownVolume is the volume of the clean solid </summary>
        public static DMesh3 MakeSoup(RepairParams p, out DMesh3 clean, out double knownVolume)
        {
            var rnd = new Random(p.Seed);
            clean = BaseShape(p);
            knownVolume = Shapes.Volume(clean);
            if (p.OverlappingShell) {
                DMesh3 second = BaseShape(p);
                MeshTransforms.Translate(second, new Vector3d(0.31, 0.17, 0.11));
                var union = new ExactMeshBoolean(clean, second, ExactMeshBoolean.Operations.Union);
                union.Compute();
                var both = new DMesh3(clean);
                new MeshEditor(both).AppendMesh(second);
                knownVolume = union.Result != null ? Shapes.Volume(union.Result) : double.NaN;
                clean = both;
            }
            double size = clean.GetBounds().DiagonalLength;
            var soup = new DMesh3();
            var tris = clean.TriangleIndices().ToList();
            var holes = new HashSet<int>();
            while (holes.Count < Math.Min(p.Holes, tris.Count / 2))
                holes.Add(tris[rnd.Next(tris.Count)]);
            var shared = new Dictionary<int, int>();
            int k = 0;
            Func<Vector3d, Vector3d> jitter = v => p.JitterExp == 0 ? v
                : v + Math.Pow(10, -p.JitterExp) * size * new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
            var appended = new List<Index3i>();
            foreach (int tid in tris) {
                if (holes.Contains(tid))
                    continue;
                Index3i t = clean.GetTriangle(tid);
                int[] nv = new int[3];
                for (int j = 0; j < 3; ++j) {
                    int vid = t[j];
                    if (p.Unweld)
                        nv[j] = soup.AppendVertex(jitter(clean.GetVertex(vid)));
                    else if (!shared.TryGetValue(vid, out nv[j]))
                        shared[vid] = nv[j] = soup.AppendVertex(jitter(clean.GetVertex(vid)));
                }
                ++k;
                var tri = (p.FlipEvery > 0 && k % p.FlipEvery == 0) ? new Index3i(nv[0], nv[2], nv[1]) : new Index3i(nv[0], nv[1], nv[2]);
                if (soup.AppendTriangle(tri) >= 0)
                    appended.Add(tri);
            }
            int dups = appended.Count * p.DuplicatePercent / 100;
            for (int i = 0; i < dups; ++i) {
                // a welded mesh cannot hold a duplicate on the same three vertices: copy its vertices
                Index3i t = appended[rnd.Next(appended.Count)];
                int a = soup.AppendVertex(soup.GetVertex(t.a)), b = soup.AppendVertex(soup.GetVertex(t.b)), c = soup.AppendVertex(soup.GetVertex(t.c));
                soup.AppendTriangle(a, b, c);
            }
            for (int i = 0; i < p.Fins; ++i) {
                Vector3d c = clean.GetVertex(clean.VertexIndices().ElementAt(rnd.Next(clean.VertexCount)));
                Vector3d n = c.Normalized;
                Vector3d u = n.Cross(Math.Abs(n.x) < 0.9 ? Vector3d.AxisX : Vector3d.AxisY).Normalized;
                double s = 0.08 * size;
                int a = soup.AppendVertex(c - n * s), b = soup.AppendVertex(c + n * s + u * s), cc = soup.AppendVertex(c + n * s - u * s);
                soup.AppendTriangle(a, b, cc);
            }
            return soup;
        }

        public static DemoResult Compute(RepairParams p)
        {
            var res = new DemoResult { FitKey = "repair:" + p.Shape + p.View };
            DMesh3 soup = MakeSoup(p, out DMesh3 clean, out double known);
            res.InputHash = Shapes.Hash(soup);
            MeshCheck before = MeshCheck.Of(soup);
            res.Groups.Add(before.ToGroup("Before (the broken soup)", known, 1e-3));

            var input = new DMesh3(soup);   // the repair works in place: keep the soup for the display
            var sw = Stopwatch.StartNew();
            var robust = new MeshRobustRepair(input) { MeasureResultVolumeIoU = true, MeasureLostArea = p.MeasureLostArea };
            bool ok;
            string exception = null;
            try {
                ok = robust.Apply();
            } catch (Exception e) {
                ok = false;
                exception = e.GetType().Name + ": " + e.Message;
            }
            long ms = sw.ElapsedMilliseconds;

            var g = new StatusGroup("MeshRobustRepair (defaults)");
            g.Check("Apply() returned", ok, "true", "false");
            if (exception != null)
                g.Add("exception", exception, RowState.Bad);
            g.Add("ResultPath", robust.ResultPath.ToString());
            g.Add("ResultReason", robust.ResultReason == "" ? "\"\"" : robust.ResultReason, robust.ResultReason == "" ? RowState.Info : RowState.Warn);
            g.Add("FallbackReason", robust.FallbackReason == "" ? "\"\"" : robust.FallbackReason);
            if (robust.UsedSolidify)
                g.Add("SolidifyReason", robust.SolidifyReason == "" ? "\"\"" : robust.SolidifyReason, RowState.Warn);
            if (robust.UsedAlphaWrap)
                g.Add("WrapReason", robust.WrapReason == "" ? "\"\"" : robust.WrapReason, RowState.Warn);
            g.Add("RepairVolumeIoU / ResultVolumeIoU", $"{MeshCheck.F(robust.RepairVolumeIoU)} / {MeshCheck.F(robust.ResultVolumeIoU)}");
            if (p.MeasureLostArea)
                g.Add("LostAreaShare", MeshCheck.F(robust.LostAreaShare));
            g.Add("time", $"{ms} ms");
            res.Groups.Add(g);

            DMesh3 result = robust.Mesh;
            res.OutputHash = result != null ? Shapes.Hash(result) : "none";
            MeshCheck after = null;
            if (result != null) {
                after = MeshCheck.Of(result);
                res.Groups.Add(after.ToGroup("After (robust.Mesh)", known, 1e-3));
            }

            double frame = 1, shift = soup.GetBounds().Width * 1.25;
            if (p.View != "after") {
                res.Meshes.Add(RenderMesh.Of(soup, "#f59e0b", 1, frame, edges: true));
                res.Meshes.Add(RenderMesh.BoundaryEdges(soup, frame));
            }
            if (p.View != "before" && result != null) {
                DMesh3 shown = new DMesh3(result);
                if (p.View == "both")
                    MeshTransforms.Translate(shown, new Vector3d(shift, 0, 0));
                res.Meshes.Add(RenderMesh.Of(shown, "#3b82f6", 1, frame, edges: true));
                res.Meshes.Add(RenderMesh.BoundaryEdges(shown, frame));
            }

            double rel = after != null ? Math.Abs(after.Volume - known) / Math.Abs(known) : double.NaN;
            if (!ok) {
                res.Headline = $"Apply() returned false: {robust.ResultReason} {exception}";
                res.HeadlineState = RowState.Bad;
            } else if (after == null || !after.Clean) {
                res.Headline = $"Apply() returned true, but the result is not clean: {after?.NotCleanReasons}";
                res.HeadlineState = RowState.Bad;
            } else if (!(rel <= 0.02)) {
                res.Headline = $"Clean ({robust.ResultPath}), but the volume is {rel:P2} off the known volume";
                res.HeadlineState = RowState.Warn;
            } else {
                res.Headline = $"Clean ({robust.ResultPath}), volume within {rel:P3}, {ms} ms";
                res.HeadlineState = RowState.Good;
            }
            return res;
        }
    }
}
