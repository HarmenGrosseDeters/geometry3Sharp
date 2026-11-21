using System.IO;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Quad-dominant remeshing (QuadRemesher) producing a DPolyMesh and round-tripping whole quad faces via PolyMeshIO.
    /// </summary>
    public static class QuadRemeshExample
    {
        /// <summary>
        /// Quad-remeshes a subdivided sphere to a target edge length and round-trips the resulting DPolyMesh through OBJ.
        /// </summary>
        public static QuadRemesher Run(out DPolyMesh roundTripped)
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh();

            var qr = new QuadRemesher(sphere) {
                TargetEdgeLength = 0.45
            };
            if (qr.Compute() != QuadRemeshStatus.Success) {
                roundTripped = null;
                return qr;
            }

            using (var sw = new StringWriter()) {
                PolyMeshIO.WriteOBJ(sw, qr.Result);
                using (var sr = new StringReader(sw.ToString()))
                    PolyMeshIO.ReadOBJ(sr, ReadOptions.Defaults, out roundTripped);
            }
            return qr;
        }
    }
}
