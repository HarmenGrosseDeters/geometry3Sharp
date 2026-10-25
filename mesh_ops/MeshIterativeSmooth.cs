using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Explicit iterative Laplacian smoothing of a set of vertices, in place: in each of Rounds rounds every
    /// listed vertex moves the fraction Alpha towards its smoothed position (one-ring centroid for Uniform,
    /// or cotangent / mean-value weighted), computed from the positions of the previous round (Jacobi style,
    /// so the result does not depend on the order or the threads). Vertices not in the list do not move, so
    /// they act as fixed constraints, eg pass only the interior vertices to keep the boundary.
    /// Each round shrinks the surface a little (no volume preservation); ProjectF can project the smoothed
    /// positions back onto a surface. Smooth() always returns true.
    /// For a global solve with soft constraints see <see cref="LaplacianMeshSmoother"/>.
    /// </summary>
    public class MeshIterativeSmooth
    {
        /// <summary> the mesh (modified in place by Smooth()) </summary>
        public DMesh3 Mesh;
        /// <summary> the vertices that are smoothed; all others stay fixed </summary>
        public int[] Vertices;

        /// <summary> step per round, clamped to [0,1]: fraction of the way to the smoothed position. Default 0.25 </summary>
        public double Alpha = 0.25f;
        /// <summary> number of smoothing rounds (clamped to [0,10000]). Default 10 </summary>
        public int Rounds = 10;

		/// <summary> smoothing weights: uniform (one-ring centroid), cotangent or mean-value </summary>
		public enum SmoothTypes {
			Uniform, Cotan, MeanValue
		};
		/// <summary> smoothing weights, default Uniform </summary>
		public SmoothTypes SmoothType = SmoothTypes.Uniform;

        /// <summary>
        /// optional: reproject smoothed position to new location, called each round as
        /// (smoothed position, Vector3f.AxisY (placeholder normal), vertex id) -> new position
        /// </summary>
        public Func<Vector3d, Vector3f, int, Vector3d> ProjectF;

        Vector3d[] SmoothedPostions;

        /// <summary> smooth the given vertices of mesh; the array is copied unless bOwnVertices </summary>
        public MeshIterativeSmooth(DMesh3 mesh, int[] vertices, bool bOwnVertices = false)
        {
            Mesh = mesh;
            Vertices = (bOwnVertices) ? vertices : (int[])vertices.Clone();

            SmoothedPostions = new Vector3d[Vertices.Length];

            ProjectF = null;
        }


        public virtual ValidationStatus Validate()
        {
            return ValidationStatus.Ok;
        }


        /// <summary> run Rounds smoothing rounds on Vertices, modifying the mesh in place. Always returns true </summary>
        public virtual bool Smooth()
        {
            int NV = Vertices.Length;

            double a = MathUtil.Clamp(Alpha, 0, 1);
            double num_rounds = MathUtil.Clamp(Rounds, 0, 10000);

            Func<DMesh3, int, double, Vector3d> smoothFunc = MeshUtil.UniformSmooth;
            if (SmoothType == SmoothTypes.MeanValue)
                smoothFunc = MeshUtil.MeanValueSmooth;
            else if (SmoothType == SmoothTypes.Cotan)
                smoothFunc = MeshUtil.CotanSmooth;

            Action<int> smooth = (i) => {
                int vID = Vertices[i];
                SmoothedPostions[i] = smoothFunc(Mesh, vID, a);
            };
            Action<int> project = (i) => {
                Vector3d pos = SmoothedPostions[i];
                SmoothedPostions[i] = ProjectF(pos, Vector3f.AxisY, Vertices[i]);
            };

            IndexRangeEnumerator indices = new IndexRangeEnumerator(0, NV);

            for (int round = 0; round < num_rounds; ++round) {

                gParallel.ForEach<int>(indices, smooth);
                if ( ProjectF != null )
                    gParallel.ForEach<int>(indices, project);

                // bake
                for (int i = 0; i < NV; ++i)
                    Mesh.SetVertex(Vertices[i], SmoothedPostions[i]);
            }

            return true;
        }
    }




}
