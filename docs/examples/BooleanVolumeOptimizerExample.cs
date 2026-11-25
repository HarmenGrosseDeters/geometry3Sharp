using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using g3;

namespace g3.Examples
{
    /// <summary>
    /// A small shape optimizer on BooleanVolumeGradient: the vertices of the second input move along the gradient of the
    /// Boolean's volume until the volume of the result hits a target (docs/guides/booleans-and-imprint.md, "Volume Gradient").
    ///
    /// Each iteration computes the exact Boolean (with RetainClassification), its volume V and the gradient g of V with
    /// respect to the vertices of input 1, and takes the projected (Newton) step x += t g with t = (Vtarget - V) / |g|^2,
    /// so the linearized volume hits the target. A step cap bounds how far one step moves a vertex. The step is
    /// made only of products and quotients of the gradient, the volume difference and the input's longest edge, so a scaling of
    /// the inputs by 2^k scales every iteration exactly (the positions by 2^k, the volumes by 2^3k). The gradient is
    /// normalized by its largest component before |g|^2 is formed, so this holds over the whole range of the exact Boolean's
    /// volume (tested at 2^-280 and 2^280; from coordinates of about 2^341 the volume itself overflows and Compute fails).
    ///
    /// At a flush contact (faces of both inputs overlapping in one plane) V has a kink. The optimizer uses the gradient of
    /// the branch the exact result realizes (BooleanVolumeGradient.Gradient) and reports the contacts of each iteration, so a
    /// caller can see where the one-sided derivative was used (each ContactRegion also gives the other branches).
    /// </summary>
    public static class BooleanVolumeOptimizerExample
    {
        /// <summary> why the optimizer stopped </summary>
        /// <summary>
        /// why the optimizer stopped. InvalidArguments: a target that is not finite, a StepCap that is not positive and finite, or
        /// a Tolerance that is negative or NaN (Error says which; nothing is computed)
        /// </summary>
        public enum StopReason { Converged, MaxIterations, ComputeFailed, ZeroGradient, ResultNotClosed, InvalidArguments }

        public sealed class Options
        {
            /// <summary>
            /// the iteration stops when |V - target| is at most Tolerance * |target| plus VolumeBound. Keep it above the relative
            /// resolution of the coordinates (eg 1e-7 for a part of size 1e-3 at UTM coordinates of 6e5, whose ulp is 2^-33):
            /// below it the steps are mostly lost to the rounding of the positions and the convergence slows down
            /// </summary>
            public double Tolerance = 1e-9;
            /// <summary> the largest number of Boolean evaluations (at least 1 is made) </summary>
            public int MaxIterations = 50;
            /// <summary> the farthest one step moves a vertex, as a fraction of the longest edge of input 1 (as given) </summary>
            public double StepCap = 0.25;
            /// <summary> store each iteration's result mesh (the rounded Boolean result) as vertex and triangle arrays </summary>
            public bool RecordMeshes = false;
            /// <summary> called with the iteration index on each new ExactMeshBoolean before its Compute (eg its rounding options or a Cancellation) </summary>
            public Action<int, ExactMeshBoolean> Configure;
        }

        /// <summary> one Boolean evaluation and the step taken after it </summary>
        public sealed class Iteration
        {
            /// <summary> the volume of the exact result, and its error bound </summary>
            public double Volume, VolumeBound;
            /// <summary> the farthest the step taken after this evaluation moves a vertex (0 for the last one; StepCap times the longest edge exactly when Capped) </summary>
            public double Step;
            /// <summary> true if the step was cut to Options.StepCap </summary>
            public bool Capped;
            /// <summary> the number of flush contact regions (kinks of V; the realized branch was used) and their total area </summary>
            public int Contacts;
            public double ContactArea;
            /// <summary> with Options.RecordMeshes: the result mesh, x y z per vertex and three indices into it per triangle </summary>
            public double[] Vertices;
            public int[] Triangles;
        }

