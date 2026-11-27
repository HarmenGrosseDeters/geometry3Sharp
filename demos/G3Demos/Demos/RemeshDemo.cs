using System.Diagnostics;
using g3;

namespace G3Demos.Demos
{
    /// <summary> isotropic remeshing (Remesher) of a procedural input, set up as the remeshing guide recommends </summary>
    public sealed class RemeshParams
    {
        public string Shape = "box";          // box | sphere | twisted | torus
        public double RelativeEdge = 0.04;    // target edge length / bounding-box diagonal
        public int Passes = 20;
        public bool PreventNormalFlips = true;
        public bool SafePreset = true;        // ApplySafePreset(): MinTriangleQuality 0.2 + PreventSelfIntersections
        public bool PreserveFeatures = true;  // MeshConstraintUtil.PreserveBoundaryAndFeatures + PinSharpVertices
        public double CreaseAngle = 40;
        public bool Project = true;           // MeshProjectionTarget.Auto(input)
        public int ScaleExp = 0;
        public bool CheckScaleInvariance = false;
    }

    public static class RemeshDemo
    {
        public static DMesh3 MakeInput(RemeshParams p, int scaleExp)
        {
            DMesh3 m;
            switch (p.Shape) {
                case "sphere":
                    m = Shapes.Sphere(Vector3d.Zero, 0.5, 6);
                    break;
                case "torus":
                    m = Shapes.Torus(0.6, 0.2, 24, 8);
                    break;
                case "twisted":
                    // a long bar twisted by 90 degrees: curved faces, sharp twisted edges, long thin input triangles
                    m = Shapes.Box(Vector3d.Zero, new Vector3d(0.15, 0.15, 0.7), 5);
                    Shapes.Transform(m, v => {
                        Vector2d c = Shapes.Circle((v.z + 0.7) / 1.4 / 4);   // up to a quarter turn (exact arithmetic, Shapes.Circle)
                        return new Vector3d(v.x * c.x - v.y * c.y, v.x * c.y + v.y * c.x, v.z);
                    });
                    break;
                default:
                    // two triangles per face: the coarse CAD case (sharp edges, corners, long input edges)
                    m = Shapes.Box(Vector3d.Zero, new Vector3d(0.5, 0.35, 0.25), 2);
                    break;
            }
            if (scaleExp != 0) {
                double s = Math.Pow(2, scaleExp);
                Shapes.Transform(m, v => v * s);
            }
            return m;
        }

        sealed class Run
        {
            public DMesh3 Mesh;
            public Remesher Remesher;
            public long Ms;
            public int Pinned;
            public string Exception;
        }

        static Run Remesh(RemeshParams p, DMesh3 input)
        {
            var mesh = new DMesh3(input);
            var r = new Run { Mesh = mesh };
            var sw = Stopwatch.StartNew();
            try {
                var remesher = new Remesher(mesh);
                r.Remesher = remesher;
                remesher.SetTargetEdgeLength(p.RelativeEdge * input.GetBounds().DiagonalLength);
                if (p.Project)
                    remesher.SetProjectionTarget(MeshProjectionTarget.Auto(input));
                if (p.PreserveFeatures) {
                    MeshConstraintUtil.PreserveBoundaryAndFeatures(remesher, p.CreaseAngle);
                    r.Pinned = MeshConstraintUtil.PinSharpVertices(remesher, 75);
                }
                remesher.PreventNormalFlips = p.PreventNormalFlips;
                if (p.SafePreset)
                    remesher.ApplySafePreset();
                for (int i = 0; i < p.Passes; ++i)
                    remesher.BasicRemeshPass();
            } catch (Exception e) {
                r.Exception = e.GetType().Name + ": " + e.Message;
            }
            r.Ms = sw.ElapsedMilliseconds;
            return r;
        }

        /// <summary>
        /// triangles whose normal points against the input surface's normal at the nearest input triangle (the owner's count
        /// of inverted triangles for remeshing studies), the smallest triangle quality (aspect: 4 sqrt 3 area / sum of squared edges,
        /// 1 = equilateral) and the largest vertex distance to the input
        /// </summary>
        public static void Measure(DMesh3 input, DMesh3 m, out int inverted, out double minQuality, out double maxDistance, out int slivers)
        {
            var tree = new DMeshAABBTree3(input, true);
            inverted = 0; slivers = 0; minQuality = 1; maxDistance = 0;
            foreach (int tid in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(tid, ref a, ref b, ref c);
                Vector3d n = (b - a).Cross(c - a);
                double area2 = n.Length;
                double sq = (b - a).LengthSquared + (c - b).LengthSquared + (a - c).LengthSquared;
                double q = sq > 0 ? 2 * Math.Sqrt(3) * area2 / sq : 0;
                minQuality = Math.Min(minQuality, q);
                if (q < 0.1)
                    slivers++;
                Vector3d centroid = (a + b + c) / 3;
                int near = tree.FindNearestTriangle(centroid);
                if (near >= 0 && n.Dot(input.GetTriNormal(near)) < 0)
                    inverted++;
            }
            foreach (int vid in m.VertexIndices()) {
                Vector3d v = m.GetVertex(vid);
                int near = tree.FindNearestTriangle(v);
                if (near >= 0)
                    maxDistance = Math.Max(maxDistance, MeshQueries.TriangleDistance(input, near, v).DistanceSquared);
            }
            maxDistance = Math.Sqrt(maxDistance);
        }

