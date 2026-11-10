// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace DeterminismHarness
{
    /// <summary> SplitMix64: the same sequence on every runtime (System.Random is not specified to be) </summary>
    sealed class Rng
    {
        ulong s;
        public Rng(ulong seed) { s = seed; }
        public ulong Next()
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        /// <summary> uniform in [0, 1) with 53 random bits </summary>
        public double Double() { return (Next() >> 11) * (1.0 / 9007199254740992.0); }
        public double Range(double a, double b) { return a + (b - a) * Double(); }
    }

    static class Inputs
    {
        public static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            return m;
        }

        public static DMesh3 Box(Vector3d center, Vector3d half)
        {
            return new TrivialBox3Generator() { Box = new Box3d(center, half) }.Generate().MakeDMesh();
        }

        /// <summary> a sphere deformed by smooth trigonometric bumps (curved, no symmetry) </summary>
        public static DMesh3 BumpySphere(int edgeVertices)
        {
            DMesh3 m = Sphere(Vector3d.Zero, 1, edgeVertices);
            foreach (int vid in m.VertexIndices().ToArray()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + 0.2 * Math.Sin(3 * p.x + 1) * Math.Cos(2 * p.y - 0.5) + 0.05 * p.z));
            }
            return m;
        }

        /// <summary> nx x ny cells of size h in the plane z = 0, interior vertices jittered, diagonals alternate; z bumpy if bump </summary>
        public static DMesh3 Grid(int nx, int ny, double h, ulong seed, bool bump)
        {
            var rng = new Rng(seed);
            var m = new DMesh3();
            var ids = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i) {
                    bool inner = i > 0 && j > 0 && i < nx && j < ny;
                    double dx = inner ? 0.3 * h * (rng.Double() - 0.5) : 0, dy = inner ? 0.3 * h * (rng.Double() - 0.5) : 0;
                    double x = i * h + dx, y = j * h + dy;
                    ids[i, j] = m.AppendVertex(new Vector3d(x, y, bump ? 0.15 * Math.Sin(2.5 * x) * Math.Cos(1.5 * y) : 0));
                }
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int a = ids[i, j], b = ids[i + 1, j], c = ids[i + 1, j + 1], d = ids[i, j + 1];
                    if ((i + j) % 2 == 0) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                    else { m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); }
                }
            return m;
        }

        public static int Nearest(DMesh3 m, Vector3d p)
        {
            int best = -1; double bd = double.MaxValue;
            foreach (int v in m.VertexIndices()) {
                double d = m.GetVertex(v).DistanceSquared(p);
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        public class SphereSDF : ImplicitFunction3d
        {
            public double Radius = 1;
            public double Value(ref Vector3d pt) { return pt.Length - Radius; }
            public Vector3d Gradient(ref Vector3d pt) { return pt.Normalized; }
        }

        /// <summary> a smooth blobby implicit with sin / exp in it </summary>
        public class BlobSDF : ImplicitFunction3d
        {
            public double Value(ref Vector3d p)
            {
                double r = p.Length;
                return r - 1 + 0.15 * Math.Sin(3 * p.x) * Math.Cos(2 * p.y + p.z) + 0.1 * Math.Exp(-p.z * p.z);
            }
        }
    }
}
