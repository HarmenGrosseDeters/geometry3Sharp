// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;

namespace gs
{
    /// <summary> one run of rim edges of a sheet inside the workpiece (a maximal chain of free edges that meet its interior) </summary>
    public sealed class SheetExtensionRun
    {
        /// <summary> number of the run, from 0 </summary>
        public int Id;
        /// <summary> number of free edges of the run </summary>
        public int Edges;
        /// <summary> the free edges of the run: edge ids of the sheet </summary>
        public List<int> EdgeIds = new List<int>();
        /// <summary> total length of the free edges of the run (the whole edge, also the part of it outside the workpiece) </summary>
        public double Length;
        /// <summary>
        /// the length of the run that lies inside the workpiece, measured per rim edge: the parts of each free edge in the workpiece's interior (an edge can
        /// be inside while both its ends are outside), summed
        /// </summary>
        public double InsideLength;
        /// <summary> InsideLength relative to the sheet's diagonal (<see cref="SheetExtensionReport.Diagonal"/>) </summary>
        public double InsideLengthRelative;
        /// <summary>
        /// the largest depth of the rim in the workpiece along the direction of the construction (the distance to the first exit of the ray of a rim point),
        /// absolute and relative to the sheet's diagonal. After Compute() it is the largest depth of any point of the strip's outer polyline or of the samples; after
        /// Diagnose() the largest of the sampled points (a lower bound of the largest depth of the rim)
        /// </summary>
        public double MaxDepth, MaxDepthRelative;
        /// <summary>
        /// the mean depth over the part of the rim that is inside (the integral of the depth, divided by InsideLength), absolute and relative to the diagonal, from the same samples
        /// in Diagnose() and Compute() (DiagnosisSamples midpoints of each inside stretch of a rim edge and the rim vertices that are inside: an estimate, not a bound)
        /// </summary>
        public double MeanDepth, MeanDepthRelative;
        /// <summary> a ray of the run leaves the workpiece nowhere ("no exit"); Diagnose() only (Compute() refuses with Status NoExit) </summary>
        public bool NoExit;
        /// <summary> NoExit or DepthAboveLimit: the ray of some rim point has no exit within the limit (the rim is too deep to be continued) </summary>
        public bool NoExitWithinLimit { get { return NoExit || DepthAboveLimit; } }
        /// <summary> a depth of the run is above the limit (<see cref="MeshSheetExtension.MaxExtension"/> times the diagonal) </summary>
        public bool DepthAboveLimit;
        /// <summary> an exit angle of the run is not above the tilt plus the margin (<see cref="MeshSheetExtension.DeltaMin"/>, <see cref="MeshSheetExtension.AngleMargin"/>) </summary>
        public bool GrazingExit;
        /// <summary> the area of the strip inside the workpiece (after Compute() only, NaN otherwise) </summary>
        public double StripAreaInside = double.NaN;
        /// <summary> the smallest exit angle alpha and the largest tilt delta of the run, radians (NaN / 0 if no ray) </summary>
        public double MinAlpha = double.NaN, MaxDelta;
        /// <summary>
        /// the direction estimate U of the run, a volume: a first-order ESTIMATE of how much the cut would change if the true continuation differed from the
        /// straight one by the sheet's own turning (<c>(1/2) g^2 sin(delta) sin(alpha) / sin(alpha - delta)</c> per unit of rim, summed over the run; docs/design/SHEET_EXTENSION.md
        /// section 5). It is not a bound: it assumes a straight alternative and a locally flat surface
        /// </summary>
        public double U;
        /// <summary> U relative to the workpiece's volume (always, NaN if the volume is 0) and relative to the smaller of the two parts of the cut (after a cut only, NaN otherwise) </summary>
        public double URelativeToWorkpiece = double.NaN, URelative = double.NaN;
        /// <summary> after Compute(): the connected components of the strip's curve on the workpiece's surface (the curve rule needs 1) and the number of distinct curve ids; -1 before </summary>
        public int CurveComponents = -1, CurveIds = -1;
        /// <summary> the status that refused this run, or Success </summary>
        public MeshSheetExtension.ExtensionStatus Refusal;
        /// <summary> in words: why this run is refused, or (Diagnose()) where its rays have no exit or the depth is above the limit; empty if there is nothing to say </summary>
        public string Witness = "";
    }

    /// <summary> what <see cref="MeshSheetExtension"/> found and did (the diagnosis of the rim, and after Compute() the strip) </summary>
    public sealed class SheetExtensionReport
    {
        /// <summary> true: only the diagnosis was made (Diagnose()); no strip was built </summary>
        public bool Diagnosed;
        /// <summary> true: Compute() succeeded and strip triangles were added (false with Status Success: no rim edge meets the workpiece's interior, the sheet is unchanged; false after Diagnose() and after a refusal, where <see cref="Candidate"/> may still hold the strip as built) </summary>
        public bool Extended;
        /// <summary> the diagonal of the sheet's bounding box: the measure of its size for the limits and the relative figures (not rotation invariant; see <see cref="MeshSheetExtension"/>) </summary>
        public double Diagonal;
        /// <summary> total length of all free edges of the sheet </summary>
        public double RimLength;
        /// <summary> the free edges that meet the workpiece's interior (the chosen edges), their total length inside, and that length relative to the diagonal and to the whole rim </summary>
        public int ChosenEdges;
        public double InsideLength, InsideLengthRelative, InsideLengthShareOfRim;
        public List<SheetExtensionRun> Runs = new List<SheetExtensionRun>();
        /// <summary> after Compute(): vertices and triangles added, and the points of subdivision of outer edges among the new vertices </summary>
        public int NewVertices, NewTriangles, SubdivisionPoints;
        /// <summary> the largest depth of any run, absolute and relative to the diagonal </summary>
        public double MaxDepth, MaxDepthRelative;
        /// <summary> the sum of the runs' U (a volume estimate, see <see cref="SheetExtensionRun.U"/>), relative to the workpiece's volume, and after a cut the smaller part of it and U relative to that part </summary>
        public double U, URelativeToWorkpiece = double.NaN, SmallerPart = double.NaN, URelative = double.NaN;
        public int CurveCount, CurveIdCount;
        /// <summary> why Compute() / Diagnose() stopped (the same as <see cref="MeshSheetExtension.Status"/>) and the witness: a text, a point, the free edge and the rim vertex of the sheet it is about (-1: none) </summary>
        public MeshSheetExtension.ExtensionStatus Status;
        public string Witness = "";
        public Vector3d WitnessPoint;
        public bool HasWitnessPoint;
        public int WitnessEdge = -1, WitnessVertex = -1;
        /// <summary> the extended sheet as built, also where a verification refused it (null before the strip exists) </summary>
        public DMesh3 Candidate;
        /// <summary> the sheet role's refusal where Status is SheetRoleRefused </summary>
        public ExactMeshBoolean.SheetRefusal SheetReason;
        public string CutStatus = "";
        /// <summary> the cut that was run as the last check (the sheet role's Difference of the workpiece by the extended sheet; null without <see cref="MeshSheetExtension.VerifyWithSheetRole"/>) </summary>
        public DMesh3 Cut;
        /// <summary> milliseconds: the construction (rim, rays, strip), the exact verification without the imprint, the imprint of the curve rule, the cut; DiagnoseMs for Diagnose() </summary>
        public double ConstructMs, VerifyMs, ImprintMs, CutMs, DiagnoseMs;
    }

