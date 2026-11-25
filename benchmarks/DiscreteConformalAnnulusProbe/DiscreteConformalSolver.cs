// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using g3;

/// <summary>
/// Prototype discrete conformal equivalence solver (Springborn-Schroeder-Pinkall 2008;
/// Bobenko-Pinkall-Springborn 2015; Gillespie-Springborn-Crane 2021) using Ptolemy intrinsic
/// Delaunay flips and damped Newton minimization of the convex discrete conformal energy (with the
/// Lobachevsky function) via geometry3Sharp's SparseCholesky.
/// </summary>
internal sealed class DiscreteConformalSolver
{
    public enum BoundaryMode
    {
        /// <summary>
        /// Uniform boundary turning per loop (+2*pi on outer loop, -2*pi on inner holes).
        /// </summary>
        UniformLoopTurning,

        /// <summary>
        /// Free boundary curvature with u = 0 fixed on all boundary vertices (Dirichlet).
        /// </summary>
        FixedBoundaryScale,

        /// <summary>
        /// Boundary turning per loop distributed by input arc length: a boundary vertex gets the share
        /// (l_prev + l_next) / (2 * perimeter) of the loop's +2*pi (outer) or -2*pi (inner holes), with the
        /// 3D lengths of the two incident boundary edges. A second boundary-curvature choice next to the
        /// uniform one, to see whether the translational holonomy depends on it.
        /// </summary>
        ArcLengthLoopTurning,
    }

    public sealed class SolveResult
    {
        public bool Converged;
        public int NewtonIterations;
        public int InitialFlips;
        public int SolveFlips;
        public int TotalFlipChecks;
        public int NearTieChecks;
        // Consistency check only: for Euclidean triangles the two signs agree by a theorem
        // ((a^2 + b^2 - e^2)/(ab) = 2 cos(gamma), and cos(g1) + cos(g2) >= 0 iff g1 + g2 <= pi
        // iff cot(g1) + cot(g2) >= 0); non-Euclidean diamonds are skipped.
        public int CotanVsHyperbolicSignDisagreements;
        // Accepted Newton iterates whose trial scale factors, on the triangulation before the
        // re-flip, gave at least one triangle that violates the triangle inequality.
        public int AcceptedIteratesNonEuclideanBeforeFlip;
        // Sum over accepted iterates of the number of such triangles.
        public int NonEuclideanTrianglesAtAcceptedIterates;
        // Line-search acceptances by the Armijo energy-descent test, and by the round-off fallback
        // (energy not above the start by more than its round-off bound and a smaller angle residual).
        public int ArmijoAcceptances;
        public int RoundoffFallbackAcceptances;
        // Negative cotan Hessian edge weights (kept, not clamped), split by interior / boundary edge, and the
        // most negative ones.
        public int NegativeInteriorHessianWeights;
        public int NegativeBoundaryHessianWeights;
        public double MostNegativeInteriorHessianWeight;
        public double MostNegativeBoundaryHessianWeight;
        public double MaxAngleResidual;
        public double MaxEdgeScaleResidual;
        public double MaxRotationalHolonomyRad;
        public double MaxTranslationalHolonomyRel;
        public int IntrinsicTriangles;
        public int FlippedIntrinsicTriangles;
        public int DegenerateIntrinsicTriangles;
        public int NonAdjacentBoundaryContacts;
        public double MeanIntrinsicConformalDistortion;
        public double MaxIntrinsicConformalDistortion;
        public double ElapsedMs;
        public double[] LogScale = Array.Empty<double>();
        public Vector2d[] SeamlessVertexUV;
        public string Note = "";
    }

    private readonly int _vertexCount;
    private readonly int _triCount;
    private readonly int[] _head;
    private readonly int[] _twin;
    private readonly double[] _baseLogLen;
    private readonly bool[] _isBoundaryVertex;
    private readonly List<List<int>> _boundaryLoops;
    private readonly List<double[]> _loopEdgeLen3D; // _loopEdgeLen3D[li][i] = 3D length of loop[i] -> loop[i+1]
    private readonly int[] _compactToV;

    // 8-point Gauss-Legendre nodes and weights on [0, 1] for Milnor-Lobachevsky function.
    private static readonly double[] GlNodes = {
        0.019855071751231884, 0.10166676129318663, 0.23723379504183551, 0.40828267875217510,
        0.59171732124782490,  0.76276620495816449, 0.89833323870681337, 0.98014492824876812,
    };
    private static readonly double[] GlWeights = {
        0.050614268145188130, 0.11119051722668724, 0.15685332293894364, 0.18134189168918099,
        0.18134189168918099,  0.15685332293894364, 0.11119051722668724, 0.050614268145188130,
    };

    private DiscreteConformalSolver(
        int vertexCount,
        int triCount,
        int[] head,
        int[] twin,
        double[] baseLogLen,
        bool[] isBoundaryVertex,
        List<List<int>> boundaryLoops,
        List<double[]> loopEdgeLen3D,
        int[] compactToV)
    {
        _vertexCount = vertexCount;
        _triCount = triCount;
        _head = head;
        _twin = twin;
        _baseLogLen = baseLogLen;
        _isBoundaryVertex = isBoundaryVertex;
        _boundaryLoops = boundaryLoops;
        _loopEdgeLen3D = loopEdgeLen3D;
        _compactToV = compactToV;
    }

