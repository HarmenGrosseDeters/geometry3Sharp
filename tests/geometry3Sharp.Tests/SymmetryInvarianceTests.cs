using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;
using Op = geometry3Sharp.Tests.RobustnessSweepTests.Op;
using Sig = geometry3Sharp.Tests.RobustnessSweepTests.Sig;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Relation S1 of docs/design/METAMORPHIC_RELATIONS.md: the 48 symmetries of the cube (axis permutations with signs; 24 of
    /// them reflections) map doubles to doubles exactly, so an operation without an axis-dependent choice must give the mapped
    /// result bit for bit, and one with such a choice (ties, the split axis of a tree) the same result within its tolerance.
    /// Three groups: the operations of the robustness sweep (<see cref="RobustnessSweepTests.Ops"/>, their signature values mapped back
    /// with the symmetry), <c>ExactMeshBoolean</c> on the box and tetrahedron families (the same solid, the exact volume of boxes) and
    /// the 2D quality mesher on the symmetries of the square (valid by the independent oracle, the same vertex count).
    /// PR tier: <see cref="Symmetry.Subset6"/> (identity, mirror x, mirror z, swap x y, quarter turn z, cyclic); the nightly job runs all 48
    /// (category Nightly: the run is the check, one test per operation over 47 symmetries).
    /// A difference by design is a classified row in <see cref="Toleranced"/> with its reason (owner 2026-11-13), a defect an entry of
    /// <see cref="KnownFailures"/> (strict: the case must still fail) with its issue; neither is fixed here.
    /// </summary>
    public class SymmetryInvarianceTests
    {
        readonly ITestOutputHelper output;
        public SymmetryInvarianceTests(ITestOutputHelper output) { this.output = output; }

        // ---------------------------------------------------------------- classification

        /// <summary>
        /// Operations compared within a tolerance instead of bit for bit, with the reason, measured by the report of all 47
        /// symmetries (G3_SYMMETRY_REPORT=1; the first of each pair is the largest relative difference seen). Tol and CountTol of -1 take the
        /// operation's own (Op.Tol, Op.CountTol of the sweep). All other operations of the sweep are bit for bit under every symmetry.
        /// </summary>
        public static readonly Dictionary<string, (string Reason, double Tol, double CountTol)> Toleranced = new Dictionary<string, (string, double, double)> {
            // last bits only (up to 3e-12): the sum x*x + y*y + z*z, a cross product or a CG / ICP iteration runs in another order after an
            // axis permutation, and a reflection swaps two corners of every triangle (a mirror alone already changes them)
            ["BoundaryFirstFlattening"] = ("last bits (1e-15): summation order", -1, -1),
            ["DMeshAABBTree3"] = ("last bits: summation order (a winding number near 0 differs by 1e-17)", -1, -1),
            ["LSCMParameterization"] = ("last bits (1e-15): summation order", -1, -1),
            ["LaplacianMeshSmoother"] = ("last bits (1.6e-16): summation order of the CG solve", -1, -1),
            ["MeshBatchQueries"] = ("last bits (2e-16): summation order", -1, -1),
            ["MeshDeviation"] = ("last bits (2e-15): summation order", -1, -1),
            ["MeshExtrudeMesh"] = ("last bits (6e-16): summation order", -1, -1),
            ["MeshGeodesicDistance"] = ("last bits (3e-15): summation order of the Poisson solve", -1, -1),
            ["MeshICP"] = ("last bits (3e-12): the iteration sums in another order", -1, -1),
            ["MeshInsertPolygon"] = ("last bits (3e-16): summation order", -1, -1),
            ["MeshIsoCurves"] = ("last bits (3e-16): summation order", -1, -1),
            ["MeshNormals"] = ("last bits (2e-16): the area weighted sum of face normals runs in corner order", -1, -1),
            ["MeshPlaneCut"] = ("last bits (6e-16): summation order", -1, -1),
            ["MeshRoundingRepair"] = ("last bits (2e-16) of the volume sum; the repair itself is the same", -1, -1),
            ["PointAABBTree3"] = ("last bits (1e-16): summation order", -1, -1),
            ["Reducer.MaxDeviation"] = ("last bits (2e-15) of the area and volume sums; the same triangles", -1, -1),
            ["Reducer.TriangleCount"] = ("last bits (2e-15) of the area and volume sums; the same triangles", -1, -1),
            // axis-aligned structure: a grid, an octree or a sampling pattern whose cells follow the axes (the sweep's own tolerances)
            ["MeshAlphaWrap"] = ("an axis-aligned octree of cells: area within 1.2e-2 (relative) under axis changes", -1, -1),
            ["MeshFidelityMetrics"] = ("sampled points on the triangles in axis order: chamfer within 8e-6", -1, -1),
            ["MeshSignedDistanceGrid+MarchingCubes"] = ("an axis-aligned grid with a float origin: volume within 8e-7", -1, -1),
            ["MeshTrimLoop"] = ("a border remesh in triangle order: 2.5e-2 in the triangle count", -1, -1),
            ["MeshWindingNumberBoolean.Difference"] = ("an axis-aligned grid (MarchingCubes / DualContouring): area within 5e-2", 0.3, -1),
            ["MeshWindingNumberBoolean.Union"] = ("an axis-aligned grid (MarchingCubes / DualContouring): area within 3e-2", -1, -1),
            ["MeshWindingNumberSolidify.DualContouring"] = ("an axis-aligned grid: triangle count within 3e-2", -1, -1),
            ["MeshWindingNumberSolidify.MarchingCubes"] = ("an axis-aligned grid: triangle count within 1.1e-1", -1, -1),
            ["PrimitiveFit3.Sphere"] = ("an iterative fit that stops at its tolerance: within 7e-6", -1, -1),
            ["VolumeSampleCells"] = ("axis-aligned cells (the sweep's tolerance is 0.5): a swap or mirror changes the sampled cells", -1, -1),
            // order dependence of a greedy or loop-based operation, seen only under the 24 reflections (the 24 rotations are bit for bit):
            // a reflection swaps two corners of every triangle, so a boundary loop starts elsewhere and runs the other way, and the
            // edge queue of a remesher visits the edges in another order. Both results are valid; their areas differ by 1e-3 to 2e-2
            ["MeshStitchLoops"] = ("loop start and direction after a reflection (the band between the loops twists differently): area within 1e-3, volume within 3e-2", 0.15, -1),
            ["Remesher"] = ("edge visiting order after a reflection: area within 2e-2, triangles within 2e-2", 0.1, 0.05),
            ["SmoothedHoleFill"] = ("loop start and direction after a reflection: area within 3e-3", 0.05, -1),
        };

        /// <summary> not run: the operation is documented as not robust and has a replacement under test below ("operation" -> reason) </summary>
        public static readonly Dictionary<string, string> Excluded = new Dictionary<string, string> {
            ["MeshBoolean"] = "R6 (docs/ROBUSTNESS_SWEEP.md): upstream boolean, not robust (it fails under 42 of 47 symmetries on this input); ExactMeshBoolean is the tested one",
        };

        /// <summary> expected failures, strict: "operation|symmetry" or "operation|*" -> issue. A fix removes the entry. </summary>
        public static readonly Dictionary<string, string> KnownFailures = new Dictionary<string, string> {
        };

        static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);

        // ---------------------------------------------------------------- the sweep operations

        static Op ByName(string name) => RobustnessSweepTests.Ops.First(o => o.Name == name);
        public static IEnumerable<object[]> PrCases() =>
            RobustnessSweepTests.Ops.Where(o => !Excluded.ContainsKey(o.Name)).SelectMany(o => Symmetry.Subset6().Where(s => !s.IsIdentity && (!o.PlanarInput || s.FixesZ))
                .Select(s => new object[] { o.Name, s.Name }));

        public static IEnumerable<object[]> NightlyOps() => RobustnessSweepTests.Ops.Where(o => !Excluded.ContainsKey(o.Name)).Select(o => new object[] { o.Name });

        static readonly ConcurrentDictionary<string, Sig> Baselines = new ConcurrentDictionary<string, Sig>();

        static Sig Run(Op op, Symmetry sym)
        {
            Sig sig = null;
            Exception failure = null;
            DMesh3 input = sym.IsIdentity ? op.Input() : sym.Transform(op.Input());
            bool finished = Timed.Finishes(() => { try { sig = op.Run(input, sym.As, op.Param); } catch (Exception e) { failure = e; } }, Budget);
            if (!finished) throw new TimeoutException($"{op.Name} under {sym.Name} did not finish within {Budget.TotalSeconds} s");
            if (failure != null) throw new Exception($"{op.Name} under {sym.Name}: {failure.GetType().Name}: {failure.Message.Split('\n')[0]}", failure);
            return sig;
        }

        /// <summary> "" if the signatures agree: bit for bit unless the operation is classified as toleranced </summary>
        public static string Compare(Op op, Sig a, Sig b, bool? forceExact = null)
        {
            if (a.Failed != b.Failed) return $"status: failed={a.Failed} on the original, {b.Failed} under the symmetry";
            if (a.Values.Count != b.Values.Count) return $"{a.Values.Count} values vs {b.Values.Count}";
            bool exact = forceExact ?? (op.BitExact && !Toleranced.ContainsKey(op.Name));
            double tolerance = op.Tol, countTolerance = op.CountTol;
            if (Toleranced.TryGetValue(op.Name, out var cls)) {
                if (cls.Tol >= 0) tolerance = cls.Tol;
                if (cls.CountTol >= 0) countTolerance = cls.CountTol;
            }
            for (int i = 0; i < a.Values.Count; ++i) {
                var x = a.Values[i]; var y = b.Values[i];
                if (exact) {
                    if (!x.value.Equals(y.value)) return $"{x.name}: {x.value:R} vs {y.value:R} (not bit identical under an exact symmetry)";
                } else if (x.count) {
                    if (!(Math.Abs(x.value - y.value) <= countTolerance * Math.Max(1, Math.Abs(x.value)))) return $"{x.name}: {x.value} vs {y.value}";
                } else if (!(Math.Abs(x.value - y.value) <= tolerance)) {
                    return $"{x.name}: {x.value:R} vs {y.value:R} (difference {Math.Abs(x.value - y.value):E2} > {tolerance:E0})";
                }
            }
            return "";
        }

        /// <summary> "" if op gives the mapped result under the symmetry, else the first difference or the exception </summary>
        public static string Outcome(Op op, Symmetry sym, bool? forceExact = null)
        {
            try {
                Sig a = Baselines.GetOrAdd(op.Name, _ => Run(op, Symmetry.All48[0]));
                Sig b = Run(op, sym);
                if (b.Contradiction != "") return "claim contradicted: " + b.Contradiction;
                return Compare(op, a, b, forceExact);
            } catch (Exception e) {
                return e.Message;
            }
        }

        static bool Report => Environment.GetEnvironmentVariable("G3_SYMMETRY_REPORT") == "1";

        void Check(string opName, Symmetry sym)
        {
            Op op = ByName(opName);
            string outcome = Outcome(op, sym);
            string key = op.Name + "|" + sym.Name;
            bool known = KnownFailures.TryGetValue(key, out string issue) || KnownFailures.TryGetValue(op.Name + "|*", out issue);
            if (Report) { output.WriteLine($"{key}: {(outcome == "" ? "ok" : outcome)}"); return; }
            if (known) Assert.True(outcome != "", $"{key} passes now: remove its entry ({issue}) from KnownFailures");
            else Assert.True(outcome == "", $"{key}: {outcome}");
        }

        [Theory, MemberData(nameof(PrCases))]
        public void SweepOperation_GivesTheMappedResult_UnderTheSymmetry(string opName, string symmetry) => Check(opName, Symmetry.ByName(symmetry));

        /// <summary> all 48 symmetries of one operation; the nightly job (the run is the check: 47 runs of an operation) </summary>
        [Theory, MemberData(nameof(NightlyOps)), Trait("Category", "Nightly")]
        public void SweepOperation_GivesTheMappedResult_UnderAll48Symmetries(string opName)
        {
            Op op = ByName(opName);
            var failures = new List<string>();
            foreach (Symmetry s in Symmetry.All48.Where(s => !s.IsIdentity && (!op.PlanarInput || s.FixesZ))) {
                string outcome = Outcome(op, s);
                string key = op.Name + "|" + s.Name;
                bool known = KnownFailures.TryGetValue(key, out string issue) || KnownFailures.TryGetValue(op.Name + "|*", out issue);
                if (Report) { string tol = Outcome(op, s, false); output.WriteLine($"{key}: {(outcome == "" ? "ok" : outcome)} || tolerance: {(tol == "" ? "ok" : tol)}"); }
                else if (known && outcome == "") failures.Add($"{key} passes now: remove its entry ({issue})");
                else if (!known && outcome != "") failures.Add($"{key}: {outcome}");
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures.Take(10)) + (failures.Count > 10 ? $"\n... {failures.Count} in all" : ""));
        }

        // ---------------------------------------------------------------- the harness itself

        [Fact]
        public void Group_Has48DistinctSymmetries_24OfThemReflections()
        {
            Assert.Equal(48, Symmetry.All48.Count);
            Assert.Equal(48, Symmetry.All48.Select(s => s.Name).Distinct().Count());
            Assert.True(Symmetry.All48[0].IsIdentity);
            Assert.Equal(24, Symmetry.All48.Count(s => s.Reflects));
            Assert.Equal(8, Symmetry.Square8().Count());
            foreach (Symmetry s in Symmetry.All48) {
                Matrix3d r = s.As.Rotation;
                double det = r[0, 0] * (r[1, 1] * r[2, 2] - r[1, 2] * r[2, 1]) - r[0, 1] * (r[1, 0] * r[2, 2] - r[1, 2] * r[2, 0]) + r[0, 2] * (r[1, 0] * r[2, 1] - r[1, 1] * r[2, 0]);
                Assert.Equal(s.Reflects ? -1.0 : 1.0, det);
            }
        }

        [Fact]
        public void Apply_IsExact_TheMatrixImage_AndInverseRoundTripsBitForBit()
        {
            var points = new[] { new Vector3d(1.5, -2.25, 3.125), new Vector3d(1e-300, 1e300, -0.0), new Vector3d(0, -0.0, 7), new Vector3d(Math.PI, -Math.E, 1.0 / 3) };
            foreach (Symmetry s in Symmetry.All48)
                foreach (Vector3d p in points) {
                    Vector3d q = s.Apply(p), viaMatrix = s.As.Rotation * p;
                    for (int k = 0; k < 3; ++k) {
                        Assert.True(q[k] == viaMatrix[k], $"{s}: {p} maps to {q}, the matrix gives {viaMatrix}");
                        Assert.Equal(BitConverter.DoubleToInt64Bits(p[k]), BitConverter.DoubleToInt64Bits(s.Inverse(q)[k]));
                        Assert.True(s.As.Inverse(q)[k] == p[k], $"{s}: the matrix inverse of {q} is not {p}");
                    }
                }
        }

        [Fact]
        public void Transform_KeepsAClosedMeshOutward_AndValid()
        {
            DMesh3 sphere = Defects.Sphere(new Vector3d(0.1, 0.2, 0.3), 1, 6);
            sphere.EnableVertexNormals(Vector3f.AxisX);
            foreach (int v in sphere.VertexIndices()) sphere.SetVertexNormal(v, (Vector3f)(sphere.GetVertex(v) - new Vector3d(0.1, 0.2, 0.3)).Normalized);
            double volume = Invariance.Volume(sphere);
            Assert.True(volume > 0);
            foreach (Symmetry s in Symmetry.All48) {
                DMesh3 m = s.Transform(sphere);
                Assert.True(m.CheckValidity(true, FailMode.ReturnOnly), s.Name);
                Assert.True(m.IsClosed(), s.Name);
                Assert.Equal(volume, Invariance.Volume(m), 12);
                Assert.Equal(sphere.TriangleCount, m.TriangleCount);
                // the vertices are the mapped vertices, bit for bit, under the same ids
                foreach (int v in sphere.VertexIndices()) Assert.Equal(s.Apply(sphere.GetVertex(v)), m.GetVertex(v));
                // the stored vertex normals of a reflected mesh still point outward (a rewind must not flip them a second time)
                foreach (int v in m.VertexIndices()) Assert.True(m.GetVertexNormal(v).Dot((Vector3f)(m.GetVertex(v) - m.GetBounds().Center)) > 0, $"{s.Name}: normal of vertex {v} points inward");
                // and mapping back (inverse on the vertices, rewind a reflection) gives the original triangle set
                var back = new DMesh3(m);
                foreach (int v in back.VertexIndices()) back.SetVertex(v, s.Inverse(back.GetVertex(v)));
                if (s.Reflects) back.ReverseOrientation(true);
                Assert.Equal("", Invariance.GeometricDifference(sphere, back));
            }
        }

        [Fact]
        public void AReflectionWithoutRewinding_TurnsTheMeshInsideOut_SoTransformMustRewind()
        {
            DMesh3 sphere = Defects.Sphere(Vector3d.Zero, 1, 6);
            Symmetry mirror = Symmetry.ByName("sym -x +y +z");
            Assert.True(mirror.Reflects);
            DMesh3 plain = Invariance.Transformed(sphere, mirror.As);
            Assert.True(Invariance.Volume(plain) < 0, "a reflected mesh keeps its corner order and is inside out");
            Assert.True(Invariance.Volume(mirror.Transform(sphere)) > 0);
        }

        /// <summary> the comparison must see an axis bias: an operation that prefers x differs under the swap of x and y </summary>
        [Fact]
        public void Compare_SeesAnOperationThatPrefersAnAxis()
        {
            // a sphere stretched along x: its vertices left of the x midpoint are not those below the y midpoint
            DMesh3 Elongated() { DMesh3 m = Defects.Sphere(Vector3d.Zero, 1, 6); foreach (int v in m.VertexIndices().ToList()) { Vector3d p = m.GetVertex(v); m.SetVertex(v, new Vector3d(3 * p.x + 0.7 * p.y, p.y, p.z)); } return m; }
            var biased = new Op {
                Name = "fake: counts the vertices with x above 0.5", Input = Elongated,
                Run = (m, s, p) => new Sig(s).Count("right", m.Vertices().Count(v => v.x > 0.5)),
            };
            Sig a = Run(biased, Symmetry.All48[0]);
            string seen = Compare(biased, a, Run(biased, Symmetry.ByName("sym +y +x +z")));
            Assert.NotEqual("", seen);
            // the operation that has no axis choice passes under the same symmetry
            var fair = new Op { Name = "fake: vertex count", Input = () => Defects.Sphere(new Vector3d(0.3, 0, 0), 1, 6), Run = (m, s, p) => new Sig(s).Count("n", m.VertexCount).Count("t", m.TriangleCount) };
            Assert.Equal("", Compare(fair, Run(fair, Symmetry.All48[0]), Run(fair, Symmetry.ByName("sym +y +x +z"))));
        }

        // ---------------------------------------------------------------- ExactMeshBoolean

        static DMesh3 BooleanRun(DMesh3 a, DMesh3 b, Ops op)
        {
            DMesh3 r = BooleanOracle.Run(a, b, op, out string error);
            Assert.True(error == "", error);
            return r;
        }

        public static IEnumerable<object[]> BooleanBoxCases() =>
            ExactBooleanMetamorphicTests.Families.Keys.SelectMany(f => Symmetry.Subset6().Where(s => !s.IsIdentity).Select(s => new object[] { f, s.Name }));

        public static IEnumerable<object[]> BooleanTetCases() =>
            ExactBooleanMetamorphicTests.TetFamilies.Keys.SelectMany(f => Symmetry.Subset6().Where(s => !s.IsIdentity).Select(s => new object[] { f, s.Name }));

        [Theory, MemberData(nameof(BooleanBoxCases))]
        public void Boolean_Boxes_GiveTheMappedSolid_UnderTheSymmetry(string family, string symmetry)
        {
            Symmetry s = Symmetry.ByName(symmetry);
            var (ia, ib) = ExactBooleanMetamorphicTests.Families[family];
            var c = new BoxCase(ia, ib);
            foreach (Ops op in BooleanOracle.AllOps) {
                DMesh3 baseline = BooleanRun(c.MeshA(), c.MeshB(), op);
                DMesh3 mapped = BooleanRun(s.Transform(c.MeshA()), s.Transform(c.MeshB()), op);
                Assert.Equal("", BooleanOracle.CleanReason(mapped));
                Assert.Equal(BooleanOracle.ExactSixVolume(baseline), BooleanOracle.ExactSixVolume(mapped));
                Assert.Equal("", BooleanOracle.SameSolid(mapped, s.Transform(baseline), 4));
            }
        }

        [Theory, MemberData(nameof(BooleanTetCases))]
        public void Boolean_Tetrahedra_GiveTheMappedSolid_UnderTheSymmetry(string family, string symmetry)
        {
            Symmetry s = Symmetry.ByName(symmetry);
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies[family];
            DMesh3 a = ta.Mesh(Vector3d.Zero), b = tb.Mesh(Vector3d.Zero);
            foreach (Ops op in BooleanOracle.AllOps) {
                DMesh3 baseline = BooleanRun(a, b, op);
                DMesh3 mapped = BooleanRun(s.Transform(a), s.Transform(b), op);
                Assert.Equal("", BooleanOracle.CleanReason(mapped));
                Assert.Equal("", BooleanOracle.SameSolid(mapped, s.Transform(baseline), 8));
            }
        }

        // ---------------------------------------------------------------- the 2D quality mesher

        static Mesh2Input Mapped(Mesh2Input input, Symmetry s)
        {
            var m = new Mesh2Input {
                X = (double[])input.X.Clone(), Y = (double[])input.Y.Clone(), MinAngleDeg = input.MinAngleDeg, MaxArea = input.MaxArea, SteinerLimit = input.SteinerLimit,
                SnapTolerance = input.SnapTolerance, ResolveCrossings = input.ResolveCrossings, Conforming = input.Conforming, ConstrainedQuality = input.ConstrainedQuality,
            };
            for (int i = 0; i < m.X.Length; ++i) { Vector2d q = s.Apply2d(new Vector2d(input.X[i], input.Y[i])); m.X[i] = q.x; m.Y[i] = q.y; }
            m.Segments.AddRange(input.Segments);
            m.Rings.AddRange(input.Rings);
            return m;
        }

        /// <summary>
        /// The snap rounding of the mesher rounds a coordinate on a tie (x.5 of a pixel) in one direction, which no reflection
        /// preserves (3.5 goes to 4, but its mirror image -3.5 goes to -3): a classified difference by design. The inputs of the
        /// fuzz have coordinates on a 0.1 grid, so a tenth of them are ties: they are moved by 0.01 off the tie.
        /// </summary>
        static void AvoidSnapTies(Mesh2Input input)
        {
            for (int i = 0; i < input.X.Length; ++i) {
                if (Math.Abs(Math.Abs(input.X[i] - Math.Floor(input.X[i])) - 0.5) < 1e-9) input.X[i] += 0.01;
                if (Math.Abs(Math.Abs(input.Y[i] - Math.Floor(input.Y[i])) - 0.5) < 1e-9) input.Y[i] += 0.01;
            }
        }

        public static IEnumerable<object[]> MesherCases() =>
            Enumerable.Range(0, 6).SelectMany(seed => Symmetry.Square4().Where(s => !s.IsIdentity).Select(s => new object[] { seed, s.Name }));

        [Theory, MemberData(nameof(MesherCases))]
        public void QualityMesh2_StarPolygon_IsValidAndTheSameSize_UnderTheSymmetryOfTheSquare(int seed, string symmetry)
        {
            Symmetry s = Symmetry.ByName(symmetry);
            Mesh2Input input = StarPolygonFuzz.Make(seed);
            AvoidSnapTies(input);
            Mesh2Input moved = Mapped(input, s);
            Mesh2Output a = new QualityMesh2Candidate().Run(input), b = new QualityMesh2Candidate().Run(moved);
            Assert.True(a.Status == Mesh2Status.Success && b.Status == Mesh2Status.Success, $"{a.Status} {a.FailureKind} / {b.Status} {b.FailureKind}");
            OracleReport report = Oracle.Check(moved, b, new OracleExpect { Milestone = Milestone.M3 });
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.Triangles.Length, b.Triangles.Length);
        }
    }
}