        public sealed class Result
        {
            public StopReason Reason;
            /// <summary> the error of a failed Compute (of the Boolean or the gradient) or of InvalidArguments, else null </summary>
            public string Error;
            public List<Iteration> Iterations = new List<Iteration>();
            /// <summary>
            /// input 1 with its moved vertices, and the Boolean result of exactly these positions (the last successful
            /// evaluation: after a failed Compute, Moved is set back to the positions of Final); Final is null if the first
            /// evaluation failed. Converged also requires Final (the rounded result) to be closed, else ResultNotClosed
            /// </summary>
            public DMesh3 Moved, Final;
        }

        /// <summary>
        /// moves the vertices of b (a copy: b is not changed) until the volume of op(a, b) is target. a and b must be closed.
        /// </summary>
        public static Result FitVolume(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op, double target, Options options = null)
        {
            options = options ?? new Options();
            var r = new Result() { Moved = new DMesh3(b) };
            if (double.IsNaN(target) || double.IsInfinity(target))
                return stop(r, StopReason.InvalidArguments, "the target volume is not finite");
            if (!(options.StepCap > 0) || double.IsInfinity(options.StepCap))
                return stop(r, StopReason.InvalidArguments, "StepCap must be positive and finite");
            if (!(options.Tolerance >= 0))
                return stop(r, StopReason.InvalidArguments, "Tolerance must be >= 0");
            DMesh3 moved = r.Moved;
            // (lengths, not coordinates, so the steps are the same under a rotation; under a scaling by 2^k they scale exactly)
            double longest = 0;
            foreach (int e in moved.EdgeIndices()) {
                Index2i ev = moved.GetEdgeV(e);
                longest = Math.Max(longest, moved.GetVertex(ev.a).Distance(moved.GetVertex(ev.b)));
            }
            double cap = options.StepCap * longest;

            DMesh3 good = null;     // the positions of Final
            for (int it = 0; ; ++it) {
                var boolean = new ExactMeshBoolean(a, moved, op) { RetainClassification = true };
                options.Configure?.Invoke(it, boolean);
                if (boolean.Compute() == false)
                    return failed(r, good, "boolean: " + boolean.Error);
                var gradient = new BooleanVolumeGradient(boolean);
                if (gradient.Compute() == false)
                    return failed(r, good, "gradient: " + gradient.Error);
                r.Final = boolean.Result;
                good = new DMesh3(moved);

                var step = new Iteration() { Volume = gradient.Volume, VolumeBound = gradient.VolumeBound, Contacts = gradient.Contacts.Count };
                foreach (BooleanVolumeGradient.ContactRegion region in gradient.Contacts)
                    step.ContactArea += region.Area;
                if (options.RecordMeshes)
                    ToArrays(boolean.Result, out step.Vertices, out step.Triangles);
                r.Iterations.Add(step);

                double residual = target - gradient.Volume;
                if (Math.Abs(residual) <= options.Tolerance * Math.Abs(target) + gradient.VolumeBound)
                    return boolean.Result.IsClosed() ? stop(r, StopReason.Converged, null)
                        : stop(r, StopReason.ResultNotClosed, "the rounded result is not closed");
                if (it + 1 >= options.MaxIterations)
                    return stop(r, StopReason.MaxIterations, null);

                // the projected step t g, t = residual / |g|^2 (the realized branch at a contact), cut to the cap. With u = g / m,
                // m the largest component (no overflow or underflow: |u| <= sqrt 3), the step is s u with s = residual / m / |u|^2;
                // a division by a common factor commutes exactly with a scaling by 2^k
                Vector3d[] g = gradient.Gradient[1];
                double m = 0;
                foreach (int v in moved.VertexIndices())
                    m = Math.Max(m, g[v].MaxAbs);
                if (m == 0)
                    return stop(r, StopReason.ZeroGradient, null);
                double uu = 0, umax = 0;
                foreach (int v in moved.VertexIndices()) {
                    Vector3d u = g[v] / m;
                    uu += u.LengthSquared;
                    umax = Math.Max(umax, u.Length);
                }
                double t = residual / m / uu;
                step.Step = Math.Abs(t) * umax;
                if (step.Step > cap) {
                    t = Math.Sign(t) * (cap / umax);
                    step.Step = cap;
                    step.Capped = true;
                }
                foreach (int v in moved.VertexIndices())
                    moved.SetVertex(v, moved.GetVertex(v) + t * (g[v] / m));
            }
        }

