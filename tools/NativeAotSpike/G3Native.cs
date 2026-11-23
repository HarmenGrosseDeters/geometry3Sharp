using System;
using System.Runtime.InteropServices;
using g3;
using gs;

namespace G3Native
{
    /// <summary> error codes of the C ABI (g3native.h) </summary>
    public static class G3Err
    {
        public const int Ok = 0;
        public const int NullArgument = 1;
        public const int InvalidMesh = 2;       // vertex / triangle arrays rejected (index out of range, non-manifold edge)
        public const int InvalidInput = 3;      // the library refused the operands (non-finite coordinates, open input if not allowed)
        public const int RoundingNotClean = 4;  // StrictRounding
        public const int BudgetExceeded = 5;    // MaxPieces
        public const int Cancelled = 6;
        public const int Exception = 7;         // managed exception, text in g3_last_error()
        public const int BadOptions = 8;        // struct_size too small or unknown operation
    }

    /// <summary> options of g3_boolean / g3_imprint; fields are only read up to struct_size (room to grow) </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct G3Options
    {
        public int struct_size;
        public int safe_rounding;        // default 1
        public int strict_rounding;      // default 0
        public int allow_open_meshes;    // default 1
        public int seed;                 // default 31337
        public long max_pieces;          // default 0 = no limit
    }

    public sealed class MeshBox { public DMesh3 Mesh; }

    public sealed class ResultBox
    {
        public DMesh3 Mesh;     // compact copy, ids 0..n-1
        public int Status;
        public string Error = "";
        public string NotCleanReason;   // computed on demand
        public IntPtr ReasonUtf8, ErrorUtf8;
    }

    /// <summary> the managed core of the C ABI; the [UnmanagedCallersOnly] wrappers are in Exports.cs </summary>
    public static class Core
    {
        [ThreadStatic] static string lastError;
        public static string LastError { get { return lastError ?? ""; } }
        public static void SetError(string s) { lastError = s; }

        public static G3Options DefaultOptions()
        {
            return new G3Options { struct_size = Marshal.SizeOf<G3Options>(), safe_rounding = 1, strict_rounding = 0, allow_open_meshes = 1, seed = 31337, max_pieces = 0 };
        }

        public static unsafe int CreateMesh(double* xyz, long nv, int* tri, long nt, out MeshBox box)
        {
            box = null;
            if (nv < 0 || nt < 0 || (nv > 0 && xyz == null) || (nt > 0 && tri == null)) { SetError("null array or negative count"); return G3Err.NullArgument; }
            if (nv > int.MaxValue / 2 || nt > int.MaxValue / 2) { SetError("mesh too large"); return G3Err.InvalidMesh; }
            var m = new DMesh3(false, false, false, false);
            for (long i = 0; i < nv; i++)
                m.AppendVertex(new Vector3d(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2]));
            for (long i = 0; i < nt; i++)
            {
                int a = tri[3 * i], b = tri[3 * i + 1], c = tri[3 * i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= nv || b >= nv || c >= nv) { SetError("triangle " + i + ": index out of range"); return G3Err.InvalidMesh; }
                if (m.AppendTriangle(a, b, c) < 0) { SetError("triangle " + i + " rejected (degenerate index triple or non-manifold edge)"); return G3Err.InvalidMesh; }
            }
            box = new MeshBox { Mesh = m };
            return G3Err.Ok;
        }

        static DMesh3 compact(DMesh3 m)
        {
            return new DMesh3(m, true, false, false, false);
        }

        static ResultBox finish(int status, string error, DMesh3 result, bool ok)
        {
            var r = new ResultBox { Status = status, Error = error ?? "" };
            if (ok && result != null) r.Mesh = compact(result);
            return r;
        }

        static int map(ExactMeshBoolean.BooleanStatus s)
        {
            switch (s)
            {
                case ExactMeshBoolean.BooleanStatus.Success: return G3Err.Ok;
                case ExactMeshBoolean.BooleanStatus.RoundingNotClean: return G3Err.RoundingNotClean;
                case ExactMeshBoolean.BooleanStatus.BudgetExceeded: return G3Err.BudgetExceeded;
                case ExactMeshBoolean.BooleanStatus.Cancelled: return G3Err.Cancelled;
                default: return G3Err.InvalidInput;
            }
        }

        static int map(ExactMeshImprint.ImprintStatus s)
        {
            switch (s)
            {
                case ExactMeshImprint.ImprintStatus.Success: return G3Err.Ok;
                case ExactMeshImprint.ImprintStatus.RoundingNotClean: return G3Err.RoundingNotClean;
                case ExactMeshImprint.ImprintStatus.BudgetExceeded: return G3Err.BudgetExceeded;
                case ExactMeshImprint.ImprintStatus.Cancelled: return G3Err.Cancelled;
                default: return G3Err.InvalidInput;
            }
        }