    /// <summary> Input vertex id of compact vertex index i (the index of SeamlessVertexUV). </summary>
    public int InputVertexID(int i) => _compactToV[i];

    public static DiscreteConformalSolver FromMesh(DMesh3 mesh)
    {
        int[] vToCompact = new int[mesh.MaxVertexID];
        Array.Fill(vToCompact, -1);
        var compactToV = new List<int>(mesh.VertexCount);
        foreach (int vid in mesh.VertexIndices()) {
            vToCompact[vid] = compactToV.Count;
            compactToV.Add(vid);
        }

        int fCount = mesh.TriangleCount;
        int heCount = 3 * fCount;
        int[] head = new int[heCount];
        int[] twin = new int[heCount];
        Array.Fill(twin, -1);
        double[] baseLogLen = new double[heCount];
        var edgeMap = new Dictionary<(int, int), int>(heCount);

        int f = 0;
        foreach (int tid in mesh.TriangleIndices()) {
            Index3i tri = mesh.GetTriangle(tid);
            int a = vToCompact[tri.a], b = vToCompact[tri.b], c = vToCompact[tri.c];
            int[] vs = { a, b, c };
            Vector3d[] ps = { mesh.GetVertex(tri.a), mesh.GetVertex(tri.b), mesh.GetVertex(tri.c) };
            for (int k = 0; k < 3; ++k) {
                int h = 3 * f + k;
                int u = vs[k], v = vs[(k + 1) % 3];
                head[h] = v;
                double len = ps[k].Distance(ps[(k + 1) % 3]);
                if (len <= 0)
                    throw new InvalidOperationException("Zero-length input edge.");
                baseLogLen[h] = Math.Log(len);
                if (edgeMap.TryGetValue((v, u), out int opp)) {
                    twin[h] = opp;
                    twin[opp] = h;
                } else {
                    edgeMap[(u, v)] = h;
                }
            }
            ++f;
        }

        bool[] isBoundary = new bool[compactToV.Count];
        var nextBoundary = new Dictionary<int, int>();
        for (int h = 0; h < heCount; ++h) {
            if (twin[h] < 0) {
                int u = head[Prev(h)], v = head[h];
                isBoundary[u] = true;
                isBoundary[v] = true;
                nextBoundary[u] = v;
            }
        }

        // Trace boundary loops in mesh orientation and order by descending 3D perimeter (outer first).
        var loops = new List<(double Perim, List<int> Verts)>();
        var visited = new HashSet<int>();
        foreach (int start in nextBoundary.Keys) {
            if (!visited.Add(start))
                continue;
            var loop = new List<int> { start };
            double perim = 0;
            int cur = start;
            while (nextBoundary.TryGetValue(cur, out int nxt)) {
                perim += mesh.GetVertex(compactToV[cur]).Distance(mesh.GetVertex(compactToV[nxt]));
                if (nxt == start)
                    break;
                if (!visited.Add(nxt))
                    break;
                loop.Add(nxt);
                cur = nxt;
            }
            loops.Add((perim, loop));
        }
        loops.Sort((x, y) => y.Perim.CompareTo(x.Perim));
        var loopLens = new List<double[]>(loops.Count);
        foreach (var (_, verts) in loops) {
            var lens = new double[verts.Count];
            for (int i = 0; i < verts.Count; ++i)
                lens[i] = mesh.GetVertex(compactToV[verts[i]]).Distance(mesh.GetVertex(compactToV[verts[(i + 1) % verts.Count]]));
            loopLens.Add(lens);
        }

        return new DiscreteConformalSolver(
            compactToV.Count,
            fCount,
            head,
            twin,
            baseLogLen,
            isBoundary,
            loops.Select(l => l.Verts).ToList(),
            loopLens,
            compactToV.ToArray());
    }

    private static int Next(int h) => 3 * (h / 3) + (h + 1) % 3;
    private static int Prev(int h) => 3 * (h / 3) + (h + 2) % 3;
    private int Tail(int h) => _head[Prev(h)];

    private double ScaledLen(int h, double[] u) =>
        Math.Exp(_baseLogLen[h] + 0.5 * (u[Tail(h)] + u[_head[h]]));

    private static double LogAddExp(double a, double b)
    {
        double m = Math.Max(a, b);
        return m + Math.Log(Math.Exp(a - m) + Math.Exp(b - m));
    }

    private static double Lobachevsky(double alpha)
    {
        if (alpha <= 0 || alpha >= Math.PI)
            return 0;
        if (alpha > 0.5 * Math.PI)
            return -Lobachevsky(Math.PI - alpha);
        double integral = 0;
        for (int i = 0; i < GlNodes.Length; ++i) {
            double t = alpha * GlNodes[i];
            integral += GlWeights[i] * Math.Log(Math.Sin(t) / t);
        }
        return alpha * (1.0 - Math.Log(2.0 * alpha) - integral);
    }

