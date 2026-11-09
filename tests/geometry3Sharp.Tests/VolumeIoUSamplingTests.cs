using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The volume IoU estimate of MeshRepairMetrics.VolumeIoUAtLeast through the public API only (so these tests also
    /// run against older versions of the library): unbiased, and as accurate as the stratified cells make it.
    /// </summary>
    public class VolumeIoUSamplingTests
    {
        // the unit box, cut at height h, rotated off the axes (its bounds are larger than the box: the region of the
        // older sampling hits the solid at about a fifth of the samples)
        static DMesh3 TiltedBox(double h)
        {
            var m = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5 * h), new Vector3d(0.5, 0.5, 0.5 * h)) }.Generate().MakeDMesh();
            Matrix3d rot = new Quaterniond(Vector3d.AxisZ, 45).ToRotationMatrix() * new Quaterniond(Vector3d.AxisX, 35).ToRotationMatrix();
            foreach (int v in m.VertexIndices().ToArray())
                m.SetVertex(v, rot * m.GetVertex(v));
            return m;
        }

        // the estimates of 40 seeds with all 20000 samples (z = 1000: no early answer), minus the true IoU h
        static double[] Errors(double h)
        {
            DMesh3 input = TiltedBox(1), result = TiltedBox(h);
            return Enumerable.Range(1, 40).Select(seed => {
                double iou;
                MeshRepairMetrics.VolumeIoUAtLeast(input, result, h, out iou, 20000, seed, 1000);
                return iou - h;
            }).ToArray();
        }

        [Fact]
        public void VolumeIoUAtLeast_Unbiased()
        {
            double[] e = Errors(0.9);
            double mean = e.Average(), sd = Math.Sqrt(e.Average(x => (x - mean) * (x - mean)));
            Assert.True(Math.Abs(mean) < 4 * sd / Math.Sqrt(e.Length) + 1e-6, $"mean {mean}, sd {sd}");
        }

        [Fact]
        public void VolumeIoUAtLeast_ClosedMeshes_AccurateEstimate()
        {
            // closed meshes: the interior counts with its exact volume and only the cells at the surfaces are sampled.
            // The error of 20000 samples near the threshold: RMS 5.4e-4 (with the region of the components' boxes,
            // before: 1.5e-3)
            double[] e = Errors(0.99);
            double rms = Math.Sqrt(e.Average(x => x * x));
            Assert.True(rms < 9e-4, "rms " + rms);
        }
    }
}
