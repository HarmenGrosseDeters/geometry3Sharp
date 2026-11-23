// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Cases of the determinism proof stage 2 (docs/design/DETERMINISM_PROOF.md section 5.5): the arrangement and the Boolean on inputs whose
// coplanar triangles meet only at shared vertices and edges (no point, no segment: the Coplanar list is filled by the pair loop in
// schedule order), on boxes that touch along faces, and on an open sheet against a closed solid. Inputs from + - * / only. A hash seed or a
// schedule that reached a result would show as a different trace between two processes or under the loop orders of `adversary`.
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

namespace DeterminismHarness
{
    static class Stage2
    {
        public static void AddTo(List<Case> list)
        {
            list.Add(new Case("stage2.coplanar-fan", serial => CoplanarFan(serial)));
            foreach (Ops op in new[] { Ops.Union, Ops.Difference, Ops.Intersection }) {
                Ops o = op;
                list.Add(new Case("stage2.touching-boxes." + o.ToString().ToLowerInvariant(), serial => TouchingBoxes(o, serial)));
            }
        }

        static void Add(Trace t, ExactMeshBoolean b, bool ok)
        {
            t.Section("status"); t.Add(ok); t.Add((int)b.Status);
            if (b.Result != null) t.Add(b.Result);
        }

        // an open triangle and an open fan of triangles around its vertex (2, 0, 0), all in the plane z = 0: the fan's triangles meet the
        // triangle only at that vertex, the coplanar pairs that meets_only_at_shared decides without a point or a segment
        static Trace CoplanarFan(bool serial)
        {
            var a = new DMesh3();
            a.AppendVertex(new Vector3d(0, 0, 0)); a.AppendVertex(new Vector3d(2, 0, 0)); a.AppendVertex(new Vector3d(0, 2, 0));
            a.AppendTriangle(0, 1, 2);
            var b = new DMesh3();
            int v = b.AppendVertex(new Vector3d(2, 0, 0));
            var rim = new List<int>();
            // (an arithmetic fan: points on a polyline around the vertex, + - * / only)
            double[][] r = { new[] { 3.0, -1 }, new[] { 4.0, 0 }, new[] { 4.5, 1 }, new[] { 4.0, 2 }, new[] { 3.0, 2.5 }, new[] { 2.5, 3.0 }, new[] { 2.25, 2.5 } };
            foreach (double[] p in r) rim.Add(b.AppendVertex(new Vector3d(p[0], p[1], 0)));
            for (int k = 0; k + 1 < rim.Count; ++k) b.AppendTriangle(v, rim[k], rim[k + 1]);
            var t = new Trace();
            t.Add(a, "input-a"); t.Add(b, "input-b");
            var boolean = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true };
            bool ok = boolean.Compute();
            Add(t, boolean, ok);
            return t;
        }

        // two boxes that share a face (coplanar triangles that coincide or meet along edges) and a third that touches along an edge
        static Trace TouchingBoxes(Ops op, bool serial)
        {
            DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
            {
                var m = new DMesh3();
                var c = new[] { new Vector3d(x0, y0, z0), new Vector3d(x1, y0, z0), new Vector3d(x1, y1, z0), new Vector3d(x0, y1, z0),
                    new Vector3d(x0, y0, z1), new Vector3d(x1, y0, z1), new Vector3d(x1, y1, z1), new Vector3d(x0, y1, z1) };
                foreach (Vector3d p in c) m.AppendVertex(p);
                int[][] f = { new[] { 0, 2, 1 }, new[] { 0, 3, 2 }, new[] { 4, 5, 6 }, new[] { 4, 6, 7 }, new[] { 0, 1, 5 }, new[] { 0, 5, 4 },
                    new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 2, 3, 7 }, new[] { 2, 7, 6 }, new[] { 3, 0, 4 }, new[] { 3, 4, 7 } };
                foreach (int[] q in f) m.AppendTriangle(q[0], q[1], q[2]);
                return m;
            }
            DMesh3 a = Box(0, 0, 0, 2, 2, 2), b = Box(2, 0.5, 0.5, 4, 1.5, 1.5), c = Box(2, 2, 2, 3, 3, 3);
            var t = new Trace();
            t.Add(a, "input-a"); t.Add(b, "input-b"); t.Add(c, "input-c");
            var two = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
            Add(t, two, two.Compute());
            var three = new ExactMeshBoolean(new DMesh3[] { a, b, c }, op);
            Add(t, three, three.Compute());
            return t;
        }
    }
}
