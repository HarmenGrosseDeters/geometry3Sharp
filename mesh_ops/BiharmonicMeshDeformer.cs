// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Minimizes the squared cotangent Laplacian of vertex displacements with exact point handles.
    /// This is an unbounded displacement solve, not bounded biharmonic skinning weights.
    /// The input mesh is not modified; output vertex normals are recomputed when present.
    /// </summary>
    public class BiharmonicMeshDeformer
    {
        public enum Status { Success, InvalidInput, UnanchoredComponent, DegenerateTriangle, NumericalRange, LinearSolveFailed, NonFiniteResult, ResourceLimit, AccuracyNotEstablished }
        /// <summary>Whether the returned positions have an independently bounded error.</summary>
        public enum AccuracyAssessment { NotEstablished, NotCertified, Certified }
        public class Settings {
            public double RelativeTolerance = 1e-8;
            /// <summary>Request bounded Euclidean position and component response accuracy using the dense interval solver (at most 16 vertices).</summary>
            public bool RequireEuclideanAccuracy = false;
            /// <summary>Maximum Euclidean position error relative to component rest radius in strict mode.</summary>
            public double PositionTolerance = 1e-8;
            /// <summary>Maximum relative error in the largest free-vertex displacement per component in strict mode.</summary>
            public double ResponseTolerance = 1e-8;
            /// <summary>Upper bound on assembled triangular outer-product contributions, before duplicate entries are compressed.</summary>
            public long MaxMatrixContributions = 10000000;
        }
        public class ResultInfo {
            public Status Status;
            public DMesh3 Mesh;
            public int Iterations;
            /// <summary>Maximum free-vertex gradient divided by an absolute contribution bound; NaN if not evaluated, including the interval fallback and fully fixed meshes.</summary>
            public double Residual;
            /// <summary>Whether the result was obtained by the bounded interval fallback.</summary>
            public bool UsedIntervalFallback;
            /// <summary>Largest certified coordinate error divided by its component rest scale; NaN when unavailable.</summary>
            public double PositionErrorBound;
            /// <summary>Certification of the returned position bound, separate from numerical convergence.</summary>
            public AccuracyAssessment Accuracy;
            public bool Converged { get { return Status == Status.Success; } }
        }
        public DMesh3 Mesh;
        public Settings Options = new Settings();
        public BiharmonicMeshDeformer(DMesh3 mesh) { Mesh = mesh; }

        static bool Add(Dictionary<int, double> row, int col, double value)
        {
            double old; row.TryGetValue(col, out old); double sum = old + value;
            if (!MathUtil.IsFinite(sum) || (value != 0 && old != 0 && (sum == old || sum == value))) return false;
            row[col] = sum; return true;
        }
        static bool Area(Vector3d a, Vector3d b, Vector3d c)
        {
            return ExactPredicates.Orient2d(new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(c.x, c.y)) != 0 ||
                ExactPredicates.Orient2d(new Vector2d(a.x, a.z), new Vector2d(b.x, b.z), new Vector2d(c.x, c.z)) != 0 ||
                ExactPredicates.Orient2d(new Vector2d(a.y, a.z), new Vector2d(b.y, b.z), new Vector2d(c.y, c.z)) != 0;
        }
        static double MaxAbs(Vector3d p) { return Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))); }
        static bool LostDifference(Vector3d target, Vector3d original, Vector3d delta) {
            for (int j = 0; j < 3; ++j)
                if (target[j] != 0 && original[j] != 0 &&
                    (delta[j] == target[j] || delta[j] == -original[j])) return true;
            return false;
        }
        static double Length(Vector3d p) {
            double m = MaxAbs(p); if (m == 0 || !MathUtil.IsFinite(m)) return m;
            p /= m; return m * Math.Sqrt(p.Dot(p));
        }
        static bool Edge(Dictionary<int, double>[] rows, int a, int b, double w) {
            return Add(rows[a], a, w) && Add(rows[b], b, w) && Add(rows[a], b, -w) && Add(rows[b], a, -w);
        }
        static bool Dot(Vector3d a, Vector3d b, out double result) {
            result = 0;
            for (int i = 0; i < 3; ++i) {
                double term = a[i] * b[i];
                if (term == 0 && a[i] != 0 && b[i] != 0) return false;
                double sum = result + term;
                if (!MathUtil.IsFinite(sum) || (term != 0 && result != 0 && (sum == result || sum == term))) return false;
                result = sum;
            }
            return true;
        }
        /// <summary>Compute from the current mesh and handle targets; repeated calls do not mutate the input.</summary>
        public ResultInfo Compute(IReadOnlyDictionary<int, Vector3d> handles)
        {
            var result = new ResultInfo { Status = Status.InvalidInput, Residual = double.NaN, PositionErrorBound = double.NaN,
                Accuracy = AccuracyAssessment.NotEstablished };
            if (Mesh == null || Mesh.VertexCount == 0 || handles == null || Options == null ||
                !(Options.RelativeTolerance > 0) || !MathUtil.IsFinite(Options.RelativeTolerance) || Options.MaxMatrixContributions <= 0 ||
                (Options.RequireEuclideanAccuracy && (!(Options.PositionTolerance > 0) || !MathUtil.IsFinite(Options.PositionTolerance) ||
                !(Options.ResponseTolerance > 0) || !MathUtil.IsFinite(Options.ResponseTolerance)))) return result;
            var ids = new List<int>(Mesh.VertexIndices()); int n = ids.Count;
            var lookup = new Dictionary<int, int>(n); var original = new Vector3d[n];
            var rest = new Vector3d[n]; var fixedV = new bool[n];
            for (int i = 0; i < n; ++i) {
                lookup.Add(ids[i], i); original[i] = Mesh.GetVertex(ids[i]);
                if (!original[i].IsFinite) return result;
            }
            foreach (var h in handles) {
                int i; if (!lookup.TryGetValue(h.Key, out i) || !h.Value.IsFinite) return result;
                fixedV[i] = true;
            }
            if (handles.Count == n) {
                foreach (int tid in Mesh.TriangleIndices()) {
                    Index3i t = Mesh.GetTriangle(tid);
                    if (!Area(original[lookup[t.a]], original[lookup[t.b]], original[lookup[t.c]])) {
                        result.Status = Status.DegenerateTriangle; return result;
                    }
                }
                var allFixed = new DMesh3(Mesh);
                foreach (var h in handles) allFixed.SetVertex(h.Key, h.Value);
                if (allFixed.HasVertexNormals) MeshNormals.QuickCompute(allFixed);
                result.Mesh = allFixed; result.Status = Status.Success; result.Accuracy = AccuracyAssessment.Certified;
                result.PositionErrorBound = 0; return result;
            }
            // Normalize each component independently to preserve scale invariance and avoid
            // interactions between distant or differently sized components.
            var visited = new bool[n]; var active = new bool[n]; var componentRoot = new int[n];
            var queue = new Queue<int>();
            for (int root = 0; root < n; ++root) {
                if (visited[root]) continue;
                var component = new List<int>(); bool anchored = false, hasFree = false;
                visited[root] = true; queue.Enqueue(root);
                while (queue.Count > 0) {
                    int v = queue.Dequeue(); component.Add(v); anchored |= fixedV[v]; hasFree |= !fixedV[v];
                    foreach (int id in Mesh.VtxVerticesItr(ids[v])) {
                        int u = lookup[id]; if (!visited[u]) { visited[u] = true; queue.Enqueue(u); }
                    }
                }
                if (!anchored) { result.Status = Status.UnanchoredComponent; return result; }
                if (!hasFree) { foreach (int v in component) componentRoot[v] = root; continue; }
                Vector3d o = original[root]; double s = 0;
                foreach (int v in component) {
                    Vector3d d = original[v] - o;
                    if (!d.IsFinite) { result.Status = Status.NumericalRange; return result; }
                    s = Math.Max(s, MaxAbs(d));
                }
                if (s == 0) s = 1;
                foreach (int v in component) { active[v] = true; componentRoot[v] = root; rest[v] = (original[v] - o) / s; }
            }
            if (Options.RequireEuclideanAccuracy) {
                bool unchanged = true;
                foreach (var h in handles) if (h.Value != original[lookup[h.Key]]) { unchanged = false; break; }
                if (unchanged) {
                    foreach (int tid in Mesh.TriangleIndices()) {
                        Index3i t = Mesh.GetTriangle(tid);
                        if (!Area(original[lookup[t.a]], original[lookup[t.b]], original[lookup[t.c]])) {
                            result.Status = Status.DegenerateTriangle; return result;
                        }
                    }
                    var copy = new DMesh3(Mesh);
                    foreach (var h in handles) copy.SetVertex(h.Key, h.Value);
                    if (copy.HasVertexNormals) MeshNormals.QuickCompute(copy);
                    result.Mesh = copy; result.Status = Status.Success;
                    result.Accuracy = AccuracyAssessment.Certified; result.PositionErrorBound = 0;
                    return result;
                }
            }
            var free = new int[n]; int count = 0;
            for (int i = 0; i < n; ++i) free[i] = fixedV[i] ? -1 : count++;
            ResultInfo NumericFallback()
            {
                Vector3d[] points; double bound;
                if (!BiharmonicIntervalFallback.TrySolve(Mesh, ids, lookup, original, componentRoot, active, free,
                    count, handles, Options.RelativeTolerance, out points, out bound)) {
                    result.Status = Options.RequireEuclideanAccuracy ? Status.AccuracyNotEstablished : Status.NumericalRange; return result;
                }
                if (Options.RequireEuclideanAccuracy && !BiharmonicIntervalFallback.MeetsEuclideanAccuracy(
                    original, points, componentRoot, active, free, bound, Options.PositionTolerance, Options.ResponseTolerance)) {
                    result.Status = Status.AccuracyNotEstablished; return result;
                }
                var copy = new DMesh3(Mesh);
                for (int i = 0; i < n; ++i) copy.SetVertex(ids[i], points[i]);
                if (copy.HasVertexNormals) MeshNormals.QuickCompute(copy);
                result.Mesh = copy; result.Status = Status.Success; result.Iterations = 1;
                result.UsedIntervalFallback = true; result.PositionErrorBound = bound; result.Residual = double.NaN;
                result.Accuracy = AccuracyAssessment.Certified;
                return result;
            }
            // Every Laplacian row contains at most the vertex and its topological
            // neighbors. Reject excessive assembly before evaluating its weights.
            long contributions = 0;
            for (int i = 0; i < n; ++i) if (active[i]) {
                long nf = free[i] >= 0 ? 1 : 0;
                foreach (int neighbor in Mesh.VtxVerticesItr(ids[i])) if (free[lookup[neighbor]] >= 0) ++nf;
                long add = nf * (nf + 1) / 2;
                if (add > Options.MaxMatrixContributions - contributions) { result.Status = Status.ResourceLimit; return result; }
                contributions += add;
            }
            if (Options.RequireEuclideanAccuracy) {
                foreach (int tid in Mesh.TriangleIndices()) {
                    Index3i t = Mesh.GetTriangle(tid);
                    if (!Area(original[lookup[t.a]], original[lookup[t.b]], original[lookup[t.c]])) {
                        result.Status = Status.DegenerateTriangle; return result;
                    }
                }
                return NumericFallback();
            }
            var rows = new Dictionary<int, double>[n]; var mass = new double[n];
            for (int i = 0; i < n; ++i) rows[i] = new Dictionary<int, double>();
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i tri = Mesh.GetTriangle(tid); int a = lookup[tri.a], b = lookup[tri.b], c = lookup[tri.c];
                if (!active[a]) {
                    if (!Area(original[a], original[b], original[c])) { result.Status = Status.DegenerateTriangle; return result; }
                    continue;
                }
                Vector3d ab = rest[b] - rest[a], ac = rest[c] - rest[a];
                double twiceArea = Length(ab.Cross(ac));
                if (!(twiceArea > 0) || !MathUtil.IsFinite(twiceArea)) {
                    result.Status = Area(original[a], original[b], original[c]) ? Status.NumericalRange : Status.DegenerateTriangle; return result;
                }
                double da, db, dc;
                if (!Dot(ab, ac, out da) || !Dot(-ab, ac - ab, out db) || !Dot(-ac, ab - ac, out dc)) {
                    return NumericFallback();
                }
                double wa = 0.5 * da / twiceArea;
                double wb = 0.5 * db / twiceArea;
                double wc = 0.5 * dc / twiceArea;
                double areaShare = twiceArea / 6;
                if (!MathUtil.IsFinite(wa) || !MathUtil.IsFinite(wb) || !MathUtil.IsFinite(wc) || !(areaShare > 0)) {
                    result.Status = Status.NumericalRange; return result;
                }
                if (!Edge(rows, b, c, wa) || !Edge(rows, a, c, wb) || !Edge(rows, a, b, wc)) {
                    return NumericFallback();
                }
                mass[a] += areaShare; mass[b] += areaShare; mass[c] += areaShare;
            }
            var displacement = new Vector3d[n];
            var motionScale = new double[n];
            foreach (var h in handles) {
                int i = lookup[h.Key]; if (!active[i]) continue;
                displacement[i] = h.Value - original[i];
                if (!displacement[i].IsFinite || LostDifference(h.Value, original[i], displacement[i])) {
                    return NumericFallback();
                }
                int root = componentRoot[i]; motionScale[root] = Math.Max(motionScale[root], MaxAbs(displacement[i]));
            }
            foreach (var h in handles) {
                int i = lookup[h.Key]; if (!active[i]) continue;
                double s = motionScale[componentRoot[i]];
                if (s == 0) continue;
                Vector3d before = displacement[i]; displacement[i] /= s;
                if ((before.x != 0 && displacement[i].x == 0) || (before.y != 0 && displacement[i].y == 0) ||
                    (before.z != 0 && displacement[i].z == 0)) { result.Status = Status.NumericalRange; return result; }
            }
            if (count > 0) {
                var chol = new SparseCholesky(count);
                var bx = new double[count]; var by = new double[count]; var bz = new double[count];
                // Q = L^T M^-1 L, assembled as outer products of individual L rows.
                for (int k = 0; k < n; ++k) {
                    if (rows[k].Count == 0) continue; // fixed isolated vertex
                    if (!(mass[k] > 0) || !MathUtil.IsFinite(mass[k])) { result.Status = Status.NumericalRange; return result; }
                    var entries = new List<KeyValuePair<int, double>>(rows[k]);
                    Vector3d constrained = Vector3d.Zero;
                    foreach (var e in entries) if (fixedV[e.Key]) constrained += e.Value * displacement[e.Key];
                    for (int i = 0; i < entries.Count; ++i) {
                        int v = entries[i].Key, vi = free[v]; if (vi < 0) continue;
                        double factor = entries[i].Value / mass[k];
                        Vector3d rhs = -factor * constrained;
                        if (!rhs.IsFinite || !MathUtil.IsFinite(factor)) { result.Status = Status.NumericalRange; return result; }
                        bx[vi] += rhs.x; by[vi] += rhs.y; bz[vi] += rhs.z;
                        for (int j = 0; j <= i; ++j) {
                            int uj = free[entries[j].Key]; if (uj < 0) continue;
                            double value = factor * entries[j].Value;
                            if (!MathUtil.IsFinite(value)) { result.Status = Status.NumericalRange; return result; }
                            chol.Add(vi, uj, value);
                        }
                    }
                }
                if (!chol.Factorize()) { result.Status = Status.LinearSolveFailed; return result; }
                var x = new double[count]; var y = new double[count]; var z = new double[count];
                chol.Solve(bx, x); chol.Solve(by, y); chol.Solve(bz, z);
                for (int i = 0; i < n; ++i) if (free[i] >= 0) {
                    int j = free[i]; displacement[i] = new Vector3d(x[j], y[j], z[j]);
                    if (!displacement[i].IsFinite) { result.Status = Status.NonFiniteResult; return result; }
                }
                // Check stationarity directly using the original sparse Laplacian rows.
                var gradient = new Vector3d[n]; double magnitude = 0;
                for (int k = 0; k < n; ++k) if (rows[k].Count > 0) {
                    Vector3d lap = Vector3d.Zero; double bound = 0;
                    foreach (var e in rows[k]) { lap += e.Value * displacement[e.Key]; bound += Math.Abs(e.Value) * MaxAbs(displacement[e.Key]); }
                    if (!lap.IsFinite || !MathUtil.IsFinite(bound)) { result.Status = Status.NumericalRange; return result; }
                    foreach (var e in rows[k]) if (free[e.Key] >= 0) {
                        Vector3d contribution = (e.Value / mass[k]) * lap;
                        if (!contribution.IsFinite) { result.Status = Status.NumericalRange; return result; }
                        gradient[e.Key] += contribution; magnitude = Math.Max(magnitude, Math.Abs(e.Value / mass[k]) * bound);
                    }
                }
                if (!MathUtil.IsFinite(magnitude)) { result.Status = Status.NumericalRange; return result; }
                double maximum = 0;
                for (int i = 0; i < n; ++i) if (free[i] >= 0) maximum = Math.Max(maximum, MaxAbs(gradient[i]));
                result.Residual = magnitude == 0 ? maximum : maximum / magnitude;
                result.Iterations = 1;
                if (!MathUtil.IsFinite(result.Residual) || result.Residual > Options.RelativeTolerance) {
                    result.Status = Status.LinearSolveFailed; return result;
                }
            }
            var output = new DMesh3(Mesh);
            for (int i = 0; i < n; ++i) {
                Vector3d position;
                if (!handles.TryGetValue(ids[i], out position)) position = original[i] + motionScale[componentRoot[i]] * displacement[i];
                if (!position.IsFinite) { result.Status = Status.NonFiniteResult; return result; }
                output.SetVertex(ids[i], position);
            }
            if (output.HasVertexNormals) MeshNormals.QuickCompute(output);
            result.Mesh = output; result.Status = Status.Success; result.Accuracy = AccuracyAssessment.NotCertified; return result;
        }
    }
}
