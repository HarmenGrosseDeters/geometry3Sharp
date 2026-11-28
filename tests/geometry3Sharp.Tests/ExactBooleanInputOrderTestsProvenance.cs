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

        [Fact]
        public void SplitNonManifoldEdges_FanAssembly_PreservesProvenanceAndGroups()
        {
            // PinchedSolid: two checkerboard cubes touching along x=y=1 bridged by top/bottom slabs so SplitNonManifoldEdges == 1.
            var inputs = new List<DMesh3> {
                TaggedBox(0, 0, 0, 1, 1, 1, 10),
                TaggedBox(1, 1, 0, 2, 2, 1, 20),
                TaggedBox(0, 0, -1, 2, 2, 0.25, 30),
                TaggedBox(0, 0, 0.75, 2, 2, 2, 40)
            };

            var ebProv = new ExactMeshBoolean(inputs, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(ebProv.Compute());
            Assert.Equal(1, ebProv.SplitNonManifoldEdges);
            Assert.True(ebProv.Result.IsClosed());
            Assert.True(ebProv.Result.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(ExactMeshBoolean.IsWindingClosed(ebProv.Result));
            AssertValidProvenance(ebProv.Result, ebProv.ResultProvenance, inputs, 1e-11);
            foreach (int tid in ebProv.Result.TriangleIndices()) {
                TriangleProvenance tp = ebProv.ResultProvenance[tid];
                Assert.Equal(inputs[tp.MeshIndex].GetTriangleGroup(tp.SourceTriangle), ebProv.Result.GetTriangleGroup(tid));
            }

            // Groups-only through the same fan-split path must produce identical groups on the top/bottom/cube faces.
            var ebGroups = new ExactMeshBoolean(inputs, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = false
            };
            Assert.True(ebGroups.Compute());
            Assert.Equal(1, ebGroups.SplitNonManifoldEdges);
            Assert.True(ebGroups.Result.IsClosed());
            Assert.True(ebGroups.Result.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(ExactMeshBoolean.IsWindingClosed(ebGroups.Result));
            Assert.Null(ebGroups.ResultProvenance);
            var areaByGroupProv = new Dictionary<int, double>();
            var areaByGroupOnly = new Dictionary<int, double>();
            foreach (int tid in ebProv.Result.TriangleIndices()) {
                int g = ebProv.Result.GetTriangleGroup(tid);
                areaByGroupProv[g] = (areaByGroupProv.TryGetValue(g, out double v) ? v : 0) + ebProv.Result.GetTriArea(tid);
            }
            foreach (int tid in ebGroups.Result.TriangleIndices()) {
                int g = ebGroups.Result.GetTriangleGroup(tid);
                areaByGroupOnly[g] = (areaByGroupOnly.TryGetValue(g, out double v) ? v : 0) + ebGroups.Result.GetTriArea(tid);
            }
            Assert.Equal(areaByGroupProv.Count, areaByGroupOnly.Count);
            foreach (var kv in areaByGroupProv)
                Assert.InRange(areaByGroupOnly[kv.Key], kv.Value - 1e-10, kv.Value + 1e-10);
        }

        [Fact]
        public void RoundingHelpers_IlogbAndLdexp_CoverNormalSubnormalAndLargeShifts()
        {
            // Normal powers of two and non-powers-of-two
            Assert.Equal(0, ExactMeshBoolean.ilogb_pos(1.0));
            Assert.Equal(0, ExactMeshBoolean.ilogb_pos(1.75));
            Assert.Equal(-1, ExactMeshBoolean.ilogb_pos(0.5));
            Assert.Equal(-1, ExactMeshBoolean.ilogb_pos(0.9999999999999999));
            Assert.Equal(600, ExactMeshBoolean.ilogb_pos(Math.Pow(2.0, 600) * 1.3));
            Assert.Equal(-600, ExactMeshBoolean.ilogb_pos(Math.Pow(2.0, -600) * 1.9));
            Assert.Equal(-1022, ExactMeshBoolean.ilogb_pos(BitConverter.Int64BitsToDouble(1L << 52)));

            // Subnormals (exp == 0 branch)
            Assert.Equal(-1074, ExactMeshBoolean.ilogb_pos(double.Epsilon));
            double sub1030 = BitConverter.Int64BitsToDouble(1L << 44); // 2^-1030
            Assert.Equal(-1030, ExactMeshBoolean.ilogb_pos(sub1030));
            Assert.Equal(-1030, ExactMeshBoolean.ilogb_pos(sub1030 * 1.5));

            // ldexp_exact: zero, zero shift, small positive/negative, >512, <-512, >1024, <-1024
            Assert.Equal(0.0, ExactMeshBoolean.ldexp_exact(0.0, 700));
            Assert.Equal(3.25, ExactMeshBoolean.ldexp_exact(3.25, 0));
            Assert.Equal(13.0, ExactMeshBoolean.ldexp_exact(3.25, 2));
            Assert.Equal(0.8125, ExactMeshBoolean.ldexp_exact(3.25, -2));
            Assert.Equal(Math.Pow(2.0, 600) * 1.5, ExactMeshBoolean.ldexp_exact(1.5, 600));
            Assert.Equal(Math.Pow(2.0, -600) * 1.5, ExactMeshBoolean.ldexp_exact(1.5, -600));
            Assert.Equal(1.5, ExactMeshBoolean.ldexp_exact(ExactMeshBoolean.ldexp_exact(1.5, 1020), -1020));
            Assert.Equal(1.5, ExactMeshBoolean.ldexp_exact(sub1030 * 1.5, 1030));
            Assert.Equal(1.0, ExactMeshBoolean.ldexp_exact(double.Epsilon, 1074));
            Assert.Equal(double.Epsilon, ExactMeshBoolean.ldexp_exact(1.0, -1074));
            // Shift exactly at +/-512 boundary
            Assert.Equal(Math.Pow(2.0, 512) * 1.25, ExactMeshBoolean.ldexp_exact(1.25, 512));
            Assert.Equal(Math.Pow(2.0, -512) * 1.25, ExactMeshBoolean.ldexp_exact(1.25, -512));
            Assert.Equal(Math.Pow(2.0, 513) * 1.25, ExactMeshBoolean.ldexp_exact(1.25, 513));
            Assert.Equal(Math.Pow(2.0, -513) * 1.25, ExactMeshBoolean.ldexp_exact(1.25, -513));
        }

        [Fact]
        public void RoundingHelpers_ScaledCrossAndCompareProv_AllBranchesAndExtremeExponents()
        {
            // General non-axis-aligned 3D vectors (tests all 6 products and all 3 signs in cross product)
            Vector3d u = new Vector3d(-2.0, 3.0, -5.0);
            Vector3d v = new Vector3d(7.0, -11.0, 13.0);
            Vector3d expectedDir = u.Cross(v).Normalized;
            Vector3d sc = ExactMeshBoolean.scaled_cross(u, v);
            Assert.True(sc.Length > 0);
            Assert.InRange(sc.Normalized.Dot(expectedDir), 1.0 - 1e-14, 1.0 + 1e-14);
            // Verify negative components and exact magnitude under power-of-two scaling (|u| max = 5 -> 2^2, |v| max = 13 -> 2^3)
            Vector3d expectedExact = new Vector3d(-2.0 / 4.0, 3.0 / 4.0, -5.0 / 4.0).Cross(new Vector3d(7.0 / 8.0, -11.0 / 8.0, 13.0 / 8.0));
            Assert.Equal(expectedExact.x, sc.x, 14);
            Assert.Equal(expectedExact.y, sc.y, 14);
            Assert.Equal(expectedExact.z, sc.z, 14);

            // Extreme exponents (2^700 and 2^-700) must not overflow or underflow to zero
            double big = ExactMeshBoolean.ldexp_exact(1.0, 700);
            double tiny = ExactMeshBoolean.ldexp_exact(1.0, -700);
            Vector3d scBig = ExactMeshBoolean.scaled_cross(u * big, v * big);
            Vector3d scTiny = ExactMeshBoolean.scaled_cross(u * tiny, v * tiny);
            Assert.Equal(expectedExact.x, scBig.x, 14);
            Assert.Equal(expectedExact.y, scBig.y, 14);
            Assert.Equal(expectedExact.z, scBig.z, 14);
            Assert.Equal(expectedExact.x, scTiny.x, 14);
            Assert.Equal(expectedExact.y, scTiny.y, 14);
            Assert.Equal(expectedExact.z, scTiny.z, 14);

            // Guard branches: zero, NaN, Infinity
            Assert.Equal(Vector3d.Zero, ExactMeshBoolean.scaled_cross(Vector3d.Zero, v));
            Assert.Equal(Vector3d.Zero, ExactMeshBoolean.scaled_cross(u, Vector3d.Zero));
            Assert.Equal(Vector3d.Zero, ExactMeshBoolean.scaled_cross(new Vector3d(double.NaN, 1, 0), v));
            Assert.Equal(Vector3d.Zero, ExactMeshBoolean.scaled_cross(u, new Vector3d(0, double.NaN, 1)));
            Assert.Equal(Vector3d.Zero, ExactMeshBoolean.scaled_cross(new Vector3d(double.PositiveInfinity, 1, 0), v));
            Assert.Equal(Vector3d.Zero, ExactMeshBoolean.scaled_cross(u, new Vector3d(0, 0, double.NegativeInfinity)));

            // compare_prov: every field (<, >, ==)
            var p00f = new TriangleProvenance { MeshIndex = 0, SourceTriangle = 0, Reversed = false };
            var p00t = new TriangleProvenance { MeshIndex = 0, SourceTriangle = 0, Reversed = true };
            var p01f = new TriangleProvenance { MeshIndex = 0, SourceTriangle = 1, Reversed = false };
            var p10f = new TriangleProvenance { MeshIndex = 1, SourceTriangle = 0, Reversed = false };
            Assert.Equal(-1, ExactMeshBoolean.compare_prov(p00f, p10f));
            Assert.Equal(1, ExactMeshBoolean.compare_prov(p10f, p00f));
            Assert.Equal(-1, ExactMeshBoolean.compare_prov(p00f, p01f));
            Assert.Equal(1, ExactMeshBoolean.compare_prov(p01f, p00f));
            Assert.Equal(-1, ExactMeshBoolean.compare_prov(p00f, p00t));
            Assert.Equal(1, ExactMeshBoolean.compare_prov(p00t, p00f));
            Assert.Equal(0, ExactMeshBoolean.compare_prov(p00t, p00t));
        }

        [Fact]
        public void RoundingHelpers_PointTriangleDist_AllSevenVoronoiRegionsDegeneratesAndPow2Scaling()
        {
            // Skewed 3D triangle with non-zero x, y, z on every vertex and edge so any coordinate/sign mutation fails.
            Vector3d a = new Vector3d(1.0, -2.0, 0.5);
            Vector3d b = new Vector3d(4.0, -1.0, 2.5);
            Vector3d c = new Vector3d(2.0, 3.0, -1.5);
            Vector3d ab = b - a, ac = c - a, bc = c - b;
            Vector3d n = ab.Cross(ac).Normalized;

            // 1. Vertex A region
            Vector3d pA = a - 0.7 * ab - 0.6 * ac + 1.1 * n;
            Assert.Equal((pA - a).LengthSquared, ExactMeshBoolean.point_triangle_dist_sq(pA, a, b, c), 11);
            Assert.Equal((pA - a).Length, ExactMeshBoolean.cand_point_triangle_dist(pA, a, b, c), 11);

            // 2. Vertex B region
            Vector3d pB = b + 0.8 * ab - 0.5 * bc - 0.9 * n;
            Assert.Equal((pB - b).LengthSquared, ExactMeshBoolean.point_triangle_dist_sq(pB, a, b, c), 11);
            Assert.Equal((pB - b).Length, ExactMeshBoolean.cand_point_triangle_dist(pB, a, b, c), 11);

            // 3. Vertex C region
            Vector3d pC = c + 0.9 * ac + 0.4 * bc + 0.7 * n;
            Assert.Equal((pC - c).LengthSquared, ExactMeshBoolean.point_triangle_dist_sq(pC, a, b, c), 11);
            Assert.Equal((pC - c).Length, ExactMeshBoolean.cand_point_triangle_dist(pC, a, b, c), 11);

            // 4. Edge AB region: orthogonal outward from midpoint of AB
            Vector3d outAB = ab.Cross(n).Normalized;
            Vector3d footAB = a + 0.35 * ab;
            Vector3d pAB = footAB + 1.2 * outAB + 0.5 * n;
            Assert.Equal((pAB - footAB).LengthSquared, ExactMeshBoolean.point_triangle_dist_sq(pAB, a, b, c), 11);
            Assert.Equal((pAB - footAB).Length, ExactMeshBoolean.cand_point_triangle_dist(pAB, a, b, c), 11);

            // 5. Edge AC region: orthogonal outward from AC
            Vector3d outAC = n.Cross(ac).Normalized;
            Vector3d footAC = a + 0.6 * ac;
            Vector3d pAC = footAC + 1.4 * outAC - 0.8 * n;
            Assert.Equal((pAC - footAC).LengthSquared, ExactMeshBoolean.point_triangle_dist_sq(pAC, a, b, c), 11);
            Assert.Equal((pAC - footAC).Length, ExactMeshBoolean.cand_point_triangle_dist(pAC, a, b, c), 11);

            // 6. Edge BC region: orthogonal outward from BC
            Vector3d outBC = bc.Cross(n).Normalized;
            Vector3d footBC = b + 0.45 * bc;
            Vector3d pBC = footBC + 1.3 * outBC + 0.6 * n;
            Assert.Equal((pBC - footBC).LengthSquared, ExactMeshBoolean.point_triangle_dist_sq(pBC, a, b, c), 11);
            Assert.Equal((pBC - footBC).Length, ExactMeshBoolean.cand_point_triangle_dist(pBC, a, b, c), 11);

            // 7. Face interior region (both above and on plane)
            Vector3d footFace = a + 0.25 * ab + 0.4 * ac;
            Vector3d pFace = footFace + 1.75 * n;
            Assert.Equal(1.75 * 1.75, ExactMeshBoolean.point_triangle_dist_sq(pFace, a, b, c), 11);
            Assert.Equal(1.75, ExactMeshBoolean.cand_point_triangle_dist(pFace, a, b, c), 11);
            Assert.Equal(0.0, ExactMeshBoolean.cand_point_triangle_dist(footFace, a, b, c), 12);

            // min_vertex_dist_sq picks each of a, b, c as minimum
            Assert.Equal(0.25, ExactMeshBoolean.min_vertex_dist_sq(a + new Vector3d(0.5, 0, 0), a, b, c), 12);
            Assert.Equal(0.25, ExactMeshBoolean.min_vertex_dist_sq(b + new Vector3d(0, 0.5, 0), a, b, c), 12);
            Assert.Equal(0.25, ExactMeshBoolean.min_vertex_dist_sq(c + new Vector3d(0, 0, 0.5), a, b, c), 12);

            // Degenerate triangles: coincident vertices (a == b == c), collapsed edges, and collinear points never yield NaN
            Vector3d q = new Vector3d(3.0, 2.0, -1.0);
            Assert.Equal((q - a).Length, ExactMeshBoolean.cand_point_triangle_dist(q, a, a, a), 12);
            double tSegAB = Math.Max(0.0, Math.Min(1.0, (q - a).Dot(ab) / ab.LengthSquared));
            double expectedSegAB = (q - (a + tSegAB * ab)).Length;
            Assert.Equal(expectedSegAB, ExactMeshBoolean.cand_point_triangle_dist(q, a, a, b), 12);
            Assert.Equal(expectedSegAB, ExactMeshBoolean.cand_point_triangle_dist(q, a, b, b), 12);
            Assert.Equal(expectedSegAB, ExactMeshBoolean.cand_point_triangle_dist(q, a, b, a), 12);
            double dCollinear = ExactMeshBoolean.cand_point_triangle_dist(new Vector3d(2.0, 1.0, 0.0), Vector3d.Zero, new Vector3d(4.0, 0.0, 0.0), new Vector3d(2.0, 0.0, 0.0));
            Assert.Equal(1.0, dCollinear, 12);
            double dTinyEdge = ExactMeshBoolean.point_triangle_dist_sq(new Vector3d(0.5, 1.0, 0.0), Vector3d.Zero, new Vector3d(1e-200, 0, 0), new Vector3d(0, 1e-200, 0));
            Assert.False(double.IsNaN(dTinyEdge));
            Assert.Equal(1.25, dTinyEdge, 12);

            // Special inputs: all zeros, NaN in any argument (including alongside zero vectors), Infinity
            Assert.Equal(0.0, ExactMeshBoolean.cand_point_triangle_dist(Vector3d.Zero, Vector3d.Zero, Vector3d.Zero, Vector3d.Zero));
            Assert.True(double.IsPositiveInfinity(ExactMeshBoolean.cand_point_triangle_dist(new Vector3d(double.NaN, 0, 0), a, b, c)));
            Assert.True(double.IsPositiveInfinity(ExactMeshBoolean.cand_point_triangle_dist(Vector3d.Zero, new Vector3d(double.NaN, 0, 0), Vector3d.Zero, Vector3d.Zero)));
            Assert.True(double.IsPositiveInfinity(ExactMeshBoolean.cand_point_triangle_dist(Vector3d.Zero, Vector3d.Zero, new Vector3d(0, double.NaN, 0), Vector3d.Zero)));
            Assert.True(double.IsPositiveInfinity(ExactMeshBoolean.cand_point_triangle_dist(Vector3d.Zero, Vector3d.Zero, Vector3d.Zero, new Vector3d(0, 0, double.NaN))));
            Assert.True(double.IsPositiveInfinity(ExactMeshBoolean.cand_point_triangle_dist(pFace, new Vector3d(double.PositiveInfinity, 0, 0), b, c)));

            // Power-of-two scaling invariance across 2^600 and 2^-600 (and subnormal 2^-1030)
            foreach (int shift in new[] { 600, -600, -1030 }) {
                Vector3d sa = new Vector3d(ExactMeshBoolean.ldexp_exact(a.x, shift), ExactMeshBoolean.ldexp_exact(a.y, shift), ExactMeshBoolean.ldexp_exact(a.z, shift));
                Vector3d sb = new Vector3d(ExactMeshBoolean.ldexp_exact(b.x, shift), ExactMeshBoolean.ldexp_exact(b.y, shift), ExactMeshBoolean.ldexp_exact(b.z, shift));
                Vector3d sc = new Vector3d(ExactMeshBoolean.ldexp_exact(c.x, shift), ExactMeshBoolean.ldexp_exact(c.y, shift), ExactMeshBoolean.ldexp_exact(c.z, shift));
                Vector3d sp = new Vector3d(ExactMeshBoolean.ldexp_exact(pFace.x, shift), ExactMeshBoolean.ldexp_exact(pFace.y, shift), ExactMeshBoolean.ldexp_exact(pFace.z, shift));
                double expected = ExactMeshBoolean.ldexp_exact(1.75, shift);
                double got = ExactMeshBoolean.cand_point_triangle_dist(sp, sa, sb, sc);
                Assert.True(Math.Abs(got - expected) <= 1e-10 * expected, $"shift {shift}: got={got:E6} expected={expected:E6}");
            }
        }

        [Fact]
        public void RederiveProvenance_DirectCases_UntouchedRollbackEditedFallbackThinWallAndTieBreak()
        {
            // Build two source meshes with distinct face locations, orientations, and groups.
            DMesh3 src0 = new DMesh3();
            src0.EnableTriangleGroups(0);
            int s0v0 = src0.AppendVertex(new Vector3d(0, 0, 0));
            int s0v1 = src0.AppendVertex(new Vector3d(2, 0, 0));
            int s0v2 = src0.AppendVertex(new Vector3d(0, 2, 0));
            int s0t0 = src0.AppendTriangle(s0v0, s0v1, s0v2); // z = 0, normal +Z
            src0.SetTriangleGroup(s0t0, 11);

            int s0v3 = src0.AppendVertex(new Vector3d(0, 0, 1));
            int s0v4 = src0.AppendVertex(new Vector3d(2, 0, 1));
            int s0v5 = src0.AppendVertex(new Vector3d(0, 2, 1));
            int s0t1 = src0.AppendTriangle(s0v3, s0v4, s0v5); // z = 1, normal +Z
            src0.SetTriangleGroup(s0t1, 12);

            DMesh3 src1 = new DMesh3();
            src1.EnableTriangleGroups(0);
            // Opposite-facing near-coincident face at z = 1e-14 (normal -Z) for thin-wall orientation tie-break
            int s1v0 = src1.AppendVertex(new Vector3d(0, 0, 1e-14));
            int s1v1 = src1.AppendVertex(new Vector3d(0, 2, 1e-14));
            int s1v2 = src1.AppendVertex(new Vector3d(2, 0, 1e-14));
            int s1t0 = src1.AppendTriangle(s1v0, s1v1, s1v2); // z = 1e-14, normal -Z
            src1.SetTriangleGroup(s1t0, 21);
            // Duplicate geometry to s0t1 for exact-distance compare_prov tie-break
            int s1v3 = src1.AppendVertex(new Vector3d(0, 0, 1));
            int s1v4 = src1.AppendVertex(new Vector3d(2, 0, 1));
            int s1v5 = src1.AppendVertex(new Vector3d(0, 2, 1));
            int s1t1 = src1.AppendTriangle(s1v3, s1v4, s1v5); // z = 1, normal +Z
            src1.SetTriangleGroup(s1t1, 22);

            var prepared = new[] {
                new ExactMeshBoolean.PreparedInput { Source = src0, Input = src0, Finite = true },
                new ExactMeshBoolean.PreparedInput { Source = src1, Input = src1, Finite = true }
            };

            // Result mesh containing:
            // tid 0: untouched triangle whose group was corrupted to -1 (simulates try_zip rollback) -> restored to 11
            // tid 1: edited triangle at z = 1.5e-14 with +Z normal sharing vertex with s0t0 (+Z at z=0) and s1t0 (-Z at z=1e-14)
            //        -> s1t0 is closer (0.5e-14 vs 1.5e-14) but opposite-facing; within scaleTol (~2e-12), aligned s0t0 MUST win!
            // tid 2: deleted hole -> must stay TriangleProvenance.None
            // tid 3: brand-new triangle with brand-new vertices (no vertex in beforeTris -> cands.Count == 0 fallback)
            //        at z = 1.0 -> exact tie between (0, s0t1, false) and (1, s1t1, false); compare_prov picks MeshIndex 0!
            // tid 4: edited triangle at z = 1e-14 with -Z normal -> matches s1t0 (-Z, Reversed=false) over s0t0 (+Z)!
            DMesh3 res = new DMesh3();
            res.EnableTriangleGroups(0);
            int rv0 = res.AppendVertex(new Vector3d(0, 0, 0));
            int rv1 = res.AppendVertex(new Vector3d(2, 0, 0));
            int rv2 = res.AppendVertex(new Vector3d(0, 2, 0));
            int rv4 = res.AppendVertex(new Vector3d(1.2, 0.2, 1.5e-14));
            int rv5 = res.AppendVertex(new Vector3d(0.2, 1.2, 1.5e-14));
            int rv6 = res.AppendVertex(new Vector3d(0.1, 0.1, 1.0));
            int rv7 = res.AppendVertex(new Vector3d(1.1, 0.1, 1.0));
            int rv8 = res.AppendVertex(new Vector3d(0.1, 1.1, 1.0));
            int rv9 = res.AppendVertex(new Vector3d(0.2, 0.2, 0.0));
            int rv10 = res.AppendVertex(new Vector3d(5, 5, 5));
            int rv11 = res.AppendVertex(new Vector3d(6, 5, 5));
            int rv12 = res.AppendVertex(new Vector3d(5, 6, 5));
            int rv13 = res.AppendVertex(new Vector3d(0.2, 1.1, 1.5e-14));
            int rv14 = res.AppendVertex(new Vector3d(1.1, 0.2, 1.5e-14));

            int t0 = res.AppendTriangle(rv0, rv1, rv2);
            res.SetTriangleGroup(t0, -1); // corrupted by rollback
            int t1 = res.AppendTriangle(rv0, rv4, rv5); // shares rv0 with beforeTris, +Z normal near z=1e-14
            int t2 = res.AppendTriangle(rv10, rv11, rv12); // will be removed to leave a hole
            int t3 = res.AppendTriangle(rv6, rv7, rv8); // brand-new vertices (fallback path), z = 1
            int t4 = res.AppendTriangle(rv0, rv13, rv14); // shares rv0, -Z normal near z=1e-14
            Assert.True(t0 >= 0 && t1 >= 0 && t2 >= 0 && t3 >= 0 && t4 >= 0);
            res.RemoveTriangle(t2, true, false);

            var pA = new TriangleProvenance { MeshIndex = 0, SourceTriangle = s0t0, Reversed = false };
            var pB = new TriangleProvenance { MeshIndex = 1, SourceTriangle = s1t0, Reversed = false };
            var pC0 = new TriangleProvenance { MeshIndex = 0, SourceTriangle = s0t1, Reversed = false };
            var pC1 = new TriangleProvenance { MeshIndex = 1, SourceTriangle = s1t1, Reversed = false };

            Index3i[] beforeTris = new[] {
                new Index3i(rv0, rv1, rv2),
                new Index3i(rv0, rv9, rv2),
                new Index3i(-1, -1, -1),
                new Index3i(rv1, rv2, rv9),
                new Index3i(rv1, rv9, rv2)
            };
            // Put pC1 BEFORE pC0 in beforeProv so compare_prov must actually break the tie to pick pC0!
            TriangleProvenance[] beforeProv = new[] { pA, pB, TriangleProvenance.None, pC1, pC0 };

            var eb = new ExactMeshBoolean(src0, src1, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true,
                GroupOffsets = new[] { 0, 100 },
                Result = res
            };
            eb.rederive_provenance(prepared, beforeTris, beforeProv);

            Assert.Equal(pA, eb.ResultProvenance[t0]);
            Assert.Equal(11, res.GetTriangleGroup(t0)); // rollback group restored
            Assert.Equal(pA, eb.ResultProvenance[t1]); // aligned +Z won over closer opposite -Z
            Assert.Equal(11, res.GetTriangleGroup(t1));
            Assert.Equal(TriangleProvenance.None, eb.ResultProvenance[t2]); // deleted hole
            Assert.Equal(pC0, eb.ResultProvenance[t3]); // fallback + compare_prov tie-break picked MeshIndex 0
            Assert.Equal(12, res.GetTriangleGroup(t3));
            Assert.Equal(pB, eb.ResultProvenance[t4]); // aligned -Z picked s1t0 with GroupOffset 100 -> 121
            Assert.Equal(121, res.GetTriangleGroup(t4));

            // Also test Reversed=true normal inversion: s0t0 (+Z at z=0) marked Reversed=true behaves as -Z,
            // so for t4 (-Z at z=1e-14), pARev (distance 1e-14, aligned -Z) vs pFarAligned (+Z at z=1, far):
            // 1) When s1t0 (-Z at z=1e-14) is marked Reversed=true (so effective normal +Z), t4 (-Z) prefers pARev (-Z at z=0) even though s1t0 is closer!
            var pARev = new TriangleProvenance { MeshIndex = 0, SourceTriangle = s0t0, Reversed = true };
            var pBRev = new TriangleProvenance { MeshIndex = 1, SourceTriangle = s1t0, Reversed = true };
            eb.rederive_provenance(prepared, new[] { new Index3i(rv0, rv1, rv2), new Index3i(rv0, rv9, rv2) }, new[] { pBRev, pARev });
            Assert.Equal(pARev, eb.ResultProvenance[t4]); // pBRev is closer (0 vs 1e-14) but reversed to +Z; pARev is reversed to -Z and aligned!

            // 2) Far-aligned cutoff: t3 (+Z at z=1) with candidates pA (+Z at z=0, far, dist=1) and pBClose (-Z at z=1 via Reversed=true on s1t1, dist=0):
            //    pA is aligned but outside alignCeiling (dist=1 >> scaleTol), so closer pBClose MUST win!
            var pC1Rev = new TriangleProvenance { MeshIndex = 1, SourceTriangle = s1t1, Reversed = true };
            eb.rederive_provenance(prepared, new[] { new Index3i(rv0, rv1, rv2), new Index3i(rv1, rv2, rv9) }, new[] { pA, pC1Rev });
            Assert.Equal(pC1Rev, eb.ResultProvenance[t3]);

            // Empty beforeProv => cands.Count == 0 guard leaves TriangleProvenance.None cleanly without throwing
            eb.rederive_provenance(prepared, new Index3i[0], new TriangleProvenance[0]);
            Assert.Equal(TriangleProvenance.None, eb.ResultProvenance[t0]);
        }

        [Fact]
        public void RederiveGroups_DirectCases_UntouchedRollbackEditedFallbackAndTieBreak()
        {
            Vector3d[] beforeVerts = new[] {
                new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0),
                new Vector3d(0, 0, 5), new Vector3d(2, 0, 5), new Vector3d(0, 2, 5)
            };
            Index3i[] beforeTris = new[] {
                new Index3i(0, 1, 2),   // z = 0, group 10
                new Index3i(-1, -1, -1), // deleted before-tri
                new Index3i(3, 4, 5),   // z = 5, group 50
                new Index3i(3, 4, 5)    // z = 5, group 30 (exact tie with idx 2 -> smaller group 30 must win!)
            };
            int[] beforeGroup = new[] { 10, 99, 50, 30 };

            DMesh3 res = new DMesh3();
            res.EnableTriangleGroups(0);
            for (int i = 0; i < beforeVerts.Length; ++i)
                res.AppendVertex(beforeVerts[i]);
            int rvFar0 = res.AppendVertex(new Vector3d(0.2, 0.2, 4.9));
            int rvFar1 = res.AppendVertex(new Vector3d(1.2, 0.2, 4.9));
            int rvFar2 = res.AppendVertex(new Vector3d(0.2, 1.2, 4.9));
            int rvNear1 = res.AppendVertex(new Vector3d(1.0, 0.1, 0.1));
            int rvNear2 = res.AppendVertex(new Vector3d(0.1, 1.0, 0.1));

            int t0 = res.AppendTriangle(0, 1, 2);
            res.SetTriangleGroup(t0, -1); // untouched, corrupted group -> restored to 10
            int t1 = res.AppendTriangle(0, rvNear1, rvNear2); // shares vertex 0 -> picks group 10
            int t2 = res.AppendTriangle(3, 4, 5); // deleted hole
            int t3 = res.AppendTriangle(rvFar0, rvFar1, rvFar2); // brand-new vertices -> fallback + tie-break picks group 30 over 50!
            res.RemoveTriangle(t2, true, false);

            var eb = new ExactMeshBoolean(new DMesh3(), new DMesh3(), ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                Result = res
            };
            eb.rederive_groups(beforeTris, beforeVerts, beforeGroup);

            Assert.Equal(10, res.GetTriangleGroup(t0));
            Assert.Equal(10, res.GetTriangleGroup(t1));
            Assert.Equal(30, res.GetTriangleGroup(t3));

            // Empty beforeTris => cands.Count == 0 guard leaves groups unchanged without throwing
            res.SetTriangleGroup(t3, 77);
            eb.rederive_groups(new Index3i[0], beforeVerts, new int[0]);
            Assert.Equal(77, res.GetTriangleGroup(t3));
        }
    }
}