        // a failed Compute: back to the last positions that computed (those of Final)
        static Result failed(Result r, DMesh3 good, string error)
        {
            if (good != null)
                r.Moved = good;
            return stop(r, StopReason.ComputeFailed, error);
        }

        static Result stop(Result r, StopReason reason, string error)
        {
            r.Reason = reason;
            r.Error = error;
            return r;
        }

        /// <summary> the mesh as compact arrays: x y z per vertex, three vertex indices per triangle </summary>
        public static void ToArrays(DMesh3 mesh, out double[] vertices, out int[] triangles)
        {
            var index = new int[mesh.MaxVertexID];
            var vs = new List<double>();
            int n = 0;
            foreach (int v in mesh.VertexIndices()) {
                index[v] = n++;
                Vector3d p = mesh.GetVertex(v);
                vs.Add(p.x); vs.Add(p.y); vs.Add(p.z);
            }
            var ts = new List<int>();
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tri = mesh.GetTriangle(t);
                ts.Add(index[tri.a]); ts.Add(index[tri.b]); ts.Add(index[tri.c]);
            }
            vertices = vs.ToArray();
            triangles = ts.ToArray();
        }

        /// <summary>
        /// writes the iterations as JSON: {"reason": ..., "iterations": [{"volume", "volume_bound", "step", "capped", "contacts",
        /// "contact_area", "vertices": [x, y, z, ...], "triangles": [i, j, k, ...]}, ...]} (the arrays only with RecordMeshes)
        /// </summary>
        public static void WriteTrajectory(Result r, TextWriter w)
        {
            // (G17: round-trips on every runtime; "R" does not always on .NET Framework)
            string R(double d) => d.ToString("G17", CultureInfo.InvariantCulture);
            w.Write("{\"reason\":\"" + r.Reason + "\",\"iterations\":[");
            for (int i = 0; i < r.Iterations.Count; ++i) {
                Iteration s = r.Iterations[i];
                w.Write(i == 0 ? "\n" : ",\n");
                w.Write("{\"volume\":" + R(s.Volume) + ",\"volume_bound\":" + R(s.VolumeBound) + ",\"step\":" + R(s.Step)
                    + ",\"capped\":" + (s.Capped ? "true" : "false") + ",\"contacts\":" + s.Contacts + ",\"contact_area\":" + R(s.ContactArea));
                if (s.Vertices != null) {
                    w.Write(",\"vertices\":[" + string.Join(",", Array.ConvertAll(s.Vertices, R)) + "]");
                    w.Write(",\"triangles\":[" + string.Join(",", s.Triangles) + "]");
                }
                w.Write("}");
            }
            w.Write("\n]}\n");
        }

        /// <summary> the unit cube [0,1]^3 </summary>
        public static DMesh3 Cube() =>
            new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();

        /// <summary> a sphere of radius 0.4 around (1.1, 0.5, 0.5), overlapping the cube's +x face (6 * 2 * 3^2 triangles) </summary>
        public static DMesh3 Sphere() =>
            new Sphere3Generator_NormalizedCube() { Radius = 0.4, EdgeVertices = 4, Box = new Box3d(new Vector3d(1.1, 0.5, 0.5), Vector3d.One) }.Generate().MakeDMesh();

        /// <summary> a box of 0.5 x 0.5 x 0.5 touching the cube's +x face (a flush contact of area 1/4) </summary>
        public static DMesh3 TouchingBox() =>
            new TrivialBox3Generator() { Box = new Box3d(new Vector3d(1.25, 0.5, 0.5), new Vector3d(0.25, 0.25, 0.25)) }.Generate().MakeDMesh();

        /// <summary>
        /// the union of the cube and the sphere, grown until its volume is 1.3 (from about 1.18)
        /// </summary>
        public static Result GrowUnion(bool recordMeshes = false) =>
            FitVolume(Cube(), Sphere(), ExactMeshBoolean.Operations.Union, 1.3, new Options() { RecordMeshes = recordMeshes });
    }
}
