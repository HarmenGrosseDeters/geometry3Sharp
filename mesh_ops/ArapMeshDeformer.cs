// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// As-rigid-as-possible surface deformation with fixed vertex positions (#886).
    /// Negative cotangent sums are clamped to zero for a nonnegative spokes energy.
    /// The input is not modified. If it has vertex normals, output normals are recomputed
    /// from the deformed faces; caller-authored normals are not preserved.
    /// </summary>
    public class ArapMeshDeformer
    {
        public enum Status { Success, InvalidInput, UnanchoredComponent, DegenerateTriangle, NumericalRange, LinearSolveFailed, NonFiniteResult, MaxIterations }
        public class Settings
        {
            public int MaxIterations = 50;
            public double RelativeTolerance = 1e-8;
        }
        /// <summary>
        /// Success and MaxIterations carry a finite output mesh with exact handle positions;
        /// MaxIterations is not convergence. All failure statuses carry no output mesh.
        /// </summary>
        public class ResultInfo
        {
            public Status Status;
            public DMesh3 Mesh;
            public int Iterations;
            /// <summary>Maximum normalized vertex motion in the last iteration. This is a stopping heuristic, not a linear-system or energy-stationarity residual.</summary>
            public double Residual;
            public bool Converged { get { return Status == Status.Success; } }
        }

        public DMesh3 Mesh;
        public Settings Options = new Settings();
        public ArapMeshDeformer(DMesh3 mesh) { Mesh = mesh; }

        struct Edge
        {
            public int A, B;
            public double Weight;
            public Edge(int a, int b, double weight) { A = a; B = b; Weight = weight; }
        }

        static double MaxAbs(Vector3d v)
        {
            return Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z)));
        }

        static double StableLength(Vector3d v)
        {
            double m = MaxAbs(v);
            if (m == 0 || !MathUtil.IsFinite(m)) return m;
            v /= m;
            return m * Math.Sqrt(v.Dot(v));
        }

        static bool TryCotangentWeight(Vector3d u, Vector3d v, out double weight)
        {
            weight = 0;
            double su = MaxAbs(u), sv = MaxAbs(v);
            if (!(su > 0) || !(sv > 0) || !MathUtil.IsFinite(su) || !MathUtil.IsFinite(sv)) return false;
            Vector3d a = u / su, b = v / sv;
            if (LostComponent(u, a) || LostComponent(v, b)) return false;
            double cross = StableLength(a.Cross(b));
            if (!(cross > 0) || !MathUtil.IsFinite(cross)) return false;
            double dot = 0;
            for (int i = 0; i < 3; ++i) {
                double product = a[i] * b[i];
                if (product == 0 && a[i] != 0 && b[i] != 0) return false;
                dot += product;
            }
            double halfDot = 0.5 * dot;
            if (dot != 0 && halfDot == 0) return false;
            weight = halfDot / cross;
            if (halfDot != 0 && weight == 0) return false;
            return MathUtil.IsFinite(weight);
        }

        static bool LostComponent(Vector3d original, Vector3d normalized)
        {
            return (original.x != 0 && normalized.x == 0) || (original.y != 0 && normalized.y == 0) ||
                   (original.z != 0 && normalized.z == 0);
        }

        // The three projected orientations decide collinearity exactly even when a
        // floating-point cross product overflows or loses a tiny component.
        static bool HasRepresentableArea(Vector3d a, Vector3d b, Vector3d c)
        {
            return ExactPredicates.Orient2d(new Vector2d(a.x, a.y), new Vector2d(b.x, b.y), new Vector2d(c.x, c.y)) != 0 ||
                   ExactPredicates.Orient2d(new Vector2d(a.x, a.z), new Vector2d(b.x, b.z), new Vector2d(c.x, c.z)) != 0 ||
                   ExactPredicates.Orient2d(new Vector2d(a.y, a.z), new Vector2d(b.y, b.z), new Vector2d(c.y, c.z)) != 0;
        }

        static Index2i EdgeKey(int a, int b)
        {
            if (a > b) { int t = a; a = b; b = t; }
            return new Index2i(a, b);
        }

        static void AddWeight(Dictionary<Index2i, double> weights, int a, int b, double w)
        {
            Index2i key = EdgeKey(a, b);
            double old;
            weights.TryGetValue(key, out old);
            weights[key] = old + w;
        }

        /// <summary>Compute a deformation without mutating the input; handle positions are hard constraints.</summary>
        public ResultInfo Compute(IReadOnlyDictionary<int, Vector3d> handles)
        {
            var result = new ResultInfo { Status = Status.InvalidInput };
            if (Mesh == null || handles == null || Options == null || Mesh.VertexCount == 0 ||
                Options.MaxIterations <= 0 || !(Options.RelativeTolerance > 0) || !MathUtil.IsFinite(Options.RelativeTolerance))
                return result;
            var ids = new List<int>(Mesh.VertexIndices());
            int n = ids.Count;
            var toIndex = new Dictionary<int, int>(n);
            var rest = new Vector3d[n];
            var worldRest = new Vector3d[n];
            var positions = new Vector3d[n];
            var fixedVertex = new bool[n];
            for (int i = 0; i < n; ++i) {
                toIndex.Add(ids[i], i);
                worldRest[i] = rest[i] = positions[i] = Mesh.GetVertex(ids[i]);
                if (!rest[i].IsFinite) return result;
            }
            foreach (var h in handles) {
                int i;
                if (!toIndex.TryGetValue(h.Key, out i) || !h.Value.IsFinite) return result;
                fixedVertex[i] = true;
            }
            if (handles.Count == n) {
                foreach (int tid in Mesh.TriangleIndices()) {
                    Index3i tri = Mesh.GetTriangle(tid);
                    Vector3d a = worldRest[toIndex[tri.a]], b = worldRest[toIndex[tri.b]], c = worldRest[toIndex[tri.c]];
                    if (!HasRepresentableArea(a, b, c)) { result.Status = Status.DegenerateTriangle; return result; }
                }
                var fullyConstrained = new DMesh3(Mesh);
                foreach (var h in handles) fullyConstrained.SetVertex(h.Key, h.Value);
                if (fullyConstrained.HasVertexNormals) MeshNormals.QuickCompute(fullyConstrained);
                result.Mesh = fullyConstrained; result.Status = Status.Success;
                return result;
            }
            // Normalize each connected component separately: an unrelated distant or large
            // component must not erase the detail of a small component.
            var origins = new Vector3d[n]; var scales = new double[n]; var activeComponent = new bool[n];
            var visited = new bool[n]; var queue = new Queue<int>();
            for (int root = 0; root < n; ++root) {
                if (visited[root]) continue;
                var component = new List<int>(); bool anchored = false, hasFree = false;
                visited[root] = true; queue.Enqueue(root);
                while (queue.Count > 0) {
                    int v = queue.Dequeue(); component.Add(v); anchored |= fixedVertex[v]; hasFree |= !fixedVertex[v];
                    foreach (int id in Mesh.VtxVerticesItr(ids[v])) {
                        int u = toIndex[id];
                        if (!visited[u]) { visited[u] = true; queue.Enqueue(u); }
                    }
                }
                if (!anchored) { result.Status = Status.UnanchoredComponent; return result; }
                if (!hasFree) {
                    foreach (int v in component) { origins[v] = rest[root]; scales[v] = 1; positions[v] = Vector3d.Zero; }
                    continue;
                }
                Vector3d origin = rest[root]; double scale = 0;
                foreach (int v in component) {
                    Vector3d delta = rest[v] - origin;
                    if (!delta.IsFinite) { result.Status = Status.NumericalRange; return result; }
                    scale = Math.Max(scale, Math.Max(Math.Abs(delta.x), Math.Max(Math.Abs(delta.y), Math.Abs(delta.z))));
                }
                if (scale == 0) scale = 1;
                foreach (int v in component) {
                    origins[v] = origin; scales[v] = scale; activeComponent[v] = hasFree;
                    rest[v] = (rest[v] - origin) / scale;
                    positions[v] = rest[v];
                }
            }
            var weights = new Dictionary<Index2i, double>();
            foreach (int tid in Mesh.TriangleIndices()) {
                Index3i tri = Mesh.GetTriangle(tid);
                int a = toIndex[tri.a], b = toIndex[tri.b], c = toIndex[tri.c];
                if (!activeComponent[a]) {
                    if (!HasRepresentableArea(worldRest[a], worldRest[b], worldRest[c])) {
                        result.Status = Status.DegenerateTriangle; return result;
                    }
                    continue;
                }
                Vector3d ab = rest[b] - rest[a], ac = rest[c] - rest[a];
                double wa, wb, wc;
                if (!TryCotangentWeight(ab, ac, out wa) ||
                    !TryCotangentWeight(-ab, ac - ab, out wb) ||
                    !TryCotangentWeight(-ac, ab - ac, out wc)) {
                    result.Status = HasRepresentableArea(worldRest[a], worldRest[b], worldRest[c]) ? Status.NumericalRange : Status.DegenerateTriangle;
                    return result;
                }
                AddWeight(weights, b, c, wa); AddWeight(weights, a, c, wb); AddWeight(weights, a, b, wc);
            }
            var edges = new List<Edge>(weights.Count);
            var neighbors = new List<int>[n];
            for (int i = 0; i < n; ++i) neighbors[i] = new List<int>();
            foreach (var pair in weights) {
                int a = pair.Key.a, b = pair.Key.b;
                if (!MathUtil.IsFinite(pair.Value)) { result.Status = Status.NumericalRange; return result; }
                // Negative cotangent sums make the spokes energy indefinite. Use a
                // nonnegative edge weight; this choice is part of the public contract.
                double weight = Math.Max(0, pair.Value);
                if (weight == 0) continue; // zero-weight edges must not influence covariance scaling
                int e = edges.Count; edges.Add(new Edge(a, b, weight));
                neighbors[a].Add(e); neighbors[b].Add(e);
            }
            // Check the actual weighted graph as well: zeroed weights must not leave a
            // free component without a constraint, even when the mesh is connected.
            Array.Clear(visited, 0, visited.Length);
            for (int root = 0; root < n; ++root) {
                if (visited[root]) continue;
                bool anchored = false; visited[root] = true; queue.Enqueue(root);
                while (queue.Count > 0) {
                    int v = queue.Dequeue(); anchored |= fixedVertex[v];
                    foreach (int e in neighbors[v]) {
                        Edge edge = edges[e]; int u = edge.A == v ? edge.B : edge.A;
                        if (!visited[u]) { visited[u] = true; queue.Enqueue(u); }
                    }
                }
                if (!anchored) { result.Status = Status.UnanchoredComponent; return result; }
            }
            var free = new int[n]; int count = 0;
            for (int i = 0; i < n; ++i) free[i] = fixedVertex[i] ? -1 : count++;
            if (count == 0) {
                result.Mesh = MakeMesh(ids, positions, origins, scales, handles);
                result.Status = result.Mesh == null ? Status.NonFiniteResult : Status.Success;
                return result;
            }
            foreach (var h in handles) {
                int i = toIndex[h.Key];
                if (!activeComponent[i]) continue;
                positions[i] = (h.Value - origins[i]) / scales[i];
                if (!positions[i].IsFinite) { result.Status = Status.NumericalRange; return result; }
            }
            var chol = new SparseCholesky(count);
            var diagonal = new double[count];
            foreach (var edge in edges) {
                int a = free[edge.A], b = free[edge.B];
                if (a >= 0) diagonal[a] += edge.Weight;
                if (b >= 0) diagonal[b] += edge.Weight;
                if (a >= 0 && b >= 0) chol.Add(a, b, -edge.Weight);
            }
            for (int i = 0; i < count; ++i) chol.Add(i, i, diagonal[i]);
            if (!chol.Factorize()) { result.Status = Status.LinearSolveFailed; return result; }
            // Start from the harmonic interpolation of handle displacements. In particular, a
            // common translation is reproduced before estimating local rotations.
            var ix = new double[count]; var iy = new double[count]; var iz = new double[count];
            foreach (var edge in edges) {
                int a = edge.A, b = edge.B;
                if (free[a] >= 0 && fixedVertex[b]) {
                    Vector3d d = edge.Weight * (positions[b] - rest[b]);
                    ix[free[a]] += d.x; iy[free[a]] += d.y; iz[free[a]] += d.z;
                }
                if (free[b] >= 0 && fixedVertex[a]) {
                    Vector3d d = edge.Weight * (positions[a] - rest[a]);
                    ix[free[b]] += d.x; iy[free[b]] += d.y; iz[free[b]] += d.z;
                }
            }
            var dx = new double[count]; var dy = new double[count]; var dz = new double[count];
            chol.Solve(ix, dx); chol.Solve(iy, dy); chol.Solve(iz, dz);
            for (int i = 0; i < n; ++i) if (free[i] >= 0) {
                int k = free[i]; positions[i] = rest[i] + new Vector3d(dx[k], dy[k], dz[k]);
                if (!positions[i].IsFinite) { result.Status = Status.NonFiniteResult; return result; }
            }
            var rotations = new Matrix3d[n];
            for (int i = 0; i < n; ++i) rotations[i] = Matrix3d.Identity;
            var svd = new FastQuaternionSVD();
            var bx = new double[count]; var by = new double[count]; var bz = new double[count];
            var sx = new double[count]; var sy = new double[count]; var sz = new double[count];
            for (int iter = 0; iter < Options.MaxIterations; ++iter) {
                for (int i = 0; i < n; ++i) {
                    if (!activeComponent[i]) continue;
                    Matrix3d cov = Matrix3d.Zero;
                    double pScale = 0, qScale = 0;
                    foreach (int e in neighbors[i]) {
                        var edge = edges[e]; int j = edge.A == i ? edge.B : edge.A;
                        Vector3d p = positions[i] - positions[j], q = rest[i] - rest[j];
                        pScale = Math.Max(pScale, Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
                        qScale = Math.Max(qScale, Math.Max(Math.Abs(q.x), Math.Max(Math.Abs(q.y), Math.Abs(q.z))));
                    }
                    if (!MathUtil.IsFinite(pScale) || !MathUtil.IsFinite(qScale)) { result.Status = Status.NonFiniteResult; return result; }
                    if (pScale > 0 && qScale > 0) foreach (int e in neighbors[i]) {
                        var edge = edges[e]; int j = edge.A == i ? edge.B : edge.A;
                        Vector3d dp = positions[i] - positions[j], dq = rest[i] - rest[j];
                        Vector3d p = dp / pScale, q = dq / qScale;
                        if (LostComponent(dp, p) || LostComponent(dq, q)) { result.Status = Status.NumericalRange; return result; }
                        for (int r = 0; r < 3; ++r) for (int c = 0; c < 3; ++c) {
                            double product = p[r] * q[c];
                            if (product == 0 && p[r] != 0 && q[c] != 0) { result.Status = Status.NumericalRange; return result; }
                            double term = edge.Weight * product;
                            if ((term == 0 && product != 0) || !MathUtil.IsFinite(term)) { result.Status = Status.NumericalRange; return result; }
                            cov[r, c] += term;
                        }
                    }
                    double max = 0;
                    for (int r = 0; r < 3; ++r) for (int c = 0; c < 3; ++c) max = Math.Max(max, Math.Abs(cov[r, c]));
                    if (!MathUtil.IsFinite(max)) { result.Status = Status.NonFiniteResult; return result; }
                    if (max > 0) {
                        for (int r = 0; r < 3; ++r) for (int c = 0; c < 3; ++c) cov[r, c] /= max;
                        svd.Solve(cov, jacobiIters: 20);
                        rotations[i] = svd.U.ToRotationMatrix() * svd.V.Conjugate().ToRotationMatrix();
                    }
                }
                Array.Clear(bx, 0, count); Array.Clear(by, 0, count); Array.Clear(bz, 0, count);
                foreach (var edge in edges) {
                    int a = edge.A, b = edge.B;
                    Vector3d term = 0.5 * edge.Weight * ((rotations[a] + rotations[b]) * (rest[a] - rest[b]));
                    if (free[a] >= 0) {
                        Vector3d value = term + (fixedVertex[b] ? edge.Weight * positions[b] : Vector3d.Zero);
                        int k = free[a]; bx[k] += value.x; by[k] += value.y; bz[k] += value.z;
                    }
                    if (free[b] >= 0) {
                        Vector3d value = -term + (fixedVertex[a] ? edge.Weight * positions[a] : Vector3d.Zero);
                        int k = free[b]; bx[k] += value.x; by[k] += value.y; bz[k] += value.z;
                    }
                }
                chol.Solve(bx, sx); chol.Solve(by, sy); chol.Solve(bz, sz);
                double motion = 0;
                for (int i = 0; i < n; ++i) if (free[i] >= 0) {
                    int k = free[i]; var next = new Vector3d(sx[k], sy[k], sz[k]);
                    if (!next.IsFinite) { result.Status = Status.NonFiniteResult; return result; }
                    motion = Math.Max(motion, StableLength(next - positions[i]));
                    positions[i] = next;
                }
                result.Iterations = iter + 1; result.Residual = motion;
                if (!MathUtil.IsFinite(result.Residual)) { result.Status = Status.NonFiniteResult; return result; }
                if (result.Residual <= Options.RelativeTolerance) {
                    result.Mesh = MakeMesh(ids, positions, origins, scales, handles);
                    result.Status = result.Mesh == null ? Status.NonFiniteResult : Status.Success;
                    return result;
                }
            }
            // A finite last iterate is useful to the caller even when the iteration budget is exhausted.
            result.Mesh = MakeMesh(ids, positions, origins, scales, handles);
            result.Status = result.Mesh == null ? Status.NonFiniteResult : Status.MaxIterations;
            return result;
        }

        DMesh3 MakeMesh(List<int> ids, Vector3d[] positions, Vector3d[] origins, double[] scales,
                        IReadOnlyDictionary<int, Vector3d> handles)
        {
            var output = new Vector3d[ids.Count];
            for (int i = 0; i < ids.Count; ++i) {
                Vector3d target;
                output[i] = handles.TryGetValue(ids[i], out target) ? target : origins[i] + scales[i] * positions[i];
                if (!output[i].IsFinite) return null;
            }
            var copy = new DMesh3(Mesh);
            for (int i = 0; i < ids.Count; ++i) copy.SetVertex(ids[i], output[i]);
            if (copy.HasVertexNormals) MeshNormals.QuickCompute(copy);
            return copy;
        }
    }
}