        public static DemoResult Compute(RemeshParams p)
        {
            var res = new DemoResult { FitKey = "remesh:" + p.Shape };
            DMesh3 input = MakeInput(p, p.ScaleExp);
            double frame = Math.Pow(2, p.ScaleExp), diag = input.GetBounds().DiagonalLength;
            res.InputHash = Shapes.Hash(input);
            MeshCheck ic = MeshCheck.Of(input);
            var ig = new StatusGroup("Input");
            ig.Add("triangles", ic.Triangles.ToString());
            ig.Check("clean", ic.Clean, "yes", ic.NotCleanReasons);
            ig.Add("target edge length", MeshCheck.F(p.RelativeEdge * diag));
            res.Groups.Add(ig);

            Run run = Remesh(p, input);
            var g = new StatusGroup("Remesher");
            if (run.Exception != null)
                g.Add("exception", run.Exception, RowState.Bad);
            Remesher rm = run.Remesher;
            if (rm != null) {
                g.Add("settings", $"PreventNormalFlips={rm.PreventNormalFlips}, MinTriangleQuality={rm.MinTriangleQuality}, PreventSelfIntersections={rm.PreventSelfIntersections}");
                g.Add("pinned sharp vertices", run.Pinned.ToString());
                g.Add("InvertingPassesRejected", rm.InvertingPassesRejected.ToString(), rm.InvertingPassesRejected > 0 ? RowState.Warn : RowState.Info);
                g.Add("SelfIntersectionsRejected", rm.SelfIntersectionsRejected.ToString());
                g.Add("NonFiniteVertices", rm.NonFiniteVertices.ToString(), rm.NonFiniteVertices > 0 ? RowState.Bad : RowState.Info);
            }
            g.Add("time", $"{run.Ms} ms ({p.Passes} passes, {(p.Passes > 0 ? run.Ms / (double)p.Passes : 0):F1} ms / pass)");
            res.Groups.Add(g);

            res.OutputHash = Shapes.Hash(run.Mesh);
            MeshCheck rc = MeshCheck.Of(run.Mesh);
            res.Groups.Add(rc.ToGroup("Result checks", ic.Volume, 1e-3));
            Measure(input, run.Mesh, out int inverted, out double minQ, out double maxDist, out int slivers);
            var q = new StatusGroup("Quality (vs. the input surface)");
            q.Check("inverted triangles", inverted == 0, "0", inverted.ToString());
            q.Add("min triangle quality", MeshCheck.F(minQ), minQ >= 0.2 ? RowState.Good : minQ >= 0.05 ? RowState.Warn : RowState.Bad);
            q.Add("slivers (quality < 0.1)", slivers.ToString(), slivers == 0 ? RowState.Good : RowState.Warn);
            q.Add("max vertex distance / diagonal", MeshCheck.F(maxDist / diag), maxDist / diag < 1e-3 ? RowState.Good : RowState.Warn);
            res.Groups.Add(q);

            if (p.CheckScaleInvariance && p.ScaleExp != 0) {
                DMesh3 in0 = MakeInput(p, 0);
                Run r0 = Remesh(p, in0);
                bool same = r0.Mesh.TriangleCount == run.Mesh.TriangleCount && r0.Mesh.VertexCount == run.Mesh.VertexCount;
                int diff = 0;
                if (same) {
                    var v0 = r0.Mesh.VertexIndices().Select(r0.Mesh.GetVertex).ToArray();
                    var v1 = run.Mesh.VertexIndices().Select(run.Mesh.GetVertex).ToArray();
                    for (int i = 0; i < v0.Length; ++i)
                        if (v0[i] * frame != v1[i]) diff++;
                }
                var si = new StatusGroup("2^k invariance (vs. the unscaled run)");
                si.Add("unscaled", $"{r0.Mesh.TriangleCount} tris");
                si.Check("bit-identical after scaling", same && diff == 0, "yes", same ? $"NO ({diff} vertices differ)" : $"NO ({r0.Mesh.TriangleCount} vs {run.Mesh.TriangleCount} triangles)");
                res.Groups.Add(si);
            }

            res.Meshes.Add(RenderMesh.Of(run.Mesh, "#22a06b", 1, frame, edges: true));
            res.Meshes.Add(RenderMesh.BoundaryEdges(run.Mesh, frame));
            bool bad = run.Exception != null || !rc.Clean || inverted > 0;
            res.Headline = bad
                ? $"Problem: {(run.Exception ?? "")} {(rc.Clean ? "" : "not clean: " + rc.NotCleanReasons + ". ")}{(inverted > 0 ? inverted + " inverted triangles" : "")}"
                : $"Clean, no inverted triangles, {rc.Triangles} triangles, min quality {minQ:F3}, {run.Ms} ms";
            res.HeadlineState = bad ? RowState.Bad : minQ < 0.05 ? RowState.Warn : RowState.Good;
            return res;
        }
    }
}
