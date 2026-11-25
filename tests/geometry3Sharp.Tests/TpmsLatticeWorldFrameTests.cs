using System;
using System.Security.Cryptography;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1569: the certificate of TpmsLattice.Compute (NotCleanReason and the exact self-intersection check) must describe the returned
    /// mesh, i.e. the world coordinates after Origin + Rotation * v, not the lattice-frame mesh before the transform.
    /// </summary>
    public class TpmsLatticeWorldFrameTests
    {
        static readonly Matrix3d Rot = (new Quaterniond(Vector3d.AxisZ, 37.0) * new Quaterniond(Vector3d.AxisX, 23.0)).ToRotationMatrix();

        static TpmsLattice Make(double origin, bool rotated, bool parallel = true)
        {
            return new TpmsLattice {
                CellSize = 1, Divisions = 32, Box = new AxisAlignedBox3d(Vector3d.Zero, 1.0),
                Origin = new Vector3d(origin, 0.7 * origin, 0.3 * origin), Rotation = rotated ? Rot : Matrix3d.Identity,
                ParallelCompute = parallel
            };
        }

        // the vertex coordinates (bit patterns) and the triangles, in index order
        static string Hash(DMesh3 mesh)
        {
            var bytes = new System.Collections.Generic.List<byte>();
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                bytes.AddRange(BitConverter.GetBytes(vid));
                for (int k = 0; k < 3; ++k) bytes.AddRange(BitConverter.GetBytes(v[k]));
            }
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                bytes.AddRange(BitConverter.GetBytes(t.a)); bytes.AddRange(BitConverter.GetBytes(t.b)); bytes.AddRange(BitConverter.GetBytes(t.c));
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16);
        }

        // Ok only for a returned mesh that is clean; otherwise no mesh and a reason
        static void AssertStatusDescribesTheMesh(TpmsLattice t)
        {
            if (t.Status == TpmsStatus.Ok) {
                Assert.NotNull(t.Mesh);
                Assert.Equal("", MeshRobustRepair.NotCleanReasons(t.Mesh));
            } else {
                Assert.True(t.Status == TpmsStatus.NotClean || t.Status == TpmsStatus.SelfIntersecting, t.Status + ": " + t.Message);
                Assert.Null(t.Mesh);
                Assert.NotEqual("", t.NotCleanReason);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FarOrigin_StatusDescribesTheReturnedWorldMesh(bool rotated)
        {
            // the #1569 repro: at 1e13 an ulp of the world coordinates (about 2e-3) is close to the cube (1/32), the transform collapses
            // and crosses triangles; master reported Ok with "degenerate triangles, self-intersections" in the returned mesh
            var t = Make(1e13, rotated);
            t.Compute();
            AssertStatusDescribesTheMesh(t);
            Assert.NotEqual(TpmsStatus.Ok, t.Status);   // at this ratio the world mesh is not clean for any grid shift
        }

        // the origin-0 reference, computed once for the three cases (test time)
        static readonly Lazy<TpmsLattice> AtZero = new Lazy<TpmsLattice>(() => { var t = Make(0, false); t.Compute(); return t; });

        [Theory]
        [InlineData(0.0, false)]
        [InlineData(1e12, false)]
        [InlineData(1e12, true)]
        public void NearerOrigins_ResultUnchanged(double origin, bool rotated)
        {
            // #1590 (the libm cause is inferred from the Linux-pass / Windows-fail pattern, not confirmed on Windows): the test used to pin SHA-256 hashes of the vertex bits. Math.Sin / Math.Cos (TpmsLattice.Evaluate) call the platform
            // libm, whose last bit differs between Linux (glibc) and Windows (CRT), so a pinned hash is a property of the platform and
            // failed on Windows even at origin 0. What the test names is platform-independent: where the world mesh is clean, it is the
            // lattice-frame mesh of the origin-0 run mapped by Origin + Rotation * v, bit for bit on the same platform (same
            // arithmetic as TpmsLattice.ToWorld), with the same triangles.
            TpmsLattice atZero = AtZero.Value;
            TpmsLattice t = origin == 0 && !rotated ? atZero : Make(origin, rotated);
            if (t != atZero) t.Compute();
            Assert.True(t.Status == TpmsStatus.Ok, t.Status + ": " + t.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(t.Mesh));
            Assert.Equal(atZero.GridShiftsUsed, t.GridShiftsUsed);
            Assert.Equal(atZero.Mesh.VertexCount, t.Mesh.VertexCount);
            Assert.Equal(atZero.Mesh.TriangleCount, t.Mesh.TriangleCount);
            Vector3d o = Make(origin, rotated).Origin;
            bool identity = !rotated;
            foreach (int vid in atZero.Mesh.VertexIndices()) {
                Vector3d v = atZero.Mesh.GetVertex(vid);
                Vector3d expected = identity ? o + v : o + Rot * v;
                Assert.Equal(expected, t.Mesh.GetVertex(vid));
            }
            foreach (int tid in atZero.Mesh.TriangleIndices())
                Assert.Equal(atZero.Mesh.GetTriangle(tid), t.Mesh.GetTriangle(tid));
            // the volume is measured in the lattice frame (before the transform): the same as at the origin, bit for bit
            Assert.Equal(atZero.RealizedVolumeFraction, t.RealizedVolumeFraction);
            Assert.Equal(atZero.MeanWallThickness, t.MeanWallThickness);
        }

        [Fact]
        public void Hash_IsStableOnOneRun()
        {
            // the hash helper stays as a same-process check: a second compute of the same lattice gives the same bits
            var a = Make(0, false); a.Compute();
            var b = Make(0, false); b.Compute();
            Assert.Equal(Hash(a.Mesh), Hash(b.Mesh));
        }

        [Fact]
        public void FarOrigin_Reusable_AndParallelEqualsSerial()
        {
            var t = Make(1e13, true);
            t.Compute();
            TpmsStatus first = t.Status; string reason = t.NotCleanReason; int shifts = t.GridShiftsUsed;
            t.Compute();
            Assert.Equal(first, t.Status); Assert.Equal(reason, t.NotCleanReason); Assert.Equal(shifts, t.GridShiftsUsed);
            AssertStatusDescribesTheMesh(t);
            var serial = Make(1e13, true, parallel: false);
            serial.Compute();
            Assert.Equal(first, serial.Status); Assert.Equal(reason, serial.NotCleanReason); Assert.Equal(shifts, serial.GridShiftsUsed);
        }

        [Fact]
        public void StatusDescribesTheMesh_UnderSimilarities()
        {
            // every similarity of the harness (including the UTM translations and rotations): Ok only with a clean returned mesh,
            // and the 2^k scales give the same mesh, scaled, bit for bit
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                var t = new TpmsLattice {
                    CellSize = s.Scale, Divisions = 12, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1) * s.Scale),
                    Origin = s.Apply(Vector3d.Zero), Rotation = s.Rotation
                };
                t.Compute();
                Assert.True(t.Status == TpmsStatus.Ok, s.Name + ": " + t.Status + ": " + t.Message);
                AssertStatusDescribesTheMesh(t);
                if (reference == null) { reference = t; continue; }
                Assert.Equal(reference.Mesh.TriangleCount, t.Mesh.TriangleCount);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Mesh, Invariance.MappedBack(t.Mesh, s)));
            }
        }
    }
}
