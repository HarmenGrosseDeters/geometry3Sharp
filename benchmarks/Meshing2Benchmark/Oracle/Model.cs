using System;
using System.Collections.Generic;

namespace Meshing2Acceptance
{
    public sealed class SegmentInput
    {
        public int A, B, Marker;
        public bool Locked;
        public SegmentInput(int a, int b, int marker = 0, bool locked = false) { A = a; B = b; Marker = marker; Locked = locked; }
    }

    public sealed class RingInput
    {
        public int[] Vertices; // a closing vertex equal to the first is allowed (GIS convention), either orientation
        public bool Hole;
        public int Marker;
        public RingInput(int[] vertices, bool hole, int marker = 0) { Vertices = vertices; Hole = hole; Marker = marker; }
    }

    public sealed class RegionInput
    {
        public int[] Ring;    // the region by ring, or
        public double[] Seed; // by a seed point {x, y}
        public int Label;
        public double? MaxArea;
    }

    /// <summary>What goes into a mesher: plain data, one type for all milestones.</summary>
    public sealed class Mesh2Input
    {
        public double[] X = new double[0], Y = new double[0];
        public List<SegmentInput> Segments = new List<SegmentInput>();
        public List<RingInput> Rings = new List<RingInput>();
        public List<double[]> HoleSeeds = new List<double[]>();
        public List<RegionInput> Regions = new List<RegionInput>();
        public double? MinAngleDeg, MaxArea;
        public int? SteinerLimit;
        public double SnapTolerance;     // 0 = automatic
        public bool ResolveCrossings = true;
        public bool Conforming;          // O2: empty circumcircles against all vertices, also across segments
        /// <summary>Longest edge bound at the centroid; the meaning of the size function is decision D9 of MESHING2D_DECISIONS.md.</summary>
        public Func<Q, Q, Q> SizeFunction;
        /// <summary>The size function in the text format, which has only constants so far; a function without it cannot be written.</summary>
        public double? SizeConstant;

        public int Count => X.Length;
    }

    public enum Mesh2Status { Success, Empty, Failed }

    public sealed class SubsegmentOutput
    {
        public int A, B, Marker;
        public SubsegmentOutput(int a, int b, int marker = 0) { A = a; B = b; Marker = marker; }
    }

    /// <summary>What comes out of a mesher (ours or a foreign one). Null lists mean "the producer does not report this".</summary>
    public sealed class Mesh2Output
    {
        public Mesh2Status Status;
        public string FailureKind;
        public double[] VX = new double[0], VY = new double[0];
        /// <summary>Optional exact vertices: when a producer has them, the oracle judges these, not the doubles.</summary>
        public Pt[] ExactVertices;
        public int[][] Triangles = new int[0][];
        public int[][] Neighbours;
        public int[] InputToVertex;
        public List<SubsegmentOutput> Subsegments;
        public int[] TriangleLabels;
        public int[] BadTriangles;
        public bool SteinerLimitReached;
        /// <summary>Point location of the result, -1 if outside; null if the producer has none.</summary>
        public Func<double, double, int> Locate;
        /// <summary>
        /// False for a foreign producer that numbers vertices its own way: the identity rules (Q8: input i is vertex i, new
        /// ids at or above the input count) are then not judged and the input mapping is derived from the coordinates.
        /// </summary>
        public bool IdentityRules = true;

        public int VertexCount => VX.Length;
    }

    /// <summary>A mesher under judgement, ours or a reference. The oracle's project knows only this interface.</summary>
    public interface IMeshing2Candidate
    {
        string Name { get; }
        Mesh2Output Run(Mesh2Input input);
    }

    public enum Violation
    {
        StatusInconsistent, EmptyOnArea, TrianglesOnDegenerate, NonFinite, BadIndex, NotPositive, EdgeRepeated, NeighbourWrong,
        VertexInTriangle, EdgesCross, InputMoved, MappingWrong, AliasUsed, VertexUnused, SteinerNotAllowed,
        HullBoundary, AreaMismatch, NotDelaunayLocal, NotDelaunayGlobal, SegmentMissing, SubsegmentNotEdge, SubsegmentUnsourced,
        SubsegmentMarker, LockedSplit, BoundaryNotConstrained, TriangleOutsideDomain, RemovedPointCovered, KeptPointMissing,
        RegionLabelWrong, AngleBelowBound, BadListWrong, AreaAboveBound, SizeAboveBound, SteinerLimitExceeded, SteinerFlagWrong,
        LocateWrong, SnapTooFar, MissingReport, InputInvalid, CountOutOfRange, DoublesNotRounded, CannotJudge
    }

    public sealed class ViolationRecord
    {
        public Violation Kind; public string Detail;
        public override string ToString() => Kind + ": " + Detail;
    }

    /// <summary>What the caller of the oracle knows about a case beyond the input.</summary>
    public sealed class OracleExpect
    {
        /// <summary>Area stated by the specification, compared with the mesh area on top of the area the oracle computes itself.</summary>
        public Q? StatedArea;
        public Q StatedAreaTolerance = Q.Zero;
        /// <summary>Radius of the tube around an input segment in which its chain may run; null = automatic (Oracle.AutoTolerance), 0 = exactly on it.</summary>
        public Q? Tube;
        public bool MayMoveVertices;
        /// <summary>Points that must lie in no triangle (hole interiors, removed seeds) and points that must lie in one.</summary>
        public List<double[]> Removed = new List<double[]>(), Kept = new List<double[]>();
        /// <summary>Upper limit of triangles times vertices for the global empty-circumcircle pass; beyond it the local check decides alone.</summary>
        public long GlobalDelaunayLimit = 3000000;
        /// <summary>A Failed status is required (F-9: crossings with noding off); otherwise it is a violation.</summary>
        public bool ExpectFailure;
        /// <summary>A valid mesh is all that is promised near precision limits (F-26) and above about 38 degrees (O3): the list of bad triangles is still checked.</summary>
        public bool AngleGuaranteed = true;
        /// <summary>The minimum angle of the input is invalid on purpose (F-30): it must not disable the other constraints, and is not itself judged.</summary>
        public bool InvalidAngle;
        /// <summary>Counts the specification states for a case (F-16, F-20, F-24, F-25).</summary>
        public int? MinTriangles, MaxVertices;
        /// <summary>Which milestone's rules apply: M1 forbids Steiner points and judges the plain Delaunay property.</summary>
        public Milestone Milestone = Milestone.M1;
    }

    public enum Milestone { M1, M2, M3 }

    /// <summary>The verdict and the exact facts of a result, which also feed the golden rows.</summary>
    public sealed class OracleReport
    {
        public readonly List<ViolationRecord> Violations = new List<ViolationRecord>();
        public bool Ok => Violations.Count == 0;
        /// <summary>No free interior edge has four cocircular points: the (constrained) Delaunay triangulation is unique.</summary>
        public bool Unique = true;
        public int TieEdges;
        public Q Area2 = Q.Zero;
        public Q? MinSin2;
        public int ConstructedVertices;
        public bool GlobalDelaunayChecked;
        public int BelowAngle;
        public bool Has(Violation v) { foreach (var r in Violations) if (r.Kind == v) return true; return false; }
        public void Add(Violation v, string detail) { if (Violations.Count < 200) Violations.Add(new ViolationRecord { Kind = v, Detail = detail }); }
        public override string ToString() => Ok ? "ok" : string.Join("; ", Violations);
    }
}
