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

        [Fact]
        public void DeepPit_ClothReachesTheRim()
        {
            // 300 m deep: the cloth falls at most half a cell per iteration, more than 500 iterations
            Func<double, double, double> pit = (x, y) => {
                double r = Math.Max(Math.Abs(x - 240) - 20, Math.Abs(y - 240) - 20);
                return -300 + Math.Min(300, Math.Max(0, r) * 1.7);      // 60 degree faces up to z = 0
            };
            var (points, truth) = Scene(pit, 480, 1.0, 5, default(Vector3d), false);
            var csf = new ClothSimulationFilter(points) { ClothResolution = 1.0, SlopeStep = 2.5 };
            Assert.True(csf.Compute());
            // the rim, 300 m above the floor, is reached: all of it is ground (beyond the crest region,
            // where a 60 degree face is at the limit of the slope post-processing)
            var rim = Enumerable.Range(0, points.Count).Where(i => Math.Max(Math.Abs(points[i].x - 240), Math.Abs(points[i].y - 240)) > 20 + 300 / 1.7 + 5).ToList();
            Assert.True(rim.Count > 10000);
            Assert.True(rim.All(i => csf.IsGround[i]), $"{rim.Count(i => !csf.IsGround[i])} rim points not ground");
            Assert.True(Errors(csf.IsGround, truth).groundMissed < 0.03, $"{Errors(csf.IsGround, truth).groundMissed:P2} missed");
        }

        [Fact]
        public void NonFinitePoints_AreSkipped()
        {
            var (points, truth) = Scene(Hills, 60, 1.0, 6, default(Vector3d), false);
            points[10] = new Vector3d(double.NaN, 0, 0);
            points[20] = new Vector3d(0, double.PositiveInfinity, 0);
            var csf = new ClothSimulationFilter(points) { ClothResolution = 1.0 };
            Assert.True(csf.Compute());
            Assert.False(csf.IsGround[10]);
            Assert.False(csf.IsGround[20]);
            truth[10] = truth[20] = false;
            Assert.True(Errors(csf.IsGround, truth).groundMissed < 0.005);
            Assert.False(new ClothSimulationFilter(new List<Vector3d>() { new Vector3d(double.NaN, 0, 0) }).Compute());
        }

        [Fact]
        public void LowVehicle_NotGround_WithSlopeStepBelowItsHeight()
        {
            // a 4 x 2 m vehicle of 1.2 m on flat ground, dense points
            var points = new List<Vector3d>();
            var truth = new List<bool>();
            for (double x = 0; x < 40; x += 0.25)
                for (double y = 0; y < 40; y += 0.25) {
                    bool car = Math.Abs(x - 20) < 2 && Math.Abs(y - 20) < 1;
                    points.Add(new Vector3d(x, y, car ? 1.2 : 0));
                    truth.Add(!car);
                }
            var csf = new ClothSimulationFilter(points) { ClothResolution = 1.0, SlopeStep = 1.0 };
            Assert.True(csf.Compute());
            var (missed, wrong) = Errors(csf.IsGround, truth);
            Assert.True(missed < 0.01, $"{missed:P2} missed");
            Assert.True(wrong < 0.05, $"{wrong:P2} of the vehicle points classified as ground");
        }

        [Fact]
        public void OpenPitWith75DegreeFaces_DefaultParameters()
        {
            // 10 m faces at 75 degrees (3.7 m drop per cloth cell): the crests and faces were not ground
            // (6.8% of the ground missed when following drops only up to SlopeStep)
            Func<double, double, double> pit = (x, y) => Pit(x - 100, y - 100, 10 / Math.Tan(75 * MathUtil.Deg2Rad));
            var (points, truth) = Scene(pit, 200, 1.0, 2, default(Vector3d), true, true);
            var csf = new ClothSimulationFilter(points) { ClothResolution = 1.0 };
            Assert.True(csf.Compute());
            var (missed, wrong) = Errors(csf.IsGround, truth);
            Assert.True(missed < 0.02, $"{missed:P2} of the ground points missed");
            Assert.True(wrong < 0.02, $"{wrong:P2} of the object points classified as ground");
        }

        static double Pit(double x, double y, double faceWidth = 8)
        {
            double c = Math.Cos(17 * MathUtil.Deg2Rad), s = Math.Sin(17 * MathUtil.Deg2Rad);
            double u = c * x + s * y, v = -s * x + c * y;
            double r = Math.Max(Math.Abs(u) - 30, Math.Abs(v) - 15);
            double z = -40;
            for (int bench = 0; bench < 4; ++bench) {
                if (r <= 0) return z;
                double face = Math.Min(r, faceWidth);
                z += 10 * face / faceWidth;
                r -= faceWidth;
                if (r <= 0) return z;
                r -= 6;
            }
            return z;
        }
    }
}
