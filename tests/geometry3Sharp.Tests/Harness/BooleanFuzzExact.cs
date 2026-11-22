using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary> Conversions for Rat (Harness/ContactAssemblies.cs): the exact value of a double, and the nearest double. </summary>
    public static class Rats
    {
        public static Rat Of(BigInteger n) => new Rat(n, BigInteger.One);

        /// <summary> the value of a finite double, exactly </summary>
        public static Rat Of(double x)
        {
            ExactPoint3.Decompose(x, out BigInteger m, out int e);
            return e >= 0 ? new Rat(m << e, BigInteger.One) : new Rat(m, BigInteger.One << -e);
        }

        public static bool IsZero(this Rat x) => x.N.IsZero;
        public static double ToDouble(this Rat x) => ExactPoint3.ToDouble(x.N, x.D, 0);
    }

    /// <summary>
    /// An exact invariant of an oriented triangle surface as a 2-chain: per supporting plane (its primitive integer normal
    /// with a fixed sign, and its offset), the signed area in units of that normal and the signed first moment (area times
    /// the sum of the corners). It does not depend on the triangulation, the order or the ids, and two-sided sheets cancel.
    /// It is additive, so identities of solids become identities of fingerprints: the boundary of A u B plus that of A n B is
    /// the boundary of A plus that of B, plane by plane. The volume follows from it (six times the volume is the sum of
    /// offset times signed area), so equal fingerprints have equal exact volumes. Not a complete invariant (a region may
    /// move in its plane with its area and centroid kept), but every difference it shows is a difference of the surfaces.
    /// </summary>
    public sealed class SurfaceFingerprint
    {
        /// <summary> per plane key: the signed area (in units of the primitive normal) and the moment's three coordinates </summary>
        public readonly Dictionary<string, Rat[]> Planes = new Dictionary<string, Rat[]>();
        readonly Dictionary<string, Rat> offsets = new Dictionary<string, Rat>();

        public static Rat[] Exact(Vector3d v) => new[] { Rats.Of(v.x), Rats.Of(v.y), Rats.Of(v.z) };
        public static Rat[] Exact(ExactPoint3 p) => new[] { new Rat(p.X, p.W), new Rat(p.Y, p.W), new Rat(p.Z, p.W) };

        /// <summary> of a mesh with double coordinates (exact: doubles are dyadic rationals) </summary>
        public static SurfaceFingerprint Of(DMesh3 mesh)
        {
            var f = new SurfaceFingerprint();
            var cache = new Dictionary<int, Rat[]>();
            Rat[] V(int v) { if (!cache.TryGetValue(v, out Rat[] r)) cache[v] = r = Exact(mesh.GetVertex(v)); return r; }
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tri = mesh.GetTriangle(t);
                f.AddTriangle(V(tri.a), V(tri.b), V(tri.c), 1);
            }
            return f.Clean();
        }

        /// <summary> of a result with its exact vertices (ExactMeshBoolean.ResultExactVertices, per vertex id) </summary>
        public static SurfaceFingerprint Of(DMesh3 mesh, ExactPoint3[] exact)
        {
            var f = new SurfaceFingerprint();
            var cache = new Dictionary<int, Rat[]>();
            Rat[] V(int v) { if (!cache.TryGetValue(v, out Rat[] r)) cache[v] = r = Exact(exact[v]); return r; }
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tri = mesh.GetTriangle(t);
                f.AddTriangle(V(tri.a), V(tri.b), V(tri.c), 1);
            }
            return f.Clean();
        }

        static BigInteger Lcm(BigInteger a, BigInteger b) => a / BigInteger.GreatestCommonDivisor(a, b) * b;

        void AddTriangle(Rat[] a, Rat[] b, Rat[] c, int sign)
        {
            Rat ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2], vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
            Rat nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (nx.IsZero() && ny.IsZero() && nz.IsZero()) return;
            // the primitive integer vector along the normal, with its first non-zero entry positive
            BigInteger l = Lcm(Lcm(nx.D, ny.D), nz.D);
            BigInteger ix = nx.N * (l / nx.D), iy = ny.N * (l / ny.D), iz = nz.N * (l / nz.D);
            BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(ix, iy), iz);
            ix /= g; iy /= g; iz /= g;
            int first = !ix.IsZero ? ix.Sign : !iy.IsZero ? iy.Sign : iz.Sign;
            if (first < 0) { ix = -ix; iy = -iy; iz = -iz; }
            Rat s = !ix.IsZero ? nx / Rats.Of(ix) : !iy.IsZero ? ny / Rats.Of(iy) : nz / Rats.Of(iz);
            if (sign < 0) s = -s;
            Rat d = Rats.Of(ix) * a[0] + Rats.Of(iy) * a[1] + Rats.Of(iz) * a[2];
            string key = ix + "," + iy + "," + iz + "|" + d;
            if (!Planes.TryGetValue(key, out Rat[] e)) { Planes[key] = e = new[] { Rat.Zero, Rat.Zero, Rat.Zero, Rat.Zero }; offsets[key] = d; }
            e[0] += s;
            for (int k = 0; k < 3; ++k) e[1 + k] += s * (a[k] + b[k] + c[k]);
        }

        SurfaceFingerprint Clean()
        {
            foreach (string k in Planes.Where(p => p.Value.All(x => x.IsZero())).Select(p => p.Key).ToList()) { Planes.Remove(k); offsets.Remove(k); }
            return this;
        }

        /// <summary> this plus sign times other (a new fingerprint) </summary>
        public SurfaceFingerprint Plus(SurfaceFingerprint other, int sign = 1)
        {
            var f = new SurfaceFingerprint();
            foreach (var p in Planes) { f.Planes[p.Key] = (Rat[])p.Value.Clone(); f.offsets[p.Key] = offsets[p.Key]; }
            foreach (var p in other.Planes) {
                if (!f.Planes.TryGetValue(p.Key, out Rat[] e)) { f.Planes[p.Key] = e = new[] { Rat.Zero, Rat.Zero, Rat.Zero, Rat.Zero }; f.offsets[p.Key] = other.offsets[p.Key]; }
                for (int k = 0; k < 4; ++k) e[k] = sign > 0 ? e[k] + p.Value[k] : e[k] - p.Value[k];
            }
            return f.Clean();
        }

        public SurfaceFingerprint Minus(SurfaceFingerprint other) => Plus(other, -1);

        public bool IsEmpty => Planes.Count == 0;

        /// <summary> six times the signed volume, exactly </summary>
        public Rat SixVolume()
        {
            Rat v = Rat.Zero;
            foreach (var p in Planes) v += offsets[p.Key] * p.Value[0];
            return v;
        }

        public double Volume() => SixVolume().ToDouble() / 6;

        /// <summary> "" if the fingerprints are equal, else the planes that differ (the first few) and the volumes </summary>
        public static string Difference(SurfaceFingerprint x, SurfaceFingerprint y)
        {
            SurfaceFingerprint d = x.Minus(y);
            if (d.IsEmpty) return "";
            var sb = new StringBuilder();
            sb.Append(FormattableString.Invariant($"{d.Planes.Count} planes differ (volumes {x.Volume():R} vs {y.Volume():R}, difference of six volumes {d.SixVolume().ToDouble():R})"));
            foreach (var p in d.Planes.OrderBy(p => p.Key, StringComparer.Ordinal).Take(3)) {
                string key = p.Key.Length > 60 ? p.Key.Substring(0, 60) + "..." : p.Key;
                sb.Append(FormattableString.Invariant($"; plane {key}: area {p.Value[0].ToDouble():R}"));
            }
            return sb.ToString();
        }
    }
}
