// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

/// <summary>
/// `nesting-cost [rounds]`: the cost of the nesting check of MeshWindingNumberBoolean (#1609, #1661): Compute() of a union of an
/// input and a far box with the check off (NestingCheck false), on (no nested shell: only MayHaveNestedShells) and on with a nested
/// outward shell (the oriented reading and the volume IoU), at two sizes of the shells (spheres of N x N x 6 faces). The
/// phase "nesting" of PhaseMilliseconds is the check's own time (present only with a nested shell).
/// </summary>
static class NestingCost
{
    static DMesh3 Sphere(Vector3d c, double r, int n)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        foreach (int v in m.VertexIndices()) m.SetVertex(v, m.GetVertex(v) + c);
        return m;
    }

    public static int Run(string[] args)
    {
        int rounds = args.Length > 0 ? int.Parse(args[0]) : 7;
        for (int w = 0; w < 3; ++w)       // (a warm-up of everything the rows use: JIT and first allocations are not in the first row)
            new MeshWindingNumberBoolean(Sphere(Vector3d.Zero, 1, 24), Sphere(new Vector3d(6, 0, 0), 0.5, 24), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 48 }.Compute();
        Console.WriteLine("shell tris  case                 check  Compute ms (best)  nesting phase ms (best)  nested  IoU refusal");
        foreach (int n in new[] { 12, 24 }) {
            foreach (bool nested in new[] { false, true }) {
                DMesh3 a = Sphere(Vector3d.Zero, 1, n);
                if (nested) MeshEditor.Append(a, Sphere(Vector3d.Zero, 0.5, n));
                else MeshEditor.Append(a, Sphere(new Vector3d(3, 0, 0), 0.5, n));      // (two apart shells: no nesting)
                DMesh3 b = Sphere(new Vector3d(6, 0, 0), 0.5, n);
                foreach (bool check in new[] { false, true }) {
                    double best = double.MaxValue, phase = double.MaxValue;
                    int count = 0; string reason = "";
                    for (int r = -1; r < rounds; ++r) {         // (round -1 is the warm-up: JIT, not counted)
                        var op = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { GridResolution = 48 };
                        typeof(MeshWindingNumberBoolean).GetField("NestingCheck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(op, check);
                        var sw = Stopwatch.StartNew();
                        op.Compute();
                        if (r >= 0) {
                            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                            phase = Math.Min(phase, op.PhaseMilliseconds.TryGetValue("nesting", out long ms) ? ms : -1);
                        }
                        count = op.ComponentsFlippedByNestingA; reason = op.ResultReason;
                    }
                    Console.WriteLine($"{a.TriangleCount,10}  {(nested ? "nested outward shell" : "two apart shells  "),-20} {(check ? "on " : "off"),5}  {best,17:F0}  {(phase < 0 ? "-" : ((long)phase).ToString()),16}  {count,6}  {(reason == "" ? "no" : "yes")}");
                }
            }
        }
        return 0;
    }
}
