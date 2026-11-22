// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using g3;

// The adversarial run of the error bound audit (docs/research/ERROR_BOUND_AUDIT_2026-11.md). Every check calls the
// library's own code by reflection (so the source is what is tested, not a transcription) and compares with the exact
// value in the library's exact dyadic type (ExactDyadic: every double is a dyadic number, and so is every exact point
// used here). Inputs are near-degenerate (the filter's value near its bound) with mantissas that make roundings large
// and exponents over the whole range each filter admits.
//
//     dotnet run --project tools/ErrorBoundAudit/Adversarial -c Release -- [samples] [seed]
//
// Prints per bound: decided cases, the largest observed |error| / bound (or wrong signs), and the reproducers of the
// findings. The whole predicates (EP-1/3/4) run on samples / 4 inputs per range. Exit code 1 if a check that the audit calls sound fails.
static class Program
{
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Lib = typeof(Vector3d).Assembly;
    static readonly Type DyT = Lib.GetType("g3.ExactDyadic", true);
    static readonly MethodInfo DyOf = DyT.GetMethod("Of", Any), DyAdd = DyT.GetMethod("op_Addition", Any),
        DySub = DyT.GetMethod("op_Subtraction", Any), DyMul = DyT.GetMethod("op_Multiply", Any);
    static readonly PropertyInfo DySign = DyT.GetProperty("Sign", Any);
    static readonly MethodInfo DyQuotient = DyT.GetMethod("Quotient", Any);

    /// <summary> an exact dyadic number: the library's ExactDyadic behind reflection </summary>
    readonly struct Dy
    {
        readonly object o;
        Dy(object o) { this.o = o; }
        public static implicit operator Dy(double x) { return new Dy(DyOf.Invoke(null, new object[] { x })); }
        public static Dy operator +(Dy a, Dy b) { return new Dy(DyAdd.Invoke(null, new[] { a.o, b.o })); }
        public static Dy operator -(Dy a, Dy b) { return new Dy(DySub.Invoke(null, new[] { a.o, b.o })); }
        public static Dy operator *(Dy a, Dy b) { return new Dy(DyMul.Invoke(null, new[] { a.o, b.o })); }
        public int Sign { get { return (int)DySign.GetValue(o); } }
        public Dy Abs() { return Sign < 0 ? (Dy)0.0 - this : this; }
        public static int Compare(Dy a, Dy b) { return (a - b).Sign; }
        /// <summary> a / b rounded to the nearest double (ExactDyadic.Quotient) </summary>
        public static double Quotient(Dy a, Dy b) { return (double)DyQuotient.Invoke(null, new[] { a.o, b.o }); }
    }

    static MethodInfo M(string type, string name, int args = -1)
    {
        Type t = Lib.GetType("g3." + type, true);
        var all = t.GetMethods(Any).Where(m => m.Name == name && (args < 0 || m.GetParameters().Length == args)).ToArray();
        if (all.Length != 1) throw new InvalidOperationException(type + "." + name + ": " + all.Length + " methods");
        return all[0];
    }

    static Random rng;
    static int failures;

    // a double with exponent in [emin, emax] and a mantissa that rounds badly
    static double Rnd(int emin, int emax)
    {
        long m;
        switch (rng.Next(4)) {
            case 0: m = (1L << 53) - 1 - rng.Next(4); break;
            case 1: m = (1L << 52) + 1 + rng.Next(4); break;
            case 2: m = (1L << 52) + (1L << rng.Next(52)) + rng.Next(2); break;
            default: m = (1L << 52) + (rng.NextInt64() & ((1L << 52) - 1)); break;
        }
        double v = Math.ScaleB((double)m, rng.Next(emin, emax + 1) - 52);
        return rng.Next(2) == 0 ? v : -v;
    }
    static Vector3d RndV(int emin, int emax) { return new Vector3d(Rnd(emin, emax), Rnd(emin, emax), Rnd(emin, emax)); }

