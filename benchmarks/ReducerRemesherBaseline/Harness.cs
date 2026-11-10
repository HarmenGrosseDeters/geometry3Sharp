// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The logic of ReducerRemesherBaseline that is not a measurement: statistics, the similarity transforms of the scale
// invariance check, the bit-for-bit mesh hash, the angle counts and the procedural inputs. Linked into
// tests/geometry3Sharp.Tests (BaselineHarnessTests), so that the harness itself is tested.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using g3;
using gs;

namespace BaselineHarness
{
    public static class Stats
    {
        /// <summary> linear-interpolated percentile (p in [0,1]) of the finite values; NaN if there are none </summary>
        public static double Percentile(IEnumerable<double> values, double p)
        {
            double[] v = values.Where(x => double.IsNaN(x) == false && double.IsInfinity(x) == false).OrderBy(x => x).ToArray();
            if (v.Length == 0) return double.NaN;
            double pos = Math.Max(0, Math.Min(1, p)) * (v.Length - 1);
            int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
            return v[lo] + (v[hi] - v[lo]) * (pos - lo);
        }

        public static double Median(IEnumerable<double> values) { return Percentile(values, 0.5); }
    }

    /// <summary> a similarity (rotation, uniform scale, translation); the set of tests/.../Harness/Invariance.cs </summary>
    public sealed class Xform
    {
        public readonly string Name;
        public readonly double Scale;
        public readonly Matrix3d Rotation;
        public readonly Vector3d Translation;

        public Xform(string name, double scale, Matrix3d rotation, Vector3d translation)
        {
            Name = name; Scale = scale; Rotation = rotation; Translation = translation;
        }

        public Vector3d Apply(Vector3d p) { return Scale * (Rotation * p) + Translation; }
        public Vector3d Inverse(Vector3d p) { return Rotation.Transpose() * ((p - Translation) / Scale); }

        /// <summary> scaling by a power of two only: exact in floating point, so a scale-free method must give the same result bit for bit </summary>
        public bool IsPowerOfTwoScaling
        {
            get {
                if (Translation != Vector3d.Zero || Rotation.EpsilonEqual(Matrix3d.Identity, 0) == false) return false;
                double m = Math.Log(Scale, 2);
                return Math.Abs(m - Math.Round(m)) < 1e-12;
            }
        }

        public static IEnumerable<Xform> All()
        {
            yield return new Xform("identity", 1, Matrix3d.Identity, Vector3d.Zero);
            yield return new Xform("utm", 1, Matrix3d.Identity, new Vector3d(632117.25, 5712043.5, 113.75));
            yield return new Xform("scale 2^-10", 1.0 / 1024, Matrix3d.Identity, Vector3d.Zero);
            yield return new Xform("scale 2^10", 1024, Matrix3d.Identity, Vector3d.Zero);
            yield return new Xform("scale 1e-3", 1e-3, Matrix3d.Identity, Vector3d.Zero);
            yield return new Xform("scale 1e3", 1e3, Matrix3d.Identity, Vector3d.Zero);
            yield return new Xform("rotated", 1, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17), Vector3d.Zero);
            yield return new Xform("rotated, scale 1e-3, utm", 1e-3, Matrix3d.AxisAngleD(Vector3d.AxisY, 23), new Vector3d(632117.25, 5712043.5, 113.75));
        }

        /// <summary> a copy of the mesh with every vertex transformed (ids and triangles unchanged) </summary>
        public DMesh3 Transform(DMesh3 m) { return Map(m, Apply); }
        /// <summary> a copy of the mesh with every vertex mapped back to the input's frame </summary>
        public DMesh3 MapBack(DMesh3 m) { return Map(m, Inverse); }

