// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using g3;

namespace DeterminismHarness
{
    /// <summary> The values a case produced, in order: integers (tag i) and doubles (tag d, as raw bits), with a label per group. </summary>
    sealed class Trace
    {
        public readonly List<string> Labels = new List<string>();
        public readonly List<long> Values = new List<long>();
        public readonly List<bool> IsDouble = new List<bool>();
        public readonly List<int> LabelOf = new List<int>();

        int label(string l)
        {
            int i = Labels.IndexOf(l);
            if (i < 0) { Labels.Add(l); i = Labels.Count - 1; }
            return i;
        }

        string current = "";
        public void Section(string name) { current = name; }
        public void Add(long v) { Values.Add(v); IsDouble.Add(false); LabelOf.Add(label(current)); }
        public void Add(int v) { Add((long)v); }
        public void Add(bool v) { Add(v ? 1L : 0L); }
        public void Add(double v) { Values.Add(BitConverter.DoubleToInt64Bits(v)); IsDouble.Add(true); LabelOf.Add(label(current)); }
        public void Add(Vector3d v) { Add(v.x); Add(v.y); Add(v.z); }
        public void Add(Vector2d v) { Add(v.x); Add(v.y); }
        public void Add(IEnumerable<double> v) { foreach (double d in v) Add(d); }

        /// <summary> vertices in id order (with their ids), triangles in id order </summary>
        public void Add(DMesh3 m)
        {
            Section("mesh");
            Add(m.VertexCount); Add(m.TriangleCount); Add(m.MaxVertexID); Add(m.MaxTriangleID);
            foreach (int v in m.VertexIndices()) { Add(v); Add(m.GetVertex(v)); }
            foreach (int t in m.TriangleIndices()) { Index3i tri = m.GetTriangle(t); Add(t); Add(tri.a); Add(tri.b); Add(tri.c); }
        }

        /// <summary> FNV-1a over the 8 bytes of every value </summary>
        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            foreach (long v in Values) {
                ulong x = (ulong)v;
                for (int k = 0; k < 8; ++k) { h ^= (x >> (8 * k)) & 0xFF; h *= 1099511628211UL; }
            }
            return h;
        }

        // line format: d|i <TAB> hex bits <TAB> label
        public void Write(string path)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false))) {
                for (int k = 0; k < Values.Count; ++k)
                    w.Write((IsDouble[k] ? "d\t" : "i\t") + Values[k].ToString("x16") + "\t" + Labels[LabelOf[k]] + "\n");
            }
        }
    }
}
