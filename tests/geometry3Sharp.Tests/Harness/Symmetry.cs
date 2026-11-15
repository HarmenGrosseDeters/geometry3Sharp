using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// One of the 48 symmetries of the cube: an axis permutation with signs, q[i] = sign[i] * p[perm[i]]. 24 are rotations,
    /// 24 reflections (they turn the triangle orientation, so a transformed mesh is re-wound and stays outward oriented).
    /// Unlike a rotation by an angle these maps are exact in floating point (a selection and a sign change, no rounding), so
    /// an operation without an axis-dependent choice must give the mapped result bit for bit. docs/design/METAMORPHIC_RELATIONS.md
    /// (relation S1). Not part of <see cref="Similarity.All"/>: 24 of them reverse orientation and the 90+ tests that use All()
    /// would change meaning.
    /// </summary>
    public sealed class Symmetry
    {
        public readonly string Name;
        readonly int[] perm;
        readonly int[] sign;

        /// <summary> the symmetry as a similarity (scale 1, no translation), for code that maps results back with Inverse() </summary>
        public readonly Similarity As;

        /// <summary> true for the 24 symmetries of determinant -1 </summary>
        public readonly bool Reflects;

        Symmetry(int[] perm, int[] sign)
        {
            this.perm = perm; this.sign = sign;
            Name = "sym " + string.Join(" ", Enumerable.Range(0, 3).Select(i => (sign[i] > 0 ? "+" : "-") + "xyz"[perm[i]]));
            var m = new double[9];
            for (int i = 0; i < 3; ++i) m[i * 3 + perm[i]] = sign[i];
            As = new Similarity(Name, 1, new Matrix3d(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]), Vector3d.Zero);
            int parity = (perm[0] > perm[1] ? 1 : 0) + (perm[0] > perm[2] ? 1 : 0) + (perm[1] > perm[2] ? 1 : 0);
            Reflects = ((parity % 2 == 0 ? 1 : -1) * sign[0] * sign[1] * sign[2]) < 0;
        }

        public bool IsIdentity => perm[0] == 0 && perm[1] == 1 && perm[2] == 2 && sign.All(s => s > 0);

        /// <summary> true if the symmetry maps the plane z = 0 to itself and fixes z (the 8 symmetries of the square), for 2D inputs </summary>
        public bool FixesZ => perm[2] == 2 && sign[2] > 0;

        /// <summary> exact: no arithmetic, only a selection of coordinates and a sign change (the sign of zero is kept) </summary>
        public Vector3d Apply(Vector3d p) => new Vector3d(sign[0] * p[perm[0]], sign[1] * p[perm[1]], sign[2] * p[perm[2]]);

        public Vector3d Inverse(Vector3d q)
        {
            var p = new double[3];
            for (int i = 0; i < 3; ++i) p[perm[i]] = sign[i] * q[i];
            return new Vector3d(p[0], p[1], p[2]);
        }

        /// <summary> the xy image of (x, y) for a symmetry that fixes z (the 8 of the square) </summary>
        public Vector2d Apply2d(Vector2d p) { Vector3d q = Apply(new Vector3d(p.x, p.y, 0)); return new Vector2d(q.x, q.y); }

        public override string ToString() => Name;

        /// <summary>
        /// A copy of the mesh mapped by the symmetry, ids and attributes kept; a reflection reverses the triangle orientation so
        /// that a closed mesh stays outward oriented (the triangles keep their ids and their vertex ids, two corners swap).
        /// </summary>
        public DMesh3 Transform(DMesh3 mesh)
        {
            var copy = new DMesh3(mesh);
            foreach (int vid in copy.VertexIndices()) {
                copy.SetVertex(vid, Apply(copy.GetVertex(vid)));
                if (copy.HasVertexNormals) {
                    Vector3f n = copy.GetVertexNormal(vid);
                    copy.SetVertexNormal(vid, new Vector3f(sign[0] * n[perm[0]], sign[1] * n[perm[1]], sign[2] * n[perm[2]]));
                }
            }
            if (Reflects) copy.ReverseOrientation(false);   // the mapped normals already point outward: do not flip them again
            return copy;
        }

        /// <summary> the 48 symmetries, the identity first </summary>
        public static IReadOnlyList<Symmetry> All48 { get; } = Build();

        /// <summary> the 24 rotations (determinant +1) </summary>
        public static IEnumerable<Symmetry> Rotations24() => All48.Where(s => !s.Reflects);

        /// <summary> the 8 symmetries of the square (they fix z): for inputs in the plane z = 0 </summary>
        public static IEnumerable<Symmetry> Square8() => All48.Where(s => s.FixesZ);

        /// <summary>
        /// The subset for the PR tier: the identity, a mirror in x, a mirror in z, a swap of x and y (a reflection that
        /// permutes), a quarter turn about z and a cyclic permutation of the axes (a rotation of 120 degrees about the diagonal).
        /// All 48 run in the nightly job.
        /// </summary>
        public static IEnumerable<Symmetry> Subset6() => new[] { "sym +x +y +z", "sym -x +y +z", "sym +x +y -z", "sym +y +x +z", "sym -y +x +z", "sym +y +z +x" }
            .Select(ByName);

        /// <summary> the subset for 2D inputs: all 8 symmetries of the square are cheap (the 2D mesher is the costly part: 4 here) </summary>
        public static IEnumerable<Symmetry> Square4() => new[] { "sym +x +y +z", "sym -x +y +z", "sym +y +x +z", "sym -y +x +z" }.Select(ByName);

        public static Symmetry ByName(string name) => All48.First(s => s.Name == name);

        /// <summary> for xunit [MemberData]: the names of a set </summary>
        public static IEnumerable<object[]> Names(IEnumerable<Symmetry> set) => set.Select(s => new object[] { s.Name });

        static IReadOnlyList<Symmetry> Build()
        {
            var result = new List<Symmetry>();
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            foreach (int[] p in perms)
                for (int bits = 0; bits < 8; ++bits)
                    result.Add(new Symmetry(p, new[] { (bits & 1) == 0 ? 1 : -1, (bits & 2) == 0 ? 1 : -1, (bits & 4) == 0 ? 1 : -1 }));
            return result;
        }
    }
}
