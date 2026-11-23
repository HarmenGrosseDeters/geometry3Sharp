using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The Betti numbers (parts b0, tunnels b1, cavities b2) of the solid bounded by a mesh, by shell nesting parity
    /// (study docs/research/EXACT_TOPOLOGY_CERTIFICATES.md). A surface check (components, genus) cannot tell a cavity
    /// from a separate part; this can.
    ///
    /// Precondition: the mesh is the boundary of a bounded solid and consists of pairwise disjoint, closed, embedded
    /// (orientable, no bowtie) shells. Touching or crossing shells are outside the derivation; the cheap parts of the
    /// precondition (closed, no bowtie, no pair of shells meeting) are asserted. The orientation of a shell does not
    /// enter (a nonzero winding number counts as containment), so check it separately where it matters.
    ///
    /// Derivation: the depth of a shell is the number of other shells containing it (one vertex suffices, since a
    /// connected shell lies on one side of every other shell). Crossing a shell alternates between material and
    /// void, so an even-depth shell bounds a part and an odd-depth shell a cavity (b2 by Alexander duality). With
    /// s shells of total genus g, the solid's Euler characteristic is s - g, so b1 = g.
    /// </summary>
    public static class SolidBetti
    {
        public static (int Parts, int Tunnels, int Cavities) Compute(DMesh3 mesh, string what = "mesh")
        {
            Assert.True(mesh.IsClosed(), what + ": not closed (SolidBetti needs closed shells)");
            MeshTopologySummary summary = MeshTopologySummary.Compute(mesh);
            Assert.True(summary.BowtieVertices == 0, what + ": bowtie vertices (SolidBetti needs embedded shells)");
            DMesh3[] shells = MeshConnectedComponents.Separate(mesh);
            DMeshAABBTree3[] trees = shells.Select(shell => new DMeshAABBTree3(shell, true)).ToArray();
            for (int i = 0; i < trees.Length; ++i)
                for (int j = i + 1; j < trees.Length; ++j)
                    Assert.False(trees[i].TestIntersection(trees[j]), $"{what}: shells {i} and {j} meet (SolidBetti needs disjoint shells)");
            int parts = 0;
            for (int i = 0; i < shells.Length; ++i) {
                Vector3d witness = shells[i].GetVertex(shells[i].VertexIndices().First());
                int depth = 0;
                for (int j = 0; j < shells.Length; ++j)
                    if (i != j && trees[j].WindingNumber(witness) != 0)
                        ++depth;
                if (depth % 2 == 0)
                    ++parts;
            }
            int genus = checked((int)Math.Round(summary.Genus));
            Assert.True(genus == summary.Genus, $"{what}: non-integer genus {summary.Genus}");
            return (parts, genus, shells.Length - parts);
        }

        public static void AssertEqual(int parts, int tunnels, int cavities, DMesh3 mesh, string what = "mesh")
        {
            var expected = (parts, tunnels, cavities);
            var actual = Compute(mesh, what);
            Assert.True(actual == expected, $"{what}: solid Betti numbers {actual}, expected {expected}");
        }
    }
}
