// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// Minimizes the squared cotangent Laplacian of vertex displacements with exact point handles.
    /// This is an unbounded displacement solve, not bounded biharmonic skinning weights.
    /// The input mesh is not modified; output vertex normals are recomputed when present.
    /// Do not call Compute concurrently on one instance or mutate its input during Compute.
    /// See docs/design/BIHARMONIC_DEFORMATION.md.
    /// </summary>
    public class BiharmonicMeshDeformer
    {
        public enum Status { Success, InvalidInput, UnanchoredComponent, DegenerateTriangle, NumericalRange, LinearSolveFailed, NonFiniteResult, ResourceLimit }

        public class Settings {
            /// <summary>Largest accepted stationarity residual of the free rows (see <see cref="ResultInfo.Residual"/>).</summary>
            public double RelativeTolerance = 1e-8;
            /// <summary>Upper bound on assembled triangular outer-product contributions, before duplicate entries are compressed.</summary>
            public long MaxMatrixContributions = 10000000;
        }

        public class ResultInfo {
            public Status Status;
            /// <summary>The deformed copy of the input mesh; null unless <see cref="Status"/> is Success.</summary>
            public DMesh3 Mesh;
            /// <summary>Number of linear solves: 1 for a solved system, 0 when every vertex is a handle.</summary>
            public int Iterations;
            /// <summary>Maximum free-vertex gradient divided by an absolute contribution bound; NaN if not evaluated
            /// (every vertex is a handle). A backward-error check, not a bound on the position error of an ill-conditioned system.</summary>
            public double Residual;
            public bool Converged { get { return Status == Status.Success; } }
        }

        public DMesh3 Mesh;
        public Settings Options = new Settings();

        public BiharmonicMeshDeformer(DMesh3 mesh) { Mesh = mesh; }

        // Largest over free vertices of |gradient_i| / sum of the absolute contributions to gradient_i, with the
        // gradient of 1/2 u^T L^T M^-1 L u. Normalized per vertex, so a coarse component is not judged against the
        // contributions of a fine one. NaN if a quantity is not finite.
        internal static double Stationarity(Dictionary<int,double>[] rows, double[] mass, int[] free, Vector3d[] displacement)
        {
            var gradient = new Vector3d[displacement.Length]; var magnitude = new double[displacement.Length];
            for (int k = 0; k < displacement.Length; ++k) if (rows[k].Count > 0) {
                Vector3d lap = Vector3d.Zero; double bound = 0;
                foreach (var e in rows[k]) { lap += e.Value * displacement[e.Key]; bound += Math.Abs(e.Value) * MaxAbs(displacement[e.Key]); }
                if (!lap.IsFinite || !MathUtil.IsFinite(bound)) return double.NaN;
                foreach (var e in rows[k]) if (free[e.Key] >= 0) {
                    Vector3d contribution = (e.Value / mass[k]) * lap;
                    if (!contribution.IsFinite) return double.NaN;
                    gradient[e.Key] += contribution; magnitude[e.Key] += Math.Abs(e.Value / mass[k]) * bound;
                }
            }
            double maximum = 0;
            for (int i = 0; i < displacement.Length; ++i) if (free[i] >= 0) {
                double g = MaxAbs(gradient[i]);
                if (!MathUtil.IsFinite(g) || !MathUtil.IsFinite(magnitude[i])) return double.NaN;
                if (g == 0) continue;
                maximum = Math.Max(maximum, magnitude[i] == 0 ? double.PositiveInfinity : g / magnitude[i]);
            }
            return maximum;
        }

        // Adds value to row[col]; fails when the sum is not finite. A term smaller than half an ulp of the entry
        // is rounded away like in any floating-point assembly (rotated meshes have right angles whose cotangent is
        // rounding noise); the stationarity residual still gates success.
        static bool Add(Dictionary<int, double> row, int col, double value)
        {
            double old; row.TryGetValue(col, out old); double sum = old + value;
            if (!MathUtil.IsFinite(sum)) return false;
            row[col] = sum; return true;
        }

        static bool Area(Vector3d a, Vector3d b, Vector3d c)
        {
            return ExactPredicates.Orient2d(new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(c.x, c.y)) != 0 ||
                ExactPredicates.Orient2d(new Vector2d(a.x, a.z), new Vector2d(b.x, b.z), new Vector2d(c.x, c.z)) != 0 ||
                ExactPredicates.Orient2d(new Vector2d(a.y, a.z), new Vector2d(b.y, b.z), new Vector2d(c.y, c.z)) != 0;
        }

        static double MaxAbs(Vector3d p) { return Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))); }

        static double Length(Vector3d p) {
            double m = MaxAbs(p); if (m == 0 || !MathUtil.IsFinite(m)) return m;
            p /= m; return m * Math.Sqrt(p.Dot(p));
        }

        static bool Edge(Dictionary<int, double>[] rows, int a, int b, double w) {
            return Add(rows[a], a, w) && Add(rows[b], b, w) && Add(rows[a], b, -w) && Add(rows[b], a, -w);
        }

        // Dot product that fails when it is not finite or a nonzero product underflows to zero.
        static bool Dot(Vector3d a, Vector3d b, out double result) {
            result = 0;
            for (int i = 0; i < 3; ++i) {
                double term = a[i] * b[i];
                if (term == 0 && a[i] != 0 && b[i] != 0) return false;
                result += term;
                if (!MathUtil.IsFinite(result)) return false;
            }
            return true;
        }

        static bool HasArea(DMesh3 mesh, Vector3d[] original, Dictionary<int, int> lookup) {
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                if (!Area(original[lookup[t.a]], original[lookup[t.b]], original[lookup[t.c]])) return false;
            }
            return true;
        }

        /// <summary>Compute from the current mesh and handle targets; repeated calls do not mutate the input.</summary>
        public ResultInfo Compute(IReadOnlyDictionary<int, Vector3d> handles)
        {
            var result = new ResultInfo { Status = Status.InvalidInput, Residual = double.NaN };
            if (Mesh == null || Mesh.VertexCount == 0 || handles == null || Options == null ||
                !(Options.RelativeTolerance > 0) || !MathUtil.IsFinite(Options.RelativeTolerance) || Options.MaxMatrixContributions <= 0) return result;
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
                // Every vertex is a handle: the answer is the target set, no system to solve.
                if (!HasArea(Mesh, original, lookup)) { result.Status = Status.DegenerateTriangle; return result; }
                var allFixed = new DMesh3(Mesh);
                foreach (var h in handles) allFixed.SetVertex(h.Key, h.Value);
                if (allFixed.HasVertexNormals) MeshNormals.QuickCompute(allFixed);
                result.Mesh = allFixed; result.Status = Status.Success;
                return result;
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
            var free = new int[n]; int count = 0;
            for (int i = 0; i < n; ++i) free[i] = fixedV[i] ? -1 : count++;
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
                    result.Status = Status.NumericalRange; return result;
                }
                double wa = 0.5 * da / twiceArea;
                double wb = 0.5 * db / twiceArea;
                double wc = 0.5 * dc / twiceArea;
                double areaShare = twiceArea / 6;
                if (!MathUtil.IsFinite(wa) || !MathUtil.IsFinite(wb) || !MathUtil.IsFinite(wc) || !(areaShare > 0)) {
                    result.Status = Status.NumericalRange; return result;
                }
                if (!Edge(rows, b, c, wa) || !Edge(rows, a, c, wb) || !Edge(rows, a, b, wc)) {
                    result.Status = Status.NumericalRange; return result;
                }
                mass[a] += areaShare; mass[b] += areaShare; mass[c] += areaShare;
            }
            var displacement = new Vector3d[n];
            var motionScale = new double[n];
            foreach (var h in handles) {
                int i = lookup[h.Key]; if (!active[i]) continue;
                displacement[i] = h.Value - original[i];
                if (!displacement[i].IsFinite) {
                    result.Status = Status.NumericalRange; return result;
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
                result.Residual = Stationarity(rows, mass, free, displacement);
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
            result.Mesh = output; result.Status = Status.Success; return result;
        }
    }
}
