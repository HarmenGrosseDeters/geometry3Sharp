using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TpmsLattice (#1372): with a DomainMesh the cut lies at the mesh up to the rounding of the contour. The distance to the
    /// mesh used to be the distance to its bounding box outside the box, a lower bound: a cube edge from a node inside the
    /// bounds to one outside them put the cut up to 0.7 cube outside the domain.
    /// </summary>
    public class TpmsLatticeDomainCutTests
    {
        const int Divisions = 10;
        const double Cube = 1.0 / Divisions;
        // the cut is within this many cubes of the domain mesh (measured 0.01 with the fix, 0.72 before)
        const double BoundInCubes = 0.15;

        // a domain with an analytic signed distance (the oracle does not use the mesh or the library's distance code)
        sealed class Domain
        {
            public string Name; public DMesh3 Mesh; public Func<Vector3d, double> Sdf; public double Volume;
        }

        static readonly Matrix3d Tilt = Quaterniond.AxisAngleD(new Vector3d(1, 2, 0.5).Normalized, 37).ToRotationMatrix();

        static Domain MakeDomain(string name)
        {
            switch (name) {
                case "sphere": {         // convex, a facet error of 0.0013
                    var c = new Vector3d(0.1, 0.2, 0.3);
                    DMesh3 m = new Sphere3Generator_NormalizedCube { Radius = 0.838, EdgeVertices = 32 }.Generate().MakeDMesh();
                    foreach (int v in m.VertexIndices()) m.SetVertex(v, m.GetVertex(v) + c);
                    return new Domain { Name = name, Mesh = m, Sdf = p => (p - c).Length - 0.838, Volume = 4.0 / 3 * Math.PI * 0.838 * 0.838 * 0.838 };
                }
                case "torus": {          // concave, tilted: the bounds touch it at a few places only
                    var c = new Vector3d(0.4, 0.5, 0.45);
                    Matrix3d inv = Tilt.Transpose();
                    return new Domain {
                        Name = name, Mesh = ContouringTopologyCertificateTests.Torus(c, 0.7, 0.33, 96, 40, Tilt),
                        Sdf = p => { Vector3d q = inv * (p - c); double u = Math.Sqrt(q.x * q.x + q.y * q.y) - 0.7; return Math.Sqrt(u * u + q.z * q.z) - 0.33; },
                        Volume = 2 * Math.PI * Math.PI * 0.7 * 0.33 * 0.33
                    };
                }
                default: {               // a rotated box: its bounding box is much larger than it
                    var c = new Vector3d(0.3, 0.6, 0.2);
                    Matrix3d inv = Tilt.Transpose();
                    var half = new Vector3d(0.5, 0.7, 0.3);
                    return new Domain {
                        Name = name, Mesh = ContouringTopologyCertificateTests.Box(c, half, Tilt),
                        Sdf = p => {
                            Vector3d q = inv * (p - c);
                            var d = new Vector3d(Math.Abs(q.x) - half.x, Math.Abs(q.y) - half.y, Math.Abs(q.z) - half.z);
                            var o = new Vector3d(Math.Max(d.x, 0), Math.Max(d.y, 0), Math.Max(d.z, 0));
                            return o.Length + Math.Min(Math.Max(d.x, Math.Max(d.y, d.z)), 0);
                        },
                        Volume = 8 * half.x * half.y * half.z
                    };
                }
            }
        }

        public static IEnumerable<object[]> Domains() =>
            from mode in new[] { "Skeletal", "Sheet", "TwoLevelSheet" } from d in new[] { "sphere", "torus", "box" } select new object[] { mode, d };

        static TpmsLattice Make(string mode, DMesh3 domain)
        {
            return new TpmsLattice {
                Type = TpmsType.Gyroid, Style = mode == "Skeletal" ? TpmsStyle.SkeletalLow : TpmsStyle.Sheet, CellSize = 1, Divisions = Divisions,
                VolumeFraction = 0.5, DomainMesh = domain, TwoLevelSheet = mode == "TwoLevelSheet"
            };
        }

        // the largest distance of a vertex of the result that is outside the domain (by the analytic signed distance)
        static double Overshoot(DMesh3 result, Func<Vector3d, double> sdf)
        {
            double worst = 0;
            foreach (Vector3d v in result.Vertices()) worst = Math.Max(worst, sdf(v));
            return worst;
        }

        [Theory]
        [MemberData(nameof(Domains))]
        public void TheCutLiesAtTheDomainMesh(string mode, string domainName)
        {
            Domain d = MakeDomain(domainName);
            Assert.True(Invariance.Volume(d.Mesh) > 0, "the domain mesh is oriented outward");
            TpmsLattice t = Make(mode, d.Mesh);
            t.Compute();
            Assert.True(t.Status == TpmsStatus.Ok, t.Status + ": " + t.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(t.Mesh));
            Assert.True(Invariance.AllFinite(t.Mesh));
            Assert.True(t.Mesh.IsClosed());
            double over = Overshoot(t.Mesh, d.Sdf);
            Assert.True(over <= BoundInCubes * Cube, domainName + " " + mode + ": the cut is " + over / Cube + " cubes outside the domain");
            // coverage: the lattice fills the whole domain, not a part of it (a volume near the fraction 0.5 of the domain, and the result reaches the domain's bounds)
            Assert.InRange(Invariance.Volume(t.Mesh) / d.Volume, 0.3, 0.7);
            AxisAlignedBox3d rb = t.Mesh.GetBounds(), db = d.Mesh.GetBounds();
            double margin = 2 * Cube;
            Assert.True(rb.Min.x <= db.Min.x + margin && rb.Min.y <= db.Min.y + margin && rb.Min.z <= db.Min.z + margin
                     && rb.Max.x >= db.Max.x - margin && rb.Max.y >= db.Max.y - margin && rb.Max.z >= db.Max.z - margin, "the result spans the domain");
        }

        [Theory]
        [InlineData("Skeletal")]
        [InlineData("Sheet")]
        [InlineData("TwoLevelSheet")]
        public void InvariantUnderSimilarities_ReusableAndParallelEqualsSerial(string mode)
        {
            Domain dom = MakeDomain("torus"); DMesh3 domain = dom.Mesh;
            DMesh3 reference = null;
            foreach (Similarity s in Similarity.All()) {
                TpmsLattice t = Make(mode, Invariance.Transformed(domain, s));
                t.CellSize = s.Scale;
                t.Origin = s.Apply(Vector3d.Zero);
                t.Rotation = s.Rotation;
                t.Compute();
                Assert.True(t.Status == TpmsStatus.Ok, s.Name + " " + t.Status + ": " + t.Message);
                DMesh3 back = Invariance.MappedBack(t.Mesh, s);
                Assert.True(Overshoot(back, dom.Sdf) <= BoundInCubes * Cube, s.Name);
                if (reference == null) { reference = back; continue; }
                Assert.Equal(reference.TriangleCount, back.TriangleCount);
                if (s.IsExact) Assert.Equal("", Invariance.Difference(reference, back));
            }
            TpmsLattice serial = Make(mode, domain); serial.ParallelCompute = false; serial.Compute();
            TpmsLattice par = Make(mode, domain); par.ParallelCompute = true;
            for (int i = 0; i < 3; ++i) {
                par.Compute();
                Assert.Equal("", Invariance.Difference(serial.Mesh, par.Mesh));    // repeated runs on one instance
            }
        }
    }
}