    private double HyperbolicDelaunayValue(int h, double[] u)
    {
        int t = _twin[h];
        int h1 = Next(h), h2 = Prev(h);
        int t1 = Next(t), t2 = Prev(t);
        double e = ScaledLen(h, u);
        double a = ScaledLen(h2, u), b = ScaledLen(h1, u);
        double c = ScaledLen(t2, u), d = ScaledLen(t1, u);
        // Exact Penner / Bobenko-Pinkall-Springborn hyperbolic Delaunay criterion:
        // (a^2 + b^2 - e^2)/(a*b) + (c^2 + d^2 - e^2)/(c*d) >= 0.
        // Scale by e to avoid overflow/underflow.
        double ae = a / e, be = b / e, ce = c / e, de = d / e;
        return (ae * ae + be * be - 1.0) / (ae * be) + (ce * ce + de * de - 1.0) / (ce * de);
    }

    private bool TryCotanSum(int h, double[] u, out double cotSum)
    {
        int t = _twin[h];
        if (!TryOppositeCotan(h, u, out double c1) || !TryOppositeCotan(t, u, out double c2)) {
            cotSum = double.NaN;
            return false;
        }
        cotSum = c1 + c2;
        return true;
    }

    private bool TryOppositeCotan(int h, double[] u, out double cot)
    {
        double e = ScaledLen(h, u);
        double b = ScaledLen(Next(h), u);
        double a = ScaledLen(Prev(h), u);
        double s = 0.5 * (a + b + e);
        double areaSq = s * (s - a) * (s - b) * (s - e);
        if (areaSq <= 0) {
            cot = double.NaN;
            return false;
        }
        cot = (a * a + b * b - e * e) / (4.0 * Math.Sqrt(areaSq));
        return true;
    }

    private bool CanFlip(int h)
    {
        int t = _twin[h];
        if (t < 0)
            return false;
        int f1 = h / 3, f2 = t / 3;
        if (f1 == f2)
            return false;
        int h1 = Next(h), h2 = Prev(h);
        if (h1 == t || h2 == t)
            return false;
        return true;
    }

    private void FlipEdgePtolemy(int h)
    {
        int t = _twin[h];
        int h1 = Next(h), h2 = Prev(h);
        int t1 = Next(t), t2 = Prev(t);

        int i = _head[h2];
        int j = _head[h];
        int k = _head[h1];
        int l = _head[t1];

        // Cross-ratio invariant Ptolemy update in unscaled log-lengths:
        // log(l_kl) = log((l_ki * l_lj + l_jk * l_il) / l_ij).
        double newBaseLog = LogAddExp(_baseLogLen[h2] + _baseLogLen[t2], _baseLogLen[h1] + _baseLogLen[t1])
            - _baseLogLen[h];

        int tw_h1 = _twin[h1], tw_h2 = _twin[h2];
        int tw_t1 = _twin[t1], tw_t2 = _twin[t2];
        double L_h1 = _baseLogLen[h1], L_h2 = _baseLogLen[h2];
        double L_t1 = _baseLogLen[t1], L_t2 = _baseLogLen[t2];

        int MapMoved(int id)
        {
            if (id == h2) return h1;
            if (id == t1) return h2;
            if (id == t2) return t1;
            if (id == h1) return t2;
            return id;
        }

        // New tri f1 = (l, k, i): h: l->k, h1: k->i, h2: i->l
        _head[h] = k;
        _head[h1] = i;
        _head[h2] = l;
        _baseLogLen[h] = newBaseLog;
        _baseLogLen[h1] = L_h2;
        _baseLogLen[h2] = L_t1;

        // New tri f2 = (k, l, j): t: k->l, t1: l->j, t2: j->k
        _head[t] = l;
        _head[t1] = j;
        _head[t2] = k;
        _baseLogLen[t] = newBaseLog;
        _baseLogLen[t1] = L_t2;
        _baseLogLen[t2] = L_h1;

        _twin[h] = t;
        _twin[t] = h;

        void Attach(int slot, int oldTwin)
        {
            int mapped = oldTwin >= 0 ? MapMoved(oldTwin) : -1;
            _twin[slot] = mapped;
            if (mapped >= 0)
                _twin[mapped] = slot;
        }

        Attach(h1, tw_h2);
        Attach(h2, tw_t1);
        Attach(t1, tw_t2);
        Attach(t2, tw_h1);
    }

    private int FlipToHyperbolicDelaunay(double[] u, SolveResult stats, double eps = 1e-12, int maxFlips = 50000)
    {
        int heCount = 3 * _triCount;
        var inQueue = new bool[heCount];
        var queue = new Queue<int>(heCount);
        for (int h = 0; h < heCount; ++h) {
            if (_twin[h] > h) {
                queue.Enqueue(h);
                inQueue[h] = true;
            }
        }

        int flips = 0;
        while (queue.Count > 0 && flips < maxFlips) {
            int h = queue.Dequeue();
            inQueue[h] = false;
            if (!CanFlip(h))
                continue;
            int t = _twin[h];
            ++stats.TotalFlipChecks;
            double hyp = HyperbolicDelaunayValue(h, u);
            if (Math.Abs(hyp) < 1e-10)
                ++stats.NearTieChecks;
            if (TryCotanSum(h, u, out double cotSum)) {
                if ((hyp < -eps && cotSum > eps) || (hyp > eps && cotSum < -eps))
                    ++stats.CotanVsHyperbolicSignDisagreements;
            }

            if (hyp < -eps) {
                FlipEdgePtolemy(h);
                ++flips;
                int[] affected = { Next(h), Prev(h), Next(t), Prev(t) };
                foreach (int a in affected) {
                    int tw = _twin[a];
                    if (tw < 0)
                        continue;
                    int canon = Math.Min(a, tw);
                    if (!inQueue[canon]) {
                        queue.Enqueue(canon);
                        inQueue[canon] = true;
                    }
                }
            }
        }
        return flips;
    }