    static Dy Det3(Dy ux, Dy uy, Dy uz, Dy vx, Dy vy, Dy vz, Dy wx, Dy wy, Dy wz)
    {
        return wx * (uy * vz - uz * vy) + wy * (uz * vx - ux * vz) + wz * (ux * vy - uy * vx);
    }
    static Dy Orient3dExact(Dy[] a, Dy[] b, Dy[] c, Dy[] d)
    {
        return Det3(b[0] - a[0], b[1] - a[1], b[2] - a[2], c[0] - a[0], c[1] - a[1], c[2] - a[2], d[0] - a[0], d[1] - a[1], d[2] - a[2]);
    }
    static Dy[] V(Vector3d p) { return new Dy[] { p.x, p.y, p.z }; }

    // an exact value within rel = k 2^-60 (|k| <= 256, so 2^-52) of the double x, relative to x: x (1 + k 2^-60)
    static Dy Near(double x) { return (Dy)x + (Dy)x * (Dy)(rng.Next(-256, 257) * Math.ScaleB(1.0, -60)); }
    static Dy[] NearV(Vector3d p) { return new[] { Near(p.x), Near(p.y), Near(p.z) }; }
    // an exact value within e of x
    static Dy Within(double x, double e) { return (Dy)x + (Dy)e * (Dy)(rng.Next(-64, 65) / 64.0); }

    // d nearly in the plane of a, b, c: a + s (b - a) + t (c - a) in doubles, then a few ulps off
    static Vector3d NearPlane(Vector3d a, Vector3d b, Vector3d c)
    {
        double s = rng.NextDouble() * 2 - 0.5, t = rng.NextDouble() * 2 - 0.5;
        Vector3d d = a + s * (b - a) + t * (c - a);
        int k = rng.Next(3);
        for (int i = rng.Next(4); i > 0; --i)
            d[k] = rng.Next(2) == 0 ? Math.BitIncrement(d[k]) : Math.BitDecrement(d[k]);
        return d;
    }

    // as NearPlane, moved off the plane by a relative 2^-k of the coordinates, k in [20, 50]
    static Vector3d OffPlane(Vector3d a, Vector3d b, Vector3d c)
    {
        Vector3d d = NearPlane(a, b, c);
        double f = Math.ScaleB(1.0, -rng.Next(20, 51));
        return new Vector3d(d.x * (1 + f * rng.Next(-3, 4)), d.y * (1 + f * rng.Next(-3, 4)), d.z * (1 + f * rng.Next(-3, 4)));
    }

    static void Report(string name, long decided, long total, long wrong, double worst, string note = "")
    {
        Console.WriteLine("{0,-44} decided {1,8} / {2,-8} wrong {3,-4} worst |error| / bound {4,-10:G4} {5}", name, decided, total, wrong, worst, note);
        if (wrong > 0) failures++;
    }

    static double Ratio(Dy err, double bound)
    {
        if (!(bound > 0)) return err.Sign == 0 ? 0 : double.PositiveInfinity;
        return Dy.Quotient(err, bound);
    }

    static void Orient3dFilter(int n)
    {
        var f = M("ExactPredicates", "Orient3dFilter");
        // (-226, -219): the permanent near UnderflowLimit; the two low ranges: below it, the filter must not decide
        foreach (var range in new[] { (-30, 30), (-226, -219), (-340, -300), (-1070, -1000), (300, 330), (-1074, 1000) }) {
            long decided = 0, wrong = 0;
            for (int i = 0; i < n; ++i) {
                Vector3d a = RndV(range.Item1, range.Item2), b = RndV(range.Item1, range.Item2), c = RndV(range.Item1, range.Item2);
                Vector3d d = rng.Next(4) == 0 ? RndV(range.Item1, range.Item2) : NearPlane(a, b, c);
                int s = (int)f.Invoke(null, new object[] { b.x - a.x, b.y - a.y, b.z - a.z, c.x - a.x, c.y - a.y, c.z - a.z, d.x - a.x, d.y - a.y, d.z - a.z });
                int exact = Orient3dExact(V(a), V(b), V(c), V(d)).Sign;
                if (ExactPredicates.Orient3d(a, b, c, d) != exact) wrong++;
                if (s == 0) continue;
                decided++;
                if (s != exact) wrong++;
            }
            Report("EP-2 Orient3dFilter, exponents " + range, decided, n, wrong, double.NaN);
        }
    }