    /// <summary>
    /// Continues the free rim of a sheet that ends just short of a closed workpiece's surface up to that surface, so that the sheet role of
    /// <see cref="ExactMeshBoolean"/> (or <see cref="SolidBooleanPipeline"/>) can cut with it. Opt-in: nothing in the library calls it. Design and the spike
    /// it follows: docs/design/SHEET_EXTENSION.md, docs/research/SHEET_EXTENSION_SPIKE_2026-11.md.
    ///
    /// What it builds. For each free edge of the sheet that meets the interior of the workpiece (chosen per edge, not per end: a long rim edge can pass through
    /// the workpiece while both its ends are outside) the rim vertices are continued along a unit direction (the mean of the two outward normals in the planes of
    /// the rim triangles) to the FIRST exit of the ray from the workpiece, plus a small overshoot, so that a rim point reaches the surface where it first
    /// meets it. One quad per rim edge, subdivided along the edge where its outer edge would pass back through the workpiece, oriented like the triangle it
    /// continues. The original triangles of the sheet are not changed: the result is a copy of the sheet (same vertex and triangle ids, same positions)
    /// with triangles and vertices appended; the original ids are guaranteed, the ids of the added ones are not part of the contract.
    ///
    /// What is exact. The construction is in doubles; it only proposes. What decides acceptance is exact: the winding number of the workpiece
    /// (the exact generalized winding number of a closed mesh) and ExactTriangleIntersection / ExactPredicates tests: every new point is
    /// strictly outside the workpiece (winding number 0 and no workpiece triangle contains the point); the curve rule (the strip imprinted by the
    /// workpiece with <see cref="ExactMeshImprint.LabelCurves"/> meets the surface in exactly one connected curve per run, and in no coplanar contact);
    /// the strip meets neither the sheet, nor itself, nor another tool (exact triangle intersection tests); and the sheet role of the exact Boolean accepts the
    /// extended sheet (last, <see cref="VerifyWithSheetRole"/>). Floating point decides only the choice of candidate points, the depth limit, the exit angle
    /// limit and the orientation test of the strip's triangles (a cosine above 1e-9), which are policies and
    /// refuse earlier than the exact checks would. A degenerate situation (a ray through an edge or vertex of the workpiece, a grazing exit) can only
    /// cause a refusal, never a wrong cut. The rim choice (which edges meet the interior) is exact for the points it tests
    /// (the exact winding number and the exact "no triangle contains the point" test for every rim vertex and for the midpoint of each stretch of an edge between its
    /// crossings with the workpiece's faces); the crossings themselves are ray-triangle intersections in doubles, so a stretch narrower than 16 rounding units is not seen.
    /// A misclassification can lead to a refusal by the exact checks, not to a cut they do not see (argued in the spike note, not proven). The cut that accepts the sheet
    /// (<see cref="VerifyWithSheetRole"/>) must also be clean (<see cref="MeshRobustRepair.NotCleanReason"/>, else <see cref="ExtensionStatus.CutNotClean"/>).
    ///
    /// The rules guarantee that the strip's new points are outside, that it meets the workpiece's surface in one connected curve per run and meets nothing else of the
    /// inputs; they do not guarantee that the strip stays in the band up to the first surface the rays meet. A thin neck whose pin stands beyond the first exit and
    /// touches the strip's polyline is cut through, and the curve rule, which is topological, accepts it when the curve stays connected (docs/design/SHEET_EXTENSION.md, section 5,
    /// the example of a pin on a neck). New triangles take the triangle group of the rim triangle they continue; new vertices copy the normal, colour and uv of the rim vertex
    /// they come from (the nearer end of the rim edge for a point of subdivision).
    ///
    /// What it does not guarantee. That the sheet was meant to go through (intent: no rule can decide it, hence opt-in); that a curved sheet continues with its
    /// curvature (each rim triangle is continued in its plane; <see cref="SheetExtensionRun.U"/> estimates what that costs, as an estimate, not a bound);
    /// anything for a sheet that ends well inside the workpiece (refused: <see cref="ExtensionStatus.DepthAboveLimit"/>, a slit or a pocket wall is no sheet to
    /// continue) or for a body with a missing face (no convention: shape cannot tell it from a sheet, the caller states the role). A sheet with an edge used by more
    /// than two triangles (by position), a sheet with a seam at a rim edge that meets the workpiece (unwelded duplicate vertices) and an open workpiece are
    /// refused. A rim vertex with more than two rim edges (a bowtie) is refused (<see cref="ExtensionStatus.BowtieVertex"/>): no per-wedge points are built.
    ///
    /// The measure of the sheet's size is the diagonal of its bounding box, which is not rotation invariant (a rotated copy has a diagonal a quarter to a third
    /// smaller): the depth limit and the overshoot cap are shares of it, so a rim close to the limit can be continued in one frame and refused in another. The
    /// strip at a corner of a curved rim is a non-planar quad whose triangulation follows the points of subdivision. The result is invariant bit for bit under scaling by 2^k and
    /// under repeated runs. Under rotation and translation the outcome can differ near the depth limit (measured against the bounding box diagonal) and where the subdivision
    /// finds or misses a way round an obstacle (a rib on the exit wall is refused in one frame and cut in another); an accepted cut has the volume of the continuation to rounding;
    /// there was no wrong cut in any frame tried.
    ///
    /// Usage: set Sheet and Workpiece, optionally OtherTools (the other tools of the Difference the extended sheet is cut with: the strip may meet none of them) and
    /// the limits, call Compute() (true: Result is the extended sheet, or the sheet unchanged where no rim edge meets the interior; false: Status says why, Report has
    /// the witness). Diagnose() computes the diagnosis alone, without building the strip or running the imprint and the cut: the depth of the rim, the length
    /// of the rim inside, "no exit within the limit" and the estimate U per run, for a caller that wants to say "the rim is N % of the diagonal inside the workpiece"
    /// about a refused cut. Compute() can be called again, also after a refusal. No parallel loops of its own (the boolean and the self-intersection test inside use
    /// theirs and are deterministic).
    /// </summary>
    public class MeshSheetExtension
    {
        /// <summary> result code of Compute() / Diagnose(); one value per refusal reason </summary>
        public enum ExtensionStatus
        {
            /// <summary> Compute(): Result is set (the sheet extended, or unchanged if Report.Extended is false); Diagnose(): the diagnosis is in Report </summary>
            Success = 0,
            /// <summary> Compute() was not called yet </summary>
            NotComputed = 1,
            /// <summary> no sheet or workpiece, an empty mesh, non-finite coordinates, or a coordinate beyond 1e100 in absolute value </summary>
            InvalidInput = 2,
            /// <summary> the workpiece is not closed (<see cref="ExactMeshBoolean.IsWindingClosed"/>) </summary>
            WorkpieceNotClosed = 3,
            /// <summary> an edge of the sheet is used by more than two triangles (by position), or a rim edge that meets the workpiece is a seam (the same positions as another rim edge: unwelded duplicate vertices); the witness names the triangle or edge </summary>
            SheetNotSurface = 4,
            /// <summary> a continued rim vertex is shared by more than two rim edges (a bowtie): refused, no per-wedge points </summary>
            BowtieVertex = 5,
            /// <summary> the two outward normals of a rim vertex cancel, or a rim triangle is degenerate </summary>
            NoDirection = 6,
            /// <summary> the ray of a rim point leaves the workpiece nowhere (defensive: a point inside a closed workpiece always has an exit; only a rounding failure of the ray query could give none, and no input is known that does) </summary>
            NoExit = 7,
            /// <summary> the first exit is farther than MaxExtension times the sheet's diagonal: Report.MaxDepth(Relative) and the witness give the depth </summary>
            DepthAboveLimit = 8,
            /// <summary> the exit angle alpha is not above delta plus the margin (the tilted alternative may not exit at all) </summary>
            GrazingExit = 9,
            /// <summary> a new point is not strictly outside the workpiece (exact: winding number 0 and no workpiece triangle contains it) </summary>
            NewPointNotOutside = 10,
            /// <summary> an outer edge of a quad passes through the workpiece and the subdivision does not get it out </summary>
            OuterEdgeInside = 11,
            /// <summary> a strip triangle is turned against the sheet triangle it continues (the quad folds over: a notch narrower than the depth) </summary>
            InvertedQuad = 12,
            /// <summary> the strip's intersection with the workpiece's surface is not exactly one connected curve per run (a pin or a wall between two rim vertices) </summary>
            CurveRule = 13,
            /// <summary> a strip triangle lies in the plane of a workpiece face and overlaps it (exact) </summary>
            CoplanarContact = 14,
            /// <summary> the strip meets the sheet beyond the shared rim (exact) </summary>
            MeetsSheet = 15,
            /// <summary> the strip meets itself (exact) </summary>
            MeetsItself = 16,
            /// <summary> the strip meets another tool or sheet (exact) </summary>
            MeetsOtherTool = 17,
            /// <summary> the imprint of the curve rule failed (Error) (defensive: the imprint has no budget, no strict rounding and no cancellation here and the input is validated finite; no input is known that fails) </summary>
            ImprintFailed = 18,
            /// <summary> the strip could not be appended to a manifold mesh (defensive: the sheet's rim is a manifold boundary, so strip triangles along it can be appended; no input is known that fails) </summary>
            ConstructionFailed = 19,
            /// <summary> the sheet role refuses the extended sheet (Report.SheetReason, Error) </summary>
            SheetRoleRefused = 20,
            /// <summary> the cut failed for another reason (budget, rounding; Error) (defensive: with finite inputs and AllowOpenMeshes the Boolean answers Success or SheetRefused; its budget and rounding failures are not known to occur here) </summary>
            CutFailed = 21,
            /// <summary> U relative to the smaller part is above MaxUOverSmallerPart </summary>
            UncertaintyAboveLimit = 22,
            /// <summary>
            /// the next surface behind the exit of a ray is closer than twice the least overshoot (1024 rounding units of the largest coordinate, <see cref="MinOvershootUnits"/>): the
            /// strip's new point could not be kept short of it, so it could pass through the wall or fin behind the slot. Refused with the gap in the witness
            /// </summary>
            GapBelowResolution = 23,
            /// <summary> the verification cut is accepted by the sheet role but is not clean (<see cref="MeshRobustRepair.NotCleanReason"/>: self-intersections, open or non-manifold parts); Error has the reason </summary>
            CutNotClean = 24
        }

        // ---------------------------------------------------------------- inputs

        /// <summary> the open sheet (not modified) </summary>
        public DMesh3 Sheet;
        /// <summary> the closed workpiece the sheet cuts (not modified) </summary>
        public DMesh3 Workpiece;
        /// <summary> the limit of the depth of the rim, as a share of the sheet's diagonal (owner decision 2026-11-23: 5 %); a deeper rim is refused </summary>
        public double MaxExtension = 0.05;
        /// <summary> the cap of the overshoot beyond the exit, as a share of the diagonal (default 1 %); the overshoot is half the distance to the next surface, at least a few rounding units </summary>
        public double MaxOvershoot = 0.01;
        /// <summary> the least overshoot, in rounding units of the largest coordinate (default 1024) </summary>
        public double MinOvershootUnits = 1024;
        /// <summary> the least tilt delta of an alternative continuation, radians (default 0.05, about 3 degrees); a curved sheet gets more (its turning per unit length times the depth) </summary>
        public double DeltaMin = 0.05;
        /// <summary> the margin on the exit angle, radians: the angle must exceed delta + margin or the exit is refused as grazing (default 0.05) </summary>
        public double AngleMargin = 0.05;
        /// <summary> the limit of U relative to the smaller part of the cut (default +infinity: no limit; open in the design). It must be 0 or more: NaN or a negative value is refused as InvalidInput, as is a finite limit with VerifyWithSheetRole = false </summary>
        public double MaxUOverSmallerPart = double.PositiveInfinity;
        /// <summary> the depth of the subdivision of an outer edge (at most 2^depth pieces of a rim edge; default 14) </summary>
        public int MaxSubdivisionDepth = 14;
        /// <summary> Diagnose(): the number of sample points per stretch of a rim edge inside the workpiece (default 8) </summary>
        public int DiagnosisSamples = 8;
        /// <summary> the other tools of the Difference the extended sheet will be cut with (their roles in OtherRoles, Solid where missing); the strip may meet none of them </summary>
        public IList<DMesh3> OtherTools;
        public IList<ExactMeshBoolean.InputRole> OtherRoles;
        /// <summary> passed to the verification cut (<see cref="ExactMeshBoolean.SheetUncutSide"/>) </summary>
        public ExactMeshBoolean.SheetUncutSides UncutSide = ExactMeshBoolean.SheetUncutSides.Refuse;
        /// <summary>
        /// the last check: run the sheet role's Difference on the extended sheet and refuse if it does not accept it or if the cut is not clean (default true). With false the strip is
        /// verified by the other exact checks only: Success then means that the helper's own checks hold and that the sheet role was NOT asked (it may still refuse the sheet); the caller's own cut is the check; Report.Cut, U relative to the smaller part and MaxUOverSmallerPart are then not available
        /// </summary>
        public bool VerifyWithSheetRole = true;
        // (with false, Status Success means: the helper's own checks hold (new points outside, one curve per run, no contact with the sheet, itself or another tool, the outer edges
        // outside the workpiece); the sheet role was not asked, so it may still refuse the sheet (eg a rim the strip does not reach), and the cut is not clean-checked. The pipeline must keep it on.)

