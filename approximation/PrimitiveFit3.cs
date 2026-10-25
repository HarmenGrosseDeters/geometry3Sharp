// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Robust fit of a plane, sphere, cylinder or cone to points (eg scan points or mesh vertices), with
    /// optional normals. Two stages:
    ///   1. RANSAC (Fischler and Bolles, "Random sample consensus", CACM 1981) over minimal samples, with
    ///      the minimal sets of Schnabel, Wahl and Klein, "Efficient RANSAC for point-cloud shape detection"
    ///      (Computer Graphics Forum 2007): plane from 3 points, sphere from 4 points, cylinder from 2 points
    ///      with normals, cone from 3 points with normals. Candidates are scored with the truncated quadratic
    ///      loss of MSAC (Torr and Zisserman, "MLESAC", CVIU 2000); a point counts as inlier when it is within
    ///      InlierDistance and, if normals are known, its normal is within MaxNormalAngle of the surface
    ///      normal (unoriented). The number of samples adapts to the inlier ratio (Confidence).
    ///   2. Least squares on the inliers: orthogonal regression (PCA) for the plane, Levenberg-Marquardt on
    ///      the geometric (orthogonal) distances for the other primitives, then the inliers are selected again;
    ///      repeated until the inlier set no longer changes (at most RefineIterations).
    /// Without RANSAC (UseRansac = false) all points are fitted by least squares, starting from closed form
    /// estimates: the algebraic sphere fit (Kasa 1976), and for cylinder and cone the axis and apex from the
    /// normals (every cylinder normal is orthogonal to the axis; every tangent plane of a cone passes through
    /// the apex, and the directions from the apex make the same angle with the axis).
    /// Cylinder and cone need normals; if none are given they are estimated by PCA of the NormalNeighbors
    /// nearest points (Hoppe et al., "Surface reconstruction from unorganized points", SIGGRAPH 1992),
    /// iteratively reweighted (Cauchy weights of the plane distances) so that outliers among the neighbors
    /// do not tilt them; where the weighted neighborhood is not flat the normal is unknown, and such points
    /// are judged by their distance only.
    /// Deterministic for a given Seed.
    /// </summary>
    public class PrimitiveFit3
    {
        public enum Primitives { Plane, Sphere, Cylinder, Cone }
        public enum Results { NotComputed, Success, TooFewPoints, NoNormals, Failed }

        public Vector3d[] Points;
        /// <summary> optional unit normals per point (orientation does not matter; zero = unknown) </summary>
        public Vector3d[] Normals;

        public Primitives Primitive = Primitives.Plane;
        public bool UseRansac = true;
        /// <summary> inlier threshold on the distance to the surface; 0 = 1% of the bounding box diagonal </summary>
        public double InlierDistance = 0;
        /// <summary> inlier threshold on the angle between the point normal and the surface normal (degrees), if normals are known </summary>
        public double MaxNormalAngle = 30;
        public int MaxIterations = 10000;
        /// <summary> RANSAC stops when a better model is found with at most this probability </summary>
        public double Confidence = 0.9999;
        public int Seed = 1;
        public int RefineIterations = 5;
        /// <summary> estimate normals (if Normals is null) for cylinder and cone </summary>
        public bool EstimateNormals = true;
        public int NormalNeighbors = 12;

        // outputs
        public Results Result = Results.NotComputed;
        public Plane3d Plane;
        /// <summary> plane: centroid of the inliers </summary>
        public Vector3d PlaneOrigin;
        public Vector3d SphereCenter;
        public double SphereRadius;
        /// <summary> cylinder: axis origin at the middle of the inliers' extent along the axis, height = that extent </summary>
        public Cylinder3d Cylinder;
        /// <summary> cone: height = largest distance of an inlier along the axis </summary>
        public Cone3d Cone;
        public bool[] IsInlier;
        public int InlierCount;
        /// <summary> root mean square and largest distance of the inliers to the surface </summary>
        public double RMSError = double.NaN, MaxError = double.NaN;
        /// <summary> number of RANSAC samples drawn </summary>
        public int Iterations;


        public PrimitiveFit3(IEnumerable<Vector3d> points, IEnumerable<Vector3d> normals = null)
        {
            Points = new List<Vector3d>(points).ToArray();
            if (normals != null)
                Normals = new List<Vector3d>(normals).ToArray();
        }

        /// <summary> fit to the vertices of mesh (all, or the given ones), with the vertex normals </summary>
        public PrimitiveFit3(DMesh3 mesh, IEnumerable<int> vertices = null)
        {
            var pts = new List<Vector3d>();
            var nrm = new List<Vector3d>();
            foreach (int vid in (vertices ?? mesh.VertexIndices())) {
                if (mesh.IsVertex(vid) == false || mesh.GetVtxEdgeCount(vid) == 0) continue;
                pts.Add(mesh.GetVertex(vid));
                nrm.Add(MeshNormals.QuickCompute(mesh, vid));
            }
            Points = pts.ToArray();
            Normals = nrm.ToArray();
        }


        public bool Compute()
        {
            Result = Results.NotComputed;
            IsInlier = null; InlierCount = 0; RMSError = MaxError = double.NaN; Iterations = 0;
            int n = (Points == null) ? 0 : Points.Length;
            int minSample = minimal_sample_size(Primitive);
            if (n < Math.Max(minSample, least_squares_size(Primitive))) {
                Result = Results.TooFewPoints;
                return false;
            }
            AxisAlignedBox3d box = BoundsUtil.Bounds(Points);
            scale = Math.Max(box.DiagonalLength, MathUtil.Epsilon);
            eps = (InlierDistance > 0) ? InlierDistance : 0.01 * scale;
            cosNormal = Math.Cos(MathUtil.Clamp(MaxNormalAngle, 0, 90) * MathUtil.Deg2Rad);

            normals = Normals;
            if (normals != null && normals.Length != n)
                normals = null;
            bool needNormals = Primitive == Primitives.Cylinder || Primitive == Primitives.Cone;
            if (normals == null && needNormals) {
                if (EstimateNormals == false) {
                    Result = Results.NoNormals;
                    return false;
                }
                normals = estimate_normals(Points, Math.Max(3, NormalNeighbors));
            }
            if (normals != null) {
                Vector3d[] unit = new Vector3d[n];
                for (int i = 0; i < n; ++i) unit[i] = (normals[i].Length > 0) ? normals[i].Normalized : Vector3d.Zero;
                normals = unit;
            }

            Model model;
            bool[] inl;
            if (UseRansac) {
                if (ransac(out model) == false) {
                    Result = Results.Failed;
                    return false;
                }
                inl = inliers(model);
            } else {
                inl = new bool[n];
                for (int i = 0; i < n; ++i) inl[i] = true;
                if (initial_estimate(inl, out model) == false) {
                    Result = Results.Failed;
                    return false;
                }
            }

            for (int iter = 0; iter < Math.Max(1, RefineIterations); ++iter) {
                Model refined;
                if (least_squares(model, inl, out refined) == false)
                    break;
                model = refined;
                if (UseRansac == false)
                    break;
                bool[] next = inliers(model);
                bool same = true;
                for (int i = 0; i < n && same; ++i) same = (next[i] == inl[i]);
                if (count(next) < least_squares_size(Primitive))
                    break;
                inl = next;
                if (same) break;
            }

            set_outputs(model, inl);
            Result = Results.Success;
            return true;
        }


        /// <summary> signed distance of p to the fitted surface (negative inside / below the plane) </summary>
        public double SignedDistance(Vector3d p)
        {
            return distance(fitted, p);
        }

        /// <summary> unit normal of the fitted surface at its point nearest to p </summary>
        public Vector3d SurfaceNormal(Vector3d p)
        {
            return normal(fitted, p);
        }



        // a primitive: point a (plane point, sphere center, cylinder axis point, cone apex), unit direction
        // d (plane normal, axis), scalar r (sphere / cylinder radius, cone half angle)
        struct Model
        {
            public Vector3d a, d;
            public double r;
            public Model(Vector3d a, Vector3d d, double r) { this.a = a; this.d = d; this.r = r; }
        }

        Model fitted;
        double scale, eps, cosNormal;
        Vector3d[] normals;


        static int minimal_sample_size(Primitives p)
        {
            switch (p) {
                case Primitives.Plane: return 3;
                case Primitives.Sphere: return 4;
                case Primitives.Cylinder: return 2;
                default: return 3;
            }
        }

        // points needed for a determined least squares problem (parameters + 1)
        static int least_squares_size(Primitives p)
        {
            switch (p) {
                case Primitives.Plane: return 3;
                case Primitives.Sphere: return 5;
                case Primitives.Cylinder: return 6;
                default: return 7;
            }
        }


        double distance(Model m, Vector3d p)
        {
            switch (Primitive) {
                case Primitives.Plane:
                    return (p - m.a).Dot(m.d);
                case Primitives.Sphere:
                    return (p - m.a).Length - m.r;
                case Primitives.Cylinder: {
                        Vector3d w = p - m.a;
                        return (w - w.Dot(m.d) * m.d).Length - m.r;
                    }
                default: {
                        Vector3d w = p - m.a;
                        double h = w.Dot(m.d);
                        double rho = (w - h * m.d).Length;
                        double sin = Math.Sin(m.r), cos = Math.Cos(m.r);
                        if (h * cos + rho * sin < 0)
                            return w.Length;
                        return rho * cos - h * sin;
                    }
            }
        }

        Vector3d normal(Model m, Vector3d p)
        {
            switch (Primitive) {
                case Primitives.Plane:
                    return m.d;
                case Primitives.Sphere:
                    return (p - m.a).Normalized;
                case Primitives.Cylinder: {
                        Vector3d w = p - m.a;
                        return (w - w.Dot(m.d) * m.d).Normalized;
                    }
                default: {
                        Vector3d w = p - m.a;
                        Vector3d radial = (w - w.Dot(m.d) * m.d).Normalized;
                        return (Math.Cos(m.r) * radial - Math.Sin(m.r) * m.d).Normalized;
                    }
            }
        }

        bool is_inlier(Model m, int i, out double dist)
        {
            dist = distance(m, Points[i]);
            if (!(Math.Abs(dist) <= eps))
                return false;
            if (normals != null && normals[i] != Vector3d.Zero) {
                Vector3d sn = normal(m, Points[i]);
                if (Math.Abs(sn.Dot(normals[i])) < cosNormal)
                    return false;
            }
            return true;
        }

        bool[] inliers(Model m)
        {
            bool[] inl = new bool[Points.Length];
            double d;
            for (int i = 0; i < Points.Length; ++i)
                inl[i] = is_inlier(m, i, out d);
            return inl;
        }

        static int count(bool[] b)
        {
            int c = 0;
            foreach (bool x in b) if (x) c++;
            return c;
        }

        bool valid(Model m)
        {
            if (double.IsNaN(m.a.x + m.a.y + m.a.z + m.d.x + m.d.y + m.d.z + m.r) || double.IsInfinity(m.a.x + m.a.y + m.a.z + m.r))
                return false;
            if (Math.Abs(m.d.Length - 1) > 1e-6)
                return false;
            switch (Primitive) {
                case Primitives.Sphere:
                case Primitives.Cylinder:
                    return m.r > 0 && m.r < 100 * scale;
                case Primitives.Cone:
                    return m.r > 1e-3 && m.r < 0.5 * Math.PI - 1e-3 && (m.a - Points[0]).Length < 100 * scale;
                default:
                    return true;
            }
        }


        bool ransac(out Model best)
        {
            int n = Points.Length;
            int k = minimal_sample_size(Primitive);
            var rnd = new Random(Seed);
            int[] sample = new int[k];
            best = new Model();
            double bestCost = double.MaxValue;
            int bestCount = 0;
            long needed = MaxIterations;
            double eps2 = eps * eps;
            for (Iterations = 0; Iterations < Math.Min(needed, MaxIterations); ) {
                Iterations++;
                for (int j = 0; j < k; ++j) {
                    int s;
                    do { s = rnd.Next(n); } while (Array.IndexOf(sample, s, 0, j) >= 0);
                    sample[j] = s;
                }
                bool known = true;
                if (Primitive == Primitives.Cylinder || Primitive == Primitives.Cone)
                    for (int j = 0; j < k && known; ++j) known = normals[sample[j]] != Vector3d.Zero;
                Model m;
                if (known == false || minimal_model(sample, out m) == false || valid(m) == false)
                    continue;
                // the sample itself must be consistent (normals)
                bool consistent = true;
                double d;
                for (int j = 0; j < k && consistent; ++j)
                    consistent = is_inlier(m, sample[j], out d);
                if (consistent == false)
                    continue;

                double cost = 0;
                int cnt = 0;
                for (int i = 0; i < n && cost < bestCost; ++i) {
                    if (is_inlier(m, i, out d)) {
                        cost += d * d; cnt++;
                    } else cost += eps2;
                }
                if (cost < bestCost) {
                    bestCost = cost; best = m; bestCount = cnt;
                    double w = (double)cnt / n;
                    double pk = Math.Pow(w, k);
                    if (pk >= 1 - 1e-12)
                        needed = Iterations;
                    else if (pk > 0) {
                        // log(1 - pk) rounds to 0 for pk < 1e-16 (few inliers in a large cloud): use -pk.
                        // Confidence >= 1 or NaN: never confident enough. Compared as doubles, a cast of
                        // +-infinity or of more than long.MaxValue is undefined (long.MinValue on .NET Framework)
                        double l1 = (pk < 1e-8) ? -pk : Math.Log(1 - pk);
                        double iters = Math.Ceiling(Math.Log(1 - Confidence) / l1);
                        if (iters < MaxIterations)
                            needed = (long)iters;
                    }
                }
            }
            return bestCount >= least_squares_size(Primitive);
        }


        bool minimal_model(int[] s, out Model m)
        {
            m = new Model();
            switch (Primitive) {
                case Primitives.Plane: {
                        Vector3d p0 = Points[s[0]], p1 = Points[s[1]], p2 = Points[s[2]];
                        Vector3d nn = (p1 - p0).Cross(p2 - p0);
                        if (nn.Length < MathUtil.ZeroTolerance * scale * scale) return false;
                        m = new Model(p0, nn.Normalized, 0);
                        return true;
                    }
                case Primitives.Sphere: {
                        // 2 (p_i - p_0) . c = |p_i|^2 - |p_0|^2, relative to p_0 for accuracy
                        Vector3d p0 = Points[s[0]];
                        double[] A = new double[9], b = new double[3];
                        for (int i = 1; i < 4; ++i) {
                            Vector3d q = Points[s[i]] - p0;
                            A[3 * (i - 1)] = 2 * q.x; A[3 * (i - 1) + 1] = 2 * q.y; A[3 * (i - 1) + 2] = 2 * q.z;
                            b[i - 1] = q.LengthSquared;
                        }
                        double[] c;
                        if (solve(A, b, 3, out c) == false) return false;
                        Vector3d cc = new Vector3d(c[0], c[1], c[2]);
                        m = new Model(p0 + cc, Vector3d.AxisZ, cc.Length);
                        return true;
                    }
                case Primitives.Cylinder: {
                        Vector3d p1 = Points[s[0]], p2 = Points[s[1]], n1 = normals[s[0]], n2 = normals[s[1]];
                        Vector3d d = n1.Cross(n2);
                        if (d.Length < 1e-3) return false;
                        d.Normalize();
                        // closest points of the lines p1 + t n1 and p2 + u n2 (both orthogonal to d)
                        Vector3d c;
                        if (line_line(p1, n1, p2, n2, out c) == false) return false;
                        Vector3d w = p1 - c;
                        double r = (w - w.Dot(d) * d).Length;
                        m = new Model(c, d, r);
                        return true;
                    }
                default: {
                        // apex = intersection of the three tangent planes
                        double[] A = new double[9], b = new double[3];
                        for (int i = 0; i < 3; ++i) {
                            Vector3d nn = normals[s[i]];
                            A[3 * i] = nn.x; A[3 * i + 1] = nn.y; A[3 * i + 2] = nn.z;
                            b[i] = nn.Dot(Points[s[i]] - Points[s[0]]);
                        }
                        double[] x;
                        if (solve(A, b, 3, out x) == false) return false;
                        Vector3d apex = Points[s[0]] + new Vector3d(x[0], x[1], x[2]);
                        // the unit directions to the points lie on a circle around the axis
                        Vector3d u0 = (Points[s[0]] - apex).Normalized, u1 = (Points[s[1]] - apex).Normalized, u2 = (Points[s[2]] - apex).Normalized;
                        Vector3d d = (u1 - u0).Cross(u2 - u0);
                        if (d.Length < MathUtil.ZeroTolerance) return false;
                        d.Normalize();
                        if (d.Dot(u0 + u1 + u2) < 0) d = -d;
                        double angle = (Math.Acos(MathUtil.Clamp(u0.Dot(d), -1, 1)) + Math.Acos(MathUtil.Clamp(u1.Dot(d), -1, 1))
                            + Math.Acos(MathUtil.Clamp(u2.Dot(d), -1, 1))) / 3;
                        m = new Model(apex, d, angle);
                        return true;
                    }
            }
        }


        // closed form estimate from the given points (no RANSAC)
        bool initial_estimate(bool[] inl, out Model m)
        {
            m = new Model();
            int n = Points.Length;
            switch (Primitive) {
                case Primitives.Plane:
                    return least_squares(m, inl, out m);
                case Primitives.Sphere: {
                        // Kasa: |p|^2 = 2 p.c + k, linear least squares relative to the centroid
                        Vector3d mean = centroid(inl);
                        double[] AtA = new double[16], Atb = new double[4];
                        for (int i = 0; i < n; ++i) {
                            if (!inl[i]) continue;
                            Vector3d q = Points[i] - mean;
                            double[] row = { 2 * q.x, 2 * q.y, 2 * q.z, 1 };
                            double rhs = q.LengthSquared;
                            for (int r = 0; r < 4; ++r) {
                                Atb[r] += row[r] * rhs;
                                for (int c = 0; c < 4; ++c) AtA[4 * r + c] += row[r] * row[c];
                            }
                        }
                        double[] x;
                        if (solve(AtA, Atb, 4, out x) == false) return false;
                        Vector3d cc = new Vector3d(x[0], x[1], x[2]);
                        double r2 = x[3] + cc.LengthSquared;
                        if (!(r2 > 0)) return false;
                        m = new Model(mean + cc, Vector3d.AxisZ, Math.Sqrt(r2));
                        return valid(m);
                    }
                case Primitives.Cylinder: {
                        // axis: the direction least present in the normals
                        double[] S = new double[9];
                        for (int i = 0; i < n; ++i) {
                            if (!inl[i]) continue;
                            add_outer(S, normals[i], normals[i]);
                        }
                        Vector3d d = smallest_eigenvector(S);
                        // circle fit (Kasa) in the plane orthogonal to the axis
                        Vector3d b1, b2;
                        Vector3d.MakePerpVectors(ref d, out b1, out b2);
                        Vector3d mean = centroid(inl);
                        double[] AtA = new double[9], Atb = new double[3];
                        for (int i = 0; i < n; ++i) {
                            if (!inl[i]) continue;
                            Vector3d q = Points[i] - mean;
                            double x = q.Dot(b1), y = q.Dot(b2);
                            double[] row = { 2 * x, 2 * y, 1 };
                            double rhs = x * x + y * y;
                            for (int r = 0; r < 3; ++r) {
                                Atb[r] += row[r] * rhs;
                                for (int c = 0; c < 3; ++c) AtA[3 * r + c] += row[r] * row[c];
                            }
                        }
                        double[] sol;
                        if (solve(AtA, Atb, 3, out sol) == false) return false;
                        double r2 = sol[2] + sol[0] * sol[0] + sol[1] * sol[1];
                        if (!(r2 > 0)) return false;
                        m = new Model(mean + sol[0] * b1 + sol[1] * b2, d, Math.Sqrt(r2));
                        return valid(m);
                    }
                default: {
                        // apex: least squares point on all tangent planes, sum n n^T (v - p) = 0
                        double[] S = new double[9], rhs = new double[3];
                        Vector3d mean = centroid(inl);
                        for (int i = 0; i < n; ++i) {
                            if (!inl[i]) continue;
                            Vector3d nn = normals[i];
                            add_outer(S, nn, nn);
                            double t = nn.Dot(Points[i] - mean);
                            rhs[0] += nn.x * t; rhs[1] += nn.y * t; rhs[2] += nn.z * t;
                        }
                        double[] x;
                        if (solve(S, rhs, 3, out x) == false) return false;
                        Vector3d apex = mean + new Vector3d(x[0], x[1], x[2]);
                        // axis: the unit directions from the apex have a constant component along it
                        Vector3d umean = Vector3d.Zero;
                        int cnt = 0;
                        for (int i = 0; i < n; ++i)
                            if (inl[i]) { umean += (Points[i] - apex).Normalized; cnt++; }
                        umean /= cnt;
                        double[] C = new double[9];
                        for (int i = 0; i < n; ++i) {
                            if (!inl[i]) continue;
                            Vector3d u = (Points[i] - apex).Normalized - umean;
                            add_outer(C, u, u);
                        }
                        Vector3d d = smallest_eigenvector(C);
                        if (d.Dot(umean) < 0) d = -d;
                        double angle = Math.Acos(MathUtil.Clamp(umean.Dot(d), -1, 1));
                        m = new Model(apex, d, angle);
                        return valid(m);
                    }
            }
        }


        // least squares fit to the inliers, from the start model
        bool least_squares(Model start, bool[] inl, out Model m)
        {
            m = start;
            int cnt = count(inl);
            if (cnt < least_squares_size(Primitive))
                return false;
            if (Primitive == Primitives.Plane) {
                var pts = new List<Vector3d>(cnt);
                for (int i = 0; i < Points.Length; ++i)
                    if (inl[i]) pts.Add(Points[i]);
                var fit = new OrthogonalPlaneFit3(pts);
                if (fit.ResultValid == false) return false;
                Vector3d nn = fit.Normal.Normalized;
                if (start.d != Vector3d.Zero && nn.Dot(start.d) < 0) nn = -nn;
                m = new Model(fit.Origin, nn, 0);
                return true;
            }

            int[] idx = new int[cnt];
            for (int i = 0, j = 0; i < Points.Length; ++i)
                if (inl[i]) idx[j++] = i;
            Model result;
            if (levenberg_marquardt(start, idx, out result) == false || valid(result) == false)
                return false;
            m = result;
            return true;
        }


        // the model for parameters x around the base model (local parameterization, x = 0 is base)
        Model local_model(Model b, Vector3d b1, Vector3d b2, double[] x)
        {
            switch (Primitive) {
                case Primitives.Sphere:
                    return new Model(b.a + new Vector3d(x[0], x[1], x[2]), b.d, b.r + x[3]);
                case Primitives.Cylinder:
                    return new Model(b.a + x[0] * b1 + x[1] * b2, (b.d + x[2] * b1 + x[3] * b2).Normalized, b.r + x[4]);
                default:
                    return new Model(b.a + x[0] * b1 + x[1] * b2 + x[2] * b.d, (b.d + x[3] * b1 + x[4] * b2).Normalized, b.r + x[5]);
            }
        }

        static int parameter_count(Primitives p)
        {
            return (p == Primitives.Sphere) ? 4 : (p == Primitives.Cylinder) ? 5 : 6;
        }

        // Levenberg-Marquardt on the orthogonal distances, numerical Jacobian
        bool levenberg_marquardt(Model start, int[] idx, out Model result)
        {
            int np = parameter_count(Primitive);
            int m = idx.Length;
            Model cur = start;
            double cost = 0;
            foreach (int i in idx) { double d = distance(cur, Points[i]); cost += d * d; }
            double lambda = 1e-3;
            double[] r = new double[m];
            double[] J = new double[m * np];
            for (int iter = 0; iter < 100; ++iter) {
                Vector3d b1, b2;
                Vector3d.MakePerpVectors(ref cur.d, out b1, out b2);
                double[] zero = new double[np];
                for (int k = 0; k < m; ++k) r[k] = distance(cur, Points[idx[k]]);
                for (int p = 0; p < np; ++p) {
                    // parameters are lengths, except the axis tilts and the cone angle
                    double h = (p_is_length(p) ? scale : 1) * 1e-7;
                    double[] xp = new double[np], xm = new double[np];
                    xp[p] = h; xm[p] = -h;
                    Model mp = local_model(cur, b1, b2, xp), mm = local_model(cur, b1, b2, xm);
                    for (int k = 0; k < m; ++k)
                        J[k * np + p] = (distance(mp, Points[idx[k]]) - distance(mm, Points[idx[k]])) / (2 * h);
                }
                double[] JtJ = new double[np * np], Jtr = new double[np];
                for (int k = 0; k < m; ++k) {
                    for (int a = 0; a < np; ++a) {
                        double ja = J[k * np + a];
                        Jtr[a] += ja * r[k];
                        for (int c = 0; c < np; ++c) JtJ[a * np + c] += ja * J[k * np + c];
                    }
                }
                bool improved = false;
                for (int attempt = 0; attempt < 20; ++attempt) {
                    double[] A = (double[])JtJ.Clone();
                    double[] g = new double[np];
                    for (int a = 0; a < np; ++a) {
                        A[a * np + a] += lambda * Math.Max(JtJ[a * np + a], 1e-12);
                        g[a] = -Jtr[a];
                    }
                    double[] dx;
                    if (solve(A, g, np, out dx)) {
                        Model cand = local_model(cur, b1, b2, dx);
                        if (valid(cand)) {
                            double c2 = 0;
                            foreach (int i in idx) { double d = distance(cand, Points[i]); c2 += d * d; }
                            if (c2 < cost) {
                                double rel = (cost - c2) / Math.Max(cost, 1e-300);
                                cur = cand; cost = c2;
                                lambda = Math.Max(lambda * 0.1, 1e-12);
                                improved = true;
                                if (rel < 1e-14) { result = cur; return true; }
                                break;
                            }
                        }
                    }
                    lambda *= 10;
                }
                if (improved == false)
                    break;
            }
            result = cur;
            return true;
        }

        bool p_is_length(int p)
        {
            switch (Primitive) {
                case Primitives.Sphere: return true;
                case Primitives.Cylinder: return p != 2 && p != 3;
                default: return p < 3;
            }
        }


        void set_outputs(Model m, bool[] inl)
        {
            fitted = m;
            IsInlier = inl;
            InlierCount = count(inl);
            double sum = 0, max = 0;
            double hmin = double.MaxValue, hmax = double.MinValue;
            Vector3d mean = Vector3d.Zero;
            for (int i = 0; i < Points.Length; ++i) {
                if (!inl[i]) continue;
                double d = distance(m, Points[i]);
                sum += d * d; max = Math.Max(max, Math.Abs(d));
                double h = (Points[i] - m.a).Dot(m.d);
                hmin = Math.Min(hmin, h); hmax = Math.Max(hmax, h);
                mean += Points[i];
            }
            if (InlierCount > 0) {
                RMSError = Math.Sqrt(sum / InlierCount);
                MaxError = max;
                mean /= InlierCount;
            }
            switch (Primitive) {
                case Primitives.Plane:
                    PlaneOrigin = mean;
                    Plane = new Plane3d(m.d, m.a);
                    break;
                case Primitives.Sphere:
                    SphereCenter = m.a; SphereRadius = m.r;
                    break;
                case Primitives.Cylinder:
                    Cylinder = new Cylinder3d(m.a + 0.5 * (hmin + hmax) * m.d, m.d, m.r, hmax - hmin);
                    break;
                default:
                    Cone = new Cone3d(m.a, m.d, m.r, hmax);
                    break;
            }
        }


        Vector3d centroid(bool[] inl)
        {
            Vector3d c = Vector3d.Zero;
            int cnt = 0;
            for (int i = 0; i < Points.Length; ++i)
                if (inl[i]) { c += Points[i]; cnt++; }
            return (cnt > 0) ? c / cnt : c;
        }

        static void add_outer(double[] S, Vector3d u, Vector3d v)
        {
            for (int r = 0; r < 3; ++r)
                for (int c = 0; c < 3; ++c)
                    S[3 * r + c] += u[r] * v[c];
        }

        static Vector3d smallest_eigenvector(double[] S)
        {
            var solver = new SymmetricEigenSolver(3, 4096);
            solver.Solve((double[])S.Clone(), SymmetricEigenSolver.SortType.Decreasing);
            return new Vector3d(solver.GetEigenvector(2)).Normalized;
        }

        // point closest to both lines p1 + t d1 and p2 + u d2 (midpoint of the closest points)
        static bool line_line(Vector3d p1, Vector3d d1, Vector3d p2, Vector3d d2, out Vector3d c)
        {
            c = Vector3d.Zero;
            Vector3d w = p1 - p2;
            double a = d1.Dot(d1), b = d1.Dot(d2), cc = d2.Dot(d2), d = d1.Dot(w), e = d2.Dot(w);
            double den = a * cc - b * b;
            if (Math.Abs(den) < 1e-12)
                return false;
            double t = (b * e - cc * d) / den, u = (a * e - b * d) / den;
            c = 0.5 * ((p1 + t * d1) + (p2 + u * d2));
            return true;
        }

        // Gaussian elimination with partial pivoting of the n x n system A x = b (row major)
        static bool solve(double[] A, double[] b, int n, out double[] x)
        {
            double[] M = (double[])A.Clone();
            x = (double[])b.Clone();
            double maxAbs = 0;
            foreach (double v in M) maxAbs = Math.Max(maxAbs, Math.Abs(v));
            if (!(maxAbs > 0))
                return false;
            for (int col = 0; col < n; ++col) {
                int piv = col;
                for (int r = col + 1; r < n; ++r)
                    if (Math.Abs(M[r * n + col]) > Math.Abs(M[piv * n + col])) piv = r;
                if (Math.Abs(M[piv * n + col]) <= 1e-14 * maxAbs)
                    return false;
                if (piv != col) {
                    for (int c = 0; c < n; ++c) { double t = M[col * n + c]; M[col * n + c] = M[piv * n + c]; M[piv * n + c] = t; }
                    double tb = x[col]; x[col] = x[piv]; x[piv] = tb;
                }
                for (int r = col + 1; r < n; ++r) {
                    double f = M[r * n + col] / M[col * n + col];
                    if (f == 0) continue;
                    for (int c = col; c < n; ++c) M[r * n + c] -= f * M[col * n + c];
                    x[r] -= f * x[col];
                }
            }
            for (int r = n - 1; r >= 0; --r) {
                double s = x[r];
                for (int c = r + 1; c < n; ++c) s -= M[r * n + c] * x[c];
                x[r] = s / M[r * n + r];
            }
            foreach (double v in x)
                if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            return true;
        }


        // normal of the plane through the neighborhood nb (nb[0] is the point itself): PCA, then iteratively
        // reweighted with Cauchy weights of the distances to the plane (scale: median absolute distance), so
        // that a few outliers among the neighbors do not tilt it. Zero (unknown) if the weighted points are
        // not flat (smallest eigenvalue not well below the middle one), eg at outliers or creases.
        static Vector3d robust_normal(Vector3d[] nb)
        {
            int m = nb.Length;
            double[] w = new double[m];
            for (int j = 0; j < m; ++j) w[j] = 1;
            Vector3d normal = Vector3d.Zero, mean = Vector3d.Zero;
            double[] evals = null;
            for (int iter = 0; iter < 5; ++iter) {
                double wsum = 0;
                mean = Vector3d.Zero;
                for (int j = 0; j < m; ++j) { mean += w[j] * nb[j]; wsum += w[j]; }
                mean /= wsum;
                double[] C = new double[9];
                for (int j = 0; j < m; ++j) {
                    Vector3d q = nb[j] - mean;
                    add_outer(C, w[j] * q, q);
                }
                var solver = new SymmetricEigenSolver(3, 4096);
                solver.Solve(C, SymmetricEigenSolver.SortType.Decreasing);
                normal = new Vector3d(solver.GetEigenvector(2)).Normalized;
                evals = solver.GetEigenvalues();
                double[] r = new double[m];
                for (int j = 0; j < m; ++j) r[j] = Math.Abs((nb[j] - mean).Dot(normal));
                double[] sorted = (double[])r.Clone();
                Array.Sort(sorted);
                double extent = Math.Sqrt(Math.Max(evals[0], 0));
                double sigma = Math.Max(1.4826 * sorted[m / 2], 1e-6 * extent);
                if (!(sigma > 0)) break;
                for (int j = 0; j < m; ++j) {
                    double u = r[j] / sigma;
                    w[j] = 1.0 / (1.0 + u * u);
                }
            }
            if (evals == null || !(evals[2] < 0.25 * evals[1]))
                return Vector3d.Zero;
            return normal;
        }

        // unoriented normals by robust PCA of the k nearest points (uniform grid search)
        static Vector3d[] estimate_normals(Vector3d[] pts, int k)
        {
            int n = pts.Length;
            k = Math.Min(k, n - 1);
            AxisAlignedBox3d box = BoundsUtil.Bounds(pts);
            double cell = Math.Max(box.MaxDim / Math.Max(1, Math.Pow(n / 4.0, 1.0 / 3.0)), MathUtil.Epsilon);
            var grid = new Dictionary<Vector3i, List<int>>();
            Func<Vector3d, Vector3i> key = (p) => new Vector3i((int)Math.Floor((p.x - box.Min.x) / cell),
                (int)Math.Floor((p.y - box.Min.y) / cell), (int)Math.Floor((p.z - box.Min.z) / cell));
            for (int i = 0; i < n; ++i) {
                Vector3i kk = key(pts[i]);
                List<int> list;
                if (grid.TryGetValue(kk, out list) == false) grid[kk] = list = new List<int>();
                list.Add(i);
            }
            int maxRing = (int)Math.Ceiling(box.MaxDim / cell) + 1;
            Vector3d[] result = new Vector3d[n];
            gParallel.ForEach(Interval1i.Range(n), (i) => {
                Vector3i c = key(pts[i]);
                var cand = new List<KeyValuePair<double, int>>();
                for (int ring = 0; ring <= maxRing; ++ring) {
                    for (int x = -ring; x <= ring; ++x)
                        for (int y = -ring; y <= ring; ++y)
                            for (int z = -ring; z <= ring; ++z) {
                                if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) != ring) continue;
                                List<int> list;
                                if (grid.TryGetValue(new Vector3i(c.x + x, c.y + y, c.z + z), out list) == false) continue;
                                foreach (int j in list)
                                    if (j != i) cand.Add(new KeyValuePair<double, int>(pts[j].DistanceSquared(pts[i]), j));
                            }
                    // every point outside the searched rings is farther than ring * cell
                    if (cand.Count >= k) {
                        cand.Sort((a, b) => a.Key.CompareTo(b.Key));
                        if (cand[k - 1].Key <= (ring * cell) * (ring * cell)) break;
                    }
                }
                cand.Sort((a, b) => a.Key.CompareTo(b.Key));
                int m = Math.Min(k, cand.Count);
                Vector3d[] nb = new Vector3d[m + 1];
                nb[0] = pts[i];
                for (int j = 0; j < m; ++j) nb[j + 1] = pts[cand[j].Value];
                result[i] = robust_normal(nb);
            });
            return result;
        }
    }
}
