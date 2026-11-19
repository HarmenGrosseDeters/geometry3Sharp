// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace g3
{
    /// <summary> How <see cref="ContourLoftStack.Compute"/> ended; anything but Success has no mesh. </summary>
    public enum ContourLoftStackStatus
    {
        /// <summary> <see cref="ContourLoftStackResult.Mesh"/> is the lofted surface, checked (docs/design/CONTOUR_LOFT.md section 10). </summary>
        Success,
        /// <summary> Fewer than two slices, a contour with fewer than 3 vertices, a vertex that is not finite or repeated, zero area, planes not increasing. </summary>
        InvalidInput,
        /// <summary> Two contours of one slice touch or cross (or a contour touches itself). </summary>
        CrossingContours,
        /// <summary> A case stage 2 does not build (reported, never a wrong mesh): a terminal contour with a child that continues, a branching or merging group whose holes have islands, an intersection that touches itself. </summary>
        Unsupported,
        /// <summary> No vertex pair of two contours to be joined by a bridge sees each other. </summary>
        BridgeNotFound,
        /// <summary> Every band of a pair of contours has an inadmissible triangle. </summary>
        NoAdmissibleBand,
        /// <summary> A stage 1 band failed or the pieces do not fit (non-manifold, not clean). </summary>
        BandFailed,
        /// <summary> The assembled mesh intersects itself (exact check). </summary>
        SelfIntersecting,
        /// <summary> A cap mesh failed or did not keep a contour's vertices and edges. </summary>
        CapFailed,
        /// <summary> A custom cost was not finite. </summary>
        InvalidCost,
        /// <summary> A band's m * n is above MaxCells. </summary>
        TooLarge
    }

    /// <summary> What a group of related contours of two consecutive slices became. </summary>
    public enum ContourLoftGroupKind
    {
        /// <summary> One contour below, one above: a stage 1 band. </summary>
        Band,
        /// <summary> One below, several above. </summary>
        Branch,
        /// <summary> Several below, one above. </summary>
        Merge,
        /// <summary> Several on both sides. </summary>
        Complex,
        /// <summary> A contour of the lower slice with no counterpart above: it ends in a cap. </summary>
        Vanishing,
        /// <summary> A contour of the upper slice with no counterpart below: it starts with a cap. </summary>
        Appearing
    }

    /// <summary> One group of a slab (the contours of slice <see cref="Slab"/> and Slab + 1 that overlap). </summary>
    public sealed class ContourLoftGroupInfo
    {
        /// <summary> Index of the lower slice of the slab. </summary>
        public int Slab { get; internal set; }
        public ContourLoftGroupKind Kind { get; internal set; }
        /// <summary> Indices (in the slice's contour list) of the group's contours in slice Slab. </summary>
        public IReadOnlyList<int> Lower { get; internal set; }
        /// <summary> Indices of the group's contours in slice Slab + 1. </summary>
        public IReadOnlyList<int> Upper { get; internal set; }
    }

    /// <summary> Result of <see cref="ContourLoftStack.Compute"/>. </summary>
    public sealed class ContourLoftStackResult
    {
        public ContourLoftStackStatus Status { get; internal set; }
        /// <summary> Why the status is not Success (empty on Success). </summary>
        public string Reason { get; internal set; } = "";
        /// <summary>
        /// Only on Success. Vertices 0 .. (number of input vertices - 1) are the input vertices in input order (slice, contour, vertex), then the
        /// vertices of intermediate contours, then cap Steiner points.
        /// </summary>
        public DMesh3 Mesh { get; internal set; }
        /// <summary> The groups of every slab, in the order they were built (empty before the failing slab). </summary>
        public IReadOnlyList<ContourLoftGroupInfo> Groups { get; internal set; } = new ContourLoftGroupInfo[0];
    }

    /// <summary>
    /// Contour lofting through a stack of slices with several contours per slice (stage 2 of docs/design/CONTOUR_LOFT.md section 10):
    /// contours of consecutive slices are related when their faces overlap; one-to-one pairs are stage 1 bands, a contour that splits
    /// or joins is tiled through bridged composite contours (at an intermediate plane when the pieces are cut by the overlap), a
    /// contour without a counterpart ends in a planar cap, holes loft like outer contours (tunnels).
    /// </summary>
    /// <remarks>
    /// <para>A slice is a list of closed contours in one plane, not touching or crossing; nesting follows from containment (even
    /// depth: solid, odd: air). Slices are given in order along their common normal (the planes must be increasing along it).
    /// The result is a closed, manifold, consistently oriented (outward) mesh when <see cref="CapEnds"/> is set; otherwise the
    /// first and last slice stay open. Self-intersections are checked exactly and reported, not excluded.</para>
    /// <para>Branching and merging: a group of several contours is decomposed through the pieces in which the contours overlap; the
    /// pieces lie in an intermediate plane at <see cref="BranchFraction"/> of the slab and a set of pieces is joined by zero-width
    /// bridges into one contour, which the stage 1 search tiles against the single contour on its other side. The surface has a
    /// ridge at the bridge (the known look of composite contours), no medial axis is used; <see cref="LoweredSaddle"/> lowers the crotch of a group whose pieces are the contours of one side.</para>
    /// <para>A slice is taken as planar: its height is the mean of its vertices' heights and the 2D work (nesting, overlay) uses the
    /// projection along the stacking direction; the exact check of the assembled mesh protects against slices that are not.</para>
    /// <para>Limits (status Unsupported, never a wrong mesh): a hole of a branching or merging solid group may have no island inside it; a
    /// contour that ends must have only children that end too. A hole of a branching or merging group is lofted through the group's intermediate plane
    /// (docs/design/CONTOUR_LOFT.md section 10.10); where its pieces lie in different solid pieces there is no bridge (BridgeNotFound). Serial and deterministic.</para>
    /// </remarks>
    public sealed class ContourLoftStack
    {
        /// <summary> Forwarded to every band; the I and J of the cost delegates' triangles index the band's own contours (a composite contour for a branch). </summary>
        public ContourLoftCost Cost = ContourLoftCost.MinimumArea;
        public Func<ContourLoftTriangle, double> CustomCost;
        public Func<ContourLoftTriangle, bool> IsAdmissible;
        public bool NormalizeForCost;
        /// <summary> Check the whole mesh for self-intersections, exactly (the bands are not checked one by one). </summary>
        public bool CheckSelfIntersections = true;
        /// <summary> Largest m * n of a band (memory). </summary>
        public long MaxCells = 1L << 22;
        /// <summary> Position of the intermediate plane of a branching or merging group between its slices, in (0, 1). </summary>
        public double BranchFraction = 0.5;
        /// <summary> Add caps on the first and the last slice (a closed solid). Contours that end between slices are always capped. </summary>
        public bool CapEnds;
        /// <summary>
        /// Opt-in (default false, the results are then unchanged): a branching or merging group whose contours are contained in one contour of
        /// the other slice (no intermediate plane) is lofted as a roof over the region between them instead of a band through bridged composite
        /// contours: the crotch between the branches is lowered (docs/design/CONTOUR_LOFT.md section 10.11). Groups with holes, cut pieces
        /// and contours that touch keep the bridged composite.
        /// </summary>
        public bool LoweredSaddle;
        /// <summary> Mesher of the roof of <see cref="LoweredSaddle"/> (contour edges are locked); null: <c>new QualityMesh2()</c> (minimum angle 20 degrees). Not changed by the loft. </summary>
        public QualityMesh2 SaddleMesher;
        /// <summary> Options of the cap meshes (not changed by the loft); null: a constrained Delaunay triangulation without refinement. </summary>
        public QualityMesh2 CapMesher;

        /// <exception cref="ArgumentNullException">slices or a contour is null.</exception>
        /// <exception cref="ArgumentException">Custom cost without CustomCost, MaxCells not positive, BranchFraction not in (0, 1).</exception>
        public ContourLoftStackResult Compute(IList<IList<IList<Vector3d>>> slices, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (slices == null) throw new ArgumentNullException(nameof(slices));
            if (Cost == ContourLoftCost.Custom && CustomCost == null) throw new ArgumentException("Cost is Custom but CustomCost is null");
            if (MaxCells <= 0) throw new ArgumentException("MaxCells must be positive");
            if (!(BranchFraction > 0 && BranchFraction < 1)) throw new ArgumentException("BranchFraction must be in (0, 1)");
            foreach (var s in slices) {
                if (s == null) throw new ArgumentNullException("a slice is null");
                foreach (var c in s) if (c == null) throw new ArgumentNullException("a contour is null");
            }
            return new ContourLoftStackRun(this, slices, cancellationToken).Execute();
        }
    }
}