        // ---------------------------------------------------------------- internal switches for the hand mutants of the tests

        [Flags]
        internal enum Rules
        {
            None = 0, Depth = 1, Grazing = 2, NewPointOutside = 4, InvertedQuad = 8, OuterEdge = 16, CurveRule = 32, CoplanarContact = 64,
            MeetsSheet = 128, MeetsOtherTool = 256, Gap = 512, CutClean = 1024, All = 2047
        }
        /// <summary> for tests: the verifications that are on (a mutant switches one off) </summary>
        internal Rules Verify = Rules.All;
        /// <summary> for tests: the least cosine between a strip triangle's normal and the normal of the sheet triangle it continues (a mutant lowers it) </summary>
        internal double MinStripCosine = 1e-9;
        /// <summary> for tests: the rim edges are chosen by vertex instead of by edge </summary>
        internal bool ChooseByVertex;
        /// <summary> for tests: the last exit of the ray instead of the first </summary>
        internal bool UseLastExit;
        /// <summary> for tests: a fixed overshoot (the cap) instead of the adaptive rule </summary>
        internal bool FixedOvershoot;
        /// <summary> for tests: no subdivision of an outer edge </summary>
        internal bool NoSubdivision = false;
        /// <summary> for tests: the strip's triangles oriented against the sheet </summary>
        internal bool FlipQuads;

        // ---------------------------------------------------------------- outputs

        /// <summary> the extended sheet, a new mesh: the sheet's triangles unchanged (same ids), the strip appended; or a copy of the sheet where no rim edge meets the workpiece's interior; null if Compute() failed (and after Diagnose()) </summary>
        public DMesh3 Result;
        /// <summary> result code; Success iff the last call returned true </summary>
        public ExtensionStatus Status = ExtensionStatus.NotComputed;
        /// <summary> why the last call failed (the witness in words), otherwise null </summary>
        public string Error;
        /// <summary> what was found and built, also where the call failed (the witness, the diagnosis so far) </summary>
        public SheetExtensionReport Report;

        /// <summary> continues the sheet's rim up to the workpiece's surface and verifies it; see the class comment. Returns true iff Status is Success </summary>
        public bool Compute()
        {
            return Run(false);
        }

        /// <summary>
        /// the diagnosis alone: where the rim is inside the workpiece and how deep, without the strip, the imprint or the cut (Result stays null, Report.Diagnosed is true).
        /// Per run: the length of the rim inside (per rim edge), the largest and the mean depth along the direction of the construction, absolute and relative to the
        /// diagonal, NoExit / DepthAboveLimit / GrazingExit where a ray has no exit or the limits would refuse, and the estimate U (a volume, relative to the workpiece's
        /// volume). The depths are sampled (<see cref="DiagnosisSamples"/> points per inside stretch of a rim edge and the rim vertices that are inside), so MaxDepth is a
        /// lower bound of the largest depth. Returns true iff the diagnosis could be made (Status Success; also where the rim is too deep for Compute()); false for
        /// invalid input, an open workpiece, a sheet that is not a surface, and a rim vertex whose direction is not defined (bowtie)
        /// </summary>
        public bool Diagnose()
        {
            return Run(true);
        }

        bool Run(bool diagnose)
        {
            Result = null;
            Error = null;
            Status = ExtensionStatus.NotComputed;
            Report = new SheetExtensionReport { Status = ExtensionStatus.NotComputed, Diagnosed = diagnose };      // not the previous run's, whatever happens below
            var b = new Builder(this, diagnose);
            DMesh3 r = b.Execute();
            Report = b.Rep;
            Status = b.Rep.Status;
            Result = Status == ExtensionStatus.Success && !diagnose ? r : null;
            Error = Status == ExtensionStatus.Success ? null : (b.Rep.Witness.Length > 0 ? b.Rep.Witness : Status.ToString());
            return Status == ExtensionStatus.Success;
        }

        /// <summary> the limit on U relative to the smaller part is exceeded; with a finite limit an empty smaller part counts as exceeded (the limit is never skipped silently) </summary>
        internal static bool UBeyondLimit(double u, double smallerPart, double limit)
        {
            if (double.IsPositiveInfinity(limit)) return false;
            return !(smallerPart > 0) || u / smallerPart > limit;
        }

        /// <summary> the volume of a closed (or nearly closed) mesh, summed against its own first vertex so that a far frame costs no digits </summary>
        internal static double Volume(DMesh3 mesh)
        {
            if (mesh == null || mesh.TriangleCount == 0)
                return 0;
            Vector3d o = mesh.GetVertex(mesh.GetTriangle(mesh.TriangleIndices().First()).a);        // (a vertex that a triangle uses: an unused one may be far away)
            double v = 0;
            foreach (int t in mesh.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                mesh.GetTriVertices(t, ref a, ref b, ref c);
                a -= o; b -= o; c -= o;
                v += a.Dot(b.Cross(c));
            }
            return v / 6;
        }

        /// <summary> the largest absolute coordinate the construction takes (it squares lengths and cubes them for the volume of the report); beyond it the input is refused as InvalidInput </summary>
        internal const double MaxCoordinate = 1e100;

        // ================================================================ the construction

        sealed class VData
        {
            public int V;
            public Vector3d Pos;
            public bool Inside, OnSurface, HasDir;
            public Vector3d Dir;
            public double G, Alpha, Delta, Kappa;
            public Vector3d P;
            public int Pid = -1;
        }

        sealed class OPoint
        {
            public double S, G, Alpha, Delta;
            public Vector3d P;
            public int Vid = -1;
            public bool OnSurface;
        }

        sealed class REdge
        {
            public int Eid, A, B, Tri, Run;
            public Vector3d PA, PB;
            public double Len;
            public List<double[]> Inside = new List<double[]>();
            public List<OPoint> Outer;
        }

