using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using g3;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The part of the checker that talks to the boolean: runs it with the recording and takes the certificate data from the
    /// boolean and its arrangement. Nothing in the other files of this class (and nothing in CertificateFile) uses it.
    /// </summary>
    internal sealed partial class BooleanCertificate
    {
        /// <summary>
        /// Runs a boolean with the recording on (and exact vertices) and returns it with its certificate data. configure
        /// may set more fields (SafeRounding, CullOutsideA, ...).
        /// </summary>
        public static BooleanCertificate Run(DMesh3 a, DMesh3 b, Ops op, out ExactMeshBoolean boolean, Action<ExactMeshBoolean> configure = null)
        {
            boolean = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, RecordCertificate = true, ComputeExactVertices = true };
            configure?.Invoke(boolean);
            if (boolean.Compute() == false)
                throw new InvalidOperationException("boolean failed: " + boolean.Error);
            return From(boolean);
        }

        /// <summary>
        /// The oracle for the property campaigns: runs a op b with the recording (Intersection without the early-out,
        /// see the class comment) and returns "" if the certificate holds and covers every triangle, else the reason.
        /// </summary>
        public static string CheckBoolean(DMesh3 a, DMesh3 b, Ops op)
        {
            BooleanCertificate c = Run(a, b, op, out _, x => { if (op == Ops.Intersection) x.CullOutsideA = false; });
            string r = c.Check();
            if (r != "") return "certificate " + op + ": " + r;
            return c.Uncovered == 0 ? "" : $"certificate {op}: {c.Uncovered} triangles not covered within Delta";
        }

        public static BooleanCertificate From(ExactMeshBoolean b)
        {
            ExactMeshArrangement arr = b.Arrangement;
            var c = new BooleanCertificate {
                Meshes = arr.Meshes, ScaleExp = arr.ScaleExp, Center = (BigInteger[])arr.Center.Clone(),
                Front = b.CertificateFront?.Select(f => f == null ? null : (int[])f.Clone()).ToArray(),
                Keep = (bool[])b.CertificateKeep?.Clone(), Flip = (bool[])b.CertificateFlip?.Clone(),
                Assembled = b.CertificateAssembled, Result = b.Result, AssembledExact = b.ResultExactVertices
            };
            int n = arr.Output.Count;
            c.PieceMesh = new int[n]; c.PieceSource = new int[n]; c.PieceCorners = new ExactPoint3[n][];
            for (int i = 0; i < n; ++i) {
                var ot = arr.Output[i];
                c.PieceMesh[i] = ot.Mesh; c.PieceSource[i] = ot.Source;
                c.PieceCorners[i] = new[] { arr.Exact(ot.A), arr.Exact(ot.B), arr.Exact(ot.C) };
            }
            int nt = arr.Triangles.Count;
            c.TriMesh = new int[nt]; c.TriTid = new int[nt]; c.TriCulled = new bool[nt];
            for (int t = 0; t < nt; ++t) {
                c.TriMesh[t] = arr.Triangles[t].Mesh; c.TriTid[t] = arr.Triangles[t].Tid;
                c.TriCulled[t] = arr.Culled != null && arr.Culled[t];
            }
            if (b.WindingPredicate != null) c.Predicate = b.WindingPredicate;
            else c.Predicate = OperationPredicate(b.Operation);
            if (b.ExactWinding == null || Array.IndexOf(b.ExactWinding, false) >= 0)
                c.NotApplicableReason = "an input is open (generalized winding numbers)";
            if (arr.CulledCount > 0) {
                if (arr.CullOutsideOf == null || arr.CullOutsideOf.Length != 1)
                    c.NotApplicableReason = c.NotApplicableReason ?? "triangles culled against several meshes (set CullOutsideA = false)";
                else if (b.WindingPredicate != null || (b.Operation != Ops.Difference && b.Operation != Ops.Intersection))
                    c.NotApplicableReason = c.NotApplicableReason ?? "culling with a predicate that is not false outside the culling mesh";
                else
                    c.CullMesh = arr.CullOutsideOf[0];
            }
            c.ReorientedTriangles = b.ReorientedTriangles;
            c.Operation = b.WindingPredicate == null ? b.Operation : (Ops?)null;
            c.SettingMaxDisplacement = b.RoundingMaxDisplacement; c.SettingUlpFactor = b.RoundingUlpFactor;
            c.SettingAbsoluteTolerance = b.RoundingAbsoluteTolerance; c.SettingStrict = b.StrictRounding;
            c.SetRounding(b.Rounding != null, b.Rounding != null ? b.Rounding.MaxLength : 0, b.Rounding != null ? b.Rounding.RemovedComponents : 0);
            c.Inputs = new List<DMesh3>(b.Meshes ?? new List<DMesh3> { b.MeshA, b.MeshB });
            return c;
        }
    }
}