        static DMesh3 Map(DMesh3 m, Func<Vector3d, Vector3d> f)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices())
                c.SetVertex(v, f(c.GetVertex(v)));
            return c;
        }

        public override string ToString() { return Name; }
    }

    public static class MeshHash
    {
        /// <summary> SHA-256 (first 16 hex digits) over the vertex ids and coordinates bit for bit and the triangles (ids included) </summary>
        public static string Of(DMesh3 m)
        {
            var bytes = new List<byte>();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                bytes.AddRange(BitConverter.GetBytes(v));
                bytes.AddRange(BitConverter.GetBytes(p.x)); bytes.AddRange(BitConverter.GetBytes(p.y)); bytes.AddRange(BitConverter.GetBytes(p.z));
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                bytes.AddRange(BitConverter.GetBytes(t));
                bytes.AddRange(BitConverter.GetBytes(tv.a)); bytes.AddRange(BitConverter.GetBytes(tv.b)); bytes.AddRange(BitConverter.GetBytes(tv.c));
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16);
        }
    }

    /// <summary> angle statistics of the triangles of a mesh: the quality columns of the baseline </summary>
    public struct AngleSummary
    {
        public int Triangles;
        /// <summary> smallest corner angle (degrees) </summary>
        public double MinAngle;
        /// <summary> the 1 % quantile of the triangles' smallest angles (p01) </summary>
        public double MinAngleP01;
        /// <summary> triangles with a corner angle below 0.5 degrees / above 170 degrees (slivers and caps; degenerate ones count in both) </summary>
        public int Below05, Above170;

        public static AngleSummary Compute(DMesh3 m)
        {
            var mins = new List<double>(m.TriangleCount);
            var s = new AngleSummary();
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                double minA, maxA, d1, d2, d3;
                TriangleQualityStatistics.Measure(a, b, c, out minA, out maxA, out d1, out d2, out d3);
                mins.Add(minA);
                if (minA < 0.5) s.Below05++;
                if (maxA > 170) s.Above170++;
            }
            s.Triangles = mins.Count;
            s.MinAngle = mins.Count > 0 ? mins.Min() : double.NaN;
            s.MinAngleP01 = Stats.Percentile(mins, 0.01);
            return s;
        }
    }

    /// <summary> the procedural inputs (no mesh files); sizes are the number of vertices along an edge </summary>
    public static class Inputs
    {
        public static DMesh3 BumpySphere(int edgeVertices)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5) + 0.05 * Math.Sin(9 * p.z)));
            }
            return m;
        }

        /// <summary> closed torus of genus 1 (major radius 1, minor radius 0.35, a wavy minor radius) with nu x nv quads, each split in two </summary>
        public static DMesh3 Torus(int nu, int nv)
        {
            var m = new DMesh3();
            int[,] id = new int[nu, nv];
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    double r = 0.35 * (1 + 0.12 * Math.Sin(3 * u + 2 * v));
                    id[i, j] = m.AppendVertex(new Vector3d((1 + r * Math.Cos(v)) * Math.Cos(u), (1 + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int i1 = (i + 1) % nu, j1 = (j + 1) % nv;
                    m.AppendTriangle(id[i, j], id[i1, j], id[i1, j1]);
                    m.AppendTriangle(id[i, j], id[i1, j1], id[i, j1]);
                }
            return m;
        }

        /// <summary> open n x n heightfield on the unit square: smooth hills plus two ripple frequencies </summary>
        public static DMesh3 Terrain(int n)
        {
            var m = new DMesh3();
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    double z = 0.08 * Math.Sin(6 * x) * Math.Cos(5 * y) + 0.01 * Math.Sin(40 * x + 13 * y) + 0.004 * Math.Cos(90 * y - 31 * x);
                    ids[i, j] = m.AppendVertex(new Vector3d(x, y, z));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        /// <summary> closed box with sharp 90 degree edges, edgeVertices vertices along an edge of each face (feature preservation) </summary>
        public static DMesh3 Box(int edgeVertices)
        {
            return new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.3)), EdgeVertices = edgeVertices, NoSharedVertices = false }.Generate().MakeDMesh();
        }

        public static double MeanEdge(DMesh3 m)
        {
            double sum = 0; int n = 0;
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e);
                sum += m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); ++n;
            }
            return n > 0 ? sum / n : 0;
        }
    }
}
