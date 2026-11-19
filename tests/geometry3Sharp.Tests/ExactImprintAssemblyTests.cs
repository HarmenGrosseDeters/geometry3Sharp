using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshImprint on procedural contact assemblies (Harness/ContactAssemblies.cs, ContactAssemblyFixtures.cs): closed parts whose faces touch exactly,
    /// with an analytical ground truth. Each part is imprinted with its neighbour and the shared contact is checked from both sides. Only what
    /// docs/design/EXACT_IMPRINT.md promises is asserted:
    ///   G1  closed manifold target gives closed manifold result, target vertices and untouched triangles unchanged, pieces oriented as their source;
    ///   G2  before rounding, the imprinted curves are the arrangement's intersection curves as exact points: so both sides of a contact carry the
    ///       same curve edges and vertices (the arrangement is symmetric in the two parts);
    ///   G5  after rounding every curve edge is an edge of the result (or counted as contracted).
    /// The areas of the regions of the contact face split by the curves are compared with the hand-derived values, in exact rational arithmetic.
    /// What is not asserted: equal doubles of the two sides after SafeRounding (the repair edits each side on its own; only G3's bound is promised),
    /// except where neither side was edited (then both are the nearest double of the same exact point).
    /// </summary>
    public class ExactImprintAssemblyTests
    {
        readonly ITestOutputHelper output;
        public ExactImprintAssemblyTests(ITestOutputHelper output) { this.output = output; }

        public static IEnumerable<object[]> Fixtures() => ContactAssemblyFixtures.Names();

        // ---- exact helpers ----

        static ExactPoint3 Exact(Vector3d v)
        {
            // the lattice coordinates v * 8 are integers; the point is (8 v) / 8
            return ExactPoint3.Create(new BigInteger(Math.Round(v.x * 8)), new BigInteger(Math.Round(v.y * 8)), new BigInteger(Math.Round(v.z * 8)), 8);
        }

        static List<P2> UV(Contact c, DMesh3 mesh, int tid, Func<int, ExactPoint3> exactOf)
        {
            Index3i t = mesh.GetTriangle(tid);
            var r = new List<P2>();
            foreach (int v in new[] { t.a, t.b, t.c }) {
                P2 uv;
                if (c.ToUV(exactOf(v), out uv) == false) return null;
                r.Add(uv);
            }
            return r;
        }

        static Rat TriArea2(List<P2> t)
        {
            Rat a = (t[1].U - t[0].U) * (t[2].V - t[0].V) - (t[1].V - t[0].V) * (t[2].U - t[0].U);
            return a.Sign < 0 ? -a : a;
        }

        // the triangles of the mesh that lie in the plane of the contact, in plane coordinates
        static List<List<P2>> InPlane(Contact c, DMesh3 mesh, Func<int, ExactPoint3> exactOf)
        {
            var r = new List<List<P2>>();
            foreach (int tid in mesh.TriangleIndices()) {
                var uv = UV(c, mesh, tid, exactOf);
                if (uv != null) r.Add(uv);
            }
            return r;
        }

        static Rat Area(IEnumerable<List<P2>> tris) => tris.Aggregate(Rat.Zero, (s, t) => s + TriArea2(t)) / Rat.Int(2);

        // area of the intersection of two triangles (convex clipping with exact rationals)
        static Rat ClipArea(List<P2> a, List<P2> b)
        {
            var poly = Orient(a);
            var clip = Orient(b);
            for (int i = 0; i < clip.Count && poly.Count > 0; ++i) {
                P2 p = clip[i], q = clip[(i + 1) % clip.Count];
                var next = new List<P2>();
                for (int j = 0; j < poly.Count; ++j) {
                    P2 s = poly[j], e = poly[(j + 1) % poly.Count];
                    Rat ds = HalfPlane(p, q, s), de = HalfPlane(p, q, e);
                    if (ds.Sign >= 0) next.Add(s);
                    if ((ds.Sign > 0 && de.Sign < 0) || (ds.Sign < 0 && de.Sign > 0)) {
                        Rat t = ds / (ds - de);
                        next.Add(new P2(s.U + t * (e.U - s.U), s.V + t * (e.V - s.V)));
                    }
                }
                poly = next;
            }
            return poly.Count < 3 ? Rat.Zero : Region2.Area2(poly) / Rat.Int(2);
        }
        static Rat HalfPlane(P2 p, P2 q, P2 r) => (q.U - p.U) * (r.V - p.V) - (q.V - p.V) * (r.U - p.U);
        static List<P2> Orient(List<P2> t) => HalfPlane(t[0], t[1], t[2]).Sign >= 0 ? t : new List<P2> { t[0], t[2], t[1] };

        static Func<int, ExactPoint3> ExactOfMesh(DMesh3 mesh) => (v) => Exact(mesh.GetVertex(v));

        // ---- the fixtures themselves ----

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Fixture_PartsAreClean_AndTheMeshesMatchTheGroundTruth(string name)
        {
            var asm = ContactAssemblyFixtures.ByName(name);
            foreach (var part in asm.Parts) {
                Assert.True(part.Mesh.CheckValidity(true, FailMode.ReturnOnly), part.Name);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(part.Mesh));
                // positive volume (outward normals)
                Assert.True(Invariance.Volume(part.Mesh) > 0, part.Name + " is inside out");
            }
            Assert.NotEmpty(asm.Contacts);
            foreach (var c in asm.Contacts) {
                var a = InPlane(c, asm.Parts[c.PartA].Mesh, ExactOfMesh(asm.Parts[c.PartA].Mesh));
                var b = InPlane(c, asm.Parts[c.PartB].Mesh, ExactOfMesh(asm.Parts[c.PartB].Mesh));
                // the faces in the plane are the stated regions
                Assert.Equal(c.FaceA.Area(), Area(a));
                Assert.Equal(c.FaceB.Area(), Area(b));
                // the contact area computed from the two triangulations (independent of the hand derivation)
                Rat overlap = Rat.Zero;
                foreach (var ta in a) foreach (var tb in b) overlap = overlap + ClipArea(ta, tb);
                Assert.Equal(c.Area, overlap);
            }
        }

        // ---- imprint helpers ----

        sealed class ImprintSide
        {
            public ExactMeshImprint Imprint;
            public DMesh3 Target;
            public Func<int, ExactPoint3> ExactOf;
        }

        static ExactMeshImprint Run(DMesh3 target, IEnumerable<DMesh3> cutters, bool rounding, Action<ExactMeshImprint> configure = null)
        {
            var imprint = new ExactMeshImprint() { Target = target, Cutters = cutters.ToList(), SafeRounding = rounding, ComputeExactVertices = true };
            configure?.Invoke(imprint);
            Assert.True(imprint.Compute(), imprint.Error);
            Assert.Equal(ExactMeshImprint.ImprintStatus.Success, imprint.Status);
            Assert.True(imprint.Result.CheckValidity(true, FailMode.ReturnOnly));
            return imprint;
        }

        // the parts that touch the part, by the contacts of the assembly
        static List<int> Neighbours(ContactAssembly asm, int part)
        {
            return asm.Contacts.Where(c => c.PartA == part || c.PartB == part).Select(c => c.PartA == part ? c.PartB : c.PartA).Distinct().OrderBy(x => x).ToList();
        }

        // every part imprinted with its neighbours
        static ImprintSide[] Sides(ContactAssembly asm, bool rounding, Action<ExactMeshImprint> configure = null)
        {
            var r = new ImprintSide[asm.Parts.Count];
            for (int i = 0; i < r.Length; ++i) {
                var imprint = Run(asm.Parts[i].Mesh, Neighbours(asm, i).Select(j => asm.Parts[j].Mesh), rounding, configure);
                r[i] = new ImprintSide { Imprint = imprint, Target = asm.Parts[i].Mesh, ExactOf = (v) => imprint.ResultExactVertices[v] };
            }
            return r;
        }

        // (ExactPoint3 is canonical: equal points have equal components; ToString() rounds to 8 decimals and is not a key)
        static string ExactKey(ExactPoint3 p) => p.X + "," + p.Y + "," + p.Z + "," + p.W;
        static string Key(P2 p) => ContactTruth.Key(p);
        static string Key(P2 a, P2 b) => ContactTruth.Key(a, b);

        // the curve edges of the imprint that lie in the plane of the contact, as exact point pairs
        static Dictionary<string, (P2 a, P2 b)> CurveEdgesInPlane(Contact c, ImprintSide s)
        {
            var r = new Dictionary<string, (P2, P2)>();
            foreach (long k in s.Imprint.CurveEdges) {
                P2 u, v;
                if (c.ToUV(s.ExactOf((int)(k >> 32)), out u) && c.ToUV(s.ExactOf((int)(k & 0xFFFFFFFF)), out v))
                    r[Key(u, v)] = (u, v);
            }
            return r;
        }

        // the segment (p, q) is a chain of mesh edges through the given vertices (every vertex on it, in order, is joined to the next by an edge)
        static void AssertChain(P2 p, P2 q, IEnumerable<P2> vertices, HashSet<string> edges, string what)
        {
            var on = new List<(Rat t, P2 pt)>();
            Rat dd = (q.U - p.U) * (q.U - p.U) + (q.V - p.V) * (q.V - p.V);
            foreach (P2 uv in vertices) {
                if (ContactTruth.Cross(p, q, uv).Sign != 0) continue;
                Rat t = ((uv.U - p.U) * (q.U - p.U) + (uv.V - p.V) * (q.V - p.V)) / dd;
                if (t.Sign < 0 || t > Rat.Int(1)) continue;
                on.Add((t, uv));
            }
            on.Sort((x, y) => x.t.CompareTo(y.t));
            Assert.True(on.Count >= 2 && on[0].pt.Equals(p) && on[on.Count - 1].pt.Equals(q), what + ": the ends of " + Key(p) + " -> " + Key(q) + " are not vertices");
            for (int i = 0; i + 1 < on.Count; ++i)
                Assert.True(edges.Contains(Key(on[i].pt, on[i + 1].pt)), what + ": no edge " + Key(on[i].pt) + " - " + Key(on[i + 1].pt) + " on the outline segment " + Key(p) + " -> " + Key(q));
        }

        // ---- the contact, from both sides ----

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Imprint_BothSidesOfEveryContactCarryTheSameOverlay_AndTheSameAreas(string name)
        {
            var asm = ContactAssemblyFixtures.ByName(name);
            var sides = Sides(asm, rounding: false);
            foreach (var s in sides) {
                // G1
                ExactMeshImprintTests.AssertKeepsTarget(s.Target, s.Imprint);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(s.Imprint.Result));
                Assert.True(Invariance.AllFinite(s.Imprint.Result));
            }
            foreach (var c in asm.Contacts) {
                // the ground truth from the input parts: the overlay of their in-plane edges inside the closed contact region
                var inA = InPlane(c, asm.Parts[c.PartA].Mesh, ExactOfMesh(asm.Parts[c.PartA].Mesh));
                var inB = InPlane(c, asm.Parts[c.PartB].Mesh, ExactOfMesh(asm.Parts[c.PartB].Mesh));
                var truth = ContactTruth.Compute(c, ContactTruth.Edges(inA), ContactTruth.Edges(inB));
                Assert.NotEmpty(truth.Pieces);
                for (int side = 0; side < 2; ++side) {
                    var s = sides[side == 0 ? c.PartA : c.PartB];
                    string what = c.Name + " (part " + (side == 0 ? c.PartA : c.PartB) + ")";
                    var tris = InPlane(c, s.Imprint.Result, s.ExactOf);
                    var meshEdges = new HashSet<string>(ContactTruth.Edges(tris).Select(e => Key(e.a, e.b)));
                    Region2 face = side == 0 ? c.FaceA : c.FaceB;
                    // (the curves of other contacts that happen to lie in the same plane, outside this face, are not part of this contact)
                    var half = new Rat(1, 2);
                    var flagged = new HashSet<string>(CurveEdgesInPlane(c, s).Where(kv => face.Locate(new P2((kv.Value.a.U + kv.Value.b.U) * half, (kv.Value.a.V + kv.Value.b.V) * half)) != Region2.Where.Outside).Select(kv => kv.Key));
                    // (1) the vertices of the part in the contact region are the overlay's vertices, no more and no fewer
                    var vertices = new Dictionary<string, P2>();
                    foreach (var t in tris) foreach (P2 v in t) if (c.Locate(v) != Region2.Where.Outside) vertices[Key(v)] = v;
                    Assert.True(truth.Vertices.Keys.ToHashSet().SetEquals(vertices.Keys), what + ": vertices differ: only in overlay " + string.Join(" ", truth.Vertices.Keys.Except(vertices.Keys).Take(5)) + "; only in part " + string.Join(" ", vertices.Keys.Except(truth.Vertices.Keys).Take(5)));
                    // (2) every piece of the overlay is an edge of the part's mesh
                    Assert.True(truth.Pieces.Keys.All(k => meshEdges.Contains(k)), what + ": overlay pieces that are no mesh edges: " + string.Join(" ", truth.Pieces.Keys.Where(k => meshEdges.Contains(k) == false).Take(6)));
                    // (3) the curves the imprint flags are pieces of the overlay, and they include every piece that lies on an edge of the other part
                    Assert.True(flagged.All(k => truth.Pieces.ContainsKey(k)), what + ": flagged curves that are no overlay pieces: " + string.Join(" ", flagged.Where(k => truth.Pieces.ContainsKey(k) == false).Take(6)));
                    int otherSide = side == 0 ? 2 : 1;
                    var unflagged = truth.Pieces.Where(kv => (kv.Value.from & otherSide) != 0 && flagged.Contains(kv.Key) == false).Select(kv => kv.Key).ToList();
                    Assert.True(unflagged.Count == 0, what + ": pieces on the other part's edges that are not flagged: " + string.Join(" ", unflagged.Take(6)));
                    // (4) the outline of the contact region is a chain of edges of the part's mesh
                    foreach (var seg in c.Outline)
                        AssertChain(seg.a, seg.b, vertices.Values, meshEdges, what);
                    // (5) the regions of the contact face split by the curves: every triangle is inside the contact region or outside it (never across its
                    // boundary), the inside ones add up to the analytical area, and the face is preserved exactly
                    Assert.Equal(face.Area(), Area(tris));
                    Rat inside = Rat.Zero, outside = Rat.Zero;
                    foreach (var t in tris) {
                        var centroid = new P2((t[0].U + t[1].U + t[2].U) / Rat.Int(3), (t[0].V + t[1].V + t[2].V) / Rat.Int(3));
                        var w = c.Locate(centroid);
                        Assert.NotEqual(Region2.Where.Boundary, w);
                        foreach (var corner in t) {
                            var wc = c.Locate(corner);
                            if (w == Region2.Where.Inside) Assert.NotEqual(Region2.Where.Outside, wc);
                            else Assert.NotEqual(Region2.Where.Inside, wc);
                        }
                        if (w == Region2.Where.Inside) inside = inside + TriArea2(t) / Rat.Int(2); else outside = outside + TriArea2(t) / Rat.Int(2);
                    }
                    Assert.Equal(c.Area, inside);
                    Assert.Equal(face.Area() - c.Area, outside);
                    output.WriteLine(what + ": overlay pieces " + truth.Pieces.Count + ", flagged " + flagged.Count + ", mesh edges " + meshEdges.Count);
                }
            }
        }

        // no curve outside the contacts: every flagged edge of a part lies in the plane of one of its contacts and inside that contact's face
        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Imprint_NoCurveOutsideTheContacts(string name)
        {
            var asm = ContactAssemblyFixtures.ByName(name);
            var sides = Sides(asm, rounding: false);
            var half = new Rat(1, 2);
            for (int i = 0; i < asm.Parts.Count; ++i) {
                var s = sides[i];
                Assert.NotEmpty(s.Imprint.CurveEdges);
                foreach (long k in s.Imprint.CurveEdges) {
                    var ea = s.ExactOf((int)(k >> 32)); var eb = s.ExactOf((int)(k & 0xFFFFFFFF));
                    bool explained = false;
                    foreach (var c in asm.Contacts) {
                        if (c.PartA != i && c.PartB != i) continue;
                        P2 u, v;
                        if (c.ToUV(ea, out u) == false || c.ToUV(eb, out v) == false) continue;
                        Region2 face = c.PartA == i ? c.FaceA : c.FaceB;
                        if (face.Locate(new P2((u.U + v.U) * half, (u.V + v.V) * half)) != Region2.Where.Outside) { explained = true; break; }
                    }
                    Assert.True(explained, asm.Parts[i].Name + ": a curve edge " + ea + " - " + eb + " is in no contact");
                }
            }
        }

        // G2: the flagged curves are the arrangement's constraint edges (as exact points), here on the contact assemblies
        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Imprint_CurveEdges_EqualTheArrangementsConstraintEdges(string name)
        {
            var asm = ContactAssemblyFixtures.ByName(name);
            var sides = Sides(asm, rounding: false);
            for (int i = 0; i < asm.Parts.Count; ++i) {
                var s = sides[i];
                var fromImprint = new HashSet<string>();
                foreach (long k in s.Imprint.CurveEdges) {
                    string a = ExactKey(s.ExactOf((int)(k >> 32))), b = ExactKey(s.ExactOf((int)(k & 0xFFFFFFFF)));
                    fromImprint.Add(string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a);
                }
                var meshes = new List<DMesh3> { asm.Parts[i].Mesh };
                meshes.AddRange(Neighbours(asm, i).Select(j => asm.Parts[j].Mesh));
                var mask = new bool[meshes.Count]; mask[0] = true;
                var arr = new ExactMeshArrangement(meshes) { SelfIntersections = false, TriangulateMeshes = mask, PreDecideCoplanar = false };
                arr.Compute();
                var fromArrangement = new HashSet<string>();
                foreach (long k in arr.ConstraintEdges) {
                    string a = ExactKey(arr.ToInputExact(arr.Exact((int)(k >> 32)))), b = ExactKey(arr.ToInputExact(arr.Exact((int)(k & 0xFFFFFFFF))));
                    fromArrangement.Add(string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a);
                }
                Assert.NotEmpty(fromArrangement);
                Assert.Equal(fromArrangement.OrderBy(x => x, StringComparer.Ordinal), fromImprint.OrderBy(x => x, StringComparer.Ordinal));
            }
        }


        // the bound of G3: max(MaxDisplacement * diagonal, UlpFactor * ulp of the largest coordinate)
        static double Bound(ExactMeshImprint r, DMesh3 target)
        {
            var b = target.CachedBounds;
            double m = Math.Max(Math.Max(Math.Abs(b.Min.x), Math.Abs(b.Max.x)), Math.Max(Math.Max(Math.Abs(b.Min.y), Math.Abs(b.Max.y)), Math.Max(Math.Abs(b.Min.z), Math.Abs(b.Max.z))));
            double ulp = Math.Abs(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(m) + 1) - m);
            return Math.Max(r.RoundingMaxDisplacement * b.DiagonalLength, r.RoundingUlpFactor * ulp);
        }

        // the frame of the coarse rounding test: lattice points stay exact doubles (multiples of 2^-11 on top of 2^38, ulp 2^-14), but the rational curve points
        // of nearby contacts collide when rounded to that grid
        static readonly Similarity Coarse = new Similarity("coarse", 1.0 / 256, Matrix3d.Identity, new Vector3d(274877906944.0, 274877906944.0, 274877906944.0));
        // (ulp 2^-11 = the lattice unit: every curve point collides with a lattice point or a neighbour)
        static readonly Similarity Coarser = new Similarity("coarser", 1.0 / 256, Matrix3d.Identity, new Vector3d(2199023255552.0, 2199023255552.0, 2199023255552.0));

        public static IEnumerable<object[]> FixturesAndCoarseFrames()
        {
            foreach (var a in ContactAssemblyFixtures.All()) { yield return new object[] { a.Name, "coarse" }; yield return new object[] { a.Name, "coarser" }; }
        }

        // SafeRounding where the doubles are coarse compared with the curve spacing: the repair has to act. G1 to G5 still hold
        [Theory]
        [MemberData(nameof(FixturesAndCoarseFrames))]
        public void RoundedOnACoarseGrid_KeepsTheCurvesOrCountsTheirContraction(string name, string frame)
        {
            var asm = ContactAssemblyFixtures.ByName(name);
            Similarity grid = frame == "coarse" ? Coarse : Coarser;
            var moved = new ContactAssembly() { Name = name, Contacts = asm.Contacts };
            for (int i = 0; i < asm.Parts.Count; ++i) moved.Parts.Add(new Part() { Index = i, Name = asm.Parts[i].Name, Mesh = Invariance.Transformed(asm.Parts[i].Mesh, grid) });
            var exact = Sides(moved, rounding: false);
            var rounded = Sides(moved, rounding: true);
            for (int i = 0; i < rounded.Length; ++i) {
                var r = rounded[i].Imprint;
                // G1 holds exactly: the target vertices are bit for bit the same, the result is closed (the pieces' distance to their source is only
                // bounded by the coarse rounding here, which AssertKeepsTarget's tolerance does not allow for)
                foreach (int v in moved.Parts[i].Mesh.VertexIndices()) {
                    Assert.True(r.Result.IsVertex(v));
                    Assert.Equal(moved.Parts[i].Mesh.GetVertex(v), r.Result.GetVertex(v));
                }
                Assert.True(r.Result.IsClosed());
                long contracted = r.Counts["curve_edges_contracted"];
                output.WriteLine(asm.Parts[i].Name + ": rounding edits " + r.Counts["rounding_edits"] + ", contracted " + contracted + ", clean " + r.RoundingClean + ", curve edges " + exact[i].Imprint.CurveEdges.Count + " -> " + r.CurveEdges.Count);
                // G5: without a contraction (of a curve edge, or of a part of a target edge, which moves the curve's end vertex to the kept one: counted
                // separately, target_edge_parts_contracted) the curve edges are exactly the exact imprint's, and edges of the result
                long collapses = r.Rounding.Collapses;
                Assert.True(contracted + r.Counts["target_edge_parts_contracted"] <= collapses);
                if (collapses == 0) {
                    Assert.Equal(exact[i].Imprint.CurveEdges.OrderBy(x => x), r.CurveEdges.OrderBy(x => x));
                }
                else {
                    // each contraction re-points the curve edges at the vanished vertex, removes the contracted edge if it was a curve edge, and merges at most one
                    // more where its ends had a common curve neighbour
                    int before = exact[i].Imprint.CurveEdges.Count;
                    Assert.True(r.CurveEdges.Count <= before && r.CurveEdges.Count >= before - 2 * collapses,
                        "curve edges " + before + " -> " + r.CurveEdges.Count + " with " + collapses + " contractions");
                }
                foreach (long k in r.CurveEdges)
                    Assert.True(r.Result.FindEdge((int)(k >> 32), (int)(k & 0xFFFFFFFF)) >= 0);
                if (r.RoundingClean) Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Result));
                double bound = Bound(r, moved.Parts[i].Mesh);
                foreach (int v in r.Result.VertexIndices()) {
                    if (r.ResultExactVertices[v].W.IsZero) continue;
                    Assert.True(r.Result.GetVertex(v).Distance(r.ResultExactVertices[v].ApproxVector3d()) <= bound * 2, "vertex " + v + " is farther than the bound from its exact point");
                }
            }
        }

        static bool SameMesh(DMesh3 a, DMesh3 b) =>
            a.MaxVertexID == b.MaxVertexID && a.MaxTriangleID == b.MaxTriangleID
            && a.VertexIndices().All(v => b.IsVertex(v) && a.GetVertex(v) == b.GetVertex(v))
            && a.TriangleIndices().All(t => b.IsTriangle(t) && a.GetTriangle(t) == b.GetTriangle(t));

        // G5 and G3 with SafeRounding: the curve edges are kept, the vertices stay within the bound of their exact points, and the two sides of
        // a contact have the same doubles where the repair edited neither
        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Rounded_KeepsTheCurves_StaysCloseToTheExactPoints_AndBothSidesRoundTheSame(string name)
        {
            var asm = ContactAssemblyFixtures.ByName(name);
            var exact = Sides(asm, rounding: false);
            var rounded = Sides(asm, rounding: true);
            var positions = new List<Dictionary<string, Vector3d>>();
            for (int i = 0; i < asm.Parts.Count; ++i) {
                var r = rounded[i].Imprint;
                // (a fixture expectation, not a guarantee: Success with RoundingClean false is allowed; these inputs are tame)
                Assert.True(r.RoundingClean, asm.Parts[i].Name + ": rounding not clean");
                Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Result));
                ExactMeshImprintTests.AssertKeepsTarget(asm.Parts[i].Mesh, r);
                // G5: every curve edge is an edge of the result, or counted as contracted
                long contracted = r.Counts["curve_edges_contracted"];
                if (r.Rounding.Collapses == 0)
                    Assert.Equal(exact[i].Imprint.CurveEdges.OrderBy(x => x), r.CurveEdges.OrderBy(x => x));
                else
                    Assert.True(r.CurveEdges.Count >= exact[i].Imprint.CurveEdges.Count - 2 * r.Rounding.Collapses);
                foreach (long k in r.CurveEdges)
                    Assert.True(r.Result.FindEdge((int)(k >> 32), (int)(k & 0xFFFFFFFF)) >= 0);
                // G3: a vertex is within the bound of its exact point (the repair moves only within it)
                double bound = Bound(r, asm.Parts[i].Mesh);
                var map = new Dictionary<string, Vector3d>();
                foreach (int v in r.Result.VertexIndices()) {
                    if (r.ResultExactVertices[v].W.IsZero) continue;
                    Vector3d pos = r.Result.GetVertex(v);
                    Assert.True(pos.Distance(r.ResultExactVertices[v].ApproxVector3d()) <= bound, "vertex " + v + " moved by the rounding");
                    map[ExactKey(r.ResultExactVertices[v])] = pos;
                }
                positions.Add(map);
                output.WriteLine(asm.Parts[i].Name + ": rounding edits " + r.Counts["rounding_edits"] + ", contracted " + contracted);
            }
            foreach (var c in asm.Contacts) {
                int a = c.PartA, b = c.PartB;
                if (rounded[a].Imprint.Counts["rounding_edits"] != 0 || rounded[b].Imprint.Counts["rounding_edits"] != 0) continue;
                // no edit: both are the nearest double of the same exact point
                foreach (var kv in positions[a])
                    if (positions[b].TryGetValue(kv.Key, out Vector3d other))
                        Assert.Equal(kv.Value, other);
            }
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Reuse_ComputeTwice_AndAfterAFailedInput_GivesTheSameResult(string name)
        {
            var asm = ContactAssemblyFixtures.ByName(name);
            var target = asm.Parts[0].Mesh;
            var cutters = Neighbours(asm, 0).Select(j => asm.Parts[j].Mesh).ToList();
            var imprint = new ExactMeshImprint() { Target = target, Cutters = cutters };
            Assert.True(imprint.Compute());
            var first = imprint.Result; var edges = new HashSet<long>(imprint.CurveEdges); var counts = new Dictionary<string, long>(imprint.Counts);
            Assert.True(imprint.Compute());
            Assert.True(SameMesh(first, imprint.Result));
            Assert.Equal(edges.OrderBy(x => x), imprint.CurveEdges.OrderBy(x => x));
            foreach (var kv in counts) Assert.Equal(kv.Value, imprint.Counts[kv.Key]);
            // a failed compute (a null cutter) leaves nothing behind
            imprint.Cutters = new List<DMesh3> { null };
            Assert.False(imprint.Compute());
            Assert.Null(imprint.Result);
            Assert.Empty(imprint.CurveEdges);
            imprint.Cutters = cutters;
            Assert.True(imprint.Compute());
            Assert.True(SameMesh(first, imprint.Result));
            Assert.Equal(edges.OrderBy(x => x), imprint.CurveEdges.OrderBy(x => x));
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Determinism_SerialEqualsParallel_AndRepeatedRuns(string name)
        {
            var asm = ContactAssemblyFixtures.ByName(name);
            var reference = Sides(asm, rounding: true);
            for (int rep = 0; rep < 2; ++rep) {
                var again = Sides(asm, rounding: true);
                for (int i = 0; i < reference.Length; ++i) {
                    Assert.True(SameMesh(reference[i].Imprint.Result, again[i].Imprint.Result));
                    Assert.Equal(reference[i].Imprint.CurveEdges.OrderBy(x => x), again[i].Imprint.CurveEdges.OrderBy(x => x));
                    Assert.Equal(reference[i].Imprint.SourceTriangle, again[i].Imprint.SourceTriangle);
                }
            }
            gParallel.SerialOnThisThread = true;
            try {
                var serial = Sides(asm, rounding: true);
                for (int i = 0; i < reference.Length; ++i) {
                    Assert.True(SameMesh(reference[i].Imprint.Result, serial[i].Imprint.Result));
                    Assert.Equal(reference[i].Imprint.CurveEdges.OrderBy(x => x), serial[i].Imprint.CurveEdges.OrderBy(x => x));
                    Assert.Equal(reference[i].Imprint.SourceTriangle, serial[i].Imprint.SourceTriangle);
                }
            } finally {
                gParallel.SerialOnThisThread = false;
            }
        }

        public static IEnumerable<object[]> FixturesAndSimilarities()
        {
            foreach (var a in ContactAssemblyFixtures.All())
                foreach (var s in Similarity.All())
                    yield return new object[] { a.Name, s.Name };
        }

        // the decisions of the exact imprint are the same for the exact similarities (the powers of two, the lattice translation); with SafeRounding
        // the powers of two give the result bit for bit; the other similarities (1e-3, rotations) round the vertices, so the parts no longer touch
        // exactly: they must still give a clean imprint with the curves of some contact
        [Theory]
        [MemberData(nameof(FixturesAndSimilarities))]
        public void Invariance_OverTheSimilarities(string name, string similarity)
        {
            var asm = ContactAssemblyFixtures.ByName(name);
            Similarity s = Similarity.ByName(similarity);
            var moved = new ContactAssembly() { Name = name };
            for (int i = 0; i < asm.Parts.Count; ++i) moved.Parts.Add(new Part() { Index = i, Name = asm.Parts[i].Name, Mesh = Invariance.Transformed(asm.Parts[i].Mesh, s) });
            moved.Contacts = asm.Contacts;
            bool exactSimilarity = s.IsExact || (s.IsRotated == false && s.Scale == 1);
            var a = Sides(asm, rounding: false);
            var b = Sides(moved, rounding: false);
            for (int i = 0; i < a.Length; ++i) {
                if (exactSimilarity) {
                    Assert.Equal("", MeshRobustRepair.NotCleanReason(b[i].Imprint.Result));
                    foreach (string key in new[] { "pieces", "new_vertices", "curve_edges", "split_triangles", "points" })
                        Assert.Equal(a[i].Imprint.Counts[key], b[i].Imprint.Counts[key]);
                    Assert.Equal(a[i].Imprint.Result.TriangleCount, b[i].Imprint.Result.TriangleCount);
                    // (the target's own vertices are the same bit for bit, scaled, for the powers of two)
                    if (s.IsExact)
                        foreach (int v in asm.Parts[i].Mesh.VertexIndices())
                            Assert.Equal(s.Apply(a[i].Imprint.Result.GetVertex(v)), b[i].Imprint.Result.GetVertex(v));
                }
                else
                    Assert.True(b[i].Imprint.CurveEdges.Count > 0);
            }
            if (exactSimilarity == false) {
                // the parts no longer touch exactly: the imprint of the rounded parts is a clean closed manifold, as far as the repair can say
                var y = Sides(moved, rounding: true);
                for (int i = 0; i < y.Length; ++i) {
                    string reason = MeshRobustRepair.NotCleanReason(y[i].Imprint.Result);
                    output.WriteLine(asm.Parts[i].Name + " under " + similarity + ": RoundingClean " + y[i].Imprint.RoundingClean + ", reason '" + reason + "', curve edges " + y[i].Imprint.CurveEdges.Count);
                    if (y[i].Imprint.RoundingClean) Assert.Equal("", reason);
                    Assert.True(y[i].Imprint.Result.IsClosed());
                }
            }
            if (s.IsExact) {
                // (the rounded run: the same vertices, triangles, curve edges and sources, scaled)
                var x = Sides(asm, rounding: true);
                var y = Sides(moved, rounding: true);
                for (int i = 0; i < x.Length; ++i) {
                    Assert.Equal(x[i].Imprint.Result.MaxVertexID, y[i].Imprint.Result.MaxVertexID);
                    foreach (int v in x[i].Imprint.Result.VertexIndices())
                        Assert.Equal(s.Apply(x[i].Imprint.Result.GetVertex(v)), y[i].Imprint.Result.GetVertex(v));
                    foreach (int t in x[i].Imprint.Result.TriangleIndices())
                        Assert.Equal(x[i].Imprint.Result.GetTriangle(t), y[i].Imprint.Result.GetTriangle(t));
                    Assert.Equal(x[i].Imprint.CurveEdges.OrderBy(k => k), y[i].Imprint.CurveEdges.OrderBy(k => k));
                    Assert.Equal(x[i].Imprint.SourceTriangle, y[i].Imprint.SourceTriangle);
                    Assert.Equal(x[i].Imprint.Counts["rounding_edits"], y[i].Imprint.Counts["rounding_edits"]);
                }
            }
        }
    }
}
