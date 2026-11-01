// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary> An input segment between two input points, with a marker; locked segments are never split by refinement. </summary>
    public sealed class Mesh2Segment
    {
        /// <summary> Input point indices of the end points. </summary>
        public int A, B;
        /// <summary> The marker its subsegments carry (0 = none). </summary>
        public int Marker;
        /// <summary> Refinement may not split it (spec O9); crossings with other constraints still do. </summary>
        public bool Locked;
        public Mesh2Segment(int a, int b, int marker = 0, bool locked = false) { A = a; B = b; Marker = marker; Locked = locked; }
    }

    /// <summary>
    /// A closed ring of input points, in either orientation; a closing point equal to the first is allowed (GIS convention).
    /// Inside means an odd number of crossings (even-odd), so a self-intersecting ring is meshed as the union of its odd parts.
    /// </summary>
    public sealed class Mesh2Ring
    {
        public int[] Vertices;
        /// <summary> A hole removes everything inside it, including constraints (spec Q7). </summary>
        public bool Hole;
        public int Marker;
        public Mesh2Ring(int[] vertices, bool hole = false, int marker = 0) { Vertices = vertices; Hole = hole; Marker = marker; }
    }

    /// <summary> A region with a label, by ring or by a seed point (the cell of the constraints that contains it). </summary>
    public sealed class Mesh2Region
    {
        /// <summary> The region's ring of input points (its edges become constraints), or null for a seed region. </summary>
        public int[] Ring;
        /// <summary> The seed point of a seed region. </summary>
        public Vector2d Seed;
        public int Label;
        /// <summary> Maximum triangle area in the region (refinement only), or null. </summary>
        public double? MaxArea;
        public static Mesh2Region ByRing(int[] ring, int label, double? maxArea = null) => new Mesh2Region { Ring = ring, Label = label, MaxArea = maxArea };
        public static Mesh2Region BySeed(Vector2d seed, int label, double? maxArea = null) => new Mesh2Region { Seed = seed, Label = label, MaxArea = maxArea };
    }

    /// <summary>
    /// The input of the constrained mesher: points (index i is the identity of point i), segments, rings, hole seeds, regions.
    /// Without an outer ring the domain is the convex hull of the points minus the holes; with outer rings a point is in it iff
    /// the innermost ring containing it is an outer ring, so islands in lakes are meshed (decision D5).
    /// </summary>
    public sealed class ConstrainedMesh2Input
    {
        public List<Vector2d> Points = new List<Vector2d>();
        public List<Mesh2Segment> Segments = new List<Mesh2Segment>();
        public List<Mesh2Ring> Rings = new List<Mesh2Ring>();
        /// <summary> Points inside holes that have no ring: the cell of the constraints around each is removed if it is bounded. </summary>
        public List<Vector2d> HoleSeeds = new List<Vector2d>();
        /// <summary> In order: where regions overlap, the last one wins. </summary>
        public List<Mesh2Region> Regions = new List<Mesh2Region>();
        /// <summary> Optional heights per input point (null: none). </summary>
        public List<double> Heights;
    }

    /// <summary> A piece of input constraints in the mesh. </summary>
    public struct Mesh2Subsegment
    {
        /// <summary> Vertex ids. </summary>
        public int A, B;
        /// <summary> The nonzero marker of its sources if they agree, otherwise 0 (decision D7); all of them are in <see cref="Markers"/>. </summary>
        public int Marker;
        /// <summary> The distinct nonzero markers of its sources, ascending. </summary>
        public int[] Markers;
        /// <summary> One of its sources is a locked segment (decision D8). </summary>
        public bool Locked;
    }
}
