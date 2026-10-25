using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Ground filtering of LiDAR point clouds (cloth simulation filter): separates the terrain from
    /// vehicles, buildings, stockpile equipment and vegetation.
    /// </summary>
    public class GroundFilterTests
    {
        // synthetic survey: ground points on terrain h (with noise), plus objects: boxes (buildings,
        // vehicles) and trees (points in a crown above the ground). Returns points and true labels.
        static (List<Vector3d> points, List<bool> ground) Scene(Func<double, double, double> h, double size, double spacing,
            int seed, Vector3d offset = default(Vector3d), bool objects = true, bool objectsOnFlatGroundOnly = false, double clearance = 2)
        {
            var rnd = new Random(seed);
            var points = new List<Vector3d>();
            var ground = new List<bool>();
            // boxes: (x, y, half size, height)
            var boxes = new List<(double x, double y, double r, double hgt)>();
            var trees = new List<(double x, double y, double r, double hgt)>();
            // ground height range under a footprint (objects on flat ground: berms, ramps, the pit floor)
            Func<double, double, double, double> relief = (cx, cy, rad) => {
                double lo = double.MaxValue, hi = double.MinValue;
                for (double bx = cx - rad; bx <= cx + rad; bx += 0.5)
                    for (double by = cy - rad; by <= cy + rad; by += 0.5) { double z = h(bx, by); lo = Math.Min(lo, z); hi = Math.Max(hi, z); }
                return hi - lo;
            };
            if (objects) {
                while (boxes.Count < 6) {
                    var b = (rnd.NextDouble() * size * 0.8 + size * 0.1, rnd.NextDouble() * size * 0.8 + size * 0.1, 3 + rnd.NextDouble() * 8, 3 + rnd.NextDouble() * 9);
                    if (!objectsOnFlatGroundOnly || relief(b.Item1, b.Item2, b.Item3 + clearance) < 0.5) boxes.Add(b);
                }
                while (trees.Count < 25) {
                    var t = (rnd.NextDouble() * size, rnd.NextDouble() * size, 2 + rnd.NextDouble() * 3, 5 + rnd.NextDouble() * 12);
                    if (!objectsOnFlatGroundOnly || relief(t.Item1, t.Item2, t.Item3 + clearance) < 0.5) trees.Add(t);
                }
            }
            for (double x = 0; x < size; x += spacing)
                for (double y = 0; y < size; y += spacing) {
                    double px = x + (rnd.NextDouble() - 0.5) * spacing, py = y + (rnd.NextDouble() - 0.5) * spacing;
                    double gz = h(px, py) + 0.03 * (rnd.NextDouble() - 0.5);
                    var box = boxes.FirstOrDefault(b => Math.Abs(px - b.x) < b.r && Math.Abs(py - b.y) < b.r);
                    var tree = trees.FirstOrDefault(t => (px - t.x) * (px - t.x) + (py - t.y) * (py - t.y) < t.r * t.r);
                    if (box.r > 0) {
                        // flat roof above the highest ground under the box
                        double top = double.MinValue;
                        for (double bx = box.x - box.r; bx <= box.x + box.r; bx += 0.5)
                            for (double by = box.y - box.r; by <= box.y + box.r; by += 0.5)
                                top = Math.Max(top, h(bx, by));
                        points.Add(offset + new Vector3d(px, py, top + box.hgt)); ground.Add(false);     // roof only
                    } else if (tree.r > 0 && rnd.NextDouble() < 0.7) {
                        points.Add(offset + new Vector3d(px, py, gz + tree.hgt * (0.5 + 0.5 * rnd.NextDouble()))); ground.Add(false);
                    } else {
                        points.Add(offset + new Vector3d(px, py, gz)); ground.Add(true);
                    }
                }
            return (points, ground);
        }

        static (double groundMissed, double objectsAsGround) Errors(bool[] result, List<bool> truth)
        {
            int g = truth.Count(t => t), o = truth.Count - g;
            int missed = Enumerable.Range(0, truth.Count).Count(i => truth[i] && !result[i]);
            int wrong = Enumerable.Range(0, truth.Count).Count(i => !truth[i] && result[i]);
            return ((double)missed / g, o > 0 ? (double)wrong / o : 0);
        }

        static double Hills(double x, double y) { return 4 * Math.Sin(x / 25.0) * Math.Cos(y / 19.0) + 0.05 * x; }

        [Fact]
        public void HillyTerrainWithBuildingsAndTrees()
        {
            var (points, truth) = Scene(Hills, 200, 1.0, 1);
            var csf = new ClothSimulationFilter(points) { ClothResolution = 1.0, Rigidness = 2 };
            Assert.True(csf.Compute());
            var (missed, wrong) = Errors(csf.IsGround, truth);
            Assert.True(missed < 0.02, $"{missed:P2} of the ground points missed");
            Assert.True(wrong < 0.02, $"{wrong:P2} of the object points classified as ground");
        }

        [Fact]
        public void OpenPitWithSteepBenches()
        {
            // benches: 10 m faces at about 51 degrees, followed by the slope post-processing. Equipment and
            // vegetation stand on the berms and the floor (on a steep face a 2.5 m object cannot be told
            // from the face in a 1 m cloth)
            Func<double, double, double> pit = (x, y) => Pit(x - 100, y - 100);
            var (points, truth) = Scene(pit, 200, 1.0, 2, default(Vector3d), true, true);
            var csf = new ClothSimulationFilter(points) { ClothResolution = 1.0, Rigidness = 2 };
            Assert.True(csf.Compute());
            var (missed, wrong) = Errors(csf.IsGround, truth);
            Assert.True(missed < 0.02, $"{missed:P2} of the ground points missed");
            Assert.True(wrong < 0.02, $"{wrong:P2} of the object points classified as ground");
        }

        [Fact]
        public void BareGround_AllGround()
        {
            var (points, truth) = Scene(Hills, 100, 1.0, 3, default(Vector3d), false);
            var csf = new ClothSimulationFilter(points) { ClothResolution = 1.0 };
            Assert.True(csf.Compute());
            Assert.True(Errors(csf.IsGround, truth).groundMissed < 0.005);
        }

        [Fact]
        public void UtmCoordinates_SameClassification()
        {
            var (local, truth) = Scene(Hills, 120, 1.0, 4);
            var (utm, _) = Scene(Hills, 120, 1.0, 4, new Vector3d(632000, 5712000, 100));
            var a = new ClothSimulationFilter(local) { ClothResolution = 1.0 };
            var b = new ClothSimulationFilter(utm) { ClothResolution = 1.0 };
            Assert.True(a.Compute() && b.Compute());
            int differ = Enumerable.Range(0, local.Count).Count(i => a.IsGround[i] != b.IsGround[i]);
            Assert.True(differ <= local.Count / 1000, $"{differ} points classified differently");
        }

        static double Pit(double x, double y)
        {
            double c = Math.Cos(17 * MathUtil.Deg2Rad), s = Math.Sin(17 * MathUtil.Deg2Rad);
            double u = c * x + s * y, v = -s * x + c * y;
            double r = Math.Max(Math.Abs(u) - 30, Math.Abs(v) - 15);
            double z = -40;
            for (int bench = 0; bench < 4; ++bench) {
                if (r <= 0) return z;
                double face = Math.Min(r, 8);
                z += 10 * face / 8;
                r -= 8;
                if (r <= 0) return z;
                r -= 6;
            }
            return z;
        }
    }
}
