// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// <see cref="Reducer"/> on a fixed spatial partition, the parts in parallel (Reducer / Remesher 2.0, T3): the same
    /// quadric edge collapses, but the cost of an expensive guard such as <see cref="Reducer.PreventSelfIntersections"/>
    /// (4 to 5 times the plain reduction on smooth meshes) is shared by the cores. On smooth meshes of 60 to 100k triangles with
    /// that guard it takes 1.3 to 2.4 times the plain serial time instead of 4.0 to 5.1 times (4 cores, ReduceToMaxQuadricError at
    /// 0.3 % of the box diagonal, docs/design/REDUCER_REMESHER_20.md section 9; a closed box with large flat faces 9.7 instead
    /// of 55 times). Opt-in: the plain reduction without such a guard is cheap enough to stay serial.
    ///
    /// How it works (the argument, and the tests that pin each part, are in <c>PartitionedReduction</c>): the bounding box
    /// is cut into <see cref="Slabs"/> slabs along its longest axis, at quantiles of the vertex coordinates, separated by
    /// a gap of one mean edge length. A triangle with all corners in one slab belongs to that slab's part, every other
    /// triangle is frozen. Each part is reduced in its own copy, together with the frozen triangles that its slab could
    /// touch, which stay as they are, and a collapse may not move a vertex out of the slab, so the parts cannot interfere
    /// with each other and the guards of each copy (also the exact self-intersection check) see every triangle that a new
    /// triangle could meet. Two rounds (the second with the cuts shifted by half a slab) are followed by one serial pass on
    /// the whole mesh. The vertex quadrics and the fold-over references are those of the whole mesh at the start.
    ///
    /// Contract:
    ///  - <b>Deterministic</b>: the result depends on the mesh, the settings and <see cref="Configure"/>, never on
    ///    <see cref="MaxThreads"/>, the number of cores or the timing: <see cref="Slabs"/> is a constant. The same
    ///    input gives the same mesh, bit for bit, in every run, and for every similarity that scales by a power of two (as long as
    ///    no coordinate or intermediate quantity is subnormal: a coordinate of 5e-312 loses bits when it is scaled, and the quadric
    ///    terms made of it underflow at one scale and not at the other).
    ///  - It is <b>not</b> the result of one serial <see cref="Reducer"/> run (the collapses happen in another order; the
    ///    triangle count is usually within a few percent), so a caller that needs the serial result uses the Reducer.
    ///  - The target is a maximum quadric error (as <see cref="Reducer.ReduceToMaxQuadricError"/>); there is no triangle
    ///    count target.
    ///  - The input mesh is not modified; the result is a new compact mesh <b>without</b> vertex normals, colours, UVs,
    ///    triangle groups and isolated vertices (also when the serial fallback is used). Vertices that no triangle uses do
    ///    not take part (they are removed from the working copy first, so they do not steer the slabs either).
    ///  - <see cref="Configure"/> may set guards and weights (PreventSelfIntersections, AreaWeightedQuadrics, the quality and
    ///    fold-over settings, EdgeFlipTolerance). It must <b>not</b> set <c>MaxDeviation</c> (the deviation reference of each
    ///    part would be its own mesh, so the bound would add up over the rounds) or external constraints: both are refused
    ///    with an <see cref="ArgumentException"/> before any work (Configure is called once on a scratch reducer to check).
    ///    Projection targets and deviation references cannot be detected and must not be set either (they would be
    ///    applied to meshes with other vertex ids). Use the Reducer for these.
    ///  - <see cref="Configure"/> is called for the reducer of every part and of the last pass (and once more on the whole
    ///    mesh to compute the quadrics), from the calling thread and from the threads of the parts, so it must be thread-safe
    ///    and give the same settings every time. Every reducer runs with <c>PreserveBoundaryShape = false</c> (set after <see cref="Configure"/>): the
    ///    boundary of an open mesh is not kept in shape (the plain Reducer keeps it by default); a caller that needs that
    ///    uses the Reducer.
    ///  - <see cref="CancelF"/> is called from several threads at the same time (and between the rounds). When it returns
    ///    true, the mesh reduced so far is returned (a valid mesh, not the full reduction). It is also given to every
    ///    reducer as <c>Progress</c>, replacing one set by <see cref="Configure"/>.
    ///  - An exception thrown while reducing on the partition path (in a part, or by Configure) surfaces as an
    ///    <see cref="AggregateException"/> for every <see cref="MaxThreads"/> (the serial fallback throws the original).
    ///  - Falls back to one serial Reducer run (same settings) when the mesh has fewer than
    ///    <see cref="MinTriangles"/> triangles (the partition does not pay below about 20k; default 20000), and when the
    ///    slabs cannot be kept apart (the gap is lost in the rounding of the coordinates). <see cref="UsedPartition"/> tells.
    ///  - The instance keeps no state between calls except the outputs; it is not thread-safe.
    /// Sources: Garland and Heckbert 1997 (quadrics); the partition with frozen borders is the approach of out-of-core
    /// simplification (P. Lindstrom, SIGGRAPH 2000; Ozaki, Kyota, Kanai 2015), here with fixed slabs for determinism
    /// (docs/SOURCES.md).
    ///
    /// Supported input range (docs/QA.md 2.5a): as the Reducer, every nonzero coordinate has a magnitude in [2^-200, 2^200]; no subnormal coordinates (the slab
    /// boxes are not exact there). The result is not invariant under scaling (the slabs and the frozen borders have
    /// absolute tolerances), see above.
    /// </summary>
    public sealed class PartitionedReducer
    {
        /// <summary> number of slabs, a constant: deriving it from the cores would make the result depend on the machine </summary>
        public static readonly int Slabs = 8;

        /// <summary> threads for the parts; 0 or less: the default of the platform. 1 is the serial reference of the same result. </summary>
        public int MaxThreads = 0;

        /// <summary> sets the options of every Reducer (parts and last pass); see the class contract </summary>
        public Action<Reducer> Configure;

        /// <summary> cancel: see the class contract (called from several threads) </summary>
        public Func<bool> CancelF;

        /// <summary> meshes with fewer triangles are reduced by one serial Reducer run (default 20000) </summary>
        public int MinTriangles = 20000;

        /// <summary> output: true if the last Reduce used the partition, false if it fell back to one serial run </summary>
        public bool UsedPartition { get; private set; }

        /// <summary> (tests) gap between the slabs in mean edge lengths; 0 makes the slabs inseparable </summary>
        internal double GapEdgeLengths = 1.0;

        // a compact copy without vertex normals, colours, UVs, triangle groups and isolated vertices
        static DMesh3 Plain(DMesh3 m)
        {
            DMesh3 c = new DMesh3(m, true, MeshComponents.None);
            if (c.HasTriangleGroups) c.DiscardTriangleGroups();
            RemoveIsolatedVertices(c);
            return c.IsCompact ? c : new DMesh3(c, true, MeshComponents.None);
        }

        static void RemoveIsolatedVertices(DMesh3 m)
        {
            foreach (int vid in m.VertexIndices().ToArray())
                if (m.GetVtxEdgeCount(vid) == 0)
                    m.RemoveVertex(vid);
        }

        /// <summary>
        /// Reduce a copy of mesh until no allowed collapse stays within the quadric error maxQuadricError (a squared
        /// distance, as <see cref="Reducer.ReduceToMaxQuadricError"/>, eg the square of 0.003 times the box diagonal; with the default
        /// <see cref="Reducer.AreaWeightedQuadrics"/> the error is weighted by triangle areas, so set it to false in <see cref="Configure"/> for a
        /// squared distance that scales with the mesh).
        /// Returns a new compact mesh; the input is not modified.
        /// </summary>
        public DMesh3 Reduce(DMesh3 mesh, double maxQuadricError)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));
            if (double.IsNaN(maxQuadricError) || maxQuadricError < 0)
                throw new ArgumentOutOfRangeException(nameof(maxQuadricError), "must be a number >= 0");
            UsedPartition = false;
            // (the options the partition cannot honour are refused before any work, on the calling thread)
            var probe = new Reducer(new DMesh3());
            Configure?.Invoke(probe);
            if (probe.MaxDeviation > 0)
                throw new ArgumentException("Configure must not set MaxDeviation: the partitioned parts would each take their own mesh as the reference");
            if (probe.Constraints != null)
                throw new ArgumentException("Configure must not set external constraints: the parts and the last pass work on meshes with other vertex ids");
            if (mesh.TriangleCount == 0)
                return Plain(mesh);

            Action<Reducer> configure = (r) => {
                Configure?.Invoke(r);
                r.PreserveBoundaryShape = false;
                if (CancelF != null) r.Progress = new ProgressCancel(CancelF);
            };
            // (a copy without the vertices no triangle uses: they would steer the axis and the cuts of the slabs)
            DMesh3 work = new DMesh3(mesh, true, MeshComponents.None);
            RemoveIsolatedVertices(work);
            if (work.TriangleCount >= MinTriangles) {
                var pr = new PartitionedReduction() { Slabs = Slabs, MaxThreads = MaxThreads, Configure = configure, GapEdgeLengths = GapEdgeLengths };
                if (CancelF != null) pr.CancelF = CancelF;
                DMesh3 result;
                try {
                    result = pr.Reduce(work, maxQuadricError);
                } catch (AggregateException) {
                    throw;
                } catch (Exception e) {
                    // (with one thread the parts run in a loop and the original escapes; the same type for every MaxThreads)
                    throw new AggregateException(e.Message, e);
                }
                // (pr has done the one serial run itself if the slabs could not be kept apart)
                UsedPartition = pr.SerialFallbacks == 0;
                return Plain(result);
            }
            var reducer = new Reducer(work);
            configure(reducer);
            reducer.ReduceToMaxQuadricError(maxQuadricError);
            return Plain(work);
        }
    }
}
