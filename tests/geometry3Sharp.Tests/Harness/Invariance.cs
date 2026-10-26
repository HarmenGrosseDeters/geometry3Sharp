using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A rigid motion plus uniform scale applied to a test input, with the inverse for the results. Invariance tests
    /// run an operation on the original and on transformed copies and compare the results, mapped back, with each
    /// other (not with absolute bounds, which hide scale dependence; see docs/QA.md).
    /// </summary>
    public sealed class Similarity
    {
        public readonly string Name;
        public readonly double Scale;
        public readonly Matrix3d Rotation;
        public readonly Vector3d Translation;

        public Similarity(string name, double scale, Matrix3d rotation, Vector3d translation)
        {
            Name = name; Scale = scale; Rotation = rotation; Translation = translation;
        }

        public Vector3d Apply(Vector3d p) => Scale * (Rotation * p) + Translation;
        public Vector3d Inverse(Vector3d p) => Rotation.Transpose() * ((p - Translation) / Scale);
        public Vector3d ApplyDirection(Vector3d d) => Rotation * d;
        public double ApplyLength(double l) => Scale * l;

        public override string ToString() => Name;

        /// <summary>
        /// True for scaling by a power of two (no rotation, no translation): exact in floating point, so a method
        /// without absolute constants gives the same result, scaled, up to the last bits.
        /// </summary>
        public bool IsExact
        {
            get {
                if (Translation != Vector3d.Zero || Rotation.EpsilonEqual(Matrix3d.Identity, 0) == false) return false;
                double m = Math.Log(Scale, 2);
                return Math.Abs(m - Math.Round(m)) < 1e-12;
            }
        }

        /// <summary>
        /// The rounding unit of a transformed coordinate, mapped back to the input's units, for an input of the given
        /// size around the origin: a comparison of results cannot be tighter than a small multiple of this.
        /// </summary>
        public double Resolution(double size)
        {
            double magnitude = Math.Max(Math.Abs(Translation.x), Math.Max(Math.Abs(Translation.y), Math.Abs(Translation.z))) + Scale * size;
            return magnitude * MathUtil.Epsilon / Scale;
        }

        public static readonly Similarity Identity = new Similarity("identity", 1, Matrix3d.Identity, Vector3d.Zero);

        /// <summary>UTM-like coordinates (zone 32U), where float-precision code loses millimetres.</summary>
        public static readonly Similarity Utm = new Similarity("utm", 1, Matrix3d.Identity, new Vector3d(632117.25, 5712043.5, 113.75));
        /// <summary>Scaling by a power of two is exact in floating point: a scale-invariant method gives the same result, scaled, bit for bit.</summary>
        public static readonly Similarity Pow2Small = new Similarity("scale 2^-10", 1.0 / 1024, Matrix3d.Identity, Vector3d.Zero);
        public static readonly Similarity Pow2Large = new Similarity("scale 2^10", 1024, Matrix3d.Identity, Vector3d.Zero);
        public static readonly Similarity Millimetre = new Similarity("scale 1e-3", 1e-3, Matrix3d.Identity, Vector3d.Zero);
        public static readonly Similarity Kilometre = new Similarity("scale 1e3", 1e3, Matrix3d.Identity, Vector3d.Zero);
        public static readonly Similarity Rotated = new Similarity("rotated",
            1, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17), Vector3d.Zero);
        public static readonly Similarity RotatedUtmMillimetre = new Similarity("rotated, scale 1e-3, utm",
            1e-3, Matrix3d.AxisAngleD(Vector3d.AxisY, 23), new Vector3d(632117.25, 5712043.5, 113.75));

        public static IEnumerable<Similarity> All()
        {
            yield return Identity;
            yield return Utm;
            yield return Pow2Small;
            yield return Pow2Large;
            yield return Millimetre;
            yield return Kilometre;
            yield return Rotated;
            yield return RotatedUtmMillimetre;
        }

        /// <summary>
        /// The resolution of Invariance.VertexDistance for an input of the given size: point-triangle distances come
        /// from squared distances with cancellation, so their rounding noise is the square root of the coordinate
        /// resolution times the size (about 1e-8 * size), not the resolution itself.
        /// </summary>
        public double DistanceResolution(double size) => 8 * Math.Sqrt(Resolution(size) * size);

        public bool IsRotated => Rotation.EpsilonEqual(Matrix3d.Identity, 0) == false;

        /// <summary>For xunit [MemberData]: the names of All().</summary>
        public static IEnumerable<object[]> Names() => All().Select(s => new object[] { s.Name });

        public static Similarity ByName(string name) => All().First(s => s.Name == name);
    }

    public static class Invariance
    {
        /// <summary>A copy of the mesh with transformed vertices (normals rotated); ids and attributes kept.</summary>
        public static DMesh3 Transformed(DMesh3 mesh, Similarity s)
        {
            var copy = new DMesh3(mesh);
            foreach (int vid in copy.VertexIndices()) {
                copy.SetVertex(vid, s.Apply(copy.GetVertex(vid)));
                if (copy.HasVertexNormals)
                    copy.SetVertexNormal(vid, (Vector3f)s.ApplyDirection(copy.GetVertexNormal(vid)));
            }
            return copy;
        }

        /// <summary>A copy of the mesh mapped back with the inverse transform.</summary>
        public static DMesh3 MappedBack(DMesh3 mesh, Similarity s)
        {
            var copy = new DMesh3(mesh);
            foreach (int vid in copy.VertexIndices())
                copy.SetVertex(vid, s.Inverse(copy.GetVertex(vid)));
            return copy;
        }

        /// <summary>
        /// The same surface with vertices and triangles in a random order (compact ids). Each triangle's corners are
        /// rotated cyclically, so orientation is kept. Results must not depend on the input order beyond ties.
        /// </summary>
        public static DMesh3 Permuted(DMesh3 mesh, int seed)
        {
            var rand = new Random(seed);
            int[] vids = mesh.VertexIndices().OrderBy(v => rand.Next()).ToArray();
            var map = new Dictionary<int, int>();
            var result = new DMesh3();
            foreach (int v in vids)
                map[v] = result.AppendVertex(mesh.GetVertex(v));
            foreach (int t in mesh.TriangleIndices().OrderBy(t => rand.Next()).ToArray()) {
                Index3i tri = mesh.GetTriangle(t);
                int k = rand.Next(3);
                result.AppendTriangle(new Index3i(map[tri[k]], map[tri[(k + 1) % 3]], map[tri[(k + 2) % 3]]));
            }
            return result;
        }

        /// <summary>Signed volume (divergence theorem), computed relative to the bounds center so that it stays accurate far from the origin.</summary>
        public static double Volume(DMesh3 mesh)
        {
            Vector3d c = mesh.TriangleCount > 0 ? MeshRepairMetrics.TriangleBounds(mesh).Center : Vector3d.Zero;
            double v = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, d = Vector3d.Zero;
            foreach (int t in mesh.TriangleIndices()) {
                mesh.GetTriVertices(t, ref a, ref b, ref d);
                v += (a - c).Dot((b - c).Cross(d - c));
            }
            return v / 6;
        }

        public static double Area(DMesh3 mesh) => mesh.TriangleIndices().Sum(t => mesh.GetTriArea(t));

        /// <summary>Every vertex coordinate is finite.</summary>
        public static bool AllFinite(DMesh3 mesh) => mesh.Vertices().All(v => v.IsFinite);

        /// <summary>
        /// Bit-exact equality of two meshes: same vertex and triangle ids, same coordinates, same triangles.
        /// Used by the determinism tests (repeated runs, parallel vs serial).
        /// </summary>
        public static string Difference(DMesh3 x, DMesh3 y)
        {
            if (x.VertexCount != y.VertexCount) return $"vertex count {x.VertexCount} vs {y.VertexCount}";
            if (x.TriangleCount != y.TriangleCount) return $"triangle count {x.TriangleCount} vs {y.TriangleCount}";
            if (x.MaxVertexID != y.MaxVertexID || x.MaxTriangleID != y.MaxTriangleID) return "id spaces differ";
            for (int v = 0; v < x.MaxVertexID; ++v) {
                if (x.IsVertex(v) != y.IsVertex(v)) return $"vertex {v} exists in one mesh only";
                if (x.IsVertex(v) && x.GetVertex(v) != y.GetVertex(v)) return $"vertex {v}: {x.GetVertex(v)} vs {y.GetVertex(v)}";
            }
            for (int t = 0; t < x.MaxTriangleID; ++t) {
                if (x.IsTriangle(t) != y.IsTriangle(t)) return $"triangle {t} exists in one mesh only";
                if (x.IsTriangle(t) && x.GetTriangle(t) != y.GetTriangle(t)) return $"triangle {t}: {x.GetTriangle(t)} vs {y.GetTriangle(t)}";
            }
            return "";
        }

        /// <summary>
        /// Equality of two meshes as sets of oriented triangles (exact coordinates), independent of vertex and triangle
        /// ids and of the starting corner of each triangle. For outputs whose id order may follow the thread schedule.
        /// </summary>
        public static string GeometricDifference(DMesh3 x, DMesh3 y)
        {
            List<string> a = CanonicalTriangles(x), b = CanonicalTriangles(y);
            if (a.Count != b.Count) return $"triangle count {a.Count} vs {b.Count}";
            for (int i = 0; i < a.Count; ++i)
                if (a[i] != b[i]) return $"triangle {a[i]} vs {b[i]}";
            return "";
        }

        static List<string> CanonicalTriangles(DMesh3 mesh)
        {
            var result = new List<string>();
            Vector3d[] p = new Vector3d[3];
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tri = mesh.GetTriangle(t);
                for (int k = 0; k < 3; ++k) p[k] = mesh.GetVertex(tri[k]);
                int first = 0;
                for (int k = 1; k < 3; ++k)
                    if (Compare(p[k], p[first]) < 0) first = k;
                result.Add(string.Join(" ", Enumerable.Range(0, 3).Select(k => Key(p[(first + k) % 3]))));
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        static int Compare(Vector3d u, Vector3d v)
        {
            int c = u.x.CompareTo(v.x);
            if (c == 0) c = u.y.CompareTo(v.y);
            if (c == 0) c = u.z.CompareTo(v.z);
            return c;
        }

        // (+ 0.0 maps -0.0 to +0.0, which Compare treats as equal: a translation turns -0.0 into +0.0)
        static string Key(Vector3d v) =>
            BitConverter.DoubleToInt64Bits(v.x + 0.0).ToString("X16") + BitConverter.DoubleToInt64Bits(v.y + 0.0).ToString("X16") + BitConverter.DoubleToInt64Bits(v.z + 0.0).ToString("X16");

        /// <summary>
        /// Symmetric Hausdorff-like distance by sampling the vertices of each mesh against the other's triangles.
        /// Rounding noise is about 1e-8 times the size (Similarity.DistanceResolution).
        /// </summary>
        public static double VertexDistance(DMesh3 x, DMesh3 y)
        {
            return Math.Max(OneSided(x, y), OneSided(y, x));
        }

        static double OneSided(DMesh3 from, DMesh3 to)
        {
            var tree = new DMeshAABBTree3(to, true);
            double max = 0;
            foreach (Vector3d p in from.Vertices()) {
                int tid = tree.FindNearestTriangle(p);
                max = Math.Max(max, Math.Sqrt(MeshQueries.TriDistanceSqr(to, tid, p)));
            }
            return max;
        }
    }
}
