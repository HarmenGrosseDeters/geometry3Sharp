using System;
using System.Collections.Generic;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// Alpha wrapping (MeshAlphaWrap) and clearance-certified passage wrapping (PassageWrap).
    /// Both produce a closed, oriented, 2-manifold, self-intersection-free surface around arbitrary input.
    /// </summary>
    public static class AlphaWrapExample
    {
        /// <summary>
        /// Wraps an open triangle soup (a sphere with one cap removed) into a closed, clean manifold surface.
        /// RelativeAlpha sets detail (diagonal / 12) and RelativeOffset sets outward offset (diagonal / 200).
        /// </summary>
        public static DMesh3 WrapOpenSoup(out MeshAlphaWrap.WrapStatus status)
        {
            DMesh3 soup = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            // remove a cap so the input is an open shell
            var toRemove = new List<int>();
            foreach (int tid in soup.TriangleIndices())
                if (soup.GetTriCentroid(tid).z > 0.7) toRemove.Add(tid);
            foreach (int tid in toRemove)
                soup.RemoveTriangle(tid);

            var wrap = new MeshAlphaWrap(soup) { RelativeAlpha = 12, RelativeOffset = 200 };
            wrap.Compute();
            status = wrap.Status;
            return wrap.Result;
        }

        /// <summary>
        /// Wraps a hollow tube while certifying that a cylindrical passage pin of radius 0.35 fits cleanly
        /// through the bore. PassageWrap selects the coarsest certified alpha and verifies clearance afterwards.
        /// </summary>
        public static PassageWrap WrapWithOpenPassage()
        {
            // build a thick hollow box-tube along Z: outer cube [-2,2]^3 minus inner bore [-1,1]x[-1,1]x[-3,3]
            DMesh3 outer = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(2, 2, 2)) }.Generate().MakeDMesh();
            DMesh3 bore = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 3)) }.Generate().MakeDMesh();
            var diff = new ExactMeshBoolean(outer, bore, ExactMeshBoolean.Operations.Difference);
            if (!diff.Compute())
                throw new InvalidOperationException($"ExactMeshBoolean difference failed: {diff.Status}");

            var pin = new PassagePin(new[] { new Vector3d(0, 0, -2.5), new Vector3d(0, 0, 2.5) }, radius: 0.35);
            var pw = new PassageWrap(diff.Result) {
                Pins = { pin },
                OffsetRatio = 30,
                Simplify = true
            };
            pw.Compute();
            return pw;
        }
    }
}
