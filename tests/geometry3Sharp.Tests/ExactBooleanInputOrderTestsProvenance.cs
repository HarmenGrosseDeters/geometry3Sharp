using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// End-to-end tests for opt-in <see cref="ExactMeshBoolean.PreserveTriangleGroups"/>,
    /// <see cref="ExactMeshBoolean.GroupOffsets"/>, and <see cref="ExactMeshBoolean.TrackFaceProvenance"/>
    /// (<see cref="ExactMeshBoolean.ResultProvenance"/>): bit-identity when off, group/material seam preservation
    /// across coplanar flush cuts, per-face provenance accuracy across SafeRounding edits, similarity invariance,
    /// reuse, and serial == parallel determinism.
    /// </summary>
    public class ExactBooleanInputOrderTestsProvenance
    {
        static DMesh3 ApplySimilarity(DMesh3 src, Similarity sim)
        {
            DMesh3 copy = new DMesh3(src);
            foreach (int vid in copy.VertexIndices())
                copy.SetVertex(vid, sim.Apply(copy.GetVertex(vid)));
            return copy;
        }

        static DMesh3 TaggedBox(double x0, double y0, double z0, double x1, double y1, double z1, int baseGroup)
        {
            var g = new TrivialBox3Generator {
                Box = new Box3d(new Vector3d(0.5 * (x0 + x1), 0.5 * (y0 + y1), 0.5 * (z0 + z1)),
                    new Vector3d(0.5 * (x1 - x0), 0.5 * (y1 - y0), 0.5 * (z1 - z0))),
                NoSharedVertices = false
            };
            DMesh3 m = g.Generate().MakeDMesh();
            m.EnableTriangleGroups(0);
            foreach (int tid in m.TriangleIndices()) {
                Vector3d n = m.GetTriNormal(tid);
                int face = Math.Abs(n.x) > 0.5 ? (n.x > 0 ? 0 : 1)
                    : Math.Abs(n.y) > 0.5 ? (n.y > 0 ? 2 : 3)
                    : (n.z > 0 ? 4 : 5);
                m.SetTriangleGroup(tid, baseGroup + face);
            }
            return m;
        }

        // 2x1x1 closed box whose top face (z = 1) has left half [0,1] in groupLeft and right half [1,2] in groupRight.
        static DMesh3 SplitTopBox(int groupLeft, int groupRight, int otherGroup)
        {
            var m = new DMesh3();
            m.EnableTriangleGroups(otherGroup);
            int[,] b = new int[3, 2];
            int[,] t = new int[3, 2];
            for (int ix = 0; ix < 3; ++ix) {
                for (int iy = 0; iy < 2; ++iy) {
                    b[ix, iy] = m.AppendVertex(new Vector3d(ix, iy, 0));
                    t[ix, iy] = m.AppendVertex(new Vector3d(ix, iy, 1));
                }
            }
            void quad(int a, int bb, int c, int d, int g)
            {
                m.SetTriangleGroup(m.AppendTriangle(a, bb, c), g);
                m.SetTriangleGroup(m.AppendTriangle(a, c, d), g);
            }
            quad(t[0, 0], t[1, 0], t[1, 1], t[0, 1], groupLeft);
            quad(t[1, 0], t[2, 0], t[2, 1], t[1, 1], groupRight);
            quad(b[0, 0], b[0, 1], b[1, 1], b[1, 0], otherGroup);
            quad(b[1, 0], b[1, 1], b[2, 1], b[2, 0], otherGroup);
            quad(b[0, 0], b[1, 0], t[1, 0], t[0, 0], otherGroup);
            quad(b[1, 0], b[2, 0], t[2, 0], t[1, 0], otherGroup);
            quad(b[1, 1], b[0, 1], t[0, 1], t[1, 1], otherGroup);
            quad(b[2, 1], b[1, 1], t[1, 1], t[2, 1], otherGroup);
            quad(b[0, 1], b[0, 0], t[0, 0], t[0, 1], otherGroup);
            quad(b[2, 0], b[2, 1], t[2, 1], t[2, 0], otherGroup);
            return m;
        }

        static DMesh3 TaggedSphere(Vector3d center, double radius, int subdivisions, int groupId)
        {
            var g = new Sphere3Generator_NormalizedCube { Radius = radius, EdgeVertices = subdivisions };
            DMesh3 m = g.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            m.EnableTriangleGroups(groupId);
            foreach (int tid in m.TriangleIndices()) m.SetTriangleGroup(tid, groupId);
            return m;
        }

        static DMesh3 TaggedCylinder(Vector3d center, double radius, double height, int slices, int groupId)
        {
            var g = new CappedCylinderGenerator { BaseRadius = (float)radius, TopRadius = (float)radius, Height = (float)height, Slices = slices };
            DMesh3 m = g.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            m.EnableTriangleGroups(groupId);
            foreach (int tid in m.TriangleIndices()) m.SetTriangleGroup(tid, groupId);
            return m;
        }

        static double PointTriangleDist(Vector3d p, DMesh3 src, int tid)
        {
            Index3i st = src.GetTriangle(tid);
            Vector3d a = src.GetVertex(st.a), b = src.GetVertex(st.b), c = src.GetVertex(st.c);
            Vector3d w = ExactMeshAttributeTransfer.LocalBarycentric(p, a, b, c, clamp: true);
            // Closest point in local frame shifted to a (avoids sqrt(eps) cancellation from DistPoint3Triangle3.GetSquared)
            Vector3d closestRel = w.y * (b - a) + w.z * (c - a);
            return ((p - a) - closestRel).Length;
        }

        static void AssertValidProvenance(DMesh3 result, TriangleProvenance[] prov, IReadOnlyList<DMesh3> inputs, double vertexTol)
        {
            Assert.NotNull(prov);
            Assert.Equal(result.MaxTriangleID, prov.Length);
            for (int tid = 0; tid < result.MaxTriangleID; ++tid) {
                if (!result.IsTriangle(tid)) {
                    Assert.Equal(TriangleProvenance.None, prov[tid]);
                    continue;
                }
                TriangleProvenance tp = prov[tid];
                Assert.True(tp.IsValid, $"Live triangle {tid} has invalid provenance");
                Assert.InRange(tp.MeshIndex, 0, inputs.Count - 1);
                DMesh3 src = inputs[tp.MeshIndex];
                Assert.True(src.IsTriangle(tp.SourceTriangle), $"SourceTriangle {tp.SourceTriangle} invalid in input {tp.MeshIndex}");

                Index3i rt = result.GetTriangle(tid);
                Vector3d p0 = result.GetVertex(rt.a), p1 = result.GetVertex(rt.b), p2 = result.GetVertex(rt.c);
                double d0 = PointTriangleDist(p0, src, tp.SourceTriangle);
                double d1 = PointTriangleDist(p1, src, tp.SourceTriangle);
                double d2 = PointTriangleDist(p2, src, tp.SourceTriangle);
                Assert.True(d0 <= vertexTol && d1 <= vertexTol && d2 <= vertexTol,
                    $"Triangle {tid} vertices drift ({d0:E3}, {d1:E3}, {d2:E3}) > {vertexTol:E3} from input {tp.MeshIndex} tri {tp.SourceTriangle}");

                // Orientation agreement with Reversed flag (for non-sliver triangles)
                Vector3d rn = (p1 - p0).Cross(p2 - p0);
                Vector3d sn = src.GetTriNormal(tp.SourceTriangle);
                if (rn.Length > 1e-12 * vertexTol && sn.Length > 1e-12) {
                    rn.Normalize();
                    double dot = rn.Dot(sn);
                    if (tp.Reversed)
                        Assert.True(dot < -0.5, $"Expected reversed normal for tri {tid}, dot={dot}");
                    else
                        Assert.True(dot > 0.5, $"Expected matching normal for tri {tid}, dot={dot}");
                }
            }
        }

        [Fact]
        public void DefaultFlagsOff_NoGroupsAndNullProvenance()
        {
            DMesh3 a = TaggedBox(0, 0, 0, 2, 2, 2, 10);
            DMesh3 b = TaggedBox(1, 1, 1, 3, 3, 3, 20);
            var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.False(eb.PreserveTriangleGroups);
            Assert.False(eb.TrackFaceProvenance);
            Assert.True(eb.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(eb.Result));
            Assert.False(eb.Result.HasTriangleGroups);
            Assert.Null(eb.ResultProvenance);
        }

        [Fact]
        public void PreserveTriangleGroups_CoplanarFlushCut_NeverMergesDifferentGroups()
        {
            // Box A has top face z = 1 split into group 10 (x in [0,1]) and group 20 (x in [1,2]).
            // Box B sits flush at z = 1 over [0.5, 1.5] x [0.25, 0.75] x [0.2, 1.0].
            // A \ B removes a 0.5x0.5 rectangle from group 10 (leaving area 0.75) and 0.5x0.5 from group 20 (leaving area 0.75).
            DMesh3 a = SplitTopBox(10, 20, 30);
            DMesh3 b = TaggedBox(0.5, 0.25, 0.2, 1.5, 0.75, 1.0, 100);

            foreach (bool trackProv in new[] { false, true }) {
                var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference) {
                    PreserveTriangleGroups = true,
                    TrackFaceProvenance = trackProv
                };
                Assert.True(eb.Compute());
                Assert.Equal("", MeshRobustRepair.NotCleanReason(eb.Result));
                Assert.True(eb.Result.HasTriangleGroups);

                var areaByGroup = new Dictionary<int, double>();
                foreach (int tid in eb.Result.TriangleIndices()) {
                    int g = eb.Result.GetTriangleGroup(tid);
                    Assert.True(g == 10 || g == 20 || g == 30 || (g >= 100 && g <= 105), "Unexpected group " + g);
                    areaByGroup[g] = (areaByGroup.TryGetValue(g, out double prev) ? prev : 0) + eb.Result.GetTriArea(tid);
                    if (g == 10 || g == 20) {
                        Index3i tv = eb.Result.GetTriangle(tid);
                        for (int k = 0; k < 3; ++k) {
                            Vector3d p = eb.Result.GetVertex(tv[k]);
                            Assert.InRange(p.z, 1.0 - 1e-12, 1.0 + 1e-12);
                            if (g == 10) Assert.True(p.x <= 1.0 + 1e-12);
                            if (g == 20) Assert.True(p.x >= 1.0 - 1e-12);
                        }
                    }
                }
                Assert.InRange(areaByGroup[10], 0.75 - 1e-11, 0.75 + 1e-11);
                Assert.InRange(areaByGroup[20], 0.75 - 1e-11, 0.75 + 1e-11);
                if (trackProv)
                    AssertValidProvenance(eb.Result, eb.ResultProvenance, new[] { a, b }, 1e-11);
                else
                    Assert.Null(eb.ResultProvenance);
            }
        }

        [Fact]
        public void PreserveTriangleGroups_RegionClippingOnAndOff_Agree_AndOffMergesCoplanarSeam()
        {
            DMesh3 a = SplitTopBox(10, 20, 30);
            DMesh3 b = TaggedBox(0.5, 0.25, 0.2, 1.5, 0.75, 1.0, 100);

            // With PreserveTriangleGroups = false, ExactPlanarSimplify merges across the coplanar seam x = 1.
            var ebOff = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference) {
                PreserveTriangleGroups = false
            };
            Assert.True(ebOff.Compute());
            Assert.False(ebOff.Result.HasTriangleGroups);

            // With PreserveTriangleGroups = true, RegionClipping = true and false both preserve exact group 10/20 areas.
            foreach (bool rc in new[] { true, false }) {
                var ebOn = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference) {
                    PreserveTriangleGroups = true,
                    RegionClipping = rc
                };
                Assert.True(ebOn.Compute());
                Assert.Equal("", MeshRobustRepair.NotCleanReason(ebOn.Result));
                double a10 = 0, a20 = 0;
                foreach (int tid in ebOn.Result.TriangleIndices()) {
                    int g = ebOn.Result.GetTriangleGroup(tid);
                    if (g == 10) a10 += ebOn.Result.GetTriArea(tid);
                    else if (g == 20) a20 += ebOn.Result.GetTriArea(tid);
                }
                Assert.InRange(a10, 0.75 - 1e-11, 0.75 + 1e-11);
                Assert.InRange(a20, 0.75 - 1e-11, 0.75 + 1e-11);
                // Preserving the coplanar group seam keeps at least as many top-face triangles as merging across it.
                Assert.True(ebOn.Result.TriangleCount >= ebOff.Result.TriangleCount);
            }
        }

        [Fact]
        public void PreserveTriangleGroups_WithGroupOffsets_DisambiguatesSameInputGroupIds()
        {
            DMesh3 a = TaggedBox(0, 0, 0, 2, 2, 2, 0);
            DMesh3 b = TaggedBox(1, 0, 0, 3, 2, 2, 0); // flush coplanar top/bottom/front/back with SAME raw group ids 0..5!

            var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                GroupOffsets = new[] { 0, 100 }
            };
            Assert.True(eb.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(eb.Result));
            Assert.True(eb.Result.HasTriangleGroups);

            var groups = new HashSet<int>();
            double areaFromA = 0, areaFromB = 0;
            foreach (int tid in eb.Result.TriangleIndices()) {
                int g = eb.Result.GetTriangleGroup(tid);
                groups.Add(g);
                if (g < 100) areaFromA += eb.Result.GetTriArea(tid);
                else areaFromB += eb.Result.GetTriArea(tid);
            }
            Assert.Contains(4, groups);     // A's +Z top face (x in [0, 2], area 4)
            Assert.Contains(104, groups);   // B's +Z top face (x in [2, 3], area 2)
            Assert.True(areaFromA > 0 && areaFromB > 0);
            // Total surface area of 3x2x2 box = 2*(6 + 6 + 4) = 32
            Assert.InRange(areaFromA + areaFromB, 32.0 - 1e-10, 32.0 + 1e-10);
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Union)]
        [InlineData(ExactMeshBoolean.Operations.Intersection)]
        [InlineData(ExactMeshBoolean.Operations.Difference)]
        public void TrackFaceProvenance_SphereAndCylinder_AllOperations_ExactProvenanceAndOrientation(ExactMeshBoolean.Operations op)
        {
            DMesh3 sphere = TaggedSphere(Vector3d.Zero, 1.0, 6, 11);
            DMesh3 cyl = TaggedCylinder(new Vector3d(0.4, 0, -1.2), 0.5, 2.4, 16, 22);

            var eb = new ExactMeshBoolean(sphere, cyl, op) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(eb.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(eb.Result));
            AssertValidProvenance(eb.Result, eb.ResultProvenance, new[] { sphere, cyl }, 1e-10);

            int fromA = 0, fromB = 0, reversedB = 0;
            foreach (int tid in eb.Result.TriangleIndices()) {
                TriangleProvenance tp = eb.ResultProvenance[tid];
                int expectedGroup = tp.MeshIndex == 0 ? 11 : 22;
                Assert.Equal(expectedGroup, eb.Result.GetTriangleGroup(tid));
                if (tp.MeshIndex == 0) {
                    fromA++;
                    Assert.False(tp.Reversed);
                } else {
                    fromB++;
                    if (tp.Reversed) reversedB++;
                }
            }
            Assert.True(fromA > 0 && fromB > 0);
            if (op == ExactMeshBoolean.Operations.Difference)
                Assert.Equal(fromB, reversedB);
            else
                Assert.Equal(0, reversedB);
        }

        [Fact]
        public void TrackFaceProvenance_NaryUnion_AndSerialBuilderFallback()
        {
            DMesh3 a = TaggedBox(0, 0, 0, 2, 2, 2, 10);
            DMesh3 b = TaggedBox(1, 0, 0, 3, 2, 2, 20);
            DMesh3 c = TaggedBox(0.5, 1, 0.5, 2.5, 3, 2.5, 30);
            var inputs = new List<DMesh3> { a, b, c };

            var ebFast = new ExactMeshBoolean(inputs, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(ebFast.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(ebFast.Result));
            AssertValidProvenance(ebFast.Result, ebFast.ResultProvenance, inputs, 1e-11);

            // Force serial assembly path (ExactMeshResultBuilder.Disabled = true) and verify identical provenance & groups.
            bool prevDisabled = ExactMeshResultBuilder.Disabled;
            try {
                ExactMeshResultBuilder.Disabled = true;
                var ebSerial = new ExactMeshBoolean(inputs, ExactMeshBoolean.Operations.Union) {
                    PreserveTriangleGroups = true,
                    TrackFaceProvenance = true
                };
                Assert.True(ebSerial.Compute());
                Assert.Equal("", MeshRobustRepair.NotCleanReason(ebSerial.Result));
                Assert.Equal("", Invariance.Difference(ebFast.Result, ebSerial.Result));
                AssertValidProvenance(ebSerial.Result, ebSerial.ResultProvenance, inputs, 1e-11);
                foreach (int tid in ebFast.Result.TriangleIndices()) {
                    Assert.Equal(ebFast.Result.GetTriangleGroup(tid), ebSerial.Result.GetTriangleGroup(tid));
                    Assert.Equal(ebFast.ResultProvenance[tid], ebSerial.ResultProvenance[tid]);
                }
            } finally {
                ExactMeshResultBuilder.Disabled = prevDisabled;
            }
        }

        [Fact]
        public void TrackFaceProvenance_EdgeTouchingCubes_NonManifoldSplitPath()
        {
            // Two cubes touching along a shared edge x=1, y=1: exercises non-manifold edge pairing/serial assembly.
            DMesh3 a = TaggedBox(0, 0, 0, 1, 1, 1, 10);
            DMesh3 b = TaggedBox(1, 1, 0, 2, 2, 1, 20);
            var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(eb.Compute());
            Assert.True(eb.NonManifoldEdges > 0);
            AssertValidProvenance(eb.Result, eb.ResultProvenance, new[] { a, b }, 1e-11);
            foreach (int tid in eb.Result.TriangleIndices()) {
                TriangleProvenance tp = eb.ResultProvenance[tid];
                int expected = (tp.MeshIndex == 0 ? a : b).GetTriangleGroup(tp.SourceTriangle);
                Assert.Equal(expected, eb.Result.GetTriangleGroup(tid));
            }
        }

        [Fact]
        public void TrackFaceProvenance_ReorientedInput_ReversedMatchesOriginalInputTriangle()
        {
            DMesh3 a = TaggedBox(0, 0, 0, 2, 2, 2, 10);
            // Flip 1 triangle of A so AreaMajority reorients it back to outward.
            int flippedTid = 0;
            a.ReverseTriOrientation(flippedTid);
            DMesh3 b = TaggedBox(1, 1, 1, 3, 3, 3, 20);

            var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) {
                Orientation = ExactMeshBoolean.OrientationModes.AreaMajority,
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(eb.Compute());
            Assert.Equal(1, eb.ReorientedTriangles);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(eb.Result));

            bool sawFlippedSource = false;
            foreach (int tid in eb.Result.TriangleIndices()) {
                TriangleProvenance tp = eb.ResultProvenance[tid];
                if (tp.MeshIndex == 0 && tp.SourceTriangle == flippedTid) {
                    sawFlippedSource = true;
                    // Original triangle flippedTid was inward; result is outward => Reversed MUST be true!
                    Assert.True(tp.Reversed);
                } else {
                    Assert.False(tp.Reversed);
                }
            }
            Assert.True(sawFlippedSource);
        }

        [Fact]
        public void SafeRounding_RoundingEditedTriangles_RederivesProvenanceAndGroups()
        {
            // Rotated nearly-coincident boxes separated by ~1 ulp trigger MeshRoundingRepair edits.
            DMesh3 a = TaggedBox(0, 0, 0, 2, 2, 2, 10);
            DMesh3 b = TaggedBox(0.5, 0.5, 0.5, 2.5, 2.5, 2.5, 20);
            double eps = 2e-15;
            MeshTransforms.Translate(b, new Vector3d(eps, -eps, eps));
            Quaterniond q = Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 27.3);
            MeshTransforms.Rotate(a, Vector3d.Zero, q);
            MeshTransforms.Rotate(b, Vector3d.Zero, q);

            // 1. Both TrackFaceProvenance + PreserveTriangleGroups
            var ebBoth = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(ebBoth.Compute());
            Assert.True(ebBoth.RoundingClean);
            // Also try a second pair if this specific angle didn't need edits
            if (ebBoth.Counts["rounding_edits"] == 0) {
                DMesh3 s1 = TaggedSphere(Vector3d.Zero, 1.0, 8, 10);
                DMesh3 s2 = TaggedSphere(new Vector3d(1e-14, 0, 0), 1.0, 8, 20);
                MeshTransforms.Rotate(s2, Vector3d.Zero, Quaterniond.AxisAngleD(Vector3d.AxisZ, 13.7));
                ebBoth = new ExactMeshBoolean(s1, s2, ExactMeshBoolean.Operations.Union) {
                    PreserveTriangleGroups = true,
                    TrackFaceProvenance = true
                };
                Assert.True(ebBoth.Compute());
                Assert.True(ebBoth.Counts["rounding_edits"] > 0, "Expected SafeRounding edits on near-coincident rotated spheres.");
                a = s1; b = s2;
            }
            Assert.True(ebBoth.Counts["rounding_edits"] > 0);
            double tol = Math.Max(1e-9, 4.0 * ebBoth.Rounding.MaxLength);
            AssertValidProvenance(ebBoth.Result, ebBoth.ResultProvenance, new[] { a, b }, tol);
            foreach (int tid in ebBoth.Result.TriangleIndices()) {
                TriangleProvenance tp = ebBoth.ResultProvenance[tid];
                int expected = (tp.MeshIndex == 0 ? a : b).GetTriangleGroup(tp.SourceTriangle);
                Assert.Equal(expected, ebBoth.Result.GetTriangleGroup(tid));
            }

            // 2. PreserveTriangleGroups alone (exercises rederive_groups path)
            var ebGroupsOnly = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = false
            };
            Assert.True(ebGroupsOnly.Compute());
            Assert.True(ebGroupsOnly.Counts["rounding_edits"] > 0, "Expected SafeRounding edits in rederive_groups test.");
            Assert.Null(ebGroupsOnly.ResultProvenance);
            Assert.True(ebGroupsOnly.Result.HasTriangleGroups);
            var validGroups = new HashSet<int>();
            foreach (int tid in a.TriangleIndices()) validGroups.Add(a.GetTriangleGroup(tid));
            foreach (int tid in b.TriangleIndices()) validGroups.Add(b.GetTriangleGroup(tid));
            foreach (int tid in ebGroupsOnly.Result.TriangleIndices())
                Assert.Contains(ebGroupsOnly.Result.GetTriangleGroup(tid), validGroups);
        }

        [Fact]
        public void Invariance_PowerOfTwoScales_BitIdenticalProvenanceAndGroups()
        {
            DMesh3 a0 = SplitTopBox(10, 20, 30);
            DMesh3 b0 = TaggedBox(0.5, 0.25, 0.2, 1.5, 0.75, 1.0, 100);

            var ebBase = new ExactMeshBoolean(a0, b0, ExactMeshBoolean.Operations.Difference) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(ebBase.Compute());

            foreach (Similarity sim in new[] { Similarity.Pow2Small, Similarity.Pow2Large }) {
                DMesh3 a = ApplySimilarity(a0, sim);
                DMesh3 b = ApplySimilarity(b0, sim);
                var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference) {
                    PreserveTriangleGroups = true,
                    TrackFaceProvenance = true
                };
                Assert.True(eb.Compute());
                Assert.Equal(ebBase.Result.MaxTriangleID, eb.Result.MaxTriangleID);
                Assert.Equal(ebBase.ResultProvenance.Length, eb.ResultProvenance.Length);
                for (int tid = 0; tid < ebBase.Result.MaxTriangleID; ++tid) {
                    Assert.Equal(ebBase.ResultProvenance[tid], eb.ResultProvenance[tid]);
                    if (ebBase.Result.IsTriangle(tid))
                        Assert.Equal(ebBase.Result.GetTriangleGroup(tid), eb.Result.GetTriangleGroup(tid));
                }
            }
        }

        [Fact]
        public void EdgeCases_EmptyUngroupedSimplifyOffAndOpenMesh()
        {
            // 1. Disjoint intersection => empty result with empty ResultProvenance and TriangleGroups enabled
            DMesh3 a = TaggedBox(0, 0, 0, 1, 1, 1, 10);
            DMesh3 b = TaggedBox(5, 5, 5, 6, 6, 6, 20);
            var ebEmpty = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Intersection) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(ebEmpty.Compute());
            Assert.Equal(0, ebEmpty.Result.TriangleCount);
            Assert.True(ebEmpty.Result.HasTriangleGroups);
            Assert.NotNull(ebEmpty.ResultProvenance);
            Assert.Empty(ebEmpty.ResultProvenance);

            // 2. Ungrouped inputs (!HasTriangleGroups) + GroupOffsets
            DMesh3 u1 = new DMesh3(TaggedBox(0, 0, 0, 2, 2, 2, 0));
            u1.DiscardTriangleGroups();
            DMesh3 u2 = new DMesh3(TaggedBox(1, 1, 1, 3, 3, 3, 0));
            u2.DiscardTriangleGroups();
            var ebUngrouped = new ExactMeshBoolean(u1, u2, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                GroupOffsets = new[] { 10, 20 }
            };
            Assert.True(ebUngrouped.Compute());
            Assert.True(ebUngrouped.Result.HasTriangleGroups);
            var seenG = new HashSet<int>();
            foreach (int tid in ebUngrouped.Result.TriangleIndices())
                seenG.Add(ebUngrouped.Result.GetTriangleGroup(tid));
            Assert.Equal(new HashSet<int> { 10, 20 }, seenG);

            // 3. SimplifyFlatRegions = false with PreserveTriangleGroups = true, TrackFaceProvenance = false
            DMesh3 sA = SplitTopBox(10, 20, 30);
            DMesh3 sB = TaggedBox(0.5, 0.25, 0.2, 1.5, 0.75, 1.0, 100);
            var ebNoSimp = new ExactMeshBoolean(sA, sB, ExactMeshBoolean.Operations.Difference) {
                SimplifyFlatRegions = false,
                PreserveTriangleGroups = true,
                TrackFaceProvenance = false
            };
            Assert.True(ebNoSimp.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(ebNoSimp.Result));
            double area10 = 0, area20 = 0;
            foreach (int tid in ebNoSimp.Result.TriangleIndices()) {
                int g = ebNoSimp.Result.GetTriangleGroup(tid);
                if (g == 10) area10 += ebNoSimp.Result.GetTriArea(tid);
                else if (g == 20) area20 += ebNoSimp.Result.GetTriArea(tid);
            }
            Assert.InRange(area10, 0.75 - 1e-11, 0.75 + 1e-11);
            Assert.InRange(area20, 0.75 - 1e-11, 0.75 + 1e-11);

            // 4. Open mesh (AllowOpenMeshes = true) keeps valid provenance and groups
            DMesh3 openA = TaggedBox(0, 0, 0, 2, 2, 2, 10);
            openA.RemoveTriangle(0, true, false);
            DMesh3 solidB = TaggedBox(1, 1, 1, 3, 3, 3, 20);
            var ebOpen = new ExactMeshBoolean(openA, solidB, ExactMeshBoolean.Operations.Union) {
                AllowOpenMeshes = true,
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(ebOpen.Compute());
            Assert.True(ebOpen.Result.TriangleCount > 0);
            AssertValidProvenance(ebOpen.Result, ebOpen.ResultProvenance, new[] { openA, solidB }, 1e-9);
        }

        [Fact]
        public void Invariance_SimilarityAll_ProvenanceAndGroupsHold()
        {
            DMesh3 a0 = SplitTopBox(10, 20, 30);
            // Pierces z = 1.0 cleanly (z up to 1.2) so even inexact 3D rotations preserve the exact 0.5x0.5 cut on both groups.
            DMesh3 b0 = TaggedBox(0.5, 0.25, 0.2, 1.5, 0.75, 1.2, 100);

            foreach (Similarity sim in Similarity.All()) {
                DMesh3 a = ApplySimilarity(a0, sim);
                DMesh3 b = ApplySimilarity(b0, sim);
                var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference) {
                    PreserveTriangleGroups = true,
                    TrackFaceProvenance = true
                };
                Assert.True(eb.Compute(), $"Failed under similarity {sim.Name}: {eb.Error}");
                Assert.Equal("", MeshRobustRepair.NotCleanReason(eb.Result));
                double diag = eb.Result.GetBounds().DiagonalLength;
                double tol = Math.Max(1e-11 * diag, eb.Rounding != null ? 4.0 * eb.Rounding.MaxLength : 0);
                AssertValidProvenance(eb.Result, eb.ResultProvenance, new[] { a, b }, tol);

                double area10 = 0, area20 = 0;
                foreach (int tid in eb.Result.TriangleIndices()) {
                    int g = eb.Result.GetTriangleGroup(tid);
                    if (g == 10) area10 += eb.Result.GetTriArea(tid);
                    else if (g == 20) area20 += eb.Result.GetTriArea(tid);
                }
                double expected = 0.75 * sim.Scale * sim.Scale;
                double relTol = Math.Max(1e-8, 16.0 * sim.Resolution(2.0 * sim.Scale) / sim.Scale);
                Assert.True(Math.Abs(area10 - expected) <= relTol * expected,
                    $"{sim.Name}: area10={area10} vs {expected} (relTol={relTol:E2})");
                Assert.True(Math.Abs(area20 - expected) <= relTol * expected,
                    $"{sim.Name}: area20={area20} vs {expected} (relTol={relTol:E2})");
            }
        }

        [Fact]
        public void Reuse_TogglingFlagsAcrossComputeCalls_CleanlyResetsState()
        {
            DMesh3 a = TaggedBox(0, 0, 0, 2, 2, 2, 10);
            DMesh3 b = TaggedBox(1, 1, 1, 3, 3, 3, 20);
            var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);

            // 1. Default off
            Assert.True(eb.Compute());
            DMesh3 baseline = new DMesh3(eb.Result);
            Assert.False(eb.Result.HasTriangleGroups);
            Assert.Null(eb.ResultProvenance);

            // 2. Both on
            eb.PreserveTriangleGroups = true;
            eb.TrackFaceProvenance = true;
            Assert.True(eb.Compute());
            Assert.True(eb.Result.HasTriangleGroups);
            Assert.NotNull(eb.ResultProvenance);
            AssertValidProvenance(eb.Result, eb.ResultProvenance, new[] { a, b }, 1e-11);

            // 3. Back to both off => bit-identical to run 1
            eb.PreserveTriangleGroups = false;
            eb.TrackFaceProvenance = false;
            Assert.True(eb.Compute());
            Assert.False(eb.Result.HasTriangleGroups);
            Assert.Null(eb.ResultProvenance);
            Assert.Equal("", Invariance.Difference(baseline, eb.Result));

            // 4. Invalid input clears ResultProvenance
            eb.TrackFaceProvenance = true;
            eb.MeshB = null;
            Assert.False(eb.Compute());
            Assert.Null(eb.Result);
            Assert.Null(eb.ResultProvenance);
        }

        [Fact]
        public void Determinism_SerialEqualsParallel_ProvenanceAndGroupsBitIdentical()
        {
            DMesh3 a = TaggedSphere(Vector3d.Zero, 1.0, 6, 10);
            DMesh3 b = TaggedSphere(new Vector3d(0.7, 0.2, 0.1), 0.9, 6, 20);

            var ebPar = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(ebPar.Compute());

            var ebSer = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true,
                ArrangementSingleShard = true
            };
            Assert.True(ebSer.Compute());

            Assert.Equal("", Invariance.Difference(ebPar.Result, ebSer.Result));
            Assert.Equal(ebPar.ResultProvenance.Length, ebSer.ResultProvenance.Length);
            for (int tid = 0; tid < ebPar.Result.MaxTriangleID; ++tid) {
                Assert.Equal(ebPar.ResultProvenance[tid], ebSer.ResultProvenance[tid]);
                if (ebPar.Result.IsTriangle(tid))
                    Assert.Equal(ebPar.Result.GetTriangleGroup(tid), ebSer.Result.GetTriangleGroup(tid));
            }
        }
    }
}