        /// <summary> defaults, overlaid with the first min(struct_size, sizeof) bytes of the caller's struct (an older, smaller struct is safe) </summary>
        public static unsafe bool ReadOptions(G3Options* opt, out G3Options o)
        {
            o = DefaultOptions();
            if (opt == null) return true;
            int n = opt->struct_size;
            if (n < 20) { SetError("struct_size too small"); return false; }
            if (n > sizeof(G3Options)) n = sizeof(G3Options);
            G3Options tmp = o;
            Buffer.MemoryCopy(opt, &tmp, sizeof(G3Options), n);
            tmp.struct_size = n; o = tmp;
            return true;
        }

        /// <summary> op: 0 union, 1 intersection, 2 difference </summary>
        public static unsafe int Boolean(MeshBox a, MeshBox b, int op, G3Options* opt, out ResultBox res)
        {
            res = null;
            if (a == null || b == null) { SetError("null mesh"); return G3Err.NullArgument; }
            if (op < 0 || op > 2) { SetError("unknown operation"); return G3Err.BadOptions; }
            G3Options o;
            if (!ReadOptions(opt, out o)) return G3Err.BadOptions;
            try
            {
                var bo = new ExactMeshBoolean(a.Mesh, b.Mesh, (ExactMeshBoolean.Operations)op) { AllowOpenMeshes = true };
                bo.SafeRounding = o.safe_rounding != 0; bo.StrictRounding = o.strict_rounding != 0;
                bo.AllowOpenMeshes = o.allow_open_meshes != 0; bo.Seed = o.seed;
                if (o.struct_size >= 32) bo.MaxPieces = o.max_pieces;
                bool ok = bo.Compute();
                res = finish(map(bo.Status), bo.Error, bo.Result, ok);
                return res.Status;
            }
            catch (Exception e) { SetError(e.GetType().Name + ": " + e.Message); return G3Err.Exception; }
        }

        public static unsafe int Imprint(MeshBox target, MeshBox cutter, G3Options* opt, out ResultBox res)
        {
            res = null;
            if (target == null || cutter == null) { SetError("null mesh"); return G3Err.NullArgument; }
            G3Options o;
            if (!ReadOptions(opt, out o)) return G3Err.BadOptions;
            try
            {
                var im = new ExactMeshImprint { Target = target.Mesh, Cutter = cutter.Mesh };
                im.SafeRounding = o.safe_rounding != 0; im.StrictRounding = o.strict_rounding != 0;
                if (o.struct_size >= 32) im.MaxPieces = o.max_pieces;
                bool ok = im.Compute();
                res = finish(map(im.Status), im.Error, im.Result, ok);
                return res.Status;
            }
            catch (Exception e) { SetError(e.GetType().Name + ": " + e.Message); return G3Err.Exception; }
        }

        /// <summary> MeshRobustRepair.NotCleanReason of the result ("" = clean), computed on first use </summary>
        public static string Reason(ResultBox r)
        {
            if (r.Mesh == null) return "";
            if (r.NotCleanReason == null) r.NotCleanReason = MeshRobustRepair.NotCleanReason(r.Mesh);
            return r.NotCleanReason;
        }

        public static unsafe void CopyOut(DMesh3 m, double* xyz, int* tri)
        {
            for (int i = 0; i < m.VertexCount; i++) { var v = m.GetVertex(i); xyz[3 * i] = v.x; xyz[3 * i + 1] = v.y; xyz[3 * i + 2] = v.z; }
            for (int i = 0; i < m.TriangleCount; i++) { var t = m.GetTriangle(i); tri[3 * i] = t.a; tri[3 * i + 1] = t.b; tri[3 * i + 2] = t.c; }
        }

        /// <summary> test helper: a normalized-cube sphere (the benchmark's procedural input) </summary>
        public static MeshBox MakeSphere(int edgeVertices, double radius, double cx, double cy, double cz)
        {
            var m = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, new Vector3d(cx, cy, cz));
            return new MeshBox { Mesh = m };
        }

        /// <summary> FNV-1a over the bits of the vertex doubles and the index triples, in id order of the compact mesh </summary>
        public static ulong Hash(DMesh3 m)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < m.VertexCount; i++)
            {
                var v = m.GetVertex(i);
                h = mix(h, (ulong)BitConverter.DoubleToInt64Bits(v.x)); h = mix(h, (ulong)BitConverter.DoubleToInt64Bits(v.y)); h = mix(h, (ulong)BitConverter.DoubleToInt64Bits(v.z));
            }
            for (int i = 0; i < m.TriangleCount; i++)
            {
                var t = m.GetTriangle(i);
                h = mix(h, (uint)t.a); h = mix(h, (uint)t.b); h = mix(h, (uint)t.c);
            }
            return h;
        }
        static ulong mix(ulong h, ulong x)
        {
            for (int k = 0; k < 8; k++) { h ^= (x >> (8 * k)) & 0xFF; h *= 1099511628211UL; }
            return h;
        }
    }
}