        struct PKey : IEquatable<PKey>
        {
            readonly double x, y, z;
            public PKey(Vector3d p) { x = p.x + 0.0; y = p.y + 0.0; z = p.z + 0.0; }
            public bool Equals(PKey o) { return x == o.x && y == o.y && z == o.z; }
            public override bool Equals(object o) { return o is PKey k && Equals(k); }
            public override int GetHashCode()
            {
                // the three coordinate words mixed (SplitMix64 finalizer); the default double hashes of grid coordinates collide
                unchecked {
                    ulong h = (ulong)BitConverter.DoubleToInt64Bits(x) * 0x9E3779B97F4A7C15UL;
                    h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(y)) * 0xBF58476D1CE4E5B9UL;
                    h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(z)) * 0x94D049BB133111EBUL;
                    h ^= h >> 31;
                    return (int)h ^ (int)(h >> 32);
                }
            }
        }

        sealed class Builder
        {
            readonly DMesh3 S, W;
            readonly MeshSheetExtension O;
            readonly bool diagnose;
            public readonly SheetExtensionReport Rep = new SheetExtensionReport();
            DMeshAABBTree3 tree;
            double D, unit, tolOn, oMin, oMax, maxDepthAbs, tolT;
            readonly Dictionary<int, VData> vdata = new Dictionary<int, VData>();
            readonly Stopwatch clock = new Stopwatch();
            double lap;
            bool failed, quiet;
            ExtensionStatus pendRefusal; string pendWitness; Vector3d? pendPoint;
            int ctxEdge = -1, ctxVertex = -1;

            public Builder(MeshSheetExtension op, bool diagnose) { S = op.Sheet; W = op.Workpiece; O = op; this.diagnose = diagnose; Rep.Diagnosed = diagnose; }

            bool Fail(ExtensionStatus r, string witness, Vector3d? p = null)
            {
                if (failed)
                    return false;
                if (quiet) { pendRefusal = r; pendWitness = witness; pendPoint = p; return false; }
                failed = true;
                Rep.Status = r;
                Rep.Witness = witness;
                Rep.WitnessEdge = ctxEdge;
                Rep.WitnessVertex = ctxVertex;
                if (p.HasValue) { Rep.WitnessPoint = p.Value; Rep.HasWitnessPoint = true; }
                return false;
            }

            double Lap() { double now = clock.Elapsed.TotalMilliseconds, d = now - lap; lap = now; return d; }

            // ---------------------------------------------------------------- queries on the workpiece

            // (a distance from FindNearestTriangle loses about sqrt(eps) relative to the coordinates, so it is used for witness texts only; every classification below is exact)
            double Dist(Vector3d p) { tree.FindNearestTriangle(p, out double d2); return Math.Sqrt(d2); }
            bool Winding(Vector3d p) { return Math.Abs(tree.WindingNumber(p)) > 0.5; }
            // some triangle of the workpiece contains the point (exact: the segment test on a degenerate segment)
            bool OnSurfaceExact(Vector3d p)
            {
                bool on = false;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal {
                    NextBoxDF = (bx, depth) => !on && bx.Contains(p),
                    NextTriangleF = tid => {
                        if (on) return;
                        Triangle3d tri = Tri(W, tid);
                        if (ExactTriangleIntersection.SegmentIntersectsTriangle(p, p, ref tri)) on = true;
                    }
                });
                return on;
            }
            // inside: the exact winding number is not 0 and the point is on no triangle (the winding number is symbolically perturbed: on the surface it says 0 or 1 by the perturbation)
            // (the winding number first: it is a few hundred nanoseconds and settles every point outside)
            bool IsInside(Vector3d p) { return Winding(p) && !OnSurfaceExact(p); }
            // strictly outside: the exact winding number is 0 and no triangle of the workpiece contains the point
            bool StrictlyOutside(Vector3d p)
            {
                return !Winding(p) && !OnSurfaceExact(p);
            }

            static AxisAlignedBox3d TriBox(Triangle3d t)
            {
                var b = new AxisAlignedBox3d(t.V0);
                b.Contain(t.V1); b.Contain(t.V2);
                return b;
            }

            // the unit normal of a triangle (zero for a degenerate one), computed from the edges scaled by a power of two: DMesh3.GetTriNormal normalizes the cross product with
            // an absolute threshold (MathUtil.Epsilon) and returns the zero vector for a triangle whose edges are below about 1e-8, which would refuse a small sheet as NoDirection
            static Vector3d TriNormal(DMesh3 m, int t)
            {
                Triangle3d tri = Tri(m, t);
                Vector3d e1 = tri.V1 - tri.V0, e2 = tri.V2 - tri.V0;
                double mx = Math.Max(Math.Max(Math.Abs(e1.x), Math.Abs(e1.y)), Math.Max(Math.Max(Math.Abs(e1.z), Math.Abs(e2.x)), Math.Max(Math.Abs(e2.y), Math.Abs(e2.z))));
                if (!(mx > 0))
                    return Vector3d.Zero;
                double sc = Math.Pow(2, -Math.Floor(Math.Log(mx, 2)));
                Vector3d n = (e1 * sc).Cross(e2 * sc);
                double l = n.Length;
                return l > 0 ? n / l : Vector3d.Zero;
            }

            static Triangle3d Tri(DMesh3 m, int t)
            {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                return new Triangle3d(a, b, c);
            }

            // the hits of the ray with the workpiece as groups (equal parameters within tolT: a ray through an edge or vertex), sorted
            List<KeyValuePair<double, List<int>>> RayGroups(Vector3d o, Vector3d d)
            {
                var ray = new Ray3d(o, d);
                var ids = new List<int>();
                tree.FindAllHitTriangles(ray, ids);
                var hits = new List<KeyValuePair<double, int>>();
                foreach (int t in ids) {
                    var intr = new IntrRay3Triangle3(ray, Tri(W, t));
                    double par = intr.Find() ? intr.RayParameter : d.Dot(W.GetTriCentroid(t) - o);
                    hits.Add(new KeyValuePair<double, int>(par, t));
                }
                hits.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));
                var groups = new List<KeyValuePair<double, List<int>>>();
                foreach (var h in hits) {
                    if (groups.Count > 0 && h.Key - groups[groups.Count - 1].Key <= tolT)
                        groups[groups.Count - 1].Value.Add(h.Value);
                    else
                        groups.Add(new KeyValuePair<double, List<int>>(h.Key, new List<int> { h.Value }));
                }
                return groups;
            }

            // the parts of the segment A-B (as fractions) that lie inside the workpiece (not on its surface)
            List<double[]> InsideIntervals(Vector3d A, Vector3d B)
            {
                var res = new List<double[]>();
                double len = (B - A).Length;
                if (len <= tolOn)
                    return res;
                Vector3d dir = (B - A) / len;
                var ray = new Ray3d(A, dir);
                var ids = new List<int>();
                tree.FindAllHitTriangles(ray, ids, len + tolT);
                var ts = new List<double> { 0, len };
                foreach (int t in ids) {
                    var intr = new IntrRay3Triangle3(ray, Tri(W, t));
                    if (intr.Find() && intr.RayParameter > 0 && intr.RayParameter < len)
                        ts.Add(intr.RayParameter);
                }
                ts.Sort();
                for (int i = 0; i + 1 < ts.Count; ++i) {
                    double a = ts[i], b = ts[i + 1];
                    if (b - a <= tolT)
                        continue;
                    if (!IsInside(A + dir * ((a + b) / 2)))
                        continue;
                    if (res.Count > 0 && res[res.Count - 1][1] >= a / len - 1e-15)
                        res[res.Count - 1][1] = b / len;
                    else
                        res.Add(new[] { a / len, b / len });
                }
                return res;
            }

            // ---------------------------------------------------------------- the rim

            double KappaAt(int v)
            {
                double k = 0;
                foreach (int e in S.VtxEdgesItr(v)) {
                    if (!S.IsBoundaryEdge(e))
                        continue;
                    int t = S.GetEdgeT(e).a;
                    Vector3d n = TriNormal(S, t), c = S.GetTriCentroid(t);
                    Index3i nb = S.GetTriNeighbourTris(t);
                    for (int i = 0; i < 3; ++i) {
                        if (nb[i] < 0) continue;
                        double ang = Math.Acos(Math.Max(-1, Math.Min(1, n.Dot(TriNormal(S, nb[i])))));
                        double dist = (S.GetTriCentroid(nb[i]) - c).Length;
                        if (dist > 0) k = Math.Max(k, ang / dist);
                    }
                }
                return k;
            }

            Vector3d OutwardNormal(int e, out bool ok)
            {
                Index2i ab = S.GetOrientedBoundaryEdgeV(e);
                Vector3d A = S.GetVertex(ab.a), B = S.GetVertex(ab.b);
                Vector3d N = TriNormal(S, S.GetEdgeT(e).a);
                Vector3d n = (B - A).Cross(N);
                double l = n.Length;
                ok = l > 0 && N.LengthSquared > 0.5;
                return ok ? n / l : Vector3d.Zero;
            }

            VData GetV(int v)
            {
                if (vdata.TryGetValue(v, out VData vd))
                    return vd;
                vd = new VData { V = v, Pos = S.GetVertex(v) };
                vdata[v] = vd;
                vd.Inside = IsInside(vd.Pos);
                vd.OnSurface = !vd.Inside && OnSurfaceExact(vd.Pos);
                return vd;
            }

            // the unit direction of a rim vertex; false (and a refusal) for a bowtie or cancelling normals
            bool Direction(VData vd)
            {
                if (vd.HasDir)
                    return true;
                ctxVertex = vd.V;
                var bnd = new List<int>();
                foreach (int e in S.VtxEdgesItr(vd.V))
                    if (S.IsBoundaryEdge(e)) bnd.Add(e);
                if (bnd.Count != 2)
                    return Fail(ExtensionStatus.BowtieVertex, "rim vertex " + vd.V + " has " + bnd.Count + " rim edges", vd.Pos);
                Vector3d n1 = OutwardNormal(bnd[0], out bool ok1), n2 = OutwardNormal(bnd[1], out bool ok2);
                Vector3d s = n1 + n2;
                if (!ok1 || !ok2 || s.Length < 1e-9)
                    return Fail(ExtensionStatus.NoDirection, "rim vertex " + vd.V + ": the outward normals cancel", vd.Pos);
                vd.Dir = s.Normalized;
                vd.HasDir = true;
                vd.Kappa = KappaAt(vd.V);
                return true;
            }

            double Overshoot(double gap)
            {
                if (O.FixedOvershoot)
                    return oMax;
                return Math.Max(oMin, Math.Min(oMax, gap / 2));
            }

            // the ray from p along d to its exit: g, alpha, delta and the gap to the next surface; false if the ray leaves the workpiece nowhere
            bool RayCore(Vector3d p, Vector3d d, double kappa, out double g, out double alpha, out double delta, out double gap)
            {
                g = alpha = delta = 0; gap = double.PositiveInfinity;
                var groups = RayGroups(p, d);
                if (groups.Count == 0)
                    return false;
                int gi = O.UseLastExit ? groups.Count - 1 : 0;
                g = groups[gi].Key;
                gap = gi + 1 < groups.Count ? groups[gi + 1].Key - g : double.PositiveInfinity;
                alpha = double.PositiveInfinity;
                foreach (int t in groups[gi].Value) {
                    double s = Math.Abs(d.Dot(TriNormal(W, t)));
                    alpha = Math.Min(alpha, Math.Asin(Math.Min(1, s)));
                }
                delta = Math.Max(O.DeltaMin, kappa * g);
                return true;
            }

            // the ray from p along d to its exit, with the limits and the exit test; fills g, alpha, delta, the new point P; false with a refusal
            bool Ray(Vector3d p, Vector3d d, double kappa, out double g, out double alpha, out double delta, out Vector3d P)
            {
                P = p;
                if (!RayCore(p, d, kappa, out g, out alpha, out delta, out double gap))
                    return Fail(ExtensionStatus.NoExit, "the ray leaves the workpiece nowhere", p);
                if ((O.Verify & Rules.Depth) != 0 && g > maxDepthAbs) {
                    if (!quiet) {
                        Rep.MaxDepth = Math.Max(Rep.MaxDepth, g);
                        Rep.MaxDepthRelative = Rep.MaxDepth / D;
                    }
                    return Fail(ExtensionStatus.DepthAboveLimit, "depth " + g.ToString("R") + " = " + (g / D).ToString("0.####") + " of the diagonal", p);
                }
                if ((O.Verify & Rules.Grazing) != 0 && alpha <= delta + O.AngleMargin)
                    return Fail(ExtensionStatus.GrazingExit, "exit angle " + (alpha * 180 / Math.PI).ToString("0.##") + " deg, delta " + (delta * 180 / Math.PI).ToString("0.##") + " deg", p + d * g);
                if ((O.Verify & Rules.Gap) != 0 && !O.FixedOvershoot && gap < 2 * oMin)
                    return Fail(ExtensionStatus.GapBelowResolution, "the next surface is " + gap.ToString("R") + " behind the exit at depth " + g.ToString("R") + ", below twice the least overshoot " + (2 * oMin).ToString("R"), p + d * g);
                P = p + d * (g + Overshoot(gap));
                if ((O.Verify & Rules.NewPointOutside) != 0 && !StrictlyOutside(P))
                    return Fail(ExtensionStatus.NewPointNotOutside, "a new point is not strictly outside the workpiece (exit at depth " + g.ToString("R") + ", next surface " + gap.ToString("R") + " beyond; winding " + tree.WindingNumber(P).ToString("R") + ", distance " + Dist(P).ToString("R") + ")", P);
                return true;
            }

            bool Prepare(VData vd)
            {
                if (!Direction(vd))
                    return false;
                if (vd.Inside && vd.G == 0 && vd.Pid == -1) {
                    ctxVertex = vd.V;
                    if (!Ray(vd.Pos, vd.Dir, vd.Kappa, out vd.G, out vd.Alpha, out vd.Delta, out vd.P))
                        return false;
                }
                return true;
            }

            OPoint EndPoint(VData vd)
            {
                if (vd.G > 0)
                    return new OPoint { P = vd.P, G = vd.G, Alpha = vd.Alpha, Delta = vd.Delta };
                return new OPoint { P = vd.Pos, OnSurface = vd.OnSurface };
            }

            // the outer segment p-q touches the workpiece (exact). An end that lies on the surface (a rim vertex exactly on it, g = 0) is not a contact by itself: for a triangle that contains
            // that end the segment is a contact only if it also lies in that triangle's plane (a line meets a plane once; the segment is conservatively a contact then, also where it
            // leaves the triangle at once). No window around the end is excluded
            bool OuterTouches(OPoint p, OPoint q)
            {
                Vector3d a = p.P, b = q.P;
                var box = new AxisAlignedBox3d(a);
                box.Contain(b);
                box.Expand(tolT);
                bool hit = false;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal {
                    NextBoxDF = (bx, depth) => !hit && bx.Intersects(box),
                    NextTriangleF = tid => {
                        if (hit) return;
                        Triangle3d tri = Tri(W, tid);
                        if (!ExactTriangleIntersection.SegmentIntersectsTriangle(a, b, ref tri)) return;
                        bool pOn = p.OnSurface && ExactTriangleIntersection.SegmentIntersectsTriangle(a, a, ref tri);
                        bool qOn = q.OnSurface && ExactTriangleIntersection.SegmentIntersectsTriangle(b, b, ref tri);
                        if (!pOn && !qOn) { hit = true; return; }
                        if (pOn && qOn) { hit = true; return; }                                  // both ends in one triangle: the segment lies in its plane
                        Vector3d other = pOn ? b : a;
                        if (ExactPredicates.Orient3d(tri.V0, tri.V1, tri.V2, other) == 0) hit = true;    // in the plane of the triangle that contains the end
                    }
                });
                return hit;
            }

            static Vector3d InterpDir(VData va, VData vb, double s)
            {
                Vector3d d = va.Dir * (1 - s) + vb.Dir * s;
                return d.Length > 1e-9 ? d.Normalized : va.Dir;
            }

            bool Refine(REdge e, VData va, VData vb, int i, int depth)
            {
                if (!OuterTouches(e.Outer[i], e.Outer[i + 1]))
                    return true;
                bool strict = (O.Verify & Rules.OuterEdge) != 0;
                if (depth >= O.MaxSubdivisionDepth)
                    return !strict || Fail(ExtensionStatus.OuterEdgeInside, "the outer edge of rim edge " + e.Eid + " passes through the workpiece after " + depth + " subdivisions", e.Outer[i].P);
                double s0 = e.Outer[i].S, s1 = e.Outer[i + 1].S;
                // the longest stretch of the rim edge inside the workpiece between the two points; tried at its middle, then at two off-centre
                // places (a new point that lands exactly on a face of the workpiece is no exit that can be used, and a nearby one often is)
                double best = 0, lo0 = 0, hi0 = 0;
                foreach (double[] iv in e.Inside) {
                    double lo = Math.Max(iv[0], s0), hi = Math.Min(iv[1], s1);
                    if (hi - lo > best) { best = hi - lo; lo0 = lo; hi0 = hi; }
                }
                if (best <= 0)
                    return !strict || Fail(ExtensionStatus.OuterEdgeInside, "no inside part of rim edge " + e.Eid + " between the points of a failing outer segment", e.Outer[i].P);
                OPoint pt = null;
                pendRefusal = ExtensionStatus.Success;
                foreach (double f in new[] { 0.5, 0.4142135623730951, 0.5857864376269049 }) {
                    double s = lo0 + (hi0 - lo0) * f;
                    Vector3d M = e.PA + (e.PB - e.PA) * s;
                    Vector3d d = InterpDir(va, vb, s);
                    if (!IsInside(M))
                        continue;
                    var cand = new OPoint { S = s };
                    quiet = true;
                    bool ok = Ray(M, d, Math.Max(va.Kappa, vb.Kappa), out cand.G, out cand.Alpha, out cand.Delta, out cand.P);
                    quiet = false;
                    if (ok) { pt = cand; break; }
                }
                if (pt == null) {
                    if (!strict) return true;
                    bool had = pendRefusal != ExtensionStatus.Success;
                    return Fail(had ? pendRefusal : ExtensionStatus.OuterEdgeInside, had ? pendWitness : "no usable point of subdivision on rim edge " + e.Eid, pendPoint ?? e.Outer[i].P);
                }
                e.Outer.Insert(i + 1, pt);
                Rep.SubdivisionPoints++;
                if (!Refine(e, va, vb, i, depth + 1))
                    return false;
                int idx = e.Outer.IndexOf(pt);
                return Refine(e, va, vb, idx, depth + 1);
            }

            // ---------------------------------------------------------------- the whole construction

            // false (with a refusal) for input that cannot be used; sets the scales, the tree and the diagonal
            bool Setup()
            {
                if (S == null || W == null || S.TriangleCount == 0 || W.TriangleCount == 0)
                    return Fail(ExtensionStatus.InvalidInput, "empty input");
                foreach (DMesh3 m in new[] { S, W })
                    foreach (int v in m.VertexIndices()) {
                        Vector3d p = m.GetVertex(v);
                        if (!(Math.Abs(p.x) <= MaxCoordinate && Math.Abs(p.y) <= MaxCoordinate && Math.Abs(p.z) <= MaxCoordinate))
                            return Fail(ExtensionStatus.InvalidInput, "non-finite coordinates, or beyond " + MaxCoordinate + ", in vertex " + v, null);
                    }
                if (O.OtherTools != null)
                    for (int i = 0; i < O.OtherTools.Count; ++i) {
                        DMesh3 tool = O.OtherTools[i];
                        if (tool == null)
                            return Fail(ExtensionStatus.InvalidInput, "OtherTools[" + i + "] is null");
                        foreach (int v in tool.VertexIndices()) {
                            Vector3d p = tool.GetVertex(v);
                            if (!(Math.Abs(p.x) <= MaxCoordinate && Math.Abs(p.y) <= MaxCoordinate && Math.Abs(p.z) <= MaxCoordinate))
                                return Fail(ExtensionStatus.InvalidInput, "non-finite coordinates, or beyond " + MaxCoordinate + ", in vertex " + v + " of OtherTools[" + i + "]", null);
                        }
                    }
                if (O.OtherRoles != null && O.OtherRoles.Count > (O.OtherTools == null ? 0 : O.OtherTools.Count))
                    return Fail(ExtensionStatus.InvalidInput, "OtherRoles has more entries (" + O.OtherRoles.Count + ") than OtherTools");
                if (!(O.MaxUOverSmallerPart >= 0))   // NaN or negative: a comparison with it would be false and the limit skipped silently
                    return Fail(ExtensionStatus.InvalidInput, "MaxUOverSmallerPart must be 0 or more (+infinity: no limit); " + O.MaxUOverSmallerPart.ToString(System.Globalization.CultureInfo.InvariantCulture) + " would skip or invert the limit");
                if (!double.IsPositiveInfinity(O.MaxUOverSmallerPart) && !O.VerifyWithSheetRole)
                    return Fail(ExtensionStatus.InvalidInput, "MaxUOverSmallerPart needs the cut of VerifyWithSheetRole (the smaller part of the cut); the limit would be skipped silently");
                if (!ExactMeshBoolean.IsWindingClosed(W))
                    return Fail(ExtensionStatus.WorkpieceNotClosed, "the workpiece is not closed");
                if (!CheckSurface())
                    return false;
                AxisAlignedBox3d sb = TriangleBounds(S), wb = TriangleBounds(W);      // of the vertices that triangles use: an unused vertex far away must not make the diagonal (and with it the depth limit) huge
                D = sb.Diagonal.Length;
                Rep.Diagonal = D;
                double scale = 0;
                foreach (AxisAlignedBox3d bx in new[] { sb, wb })
                    scale = Math.Max(scale, Math.Max(Math.Max(Math.Abs(bx.Min.x), Math.Abs(bx.Max.x)), Math.Max(Math.Max(Math.Abs(bx.Min.y), Math.Abs(bx.Max.y)), Math.Max(Math.Abs(bx.Min.z), Math.Abs(bx.Max.z)))));
                if (scale == 0) scale = 1;
                unit = Math.Pow(2, -52) * scale;
                tolOn = 64 * unit;
                tolT = 16 * unit;
                oMin = O.MinOvershootUnits * unit;
                oMax = Math.Max(O.MaxOvershoot * D, oMin);
                maxDepthAbs = O.MaxExtension * D;
                tree = new DMeshAABBTree3(W, true);
                return true;
            }

            static AxisAlignedBox3d TriangleBounds(DMesh3 m)
            {
                AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
                foreach (int t in m.TriangleIndices()) {
                    Index3i tv = m.GetTriangle(t);
                    for (int i = 0; i < 3; ++i) box.Contain(m.GetVertex(tv[i]));
                }
                return box;
            }

            // an edge used by more than two triangles by position is no surface (the sheet role refuses it as NotSurface, so do we, before any work)
            Dictionary<long, int> posEdges;
            Dictionary<PKey, int> posIds;

            long PosEdgeKey(int va, int vb)
            {
                int a = PosId(va), b = PosId(vb);
                return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            }

            int PosId(int v)
            {
                var k = new PKey(S.GetVertex(v));
                if (!posIds.TryGetValue(k, out int id)) posIds[k] = id = posIds.Count;
                return id;
            }

            bool CheckSurface()
            {
                posEdges = new Dictionary<long, int>(EdgeKeyComparer.Instance);   // (the default hash of a long is a ^ b: the keys of a grid collide, and the check was quadratic)
                posIds = new Dictionary<PKey, int>();
                foreach (int t in S.TriangleIndices()) {
                    Index3i tv = S.GetTriangle(t);
                    for (int i = 0; i < 3; ++i) {
                        int a = tv[i], b = tv[(i + 1) % 3];
                        if (PosId(a) == PosId(b)) continue;
                        long k = PosEdgeKey(a, b);
                        posEdges.TryGetValue(k, out int n);
                        posEdges[k] = ++n;
                        if (n > 2)
                            return Fail(ExtensionStatus.SheetNotSurface, "an edge of the sheet is used by more than two triangles (by position); triangle " + t, S.GetTriCentroid(t));
                    }
                }
                return true;
            }

            List<REdge> ChooseEdges()
            {
                var edges = new List<REdge>();
                foreach (int eid in S.BoundaryEdgeIndices()) {
                    Index2i ab = S.GetOrientedBoundaryEdgeV(eid);
                    var e = new REdge { Eid = eid, A = ab.a, B = ab.b, Tri = S.GetEdgeT(eid).a };
                    e.PA = S.GetVertex(e.A); e.PB = S.GetVertex(e.B);
                    e.Len = (e.PB - e.PA).Length;
                    Rep.RimLength += e.Len;
                    e.Inside = InsideIntervals(e.PA, e.PB);
                    ctxEdge = eid;
                    bool chosen = O.ChooseByVertex ? (GetV(e.A).Inside || GetV(e.B).Inside) : e.Inside.Count > 0;
                    if (chosen) edges.Add(e);
                }
                ctxEdge = -1;
                return edges;
            }

            // a chosen rim edge whose positions are those of another edge is a seam of unwelded duplicate vertices: not a rim
            bool CheckSeams(List<REdge> edges)
            {
                foreach (REdge e in edges)
                    if (posEdges.TryGetValue(PosEdgeKey(e.A, e.B), out int n) && n > 1) {
                        ctxEdge = e.Eid;
                        return Fail(ExtensionStatus.SheetNotSurface, "rim edge " + e.Eid + " is a seam (another edge has the same positions: unwelded duplicate vertices)", (e.PA + e.PB) / 2);
                    }
                return true;
            }

            // runs: chosen edges joined at a vertex for which joins(v) holds
            List<SheetExtensionRun> BuildRuns(List<REdge> edges, Func<int, bool> joins)
            {
                var uf = new int[edges.Count];
                for (int i = 0; i < uf.Length; ++i) uf[i] = i;
                Func<int, int> find = null;
                find = x => { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; };
                var atVertex = new Dictionary<int, int>();
                for (int i = 0; i < edges.Count; ++i)
                    foreach (int v in new[] { edges[i].A, edges[i].B }) {
                        if (!joins(v)) continue;
                        if (atVertex.TryGetValue(v, out int j)) uf[find(i)] = find(j); else atVertex[v] = i;
                    }
                var runIds = new Dictionary<int, int>();
                var runs = new List<SheetExtensionRun>();
                for (int i = 0; i < edges.Count; ++i) {
                    int r = find(i);
                    if (!runIds.TryGetValue(r, out int id)) { runIds[r] = id = runs.Count; runs.Add(new SheetExtensionRun { Id = id }); }
                    edges[i].Run = id;
                    SheetExtensionRun run = runs[id];
                    run.Edges++;
                    run.EdgeIds.Add(edges[i].Eid);
                    run.Length += edges[i].Len;
                    foreach (double[] iv in edges[i].Inside)
                        run.InsideLength += (iv[1] - iv[0]) * edges[i].Len;
                }
                foreach (SheetExtensionRun run in runs) {
                    run.InsideLengthRelative = D > 0 ? run.InsideLength / D : 0;
                    Rep.InsideLength += run.InsideLength;
                }
                Rep.InsideLengthRelative = D > 0 ? Rep.InsideLength / D : 0;
                Rep.InsideLengthShareOfRim = Rep.RimLength > 0 ? Rep.InsideLength / Rep.RimLength : 0;
                Rep.Runs = runs;
                return runs;
            }

            public DMesh3 Execute()
            {
                clock.Start();
                Rep.Status = ExtensionStatus.Success;
                if (!Setup())
                    return null;
                // 1. where: the rim edges that meet the interior of the workpiece
                List<REdge> edges = ChooseEdges();
                Rep.ChosenEdges = edges.Count;
                if (edges.Count > 0 && !CheckSeams(edges))
                    return null;
                if (diagnose)
                    return Diagnosis(edges);
                if (edges.Count == 0) {
                    Rep.ConstructMs = Lap();
                    return RunCutOnly();
                }

                // 2./3. direction, ray, depth per rim vertex of a chosen edge
                foreach (REdge e in edges) {
                    ctxEdge = e.Eid;
                    if (!Prepare(GetV(e.A)) || !Prepare(GetV(e.B)))
                        return Finish(null);
                }
                ctxEdge = -1; ctxVertex = -1;
                List<SheetExtensionRun> runs = BuildRuns(edges, v => vdata[v].G > 0);

                // 4. the outer polyline of each edge, subdivided where it passes through the workpiece
                foreach (REdge e in edges) {
                    ctxEdge = e.Eid;
                    VData va = vdata[e.A], vb = vdata[e.B];
                    OPoint pa = EndPoint(va), pb = EndPoint(vb);
                    pa.S = 0; pb.S = 1;
                    e.Outer = new List<OPoint> { pa, pb };
                    if (!O.NoSubdivision) {
                        if (!Refine(e, va, vb, 0, 0))
                            return Finish(null);
                    }
                    else if ((O.Verify & Rules.OuterEdge) != 0 && OuterTouches(pa, pb)) {
                        Fail(ExtensionStatus.OuterEdgeInside, "the outer edge of rim edge " + e.Eid + " passes through the workpiece (no subdivision)", pa.P);
                        return Finish(null);
                    }
                }
                ctxEdge = -1;
                Rep.ConstructMs += Lap();

                // 5a. verification before the mesh exists: inverted quads (the new points were verified where they were made)
                if ((O.Verify & Rules.InvertedQuad) != 0) {
                    // no strip triangle is turned against the sheet triangle it continues (a quad whose outer edge runs backwards folds over)
                    foreach (REdge e in edges) {
                        Vector3d nSheet = TriNormal(S, e.Tri);
                        foreach (Index3i lt in StripLocal(e.Outer.Count - 1)) {
                            Vector3d a = LocalPos(e, lt.a), b = LocalPos(e, lt.b), c = LocalPos(e, lt.c);
                            if (a == b || b == c || a == c) continue;
                            Vector3d n = (b - a).Cross(c - a);
                            double l = n.Length;
                            if (l > 0 && n.Dot(nSheet) / l > O.MinStripCosine) continue;
                            ctxEdge = e.Eid;
                            Fail(ExtensionStatus.InvertedQuad, "a strip triangle of rim edge " + e.Eid + " is turned against the sheet (or degenerate): " + a + " " + b + " " + c, a);
                            return Finish(null);
                        }
                    }
                }
                Rep.VerifyMs += Lap();

                // the strip
                DMesh3 ext = new DMesh3(S);
                var newTris = new HashSet<int>();      // the ids AppendTriangle returned: it reuses freed ids, so the strip is not "the ids above the sheet's"
                if (ext.MaxVertexID != S.MaxVertexID || ext.MaxTriangleID != S.MaxTriangleID) {
                    Fail(ExtensionStatus.ConstructionFailed, "the copy of the sheet changed its ids");
                    return Finish(null);
                }
                int nv0 = ext.VertexCount;
                var stripTris = new List<List<Index3i>>();
                for (int i = 0; i < runs.Count; ++i) stripTris.Add(new List<Index3i>());
                foreach (REdge e in edges) {
                    VData va = vdata[e.A], vb = vdata[e.B];
                    foreach (OPoint p in e.Outer) {
                        if (p.S == 0) p.Vid = va.G > 0 ? Pid(ext, va) : e.A;
                        else if (p.S == 1) p.Vid = vb.G > 0 ? Pid(ext, vb) : e.B;
                        else p.Vid = AppendLike(ext, p.P, p.S < 0.5 ? e.A : e.B);
                    }
                    var tris = new List<Index3i>();
                    int nseg = e.Outer.Count - 1;
                    foreach (Index3i lt in StripLocal(nseg))
                        tris.Add(new Index3i(LocalVid(e, lt.a), LocalVid(e, lt.b), LocalVid(e, lt.c)));
                    foreach (Index3i t in tris) {
                        if (t.a == t.b || t.b == t.c || t.a == t.c)
                            continue;
                        Index3i tt = O.FlipQuads ? new Index3i(t.a, t.c, t.b) : t;
                        int newId = ext.AppendTriangle(tt, S.HasTriangleGroups ? S.GetTriangleGroup(e.Tri) : -1);
                        if (newId >= 0) newTris.Add(newId);
                        if (newId < 0) {
                            ctxEdge = e.Eid;
                            Fail(ExtensionStatus.ConstructionFailed, "a strip triangle cannot be appended to a manifold mesh", ext.GetVertex(t.a));
                            Rep.Candidate = null;
                            return Finish(null);
                        }
                        stripTris[e.Run].Add(tt);
                    }
                }
                Rep.NewVertices = ext.VertexCount - nv0;
                Rep.NewTriangles = ext.TriangleCount - S.TriangleCount;
                Rep.Candidate = ext;
                RunStats(edges, runs);
                Rep.ConstructMs += Lap();

                // 5b. the curve rule per run (and the coplanar contact)
                int totalCurves = 0;
                for (int r = 0; r < runs.Count; ++r) {
                    var strip = new DMesh3();
                    var map = new Dictionary<int, int>();
                    foreach (Index3i t in stripTris[r]) {
                        var l = new int[3];
                        for (int k = 0; k < 3; ++k) {
                            if (!map.TryGetValue(t[k], out l[k])) map[t[k]] = l[k] = strip.AppendVertex(ext.GetVertex(t[k]));
                        }
                        strip.AppendTriangle(l[0], l[1], l[2]);
                    }
                    if ((O.Verify & Rules.CoplanarContact) != 0 && Coplanar(strip, out Vector3d at)) {
                        runs[r].Refusal = ExtensionStatus.CoplanarContact;
                        Fail(ExtensionStatus.CoplanarContact, "run " + r + ": a strip triangle lies in the plane of a face of the workpiece and overlaps it", at);
                        Rep.VerifyMs += Lap();
                        return Finish(null);
                    }
                    Rep.VerifyMs += Lap();
                    var imp = new ExactMeshImprint { Target = strip, Cutter = W, LabelCurves = true };
                    imp.Compute();
                    Rep.ImprintMs += Lap();
                    if (imp.Status != ExactMeshImprint.ImprintStatus.Success) {
                        Fail(ExtensionStatus.ImprintFailed, "run " + r + ": the imprint of the strip failed: " + imp.Status + " " + imp.Error);
                        return Finish(null);
                    }
                    int comps = Components(imp, out Vector3d second);
                    runs[r].CurveComponents = comps;
                    runs[r].CurveIds = imp.CurveId.Values.Distinct().Count();
                    Rep.CurveIdCount += runs[r].CurveIds;
                    totalCurves += comps;
                    double area = 0;
                    foreach (int t in imp.Result.TriangleIndices()) {
                        Vector3d c = imp.Result.GetTriCentroid(t);
                        if (IsInside(c)) area += imp.Result.GetTriArea(t);
                    }
                    runs[r].StripAreaInside = area;
                    if ((O.Verify & Rules.CurveRule) != 0 && comps != 1) {
                        runs[r].Refusal = ExtensionStatus.CurveRule;
                        Fail(ExtensionStatus.CurveRule, "run " + r + ": the strip meets the workpiece's surface in " + comps + " separate curves where 1 was expected (" + (comps > 1 ? "the second one near " + second.ToString() : "no curve") + "): the strip pierces a part of the workpiece between its rim and its outer edge", comps > 1 ? second : (Vector3d?)null);
                        Rep.VerifyMs += Lap();
                        return Finish(null);
                    }
                    Rep.VerifyMs += Lap();
                }
                Rep.CurveCount = totalCurves;

                // 5c. the strip meets neither the sheet, nor itself, nor another tool
                if ((O.Verify & Rules.MeetsSheet) != 0) {
                    foreach (Index2i pr in MeshRepairMetrics.SelfIntersectionPairs(ext)) {
                        bool n0 = newTris.Contains(pr.a), n1 = newTris.Contains(pr.b);
                        if (!n0 && !n1) continue;
                        Fail(n0 && n1 ? ExtensionStatus.MeetsItself : ExtensionStatus.MeetsSheet, "triangles " + pr.a + " and " + pr.b + " of the extended sheet intersect", ext.GetTriCentroid(n0 ? pr.a : pr.b));
                        Rep.VerifyMs += Lap();
                        return Finish(null);
                    }
                }
                if ((O.Verify & Rules.MeetsOtherTool) != 0 && O.OtherTools != null) {
                    foreach (DMesh3 tool in O.OtherTools) {
                        var tt = new DMeshAABBTree3(tool, true);
                        bool hit = false; Vector3d at = Vector3d.Zero;
                        foreach (int t in ext.TriangleIndices()) {
                            if (hit || !newTris.Contains(t)) continue;
                            Triangle3d a = Tri(ext, t);
                            var box = TriBox(a);
                            tt.DoTraversal(new DMeshAABBTree3.TreeTraversal {
                                NextBoxDF = (bx, depth) => !hit && bx.Intersects(box),
                                NextTriangleF = tid => {
                                    if (hit) return;
                                    Triangle3d b = Tri(tool, tid);
                                    if (ExactTriangleIntersection.Intersects(ref a, ref b)) { hit = true; at = ext.GetTriCentroid(t); }
                                }
                            });
                        }
                        if (hit) {
                            Fail(ExtensionStatus.MeetsOtherTool, "the strip meets another tool", at);
                            Rep.VerifyMs += Lap();
                            return Finish(null);
                        }
                    }
                }
                Rep.VerifyMs += Lap();

                // 5d. the sheet role accepts the extended sheet
                if (O.VerifyWithSheetRole) {
                    if (!Cut(ext, runs))
                        return Finish(null);
                }
                Rep.Extended = !failed;
                return Finish(ext);
            }

            // the triangles of the strip of one rim edge with n outer segments, as local corner numbers: 0 = A, k + 1 = outer point k, n + 2 = B;
            // the polygon A, P0 .. Pn, B (the closing edge B-A is the rim edge) is split by a fan from A up to the middle point and from B after it
            static List<Index3i> StripLocal(int n)
            {
                int m = n / 2;
                var tris = new List<Index3i>();
                for (int k = 0; k < m; ++k) tris.Add(new Index3i(0, k + 1, k + 2));
                tris.Add(new Index3i(0, m + 1, n + 2));
                for (int k = m; k < n; ++k) tris.Add(new Index3i(k + 1, k + 2, n + 2));
                return tris;
            }

            static Vector3d LocalPos(REdge e, int i) { return i == 0 ? e.PA : (i == e.Outer.Count + 1 ? e.PB : e.Outer[i - 1].P); }
            static int LocalVid(REdge e, int i) { return i == 0 ? e.A : (i == e.Outer.Count + 1 ? e.B : e.Outer[i - 1].Vid); }

            DMesh3 RunCutOnly()
            {
                // no rim edge meets the interior: nothing to extend; the cut of the sheet as it is (the sheet role decides)
                if (O.VerifyWithSheetRole) {
                    if (!Cut(S, Rep.Runs))
                        return null;
                }
                return new DMesh3(S);
            }

            DMesh3 Finish(DMesh3 result)
            {
                if (failed)
                    return null;
                return result;
            }

            // a new vertex at pos with the normal, colour and uv of the sheet's vertex src (where the sheet has them; they are copies of the rim vertex's, not recomputed)
            int AppendLike(DMesh3 ext, Vector3d pos, int src)
            {
                NewVertexInfo info = S.GetVertexAll(src);
                info.v = pos;
                return ext.AppendVertex(info);
            }

            int Pid(DMesh3 ext, VData vd)
            {
                if (vd.Pid < 0) vd.Pid = AppendLike(ext, vd.P, vd.V);
                return vd.Pid;
            }

            bool Coplanar(DMesh3 strip, out Vector3d at)
            {
                bool found = false; Vector3d where = Vector3d.Zero;
                foreach (int t in strip.TriangleIndices()) {
                    Triangle3d a = Tri(strip, t);
                    var box = TriBox(a);
                    tree.DoTraversal(new DMeshAABBTree3.TreeTraversal {
                        NextBoxDF = (bx, depth) => !found && bx.Intersects(box),
                        NextTriangleF = tid => {
                            if (found) return;
                            Triangle3d b = Tri(W, tid);
                            if (ExactPredicates.Orient3d(b.V0, b.V1, b.V2, a.V0) != 0 || ExactPredicates.Orient3d(b.V0, b.V1, b.V2, a.V1) != 0 || ExactPredicates.Orient3d(b.V0, b.V1, b.V2, a.V2) != 0)
                                return;
                            if (ExactTriangleIntersection.Intersects(ref a, ref b)) { found = true; where = (a.V0 + a.V1 + a.V2) / 3; }
                        }
                    });
                    if (found) break;
                }
                at = where;
                return found;
            }

            // connected components of the curve edges of an imprint (edges that share a vertex are one component)
            static int Components(ExactMeshImprint imp, out Vector3d second)
            {
                var parent = new Dictionary<int, int>();
                Func<int, int> find = null;
                find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
                foreach (long k in imp.CurveEdges) {
                    int a = (int)(k >> 32), b = (int)(k & 0xFFFFFFFF);
                    if (!parent.ContainsKey(a)) parent[a] = a;
                    if (!parent.ContainsKey(b)) parent[b] = b;
                    parent[find(a)] = find(b);
                }
                var roots = new Dictionary<int, int>();
                second = Vector3d.Zero;
                foreach (int v in parent.Keys.OrderBy(x => x)) {
                    int r = find(v);
                    if (!roots.ContainsKey(r)) {
                        roots[r] = v;
                        if (roots.Count == 2) second = imp.Result.GetVertex(v);
                    }
                }
                return roots.Count;
            }

            // the per-run depth, direction estimate and mean depth after the construction: the largest depth, the exit angle and the tilt of the strip's polyline points; the mean depth
            // and U from the same samples as the diagnosis (SampleRuns), over the part of the rim that is inside, as InsideLength is measured
            void RunStats(List<REdge> edges, List<SheetExtensionRun> runs)
            {
                foreach (REdge e in edges) {
                    SheetExtensionRun r = runs[e.Run];
                    foreach (OPoint p in e.Outer) {
                        r.MaxDepth = Math.Max(r.MaxDepth, p.G);
                        if (p.G > 0) {
                            r.MinAlpha = double.IsNaN(r.MinAlpha) ? p.Alpha : Math.Min(r.MinAlpha, p.Alpha);
                            r.MaxDelta = Math.Max(r.MaxDelta, p.Delta);
                        }
                    }
                }
                var gInt = new double[runs.Count];
                SampleRuns(edges, runs, gInt, false);
                FinishRuns(runs, gInt);
            }

            void FinishRuns(List<SheetExtensionRun> runs, double[] gInt)
            {
                double vw = Math.Abs(Volume(W));
                for (int i = 0; i < runs.Count; ++i) {
                    SheetExtensionRun r = runs[i];
                    r.MeanDepth = r.InsideLength > 0 ? gInt[i] / r.InsideLength : 0;
                    r.MaxDepthRelative = D > 0 ? r.MaxDepth / D : 0;
                    r.MeanDepthRelative = D > 0 ? r.MeanDepth / D : 0;
                    r.URelativeToWorkpiece = vw > 0 ? r.U / vw : double.NaN;
                    Rep.U += r.U;
                    Rep.MaxDepth = Math.Max(Rep.MaxDepth, r.MaxDepth);
                }
                Rep.MaxDepthRelative = D > 0 ? Rep.MaxDepth / D : 0;
                Rep.URelativeToWorkpiece = vw > 0 ? Rep.U / vw : double.NaN;
            }

            // the first-order estimate of the cut's change per unit of rim for a continuation tilted by delta (design section 5)
            static double UnitU(double g, double alpha, double delta)
            {
                if (g <= 0 || alpha <= delta)
                    return 0;
                return 0.5 * g * g * Math.Sin(delta) * Math.Sin(alpha) / Math.Sin(alpha - delta);
            }

            bool Cut(DMesh3 sheet, List<SheetExtensionRun> runs)
            {
                var meshes = new List<DMesh3> { W, sheet };
                var roles = new List<ExactMeshBoolean.InputRole> { ExactMeshBoolean.InputRole.Solid, ExactMeshBoolean.InputRole.Sheet };
                if (O.OtherTools != null)
                    for (int i = 0; i < O.OtherTools.Count; ++i) {
                        meshes.Add(O.OtherTools[i]);
                        roles.Add(O.OtherRoles != null && i < O.OtherRoles.Count ? O.OtherRoles[i] : ExactMeshBoolean.InputRole.Solid);
                    }
                Lap();
                var op = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Difference) {
                    AllowOpenMeshes = true, Roles = roles, SheetUncutSide = O.UncutSide
                };
                op.Compute();
                Rep.CutMs += Lap();
                Rep.CutStatus = op.Status.ToString();
                if (op.Status == ExactMeshBoolean.BooleanStatus.SheetRefused) {
                    Rep.SheetReason = op.SheetReason;
                    Rep.CutStatus += " " + op.SheetReason;
                    return Fail(ExtensionStatus.SheetRoleRefused, "the sheet role refuses the sheet: " + op.SheetReason + " (" + op.Error + ")", op.SheetWitnessPoint);
                }
                if (op.Status != ExactMeshBoolean.BooleanStatus.Success || op.Result == null)
                    return Fail(ExtensionStatus.CutFailed, "the cut failed: " + op.Status + " " + op.Error);
                Rep.Cut = op.Result;
                if ((O.Verify & Rules.CutClean) != 0) {
                    string notClean = MeshRobustRepair.NotCleanReason(op.Result);
                    if (!string.IsNullOrEmpty(notClean))
                        return Fail(ExtensionStatus.CutNotClean, "the cut with the sheet is accepted by the sheet role but is not clean: " + notClean);
                }
                double vw = Volume(W), vk = Volume(op.Result);
                Rep.SmallerPart = Math.Min(vk, vw - vk);
                if (Rep.SmallerPart > 0) {
                    Rep.URelative = Rep.U / Rep.SmallerPart;
                    foreach (SheetExtensionRun r in runs)
                        r.URelative = r.U / Rep.SmallerPart;
                }
                if (UBeyondLimit(Rep.U, Rep.SmallerPart, O.MaxUOverSmallerPart))
                    return Fail(ExtensionStatus.UncertaintyAboveLimit, Rep.SmallerPart > 0 ? "U is " + Rep.URelative.ToString("0.####") + " of the smaller part" : "the smaller part of the cut is empty: U cannot be related to it");
                return true;
            }

            // ---------------------------------------------------------------- the diagnosis alone

            // the depth sampled along the inside stretches of the chosen rim edges (DiagnosisSamples midpoints per stretch, and the rim vertices that are inside), with the direction
            // interpolated as in the construction: the integral of the depth into gInt, the estimate U and the largest depth into the runs; with diagnosis also the flags
            // (no exit, above the limit, grazing) and the deepest point as the witness. Compute() uses it for MeanDepth and U (the strip's polyline has too few points on a rim edge
            // with both ends outside), so that the two modes report the same numbers
            void SampleRuns(List<REdge> edges, List<SheetExtensionRun> runs, double[] gInt, bool diagnosis)
            {
                int n = Math.Max(1, O.DiagnosisSamples);
                double deepG = 0;
                foreach (REdge e in edges) {
                    SheetExtensionRun run = runs[e.Run];
                    VData va = vdata[e.A], vb = vdata[e.B];
                    foreach (double[] iv in e.Inside) {
                        var pts = new List<KeyValuePair<double, double>>();
                        double dl = (iv[1] - iv[0]) * e.Len / n;
                        if (iv[0] <= 0 && va.Inside) pts.Add(new KeyValuePair<double, double>(0, 0));
                        for (int i = 0; i < n; ++i) pts.Add(new KeyValuePair<double, double>(iv[0] + (iv[1] - iv[0]) * (i + 0.5) / n, dl));
                        if (iv[1] >= 1 && vb.Inside) pts.Add(new KeyValuePair<double, double>(1, 0));
                        foreach (var pt in pts) {
                            double s = pt.Key, w = pt.Value;
                            Vector3d M = e.PA + (e.PB - e.PA) * s;
                            Vector3d d = InterpDir(va, vb, s);
                            if (!RayCore(M, d, Math.Max(va.Kappa, vb.Kappa), out double g, out double alpha, out double delta, out double gap)) {
                                if (diagnosis) {
                                    if (!run.NoExit) run.Witness = "the ray of rim edge " + e.Eid + " at " + M + " leaves the workpiece nowhere";
                                    run.NoExit = true;
                                }
                                continue;
                            }
                            run.MaxDepth = Math.Max(run.MaxDepth, g);
                            gInt[e.Run] += g * w;
                            run.U += UnitU(g, alpha, delta) * w;
                            if (!diagnosis) continue;
                            if (g > maxDepthAbs) run.DepthAboveLimit = true;
                            if (alpha <= delta + O.AngleMargin) run.GrazingExit = true;
                            run.MinAlpha = double.IsNaN(run.MinAlpha) ? alpha : Math.Min(run.MinAlpha, alpha);
                            run.MaxDelta = Math.Max(run.MaxDelta, delta);
                            if (g > deepG) { deepG = g; Rep.WitnessEdge = e.Eid; Rep.WitnessPoint = M + d * g; Rep.HasWitnessPoint = true; }
                        }
                    }
                }
            }

            // sampled depth of the chosen edges: no strip, no imprint, no cut; a ray without exit or above a limit is a flag of its run, not a refusal
            DMesh3 Diagnosis(List<REdge> edges)
            {
                foreach (REdge e in edges) {
                    ctxEdge = e.Eid;
                    if (!Direction(GetV(e.A)) || !Direction(GetV(e.B)))
                        return null;
                }
                ctxEdge = -1; ctxVertex = -1;
                List<SheetExtensionRun> runs = BuildRuns(edges, v => vdata[v].Inside);
                var gInt = new double[runs.Count];
                SampleRuns(edges, runs, gInt, true);
                FinishRuns(runs, gInt);
                foreach (SheetExtensionRun run in runs)
                    if (run.DepthAboveLimit && run.Witness.Length == 0)
                        run.Witness = "depth " + run.MaxDepth.ToString("R") + " = " + run.MaxDepthRelative.ToString("0.####") + " of the diagonal, above the limit";
                Rep.DiagnoseMs = Lap();
                return null;
            }
        }
    }
}
