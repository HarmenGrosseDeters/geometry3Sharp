namespace g3
{
    /// <summary>Outcome of a point-set triangulation; Empty is a valid result, not a failure.</summary>
    public enum Triangulation2Status
    {
        /// <summary>A verified nonempty triangulation is available.</summary>
        Success,
        /// <summary>An owned mesh with no faces is available for empty or collinear input.</summary>
        Empty,
        /// <summary>No mesh is published.</summary>
        Failed
    }

    /// <summary>Reason no triangulation was published. Invalid arguments and cancellation instead throw.</summary>
    public enum Triangulation2FailureKind
    {
        /// <summary>No failure.</summary>
        None,
        /// <summary>A numerical operation could not be completed; reserved for diagnostics.</summary>
        NumericalFailure,
        /// <summary>Legalization could not make progress.</summary>
        IterationLimit,
        /// <summary>Construction or verification found invalid topology.</summary>
        InvalidTopology
    }

    /// <summary>Immutable triangulation outcome. Successful and empty results own their mesh.</summary>
    public sealed class Triangulation2Result
    {
        /// <summary>Whether a verified mesh, an empty mesh or no mesh is available.</summary>
        public Triangulation2Status Status { get; }
        /// <summary>None on Success or Empty; otherwise the failure category.</summary>
        public Triangulation2FailureKind FailureKind { get; }
        /// <summary>Optional offending coordinate; null when no specific coordinate is diagnosed.</summary>
        public Vector2d? ProblemCoordinate { get; }
        /// <summary>Optional offending input slot; null when no specific slot is diagnosed.</summary>
        public int? ProblemInputIndex { get; }
        /// <summary>Owned read-only mesh on Success or Empty, null on Failed.</summary>
        public TriangulationMesh2 Mesh { get; }

        internal Triangulation2Result(Triangulation2Status status, Triangulation2FailureKind failureKind,
            TriangulationMesh2 mesh, Vector2d? problemCoordinate = null, int? problemInputIndex = null)
        {
            Status = status;
            FailureKind = failureKind;
            Mesh = mesh;
            ProblemCoordinate = problemCoordinate;
            ProblemInputIndex = problemInputIndex;
        }
    }
}
