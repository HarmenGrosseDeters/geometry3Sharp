// Micro benchmark for the coordinator's question on #719 (overflow-safe norms in antipodal_value): the function of
// master, copied, with the range check in two forms. micro norm [segments] [points] [reps]
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using g3;

static class NormBench
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static double SafeLength(double vx, double vy, double vz)
    {
        double m = Math.Max(Math.Abs(vx), Math.Max(Math.Abs(vy), Math.Abs(vz)));
        if (m == 0 || double.IsInfinity(m)) return m;
        double x = vx / m, y = vy / m, z = vz / m;
        return m * Math.Sqrt(x * x + y * y + z * z);
    }
        public static int Base(Vector3d p, double pm, Vector3d x1, int chi, Vector3d[] segments, out double w, out double bound,
            out double roundingBound, double pointError, double directionError, double maxBound)
        {
            w = 0.5;
            bound = 0;
            roundingBound = 0;
            double sum = 0, err = 0, abs = 0, errRounding = 0;
            for (int k = 0; k < segments.Length; k += 2) {
                Vector3d a = segments[k] - p, b = segments[k + 1] - p;
                double la = a.Length, lb = b.Length;
                // the error of the unit vectors: the rounding of p and of the ends (relative 2^-52 of their L1 size, as
                // a distance), divided by the distance, plus the normalization; num and den are sums of a few products
                // of unit vectors, so their errors are a small multiple of it (taken generously: 16 times)
                // (pointError: a further error of p as a distance, directionError: of x1, both 0 for an exact point's
                // rounding alone, L2's approximated centroids give them)
                double ea = (2.3e-16 * (pm + Math.Abs(segments[k].x) + Math.Abs(segments[k].y) + Math.Abs(segments[k].z)) + pointError) / la;
                double eb = (2.3e-16 * (pm + Math.Abs(segments[k + 1].x) + Math.Abs(segments[k + 1].y) + Math.Abs(segments[k + 1].z)) + pointError) / lb;
                double tol = 16 * (ea + eb + 1e-15 + directionError);
                double tolRounding = pointError == 0 && directionError == 0 ? tol
                    : 16 * (2.3e-16 * (pm + Math.Abs(segments[k].x) + Math.Abs(segments[k].y) + Math.Abs(segments[k].z)) / la
                        + 2.3e-16 * (pm + Math.Abs(segments[k + 1].x) + Math.Abs(segments[k + 1].y) + Math.Abs(segments[k + 1].z)) / lb + 1e-15);
                // (the point on or next to an end of the segment)
                if (!(tol < 0.01))
                    return 1;
                a /= la;
                b /= lb;
                // (a and b opposite: the point on or next to the segment)
                if (1 + a.Dot(b) <= tol)
                    return 1;
                double num = a.Dot(b.Cross(x1)), den = 1 + a.Dot(b) + a.Dot(x1) + b.Dot(x1);
                // the branch cut of 2 atan2(num, den) is num = 0, den < 0 (the ray passes through the segment, where the
                // exact crossing count and the rounded sum could disagree by 1), and both near 0 is the singular point:
                // within the error, another ray
                if (Math.Abs(num) <= tol && den <= tol)
                    return 2;
                // (ExactMeshBoolean.portable_atan2, not Math.Atan2: the runtime's atan2 differs by an ulp between runtimes, and a value
                // next to the edge of the snap could then be decided differently)
                // the error of the term: a perturbation of (den, num) by at most sqrt(2) tol turns its angle by at most
                // arcsin(sqrt(2) tol / hypot), below 1.0001 sqrt(2) tol / hypot while that ratio is below 0.02, so
                // twice the angle stays below 4 tol / hypot (the limit maxBound, at most 1e-4, on the whole sum keeps every
                // tol / hypot below pi 1e-4); num and den both small, the ray nearly through an end of the segment, makes
                // the term ill-conditioned and the bound large, so another ray is taken there
                double hypot = Math.Sqrt(num * num + den * den);
                double termError = 4 * tol / hypot;
                errRounding += 4 * tolRounding / hypot + 2 * ExactMeshBoolean.PortableAtan2Error;
                double theta = 2 * ExactMeshBoolean.portable_atan2(num, den);
                sum += theta;
                abs += Math.Abs(theta);
                // plus twice the error of ExactMeshBoolean.portable_atan2 (ExactMeshBoolean.PortableAtan2Error)
                err += termError + 2 * ExactMeshBoolean.PortableAtan2Error;
            }
            // (the recursive summation: at most n u times the sum of the absolute terms)
            err += 2.3e-16 * (segments.Length / 2) * abs;
            w = chi + sum / MathUtil.FourPI;
            // (and the roundings of the division by 4 pi (also of 4 pi itself) and of the addition of chi)
            bound = err / MathUtil.FourPI + 2.3e-16 * (abs / MathUtil.FourPI + Math.Abs(w));
            roundingBound = (errRounding + 2.3e-16 * (segments.Length / 2) * abs) / MathUtil.FourPI + 2.3e-16 * (abs / MathUtil.FourPI + Math.Abs(w));
            // (a bound above 1e-6 would make the snap a guess: another ray; the bound is about 1e-13 per segment for a
            // point not next to one)
            if (!(bound <= maxBound))
                return 2;
            return 0;
        }

        public static int SquaredCheck(Vector3d p, double pm, Vector3d x1, int chi, Vector3d[] segments, out double w, out double bound,
            out double roundingBound, double pointError, double directionError, double maxBound)
        {
            w = 0.5;
            bound = 0;
            roundingBound = 0;
            double sum = 0, err = 0, abs = 0, errRounding = 0;
            for (int k = 0; k < segments.Length; k += 2) {
                Vector3d a = segments[k] - p, b = segments[k + 1] - p;
                double sa = a.x * a.x + a.y * a.y + a.z * a.z, sb = b.x * b.x + b.y * b.y + b.z * b.z;
                double la, lb;
                if (sa >= 1e-200 && sa <= 1e300 && sb >= 1e-200 && sb <= 1e300) { la = Math.Sqrt(sa); lb = Math.Sqrt(sb); }
                else { la = SafeLength(a.x, a.y, a.z); lb = SafeLength(b.x, b.y, b.z); }
                // the error of the unit vectors: the rounding of p and of the ends (relative 2^-52 of their L1 size, as
                // a distance), divided by the distance, plus the normalization; num and den are sums of a few products
                // of unit vectors, so their errors are a small multiple of it (taken generously: 16 times)
                // (pointError: a further error of p as a distance, directionError: of x1, both 0 for an exact point's
                // rounding alone, L2's approximated centroids give them)
                double ea = (2.3e-16 * (pm + Math.Abs(segments[k].x) + Math.Abs(segments[k].y) + Math.Abs(segments[k].z)) + pointError) / la;
                double eb = (2.3e-16 * (pm + Math.Abs(segments[k + 1].x) + Math.Abs(segments[k + 1].y) + Math.Abs(segments[k + 1].z)) + pointError) / lb;
                double tol = 16 * (ea + eb + 1e-15 + directionError);
                double tolRounding = pointError == 0 && directionError == 0 ? tol
                    : 16 * (2.3e-16 * (pm + Math.Abs(segments[k].x) + Math.Abs(segments[k].y) + Math.Abs(segments[k].z)) / la
                        + 2.3e-16 * (pm + Math.Abs(segments[k + 1].x) + Math.Abs(segments[k + 1].y) + Math.Abs(segments[k + 1].z)) / lb + 1e-15);
                // (the point on or next to an end of the segment)
                if (!(tol < 0.01))
                    return 1;
                a /= la;
                b /= lb;
                // (a and b opposite: the point on or next to the segment)
                if (1 + a.Dot(b) <= tol)
                    return 1;
                double num = a.Dot(b.Cross(x1)), den = 1 + a.Dot(b) + a.Dot(x1) + b.Dot(x1);
                // the branch cut of 2 atan2(num, den) is num = 0, den < 0 (the ray passes through the segment, where the
                // exact crossing count and the rounded sum could disagree by 1), and both near 0 is the singular point:
                // within the error, another ray
                if (Math.Abs(num) <= tol && den <= tol)
                    return 2;
                // (ExactMeshBoolean.portable_atan2, not Math.Atan2: the runtime's atan2 differs by an ulp between runtimes, and a value
                // next to the edge of the snap could then be decided differently)
                // the error of the term: a perturbation of (den, num) by at most sqrt(2) tol turns its angle by at most
                // arcsin(sqrt(2) tol / hypot), below 1.0001 sqrt(2) tol / hypot while that ratio is below 0.02, so
                // twice the angle stays below 4 tol / hypot (the limit maxBound, at most 1e-4, on the whole sum keeps every
                // tol / hypot below pi 1e-4); num and den both small, the ray nearly through an end of the segment, makes
                // the term ill-conditioned and the bound large, so another ray is taken there
                double hypot = Math.Sqrt(num * num + den * den);
                double termError = 4 * tol / hypot;
                errRounding += 4 * tolRounding / hypot + 2 * ExactMeshBoolean.PortableAtan2Error;
                double theta = 2 * ExactMeshBoolean.portable_atan2(num, den);
                sum += theta;
                abs += Math.Abs(theta);
                // plus twice the error of ExactMeshBoolean.portable_atan2 (ExactMeshBoolean.PortableAtan2Error)
                err += termError + 2 * ExactMeshBoolean.PortableAtan2Error;
            }
            // (the recursive summation: at most n u times the sum of the absolute terms)
            err += 2.3e-16 * (segments.Length / 2) * abs;
            w = chi + sum / MathUtil.FourPI;
            // (and the roundings of the division by 4 pi (also of 4 pi itself) and of the addition of chi)
            bound = err / MathUtil.FourPI + 2.3e-16 * (abs / MathUtil.FourPI + Math.Abs(w));
            roundingBound = (errRounding + 2.3e-16 * (segments.Length / 2) * abs) / MathUtil.FourPI + 2.3e-16 * (abs / MathUtil.FourPI + Math.Abs(w));
            // (a bound above 1e-6 would make the snap a guess: another ray; the bound is about 1e-13 per segment for a
            // point not next to one)
            if (!(bound <= maxBound))
                return 2;
            return 0;
        }

        public static int NormWindow(Vector3d p, double pm, Vector3d x1, int chi, Vector3d[] segments, out double w, out double bound,
            out double roundingBound, double pointError, double directionError, double maxBound)
        {
            w = 0.5;
            bound = 0;
            roundingBound = 0;
            double sum = 0, err = 0, abs = 0, errRounding = 0;
            for (int k = 0; k < segments.Length; k += 2) {
                Vector3d a = segments[k] - p, b = segments[k + 1] - p;
                double la = a.Length, lb = b.Length;
                if (!(la >= 1e-150 && la <= 1e150)) la = SafeLength(a.x, a.y, a.z);
                if (!(lb >= 1e-150 && lb <= 1e150)) lb = SafeLength(b.x, b.y, b.z);
                // the error of the unit vectors: the rounding of p and of the ends (relative 2^-52 of their L1 size, as
                // a distance), divided by the distance, plus the normalization; num and den are sums of a few products
                // of unit vectors, so their errors are a small multiple of it (taken generously: 16 times)
                // (pointError: a further error of p as a distance, directionError: of x1, both 0 for an exact point's
                // rounding alone, L2's approximated centroids give them)
                double ea = (2.3e-16 * (pm + Math.Abs(segments[k].x) + Math.Abs(segments[k].y) + Math.Abs(segments[k].z)) + pointError) / la;
                double eb = (2.3e-16 * (pm + Math.Abs(segments[k + 1].x) + Math.Abs(segments[k + 1].y) + Math.Abs(segments[k + 1].z)) + pointError) / lb;
                double tol = 16 * (ea + eb + 1e-15 + directionError);
                double tolRounding = pointError == 0 && directionError == 0 ? tol
                    : 16 * (2.3e-16 * (pm + Math.Abs(segments[k].x) + Math.Abs(segments[k].y) + Math.Abs(segments[k].z)) / la
                        + 2.3e-16 * (pm + Math.Abs(segments[k + 1].x) + Math.Abs(segments[k + 1].y) + Math.Abs(segments[k + 1].z)) / lb + 1e-15);
                // (the point on or next to an end of the segment)
                if (!(tol < 0.01))
                    return 1;
                a /= la;
                b /= lb;
                // (a and b opposite: the point on or next to the segment)
                if (1 + a.Dot(b) <= tol)
                    return 1;
                double num = a.Dot(b.Cross(x1)), den = 1 + a.Dot(b) + a.Dot(x1) + b.Dot(x1);
                // the branch cut of 2 atan2(num, den) is num = 0, den < 0 (the ray passes through the segment, where the
                // exact crossing count and the rounded sum could disagree by 1), and both near 0 is the singular point:
                // within the error, another ray
                if (Math.Abs(num) <= tol && den <= tol)
                    return 2;
                // (ExactMeshBoolean.portable_atan2, not Math.Atan2: the runtime's atan2 differs by an ulp between runtimes, and a value
                // next to the edge of the snap could then be decided differently)
                // the error of the term: a perturbation of (den, num) by at most sqrt(2) tol turns its angle by at most
                // arcsin(sqrt(2) tol / hypot), below 1.0001 sqrt(2) tol / hypot while that ratio is below 0.02, so
                // twice the angle stays below 4 tol / hypot (the limit maxBound, at most 1e-4, on the whole sum keeps every
                // tol / hypot below pi 1e-4); num and den both small, the ray nearly through an end of the segment, makes
                // the term ill-conditioned and the bound large, so another ray is taken there
                double hypot = Math.Sqrt(num * num + den * den);
                double termError = 4 * tol / hypot;
                errRounding += 4 * tolRounding / hypot + 2 * ExactMeshBoolean.PortableAtan2Error;
                double theta = 2 * ExactMeshBoolean.portable_atan2(num, den);
                sum += theta;
                abs += Math.Abs(theta);
                // plus twice the error of ExactMeshBoolean.portable_atan2 (ExactMeshBoolean.PortableAtan2Error)
                err += termError + 2 * ExactMeshBoolean.PortableAtan2Error;
            }
            // (the recursive summation: at most n u times the sum of the absolute terms)
            err += 2.3e-16 * (segments.Length / 2) * abs;
            w = chi + sum / MathUtil.FourPI;
            // (and the roundings of the division by 4 pi (also of 4 pi itself) and of the addition of chi)
            bound = err / MathUtil.FourPI + 2.3e-16 * (abs / MathUtil.FourPI + Math.Abs(w));
            roundingBound = (errRounding + 2.3e-16 * (segments.Length / 2) * abs) / MathUtil.FourPI + 2.3e-16 * (abs / MathUtil.FourPI + Math.Abs(w));
            // (a bound above 1e-6 would make the snap a guess: another ray; the bound is about 1e-13 per segment for a
            // point not next to one)
            if (!(bound <= maxBound))
                return 2;
            return 0;
        }


    delegate int F(Vector3d p, double pm, Vector3d x1, int chi, Vector3d[] segments, out double w, out double bound, out double rb, double pe, double de, double mb);

    public static int Run(string[] args)
    {
        int nseg = args.Length > 0 ? int.Parse(args[0]) : 2000, npts = args.Length > 1 ? int.Parse(args[1]) : 2000, reps = args.Length > 2 ? int.Parse(args[2]) : 7;
        var rnd = new Random(7);
        // a closed boundary polyline on a wavy circle of radius 1000 (grid-like magnitudes), points inside a box
        var seg = new Vector3d[2 * nseg];
        for (int k = 0; k < nseg; ++k) {
            double t0 = 2 * Math.PI * k / nseg, t1 = 2 * Math.PI * (k + 1) / nseg;
            seg[2 * k] = new Vector3d(1000 * Math.Cos(t0), 1000 * Math.Sin(t0), 50 * Math.Sin(7 * t0));
            seg[2 * k + 1] = new Vector3d(1000 * Math.Cos(t1), 1000 * Math.Sin(t1), 50 * Math.Sin(7 * t1));
        }
        var pts = new Vector3d[npts];
        for (int i = 0; i < npts; ++i) pts[i] = new Vector3d(600 * (rnd.NextDouble() - 0.5), 600 * (rnd.NextDouble() - 0.5), 400 * (rnd.NextDouble() - 0.5) + 300);
        Vector3d x1 = new Vector3d(0.3, 0.5, 0.81).Normalized;
        var fs = new (string, F)[] { ("library (master c0f8aa9 worktree)", ExactMeshBoolean.antipodal_value), ("copy of master, unchanged", Base), ("squared norm in [1e-200, 1e300] before the sqrt", SquaredCheck), ("plan (a): norm outside [1e-150, 1e150] rescaled", NormWindow) };
        double[] best = new double[fs.Length]; double[] sums = new double[fs.Length];
        for (int i = 0; i < fs.Length; ++i) best[i] = double.MaxValue;
        for (int r = 0; r < reps + 1; ++r)
            for (int i = 0; i < fs.Length; ++i) {
                double acc = 0; int st = 0;
                long t0 = Stopwatch.GetTimestamp();
                for (int q = 0; q < npts; ++q) { double w, b, rb; st += fs[i].Item2(pts[q], 1500, x1, 0, seg, out w, out b, out rb, 0, 0, 1e-6); acc += w; }
                double ns = (Stopwatch.GetTimestamp() - t0) * 1e9 / Stopwatch.Frequency / ((double)npts * nseg);
                if (r > 0) best[i] = Math.Min(best[i], ns);
                sums[i] = acc + st;
            }
        Console.WriteLine("| variant | ns per segment term (best of " + reps + ") | checksum |");
        Console.WriteLine("|---|---|---|");
        for (int i = 0; i < fs.Length; ++i) Console.WriteLine($"| {fs[i].Item1} | {best[i]:F2} | {sums[i]:R} |");
        return 0;
    }
}