    private int CountNonEuclideanTriangles(double[] u)
    {
        int count = 0;
        for (int f = 0; f < _triCount; ++f) {
            double l0 = ScaledLen(3 * f, u), l1 = ScaledLen(3 * f + 1, u), l2 = ScaledLen(3 * f + 2, u);
            if (!(l0 < l1 + l2 && l1 < l2 + l0 && l2 < l0 + l1))
                ++count;
        }
        return count;
    }

    private bool EvaluateAnglesAndEnergy(
        double[] u,
        double[] targetTheta,
        out double[] theta,
        out double energy,
        out double energyRoundoff,
        out double[] halfEdgeCotan)
    {
        theta = new double[_vertexCount];
        halfEdgeCotan = new double[3 * _triCount];
        double sumTri = 0, sumAbs = 0;
        for (int f = 0; f < _triCount; ++f) {
            int h0 = 3 * f, h1 = 3 * f + 1, h2 = 3 * f + 2;
            // Edges: h0 = v0->v1 (opposite v2), h1 = v1->v2 (opposite v0), h2 = v2->v0 (opposite v1)
            int v0 = _head[h2], v1 = _head[h0], v2 = _head[h1];
            double lam2 = _baseLogLen[h0] + 0.5 * (u[v0] + u[v1]);
            double lam0 = _baseLogLen[h1] + 0.5 * (u[v1] + u[v2]);
            double lam1 = _baseLogLen[h2] + 0.5 * (u[v2] + u[v0]);
            double l0 = Math.Exp(lam0), l1 = Math.Exp(lam1), l2 = Math.Exp(lam2);

            double s = 0.5 * (l0 + l1 + l2);
            double areaSq = s * (s - l0) * (s - l1) * (s - l2);
            if (areaSq <= 0 || !double.IsFinite(areaSq)) {
                energy = double.PositiveInfinity;
                energyRoundoff = 0;
                return false;
            }
            double fourArea = 4.0 * Math.Sqrt(areaSq);

            double c0 = Math.Clamp((l1 * l1 + l2 * l2 - l0 * l0) / (2.0 * l1 * l2), -1.0, 1.0);
            double c1 = Math.Clamp((l2 * l2 + l0 * l0 - l1 * l1) / (2.0 * l2 * l0), -1.0, 1.0);
            double c2 = Math.Clamp((l0 * l0 + l1 * l1 - l2 * l2) / (2.0 * l0 * l1), -1.0, 1.0);
            double a0 = Math.Acos(c0);
            double a1 = Math.Acos(c1);
            double a2 = Math.Acos(c2);

            theta[v0] += a0;
            theta[v1] += a1;
            theta[v2] += a2;

            halfEdgeCotan[h0] = (l0 * l0 + l1 * l1 - l2 * l2) / fourArea;
            halfEdgeCotan[h1] = (l1 * l1 + l2 * l2 - l0 * l0) / fourArea;
            halfEdgeCotan[h2] = (l2 * l2 + l0 * l0 - l1 * l1) / fourArea;

            // Full per-triangle term of Springborn-Schroeder-Pinkall (2008) with lambda = 2 log(l):
            // 2 f(lambda) - (pi/2) * sum(lambda), f = 1/2 sum(alpha*lambda) + sum(Lobachevsky(alpha)).
            // The -pi*sum(log l) term includes the u-independent part, which changes under a Ptolemy
            // flip; with it the energy is one function of u across flips (Bobenko-Pinkall-Springborn 2015).
            double tri = 2.0 * (a0 * lam0 + a1 * lam1 + a2 * lam2
                + Lobachevsky(a0) + Lobachevsky(a1) + Lobachevsky(a2))
                - Math.PI * (lam0 + lam1 + lam2);
            sumTri += tri;
            sumAbs += 2.0 * (Math.Abs(a0 * lam0) + Math.Abs(a1 * lam1) + Math.Abs(a2 * lam2))
                + Math.PI * (Math.Abs(lam0) + Math.Abs(lam1) + Math.Abs(lam2)) + 1.0;
        }

        double lin = 0;
        for (int v = 0; v < _vertexCount; ++v) {
            lin += targetTheta[v] * u[v];
            sumAbs += Math.Abs(targetTheta[v] * u[v]);
        }

        energy = sumTri + lin;
        // Generous bound for the round-off of the sum (and the quadrature error of the Lobachevsky
        // function, about 1e-12 per call for 8-point Gauss-Legendre on [0, pi/2]).
        energyRoundoff = 64.0 * 1.1102230246251565e-16 * sumAbs + 1e-12 * 3 * _triCount;
        return double.IsFinite(energy);
    }

