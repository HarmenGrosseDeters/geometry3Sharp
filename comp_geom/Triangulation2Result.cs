// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
namespace g3
{
    /// <summary> Outcome of a 2D triangulation. Empty is a valid result (no area), not a failure. </summary>
    public enum Triangulation2Status
    {
        /// <summary> A verified triangulation with at least one triangle. </summary>
        Success,
        /// <summary> A mesh without triangles: fewer than three distinct points, or all collinear. </summary>
        Empty,
        /// <summary> No mesh is published; see <see cref="Triangulation2Result.FailureKind"/>. </summary>
        Failed
    }

    /// <summary> Why no triangulation was published. Invalid arguments and cancellation throw instead. </summary>
    public enum Triangulation2FailureKind
    {
        /// <summary> No failure. </summary>
        None,
        /// <summary> The verification of the finished mesh found a defect (an internal error; never expected). </summary>
        InvalidTopology
    }

    /// <summary> Immutable outcome of a triangulation. Successful and empty results own their mesh. </summary>
    public sealed class Triangulation2Result
    {
        /// <summary> Whether a verified mesh, an empty mesh or no mesh is available. </summary>
        public Triangulation2Status Status { get; }
        /// <summary> None on Success or Empty; otherwise the failure category. </summary>
        public Triangulation2FailureKind FailureKind { get; }
        /// <summary> On failure, a coordinate where the defect was found; null otherwise. </summary>
        public Vector2d? ProblemCoordinate { get; }
        /// <summary> On failure, a description of the defect; null otherwise. </summary>
        public string FailureDetail { get; }
        /// <summary> The mesh on Success or Empty, null on Failed. </summary>
        public TriangulationMesh2 Mesh { get; }

        internal Triangulation2Result(Triangulation2Status status, Triangulation2FailureKind failureKind,
            TriangulationMesh2 mesh, Vector2d? problemCoordinate = null, string failureDetail = null)
        {
            Status = status;
            FailureKind = failureKind;
            Mesh = mesh;
            ProblemCoordinate = problemCoordinate;
            FailureDetail = failureDetail;
        }
    }
}
