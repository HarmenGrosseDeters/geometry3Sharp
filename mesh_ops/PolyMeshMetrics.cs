// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Quality numbers of a polygon mesh (quad remeshing, docs/design/QUAD_REMESHING.md section 1): the face mix, the irregular
    /// vertices, the corner angles of the quads and the validity as a manifold polygon mesh. Faces that DPolyMesh.Validate() flags
    /// (fewer than 3 vertices, repeated or out of range indices) are skipped everywhere. Computed by Compute(); serial and deterministic.
    /// </summary>
    public class PolyMeshMetrics
    {
        /// <summary> faces used, and by number of sides </summary>
        public int Faces, Triangles, Quads, Pentagons, Hexagons, LargerFaces;
        /// <summary> the largest number of sides </summary>
        public int MaxSides;
        /// <summary> vertices used by a face, edges, V - E + F </summary>
        public int Vertices, Edges, EulerCharacteristic;
        /// <summary> vertices with a boundary edge (an edge of one face only) </summary>
        public int BoundaryVertices;
        /// <summary> vertices of valence other than 4 (other than 3 on the boundary: a quad at the boundary has two edges and a straight boundary three) </summary>
        public int IrregularVertices;
        /// <summary> edges in more than 2 faces, directed edges used twice (inconsistent orientation), vertices whose faces do not form one fan </summary>
        public int NonManifoldEdges, InconsistentEdges, NonManifoldVertices;
        /// <summary> the fraction of faces that are quads </summary>
        public double QuadFraction { get { return Faces == 0 ? 0 : (double)Quads / Faces; } }
        /// <summary> a manifold polygon mesh: no edge in more than 2 faces, consistently oriented, every vertex one fan </summary>
        public bool IsManifold { get { return Faces > 0 && NonManifoldEdges == 0 && InconsistentEdges == 0 && NonManifoldVertices == 0; } }

        /// <summary> |angle - 90| in degrees of every corner of every quad (sorted), and the longest / shortest edge of each quad (sorted) </summary>
        public double[] CornerDeviations = new double[0], Aspects = new double[0];

        /// <summary> the q-quantile (0..1) of the sorted values, NaN if empty </summary>
        public static double Percentile(double[] sorted, double q)
        {
            if (sorted == null || sorted.Length == 0) return double.NaN;
            return sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        }

        public static PolyMeshMetrics Compute(DPolyMesh mesh)
        {
            var r = new PolyMeshMetrics();
            int nv = mesh.VertexCount;
            var edgeFaces = new Dictionary<long, int>();       // undirected edge -> number of faces
            var directed = new HashSet<long>();
            var corners = new List<double>(); var aspects = new List<double>();
            var used = new bool[nv];
            var vFaces = new List<int>[nv];
            var faceOk = new bool[mesh.FaceCount];
            for (int f = 0; f < mesh.FaceCount; ++f) {
                int[] fv = mesh.GetFace(f);
                if (fv.Length < 3) continue;
                bool ok = true;
                var seen = new HashSet<int>();
                foreach (int v in fv) if (v < 0 || v >= nv || !seen.Add(v)) { ok = false; break; }
                if (!ok) continue;
                faceOk[f] = true;
                r.Faces++; r.MaxSides = Math.Max(r.MaxSides, fv.Length);
                if (fv.Length == 3) r.Triangles++; else if (fv.Length == 4) r.Quads++; else if (fv.Length == 5) r.Pentagons++; else if (fv.Length == 6) r.Hexagons++; else r.LargerFaces++;
                for (int k = 0; k < fv.Length; ++k) {
                    int a = fv[k], b = fv[(k + 1) % fv.Length];
                    used[a] = true;
                    if (vFaces[a] == null) vFaces[a] = new List<int>();
                    vFaces[a].Add(f);
                    long key = a < b ? (long)a * nv + b : (long)b * nv + a;
                    int c; edgeFaces.TryGetValue(key, out c); edgeFaces[key] = c + 1;
                    if (!directed.Add((long)a * nv + b)) r.InconsistentEdges++;
                }
                if (fv.Length == 4) {
                    double mn = double.MaxValue, mx = 0;
                    for (int k = 0; k < 4; ++k) {
                        Vector3d p = mesh.GetVertex(fv[k]);
                        Vector3d u = mesh.GetVertex(fv[(k + 3) % 4]) - p, w = mesh.GetVertex(fv[(k + 1) % 4]) - p;
                        double lu = u.Length, lw = w.Length;
                        if (lu > 0 && lw > 0) corners.Add(Math.Abs(Vector3d.AngleD(u / lu, w / lw) - 90));
                        mn = Math.Min(mn, lw); mx = Math.Max(mx, lw);
                    }
                    aspects.Add(mx / Math.Max(mn, double.Epsilon));
                }
            }
            r.Edges = edgeFaces.Count;
            var valence = new int[nv]; var onBoundary = new bool[nv];
            foreach (var kv in edgeFaces) {
                int a = (int)(kv.Key / nv), b = (int)(kv.Key % nv);
                valence[a]++; valence[b]++;
                if (kv.Value == 1) { onBoundary[a] = true; onBoundary[b] = true; }
                else if (kv.Value > 2) r.NonManifoldEdges++;
            }
            for (int v = 0; v < nv; ++v) {
                if (!used[v]) continue;
                r.Vertices++;
                if (onBoundary[v]) { r.BoundaryVertices++; if (valence[v] != 3) r.IrregularVertices++; }
                else if (valence[v] != 4) r.IrregularVertices++;
                if (!OneFan(mesh, v, vFaces[v], nv, edgeFaces)) r.NonManifoldVertices++;
            }
            r.EulerCharacteristic = r.Vertices - r.Edges + r.Faces;
            corners.Sort(); aspects.Sort();
            r.CornerDeviations = corners.ToArray(); r.Aspects = aspects.ToArray();
            return r;
        }

        // the faces at v are connected through edges at v (one fan or one disc)
        static bool OneFan(DPolyMesh mesh, int v, List<int> faces, int nv, Dictionary<long, int> edgeFaces)
        {
            if (faces.Count <= 1) return true;
            // two faces at v are adjacent if they share an edge at v: union over the edges (v, w)
            var parent = new Dictionary<int, int>();
            foreach (int f in faces) parent[f] = f;
            Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            var byNeighbour = new Dictionary<int, int>();
            foreach (int f in faces) {
                int[] fv = mesh.GetFace(f);
                int k = Array.IndexOf(fv, v);
                foreach (int w in new[] { fv[(k + 1) % fv.Length], fv[(k + fv.Length - 1) % fv.Length] }) {
                    int other;
                    if (byNeighbour.TryGetValue(w, out other)) { int a = find(f), b = find(other); if (a != b) parent[a] = b; }
                    else byNeighbour[w] = f;
                }
            }
            int root = find(faces[0]);
            foreach (int f in faces) if (find(f) != root) return false;
            return true;
        }
    }
}