    public SolveResult Solve(BoundaryMode mode, int maxNewtonIters = 60, double tol = 1e-11)
    {
        var sw = Stopwatch.StartNew();
        var res = new SolveResult();
        double[] u = new double[_vertexCount];

        // Build target angles and fixed-vertex mask.
        double[] targetTheta = new double[_vertexCount];
        bool[] fixedU = new bool[_vertexCount];
        for (int v = 0; v < _vertexCount; ++v)
            targetTheta[v] = 2.0 * Math.PI;

        if (mode == BoundaryMode.FixedBoundaryScale) {
            for (int v = 0; v < _vertexCount; ++v) {
                if (_isBoundaryVertex[v])
                    fixedU[v] = true;
            }
        } else {
            for (int li = 0; li < _boundaryLoops.Count; ++li) {
                var loop = _boundaryLoops[li];
                double turnSign = li == 0 ? 1.0 : -1.0; // outer loop +2*pi, inner holes -2*pi
                double[] lens = _loopEdgeLen3D[li];
                double perim = lens.Sum();
                for (int i = 0; i < loop.Count; ++i) {
                    double share = mode == BoundaryMode.ArcLengthLoopTurning
                        ? 0.5 * (lens[(i + loop.Count - 1) % loop.Count] + lens[i]) / perim
                        : 1.0 / loop.Count;
                    targetTheta[loop[i]] = Math.PI - turnSign * 2.0 * Math.PI * share;
                }
            }
            fixedU[0] = true; // fix global scale factor
        }

        int[] varIndex = new int[_vertexCount];
        Array.Fill(varIndex, -1);
        int freeCount = 0;
        for (int v = 0; v < _vertexCount; ++v) {
            if (!fixedU[v])
                varIndex[v] = freeCount++;
        }

        res.InitialFlips = FlipToHyperbolicDelaunay(u, res);
        if (!EvaluateAnglesAndEnergy(u, targetTheta, out double[] theta, out double energy, out double energyRoundoff, out double[] halfEdgeCotan)) {
            res.Note = "Initial Delaunay triangulation is non-Euclidean.";
            res.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        double ComputeResidual(double[] curTheta)
        {
            double maxR = 0;
            for (int v = 0; v < _vertexCount; ++v) {
                if (!fixedU[v])
                    maxR = Math.Max(maxR, Math.Abs(curTheta[v] - targetTheta[v]));
            }
            return maxR;
        }

        double maxRes = ComputeResidual(theta);
        for (int iter = 0; iter < maxNewtonIters && maxRes > tol; ++iter) {
            if (freeCount == 0)
                break;

            // Assemble the cotan Hessian on free vertices:
            // H_ii = sum_e 0.5*(cot_L + cot_R), H_ij = -0.5*(cot_L + cot_R).
            // This is the exact Hessian of the energy (Springborn-Schroeder-Pinkall 2008). It is the cotan
            // Laplacian of a Euclidean triangulation, so it is positive semi-definite even with negative
            // weights (boundary edges with an obtuse opposite angle are not constrained by the Delaunay
            // flips), and positive definite on the free vertices. Weights are not clamped; negative ones
            // are only counted (NegativeHessianWeights).
            var entries = new List<KeyValuePair<Index2i, double>>(6 * _triCount);
            double[] diag = new double[freeCount];
            for (int h = 0; h < 3 * _triCount; ++h) {
                int tw = _twin[h];
                if (tw >= 0 && tw < h)
                    continue;
                double w = 0.5 * (halfEdgeCotan[h] + (tw >= 0 ? halfEdgeCotan[tw] : 0.0));
                if (w < 0) {
                    if (tw >= 0) {
                        ++res.NegativeInteriorHessianWeights;
                        res.MostNegativeInteriorHessianWeight = Math.Min(res.MostNegativeInteriorHessianWeight, w);
                    } else {
                        ++res.NegativeBoundaryHessianWeights;
                        res.MostNegativeBoundaryHessianWeight = Math.Min(res.MostNegativeBoundaryHessianWeight, w);
                    }
                }
                int a = Tail(h), b = _head[h];
                int ia = varIndex[a], ib = varIndex[b];
                if (ia >= 0) diag[ia] += w;
                if (ib >= 0) diag[ib] += w;
                if (ia >= 0 && ib >= 0 && ia != ib) {
                    int lo = Math.Min(ia, ib), hi = Math.Max(ia, ib);
                    entries.Add(new KeyValuePair<Index2i, double>(new Index2i(lo, hi), -w));
                }
            }
            for (int i = 0; i < freeCount; ++i)
                entries.Add(new KeyValuePair<Index2i, double>(new Index2i(i, i), diag[i]));

            double[] rhs = new double[freeCount];
            for (int v = 0; v < _vertexCount; ++v) {
                int iv = varIndex[v];
                if (iv >= 0)
                    rhs[iv] = theta[v] - targetTheta[v]; // -grad E = Theta(u) - Theta_target
            }

            var chol = new SparseCholesky(freeCount, entries);
            if (!chol.IsValid) {
                res.Note = $"SparseCholesky failed at iter {iter}: {chol.Error}";
                break;
            }
            double[] step = new double[freeCount];
            chol.Solve(rhs, step);
            // Directional derivative of E along step: grad E = -rhs, so grad.step = -rhs.step < 0.
            double slope = 0;
            for (int i = 0; i < freeCount; ++i)
                slope -= rhs[i] * step[i];

            // Clamp extreme single step to avoid exp overflow in trial points.
            double maxStep = step.Max(Math.Abs);
            double alpha = maxStep > 4.0 ? 4.0 / maxStep : 1.0;

            // Save triangulation state for backtracking line search.
            int[] savedHead = (int[])_head.Clone();
            int[] savedTwin = (int[])_twin.Clone();
            double[] savedLogLen = (double[])_baseLogLen.Clone();

            bool accepted = false;
            double[] trialU = new double[_vertexCount];
            double[] trialTheta = theta;
            double[] trialCotan = halfEdgeCotan;
            double trialEnergy = energy;
            double trialRoundoff = energyRoundoff;
            double trialRes = maxRes;
            int trialNonEuclidean = 0;

            for (int ls = 0; ls < 30; ++ls) {
                Array.Copy(savedHead, _head, _head.Length);
                Array.Copy(savedTwin, _twin, _twin.Length);
                Array.Copy(savedLogLen, _baseLogLen, _baseLogLen.Length);

                Array.Copy(u, trialU, _vertexCount);
                for (int v = 0; v < _vertexCount; ++v) {
                    int iv = varIndex[v];
                    if (iv >= 0)
                        trialU[v] += alpha * step[iv];
                }

                trialNonEuclidean = CountNonEuclideanTriangles(trialU);
                int stepFlips = FlipToHyperbolicDelaunay(trialU, res);
                if (EvaluateAnglesAndEnergy(trialU, targetTheta, out trialTheta, out trialEnergy, out trialRoundoff, out trialCotan)) {
                    trialRes = ComputeResidual(trialTheta);
                    // Armijo energy descent (c = 1e-4) on the full energy, which is comparable across flips.
                    bool armijo = trialEnergy <= energy + 1e-4 * alpha * slope;
                    // Near convergence the decrease (about residual^2) falls below the round-off of E;
                    // then accept a step that does not raise E beyond that bound and lowers the residual.
                    bool roundoff = !armijo
                        && trialEnergy <= energy + Math.Max(energyRoundoff, trialRoundoff)
                        && trialRes < maxRes;
                    if (armijo || roundoff) {
                        if (armijo) ++res.ArmijoAcceptances; else ++res.RoundoffFallbackAcceptances;
                        res.SolveFlips += stepFlips;
                        accepted = true;
                        break;
                    }
                }
                alpha *= 0.5;
            }

            if (!accepted) {
                Array.Copy(savedHead, _head, _head.Length);
                Array.Copy(savedTwin, _twin, _twin.Length);
                Array.Copy(savedLogLen, _baseLogLen, _baseLogLen.Length);
                res.Note = $"Line search stalled at iter {iter} (res={maxRes:G4}).";
                break;
            }

            u = trialU;
            theta = trialTheta;
            halfEdgeCotan = trialCotan;
            energy = trialEnergy;
            energyRoundoff = trialRoundoff;
            maxRes = trialRes;
            if (trialNonEuclidean > 0) {
                ++res.AcceptedIteratesNonEuclideanBeforeFlip;
                res.NonEuclideanTrianglesAtAcceptedIterates += trialNonEuclidean;
            }
            res.NewtonIterations = iter + 1;
        }

        res.Converged = maxRes <= tol;
        res.MaxAngleResidual = maxRes;
        res.LogScale = u;
        res.IntrinsicTriangles = _triCount;

        // Unfold the resulting intrinsic triangulation across a spanning tree and measure holonomy.
        LayoutAndMeasure(u, res);
        res.ElapsedMs = sw.Elapsed.TotalMilliseconds;
        return res;
    }

    private void LayoutAndMeasure(double[] u, SolveResult res)
    {
        var triCoords = new Vector2d[_triCount, 3];
        var placed = new bool[_triCount];
        var queue = new Queue<int>();

        // Seed triangle 0.
        double l01 = ScaledLen(0, u);
        double l12 = ScaledLen(1, u);
        double l20 = ScaledLen(2, u);
        triCoords[0, 0] = Vector2d.Zero;
        triCoords[0, 1] = new Vector2d(l01, 0);
        triCoords[0, 2] = ThirdVertexCCW(triCoords[0, 0], triCoords[0, 1], l20, l12);
        placed[0] = true;
        queue.Enqueue(0);

        while (queue.Count > 0) {
            int f = queue.Dequeue();
            for (int k = 0; k < 3; ++k) {
                int h = 3 * f + k;
                int tw = _twin[h];
                if (tw < 0)
                    continue;
                int fAdj = tw / 3;
                if (placed[fAdj])
                    continue;
                int kAdj = tw % 3;
                // h goes local k -> (k+1)%3; tw goes local kAdj -> (kAdj+1)%3 in opposite direction.
                Vector2d pA = triCoords[f, (k + 1) % 3]; // tail of tw = kAdj
                Vector2d pB = triCoords[f, k];           // head of tw = (kAdj+1)%3
                int kThird = (kAdj + 2) % 3;
                double lenAToThird = ScaledLen(3 * fAdj + kThird, u);     // edge kThird -> kAdj
                double lenBToThird = ScaledLen(3 * fAdj + (kAdj + 1) % 3, u); // edge (kAdj+1)%3 -> kThird
                triCoords[fAdj, kAdj] = pA;
                triCoords[fAdj, (kAdj + 1) % 3] = pB;
                triCoords[fAdj, kThird] = ThirdVertexCCW(pA, pB, lenAToThird, lenBToThird);
                placed[fAdj] = true;
                queue.Enqueue(fAdj);
            }
        }

        // Bounding diameter for relative translational holonomy.
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        int flipped = 0, degen = 0;
        double sumArea = 0, sumConfArea = 0, maxConf = 0;
        double maxEdgeRes = 0;

        for (int f = 0; f < _triCount; ++f) {
            Vector2d p0 = triCoords[f, 0], p1 = triCoords[f, 1], p2 = triCoords[f, 2];
            for (int k = 0; k < 3; ++k) {
                Vector2d p = triCoords[f, k];
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
                minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
            }
            // ThirdVertexCCW places every third vertex on the left of its edge (with ySq clamped
            // to >= 0), so 'flipped' is 0 by construction; it guards the layout code, not the solve.
            // The degeneracy test is relative to the longest edge squared (scale-free).
            double area2 = (p1.x - p0.x) * (p2.y - p0.y) - (p1.y - p0.y) * (p2.x - p0.x);
            double maxLenSq = Math.Max((p1 - p0).LengthSquared, Math.Max((p2 - p1).LengthSquared, (p0 - p2).LengthSquared));
            if (area2 < -1e-12 * maxLenSq) ++flipped;
            else if (Math.Abs(area2) <= 1e-12 * maxLenSq) ++degen;

            // Check edge length match against scaled intrinsic metric.
            double e0 = p0.Distance(p1), s0 = ScaledLen(3 * f + 0, u);
            double e1 = p1.Distance(p2), s1 = ScaledLen(3 * f + 1, u);
            double e2 = p2.Distance(p0), s2 = ScaledLen(3 * f + 2, u);
            maxEdgeRes = Math.Max(maxEdgeRes, Math.Abs(e0 / s0 - 1.0));
            maxEdgeRes = Math.Max(maxEdgeRes, Math.Abs(e1 / s1 - 1.0));
            maxEdgeRes = Math.Max(maxEdgeRes, Math.Abs(e2 / s2 - 1.0));

            // Conformal distortion of the affine map from unscaled intrinsic triangle to laid-out UV triangle.
            double o0 = Math.Exp(_baseLogLen[3 * f + 0]);
            double o1 = Math.Exp(_baseLogLen[3 * f + 1]);
            double o2 = Math.Exp(_baseLogLen[3 * f + 2]);
            double sOrig = 0.5 * (o0 + o1 + o2);
            double origAreaSq = sOrig * (sOrig - o0) * (sOrig - o1) * (sOrig - o2);
            if (origAreaSq > 0 && area2 > 0) {
                Vector2d q0 = Vector2d.Zero;
                Vector2d q1 = new Vector2d(o0, 0);
                Vector2d q2 = ThirdVertexCCW(q0, q1, o2, o1);
                double qc = ConformalRatio(q0, q1, q2, p0, p1, p2);
                double origArea = Math.Sqrt(origAreaSq);
                sumArea += origArea;
                sumConfArea += origArea * qc;
                maxConf = Math.Max(maxConf, qc);
            }
        }

        double diag = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        double maxRot = 0, maxTrans = 0;
        for (int h = 0; h < 3 * _triCount; ++h) {
            int tw = _twin[h];
            if (tw < 0 || tw < h)
                continue;
            int f1 = h / 3, k1 = h % 3;
            int f2 = tw / 3, k2 = tw % 3;
            Vector2d a1 = triCoords[f1, k1], b1 = triCoords[f1, (k1 + 1) % 3];
            Vector2d b2 = triCoords[f2, k2], a2 = triCoords[f2, (k2 + 1) % 3];
            Vector2d d1 = b1 - a1, d2 = b2 - a2;
            double rot = Math.Abs(Math.Atan2(d1.x * d2.y - d1.y * d2.x, d1.x * d2.x + d1.y * d2.y));
            double trans = Math.Max(a1.Distance(a2), b1.Distance(b2));
            maxRot = Math.Max(maxRot, rot);
            maxTrans = Math.Max(maxTrans, trans);
        }

        res.FlippedIntrinsicTriangles = flipped;
        res.DegenerateIntrinsicTriangles = degen;
        res.MaxEdgeScaleResidual = maxEdgeRes;
        res.MaxRotationalHolonomyRad = maxRot;
        res.MaxTranslationalHolonomyRel = diag > 0 ? maxTrans / diag : maxTrans;
        res.MeanIntrinsicConformalDistortion = sumArea > 0 ? sumConfArea / sumArea : double.NaN;
        res.MaxIntrinsicConformalDistortion = maxConf;

        if (res.MaxTranslationalHolonomyRel < 1e-8 && res.MaxRotationalHolonomyRad < 1e-8) {
            var uv = new Vector2d[_vertexCount];
            for (int f = 0; f < _triCount; ++f) {
                for (int k = 0; k < 3; ++k)
                    uv[_head[3 * f + (k + 2) % 3]] = triCoords[f, k];
            }
            res.SeamlessVertexUV = uv;
            res.NonAdjacentBoundaryContacts = CountBoundaryContacts(uv);
        } else {
            res.NonAdjacentBoundaryContacts = -1;
        }
    }

    private int CountBoundaryContacts(Vector2d[] uv)
    {
        var edges = new List<(int a, int b)>();
        for (int h = 0; h < 3 * _triCount; ++h) {
            if (_twin[h] < 0)
                edges.Add((Tail(h), _head[h]));
        }
        int count = 0;
        for (int i = 0; i < edges.Count; ++i) {
            var e = edges[i];
            for (int j = i + 1; j < edges.Count; ++j) {
                var f = edges[j];
                if (e.a == f.a || e.a == f.b || e.b == f.a || e.b == f.b)
                    continue;
                if (SegmentsIntersect(uv[e.a], uv[e.b], uv[f.a], uv[f.b]))
                    ++count;
            }
        }
        return count;
    }

    private static bool SegmentsIntersect(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
    {
        static bool Between(Vector2d p, Vector2d x, Vector2d y) =>
            Math.Min(x.x, y.x) <= p.x && p.x <= Math.Max(x.x, y.x)
            && Math.Min(x.y, y.y) <= p.y && p.y <= Math.Max(x.y, y.y);
        int abc = ExactPredicates.Orient2d(a, b, c), abd = ExactPredicates.Orient2d(a, b, d);
        int cda = ExactPredicates.Orient2d(c, d, a), cdb = ExactPredicates.Orient2d(c, d, b);
        return (abc * abd < 0 && cda * cdb < 0)
            || (abc == 0 && Between(c, a, b)) || (abd == 0 && Between(d, a, b))
            || (cda == 0 && Between(a, c, d)) || (cdb == 0 && Between(b, c, d));
    }

    private static Vector2d ThirdVertexCCW(Vector2d a, Vector2d b, double ac, double bc)
    {
        Vector2d ab = b - a;
        double c = ab.Length;
        Vector2d ex = ab / c;
        Vector2d ey = new Vector2d(-ex.y, ex.x);
        double x = (ac * ac + c * c - bc * bc) / (2.0 * c);
        double ySq = Math.Max(0.0, ac * ac - x * x);
        return a + x * ex + Math.Sqrt(ySq) * ey;
    }

    private static double ConformalRatio(
        Vector2d q0, Vector2d q1, Vector2d q2,
        Vector2d p0, Vector2d p1, Vector2d p2)
    {
        // Jacobian J maps (q1-q0, q2-q0) -> (p1-p0, p2-p0).
        double qx1 = q1.x - q0.x, qy1 = q1.y - q0.y;
        double qx2 = q2.x - q0.x, qy2 = q2.y - q0.y;
        double detQ = qx1 * qy2 - qy1 * qx2;
        double px1 = p1.x - p0.x, py1 = p1.y - p0.y;
        double px2 = p2.x - p0.x, py2 = p2.y - p0.y;
        double j00 = (px1 * qy2 - px2 * qy1) / detQ;
        double j01 = (-px1 * qx2 + px2 * qx1) / detQ;
        double j10 = (py1 * qy2 - py2 * qy1) / detQ;
        double j11 = (-py1 * qx2 + py2 * qx1) / detQ;
        double frobSq = j00 * j00 + j01 * j01 + j10 * j10 + j11 * j11;
        double detJ = j00 * j11 - j01 * j10;
        double disc = Math.Sqrt(Math.Max(0.0, frobSq * frobSq - 4.0 * detJ * detJ));
        double s1 = Math.Sqrt(Math.Max(1e-30, 0.5 * (frobSq + disc)));
        double s2 = Math.Sqrt(Math.Max(1e-30, 0.5 * (frobSq - disc)));
        return s1 / s2;
    }

    /// <summary>
    /// Exact integer check of the Penner hyperbolic Delaunay polynomial
    /// (a^2 + b^2 - e^2)*c*d + (c^2 + d^2 - e^2)*a*b on exact rational lengths (given as binary64 inputs).
    /// </summary>
    public static int ExactHyperbolicDelaunaySign(double a, double b, double c, double d, double e)
    {
        static (BigInteger m, int exp) Decompose(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int rawExp = (int)((bits >> 52) & 0x7FFL);
            long frac = bits & ((1L << 52) - 1);
            if (rawExp == 0)
                return (new BigInteger(frac), -1074);
            return (new BigInteger((1L << 52) | frac), rawExp - 1075);
        }

        var da = Decompose(a); var db = Decompose(b);
        var dc = Decompose(c); var dd = Decompose(d);
        var de = Decompose(e);
        int minExp = Math.Min(Math.Min(Math.Min(da.exp, db.exp), Math.Min(dc.exp, dd.exp)), de.exp);
        BigInteger A = da.m << (da.exp - minExp);
        BigInteger B = db.m << (db.exp - minExp);
        BigInteger C = dc.m << (dc.exp - minExp);
        BigInteger D = dd.m << (dd.exp - minExp);
        BigInteger E = de.m << (de.exp - minExp);

        BigInteger val = (A * A + B * B - E * E) * C * D + (C * C + D * D - E * E) * A * B;
        return val.Sign;
    }
}