    // a point of the circle (sphere) of radius r about o at a random direction, rounded: four (five) of them are
    // nearly cocircular (cospherical), each off by the rounding of its coordinates
    static Vector3d OnSphere(Vector3d o, double r, bool planar)
    {
        double t = rng.NextDouble() * 2 * Math.PI, z = planar ? 0 : rng.NextDouble() * 2 - 1, q = Math.Sqrt(1 - z * z);
        return new Vector3d(o.x + r * q * Math.Cos(t), o.y + r * q * Math.Sin(t), planar ? 0 : o.z + r * z);
    }

    static void PublicPredicates(int n)
    {
        foreach (var range in new[] { (-30, 30), (-270, -250), (-540, -530), (-1074, -1040), (240, 255) }) {
            long w2 = 0, wc = 0, ws = 0, zc = 0, zs = 0;
            for (int i = 0; i < n; ++i) {
                Vector3d a = RndV(range.Item1, range.Item2), b = RndV(range.Item1, range.Item2);
                Vector2d a2 = a.xy, b2 = b.xy, c2 = new Vector2d(a.x + 0.5 * (b.x - a.x), a.y + 0.5 * (b.y - a.y));
                if (rng.Next(2) == 0) c2.x = Math.BitIncrement(c2.x);
                Dy ux = (Dy)b2.x - a2.x, uy = (Dy)b2.y - a2.y, vx = (Dy)c2.x - a2.x, vy = (Dy)c2.y - a2.y;
                if (ExactPredicates.Orient2d(a2, b2, c2) != (ux * vy - uy * vx).Sign) w2++;
                // four points nearly on one circle, five nearly on one sphere (centre and radius of the range's size)
                Vector3d o = RndV(range.Item1, range.Item2);
                double r = Math.Abs(Rnd(range.Item1, range.Item2));
                Vector3d[] p = new Vector3d[5];
                for (int k = 0; k < 4; ++k) p[k] = OnSphere(o, r, true);
                Dy[] P0 = V(p[0]), P1 = V(p[1]), P2 = V(p[2]), P3 = V(p[3]);
                Dy adx = P0[0] - P3[0], ady = P0[1] - P3[1], bdx = P1[0] - P3[0], bdy = P1[1] - P3[1], cdx = P2[0] - P3[0], cdy = P2[1] - P3[1];
                Dy ic = (adx * adx + ady * ady) * (bdx * cdy - cdx * bdy) + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
                    + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady);
                if (ExactPredicates.InCircle(p[0].xy, p[1].xy, p[2].xy, p[3].xy) != ic.Sign) wc++;
                if (ic.Sign == 0) zc++;
                for (int k = 0; k < 5; ++k) p[k] = OnSphere(o, r, false);
                Dy[] E = V(p[4]);
                Dy sp = Det4(Row(V(p[0]), E), Row(V(p[1]), E), Row(V(p[2]), E), Row(V(p[3]), E));
                // (ExactPredicates.InSphere returns the negated sign of Shewchuk's determinant, the one Det4 expands)
                if (ExactPredicates.InSphere(p[0], p[1], p[2], p[3], p[4]) != -sp.Sign) ws++;
                if (sp.Sign == 0) zs++;
            }
            Report("EP-1/3/4 Orient2d, InCircle, InSphere " + range, n, n, w2 + wc + ws, double.NaN, "wrong: " + w2 + " " + wc + " " + ws + "; exact zeros: " + zc + " " + zs);
        }
    }
    static Dy[] Row(Dy[] p, Dy[] e)
    {
        Dy x = p[0] - e[0], y = p[1] - e[1], z = p[2] - e[2];
        return new[] { x, y, z, x * x + y * y + z * z };
    }
    static Dy Det4(Dy[] a, Dy[] b, Dy[] c, Dy[] d)
    {
        // expansion along the last column (the lifts), Shewchuk's insphere determinant
        Dy ab = a[0] * b[1] - b[0] * a[1], bc = b[0] * c[1] - c[0] * b[1], cd = c[0] * d[1] - d[0] * c[1], da = d[0] * a[1] - a[0] * d[1];
        Dy ac = a[0] * c[1] - c[0] * a[1], bd = b[0] * d[1] - d[0] * b[1];
        Dy abc = a[2] * bc - b[2] * ac + c[2] * ab, bcd = b[2] * cd - c[2] * bd + d[2] * bc;
        Dy cda = c[2] * da + d[2] * ac + a[2] * cd, dab = d[2] * ab + a[2] * bd + b[2] * da;
        return (d[3] * abc - c[3] * dab) + (b[3] * cda - a[3] * bcd);
    }

    static void BooleanOrient(int n)
    {
        var f = M("ExactMeshBoolean", "orient3d_filtered");
        var fe = M("ExactMeshBoolean", "orient3d_filtered_err");
        foreach (var range in new[] { (-30, 30), (-299, -280), (-110, -90), (280, 330) }) {
            long decided = 0, wrong = 0, decidedE = 0, wrongE = 0;
            for (int i = 0; i < n; ++i) {
                Vector3d a = RndV(range.Item1, range.Item2), b = RndV(range.Item1, range.Item2), c = RndV(range.Item1, range.Item2);
                Vector3d d = OffPlane(a, b, c);
                int exact = Orient3dExact(NearV(a), NearV(b), NearV(c), NearV(d)).Sign;
                int s = (int)f.Invoke(null, new object[] { a, b, c, d });
                if (s != 2) { decided++; if (s != -exact) wrong++; }
                // absolute errors per point
                double[] e = new double[4];
                Vector3d[] p = { a, b, c, d };
                Dy[][] x = new Dy[4][];
                for (int k = 0; k < 4; ++k) {
                    e[k] = Math.Abs(Rnd(range.Item1 - 60, range.Item1 - 45));
                    x[k] = new[] { Within(p[k].x, e[k]), Within(p[k].y, e[k]), Within(p[k].z, e[k]) };
                }
                int se = (int)fe.Invoke(null, new object[] { a, b, c, d, e[0], e[1], e[2], e[3] });
                if (se != 2) { decidedE++; if (se != -Orient3dExact(x[0], x[1], x[2], x[3]).Sign) wrongE++; }
            }
            Report("EB-4 orient3d_filtered " + range, decided, n, wrong, double.NaN);
            Report("EB-5 orient3d_filtered_err " + range, decidedE, n, wrongE, double.NaN);
        }
    }

    static void PlaneSides(int n)
    {
        var fp = M("ExactMeshBoolean", "plane_side_filtered");
        var ff = M("ExactMeshBoolean", "front_side_filtered");
        var fa = M("ExactMeshBoolean", "plane_side_abs");
        foreach (var range in new[] { (-30, 30), (-150, -140), (-299, -290), (140, 160) }) {
            long dp = 0, wp = 0, df = 0, wf = 0, da = 0, wa = 0;
            for (int i = 0; i < n; ++i) {
                Vector3d nrm = RndV(range.Item1, range.Item2), g = RndV(range.Item1, range.Item2);
                // x nearly on the plane n . (x - g) = 0
                Vector3d t = RndV(range.Item1, range.Item2);
                Vector3d x = g + (t - nrm * (t.Dot(nrm) / nrm.Dot(nrm)));
                if (rng.Next(2) == 0) x.x = Math.BitIncrement(x.x);
                double dd = nrm.Dot(g);
                double[] pa = { nrm.x, nrm.y, nrm.z, dd };
                Dy[] N = NearV(nrm), X = NearV(x), G = NearV(g);
                Dy D = Near(dd);
                int s = (int)fp.Invoke(null, new object[] { pa, x.x, x.y, x.z });
                if (s != 2) { dp++; if (s != (N[0] * X[0] + N[1] * X[1] + N[2] * X[2] - D).Sign) wp++; }
                s = (int)ff.Invoke(null, new object[] { pa, x.x, x.y, x.z, g.x, g.y, g.z });
                if (s != 2) { df++; if (s != (N[0] * (X[0] - G[0]) + N[1] * (X[1] - G[1]) + N[2] * (X[2] - G[2])).Sign) wf++; }
                double e = Math.Abs(Rnd(range.Item1 - 60, range.Item1 - 40));
                Dy[] XA = { Within(x.x, e), Within(x.y, e), Within(x.z, e) };
                s = (int)fa.Invoke(null, new object[] { pa, x, e });
                if (s != 2) { da++; if (s != (N[0] * XA[0] + N[1] * XA[1] + N[2] * XA[2] - D).Sign) wa++; }
            }
            Report("EB-2 plane_side_filtered " + range, dp, n, wp, double.NaN);
            Report("EB-1 front_side_filtered " + range, df, n, wf, double.NaN);
            Report("EB-3 plane_side_abs " + range, da, n, wa, double.NaN);
        }
    }

    static void Certified(int n)
    {
        Type t = Lib.GetType("g3.CertifiedDet3", true);
        var f = t.GetMethod("Compute", Any);
        FieldInfo det = t.GetField("Det", Any), eb = t.GetField("E", Any);
        foreach (var range in new[] { (-30, 30), (-360, -330), (-1074, -1040), (300, 335) }) {
            long decided = 0, wrong = 0;
            double worst = 0;
            for (int i = 0; i < n; ++i) {
                Vector3d a = RndV(range.Item1, range.Item2), b = RndV(range.Item1, range.Item2), c = RndV(range.Item1, range.Item2);
                Vector3d d = NearPlane(a, b, c);
                object r = f.Invoke(null, new object[] { d - a, b - a, c - a });
                double dv = (double)det.GetValue(r), e = (double)eb.GetValue(r);
                if (double.IsInfinity(e)) continue;
                decided++;
                // Compute(r1, r2, r3) = r1 . (r2 x r3): w = d - a, u = b - a, v = c - a
                Dy err = (Orient3dExact(V(a), V(b), V(c), V(d)) - dv).Abs();
                if (Dy.Compare(err, e) > 0) wrong++;
                worst = Math.Max(worst, Ratio(err, e));
            }
            Report("CD-1 CertifiedDet3 " + range, decided, n, wrong, worst);
        }
    }

    static void Arrangement(int n)
    {
        var o2 = M("ExactMeshArrangement", "orient2d_approx");
        var ic = M("ExactMeshArrangement", "incircle_approx");
        var al = M("ExactMeshArrangement", "along_approx");
        var pv = M("ExactMeshArrangement", "plane_value");
        var ok = M("ExactMeshArrangement", "order_key");
        foreach (var range in new[] { (-10, 40), (-299, -270), (-520, -500), (60, 70) }) {
            long w = 0, cnt = 0;
            double[] worst = new double[5];
            for (int i = 0; i < n; ++i) {
                Vector3d a = RndV(range.Item1, range.Item2), b = RndV(range.Item1, range.Item2);
                Vector3d c = a + (rng.NextDouble() * 3 - 1) * (b - a), d = RndV(range.Item1, range.Item2);
                if (rng.Next(2) == 0) c.y = Math.BitIncrement(c.y);
                double e = rng.Next(3) == 0 ? 0 : Math.Abs(Rnd(range.Item1 - 70, range.Item1 - 30));
                Dy[] A = { Within(a.x, e), Within(a.y, e), Within(a.z, e) }, B = { Within(b.x, e), Within(b.y, e), Within(b.z, e) };
                Dy[] C = { Within(c.x, e), Within(c.y, e), Within(c.z, e) }, D = { Within(d.x, e), Within(d.y, e), Within(d.z, e) };
                object[] args = { a, b, c, 0, 1, e, 0.0 };
                double det = (double)o2.Invoke(null, args), err = (double)args[6];
                if (!double.IsInfinity(err) && !double.IsNaN(err)) {
                    cnt++;
                    Dy x = (((B[0] - A[0]) * (C[1] - A[1]) - (B[1] - A[1]) * (C[0] - A[0])) - det).Abs();
                    if (Dy.Compare(x, err) > 0) w++;
                    worst[0] = Math.Max(worst[0], Ratio(x, err));
                }
                args = new object[] { a, b, d, c, 0, 1, e, 0.0 };
                det = (double)ic.Invoke(null, args); err = (double)args[7];
                if (!double.IsInfinity(err) && !double.IsNaN(err)) {
                    Dy adx = A[0] - C[0], ady = A[1] - C[1], bdx = B[0] - C[0], bdy = B[1] - C[1], cdx = D[0] - C[0], cdy = D[1] - C[1];
                    Dy x = ((adx * adx + ady * ady) * (bdx * cdy - cdx * bdy) + (bdx * bdx + bdy * bdy) * (cdx * ady - adx * cdy)
                        + (cdx * cdx + cdy * cdy) * (adx * bdy - bdx * ady) - det).Abs();
                    if (Dy.Compare(x, err) > 0) w++;
                    worst[1] = Math.Max(worst[1], Ratio(x, err));
                }
                Vector3d fd = RndV(0, 60);
                Dy[] FD = NearV(fd);
                args = new object[] { fd.x, fd.y, fd.z, a, b, e, 0.0 };
                det = (double)al.Invoke(null, args); err = (double)args[6];
                if (!double.IsInfinity(err) && !double.IsNaN(err)) {
                    Dy x = (FD[0] * (A[0] - B[0]) + FD[1] * (A[1] - B[1]) + FD[2] * (A[2] - B[2]) - det).Abs();
                    if (Dy.Compare(x, err) > 0) w++;
                    worst[2] = Math.Max(worst[2], Ratio(x, err));
                }
                double dd = fd.Dot(a);
                double[] pa = { fd.x, fd.y, fd.z, dd };
                Dy DD = Near(dd);
                args = new object[] { pa, 0, a, e, 0.0 };
                det = (double)pv.Invoke(null, args); err = (double)args[4];
                if (!double.IsInfinity(err) && !double.IsNaN(err)) {
                    Dy x = (FD[0] * A[0] + FD[1] * A[1] + FD[2] * A[2] - DD - det).Abs();
                    if (Dy.Compare(x, err) > 0) w++;
                    worst[3] = Math.Max(worst[3], Ratio(x, err));
                }
                args = new object[] { a, e, 0.0 };
                det = (double)ok.Invoke(null, args); err = (double)args[2];
                if (!double.IsInfinity(err) && !double.IsNaN(err)) {
                    Dy x = ((Dy)1.0 * A[0] + (Dy)0.7548776662466927 * A[1] + (Dy)0.5698402909980532 * A[2] - det).Abs();
                    if (Dy.Compare(x, err) > 0) w++;
                    worst[4] = Math.Max(worst[4], Ratio(x, err));
                }
            }
            Report("AR-6/12/13/4/8 arrangement " + range, cnt, n, w, worst.Max(), "orient2d " + worst[0].ToString("G3") + " incircle " + worst[1].ToString("G3")
                + " along " + worst[2].ToString("G3") + " plane_value " + worst[3].ToString("G3") + " order_key " + worst[4].ToString("G3"));
        }
    }

    // ---- the findings: reproducers ----

    static void Findings()
    {
        Console.WriteLine();
        Console.WriteLine("Findings (reproducers):");

        // F1: negative_volume without an underflow guard. One triangle and the reference vertex o = its first corner
        // give a zero term; two triangles at scale 2^-358: the products of three differences
        // are below the subnormal range, the bound (32 + n) u S underflows to 0, and v6 < -0 decides.
        var nv = M("ExactMeshBoolean", "negative_volume");
        int found = 0;
        for (int trial = 0; trial < 200000 && found < 1; ++trial) {
            var mesh = new DMesh3();
            var p = new Vector3d[5];
            for (int k = 0; k < 5; ++k) p[k] = RndV(-359, -357);
            for (int k = 0; k < 5; ++k) mesh.AppendVertex(p[k]);
            int t0 = mesh.AppendTriangle(0, 1, 2), t1 = mesh.AppendTriangle(1, 3, 4);
            if (t0 < 0 || t1 < 0) continue;
            var comp = new List<int> { t0, t1 };
            bool neg = (bool)nv.Invoke(null, new object[] { mesh, comp });
            // exact v6 with o = vertex a of the first triangle (= p[0]): the first term is 0
            Dy v6 = (Dy)0.0;
            foreach (int t in comp) {
                Index3i tri = mesh.GetTriangle(t);
                Dy[] o = V(p[0]), a = V(p[tri.a]), b = V(p[tri.b]), c = V(p[tri.c]);
                // x . (y x z), x = a - o, y = b - o, z = c - o
                v6 = v6 + Det3(b[0] - o[0], b[1] - o[1], b[2] - o[2], c[0] - o[0], c[1] - o[1], c[2] - o[2], a[0] - o[0], a[1] - o[1], a[2] - o[2]);
            }
            if (neg && v6.Sign >= 0) {
                found++;
                Console.WriteLine("F1 negative_volume: 'certainly negative' for an exact v6 with sign {0}; vertices:", v6.Sign);
                for (int k = 0; k < 5; ++k) Console.WriteLine("     ({0:R}, {1:R}, {2:R})", p[k].x, p[k].y, p[k].z);
                Console.WriteLine("     triangles (0, 1, 2), (1, 3, 4)");
            }
        }
        if (found == 0) Console.WriteLine("F1 negative_volume: no reproducer found in this run");

        // F2: SecondPoint's clamp of the exponent
        Vector3d origin = new Vector3d(Math.ScaleB(1.0, 1000), 0, 0), dir = new Vector3d(double.Epsilon, 0, 0);
        Vector3d sp = (Vector3d)M("ExactRayTriangle", "SecondPoint").Invoke(null, new object[] { origin, dir });
        Console.WriteLine("F2 SecondPoint(origin = (2^1000, 0, 0), direction = (2^-1074, 0, 0)) == origin: {0} (a finite, nonzero direction; documented: origin only for a zero direction or a value that is not finite)", sp == origin);

        // F3: same_ray's dot product underflows to 0 for collinear points on one ray
        var sr = M("ExactTriangleIntersection", "same_ray");
        Vector2d s = Vector2d.Zero, a2 = new Vector2d(1e-170, 0), b2 = new Vector2d(2e-170, 0);
        Console.WriteLine("F3 same_ray(s = 0, a = (1e-170, 0), b = (2e-170, 0)) = {0} (a and b are on one ray from s; scaled by 2^600 it is {1})",
            sr.Invoke(null, new object[] { s, a2, b2 }), sr.Invoke(null, new object[] { s, a2 * Math.ScaleB(1, 600), b2 * Math.ScaleB(1, 600) }));

        // F4: boundary_bound's Total, a plain sum of n lengths rounded up by 1e-15 each, against the exact sum
        var bb = M("ExactMeshBoolean", "boundary_bound");
        foreach (int count in new[] { 1000, 100000, 1000000 }) {
            var g = new Vector3d[2 * (count + 1)];
            g[0] = Vector3d.Zero; g[1] = new Vector3d(1, 0, 0);
            // segments on the x axis of length just above 1.5 ulp(1): each addition to a sum in [1, 2) rounds down by about half an ulp
            double len = 1.4999 * Math.ScaleB(1.0, -52);
            for (int k = 1; k <= count; ++k) { g[2 * k] = new Vector3d(2, 0, 0); g[2 * k + 1] = new Vector3d(2 + 0, len, 0); }
            object b = bb.Invoke(null, new object[] { g });
            double total = (double)b.GetType().GetField("Total", Any).GetValue(b);
            double[] lengths = (double[])b.GetType().GetField("Lengths", Any).GetValue(b);
            Dy exact = (Dy)1.0;
            // the exact sum of the true lengths (1 and count times len: every segment is axis-parallel)
            Dy sum = (Dy)len * (Dy)(double)count + exact;
            double rel = Dy.Quotient(sum - total, total);
            Console.WriteLine("F4 boundary_bound, 1 + {0} short segments: Total = {1:R}, exact sum of the lengths - Total = {2:G4} * Total (the callers' margin: 1e-12)", count, total, rel);
        }
    }

    static int Main(string[] args)
    {
        int n = args.Length > 0 ? int.Parse(args[0]) : 20000;
        rng = new Random(args.Length > 1 ? int.Parse(args[1]) : 1);
        Orient3dFilter(n);
        PublicPredicates(n / 4);
        BooleanOrient(n);
        PlaneSides(n);
        Certified(n);
        Arrangement(n);
        Findings();
        Console.WriteLine(failures == 0 ? "\nno wrong decision in the bounds the audit calls sound" : "\nFAILED: " + failures + " checks with wrong decisions");
        return failures == 0 ? 0 : 1;
    }
}
