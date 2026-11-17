using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace G3Native
{
    /// <summary>
    /// The C ABI (see g3native.h). Handles are GCHandles; every function returns an error code and never lets an
    /// exception cross the boundary. Strings are UTF-8, owned by the handle they came from.
    /// </summary>
    public static unsafe class Exports
    {
        static T get<T>(IntPtr h) where T : class { return h == IntPtr.Zero ? null : (T)GCHandle.FromIntPtr(h).Target; }
        static IntPtr put(object o) { return GCHandle.ToIntPtr(GCHandle.Alloc(o)); }
        static IntPtr utf8(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            var p = (byte*)NativeMemory.Alloc((nuint)b.Length + 1);
            for (int i = 0; i < b.Length; i++) p[i] = b[i];
            p[b.Length] = 0;
            return (IntPtr)p;
        }
        static void free(ref IntPtr p) { if (p != IntPtr.Zero) { NativeMemory.Free((void*)p); p = IntPtr.Zero; } }

        [UnmanagedCallersOnly(EntryPoint = "g3_version")]
        public static int Version() { return 1; }

        [UnmanagedCallersOnly(EntryPoint = "g3_options_default")]
        public static void OptionsDefault(G3Options* o) { if (o != null) *o = Core.DefaultOptions(); }

        [UnmanagedCallersOnly(EntryPoint = "g3_last_error")]
        public static IntPtr LastError()
        {   // valid until the next failing call on this thread
            free(ref lastErrPtr); lastErrPtr = utf8(Core.LastError); return lastErrPtr;
        }
        [ThreadStatic] static IntPtr lastErrPtr;

        [UnmanagedCallersOnly(EntryPoint = "g3_mesh_create")]
        public static int MeshCreate(double* xyz, long nv, int* tri, long nt, IntPtr* outMesh)
        {
            if (outMesh == null) return G3Err.NullArgument;
            *outMesh = IntPtr.Zero;
            try { int rc = Core.CreateMesh(xyz, nv, tri, nt, out var box); if (rc == 0) *outMesh = put(box); return rc; }
            catch (Exception e) { Core.SetError(e.GetType().Name + ": " + e.Message); return G3Err.Exception; }
        }

        [UnmanagedCallersOnly(EntryPoint = "g3_mesh_free")]
        public static void MeshFree(IntPtr m) { if (m != IntPtr.Zero) GCHandle.FromIntPtr(m).Free(); }

        [UnmanagedCallersOnly(EntryPoint = "g3_mesh_counts")]
        public static int MeshCounts(IntPtr m, long* nv, long* nt)
        {
            var b = get<MeshBox>(m); if (b == null || nv == null || nt == null) return G3Err.NullArgument;
            *nv = b.Mesh.VertexCount; *nt = b.Mesh.TriangleCount; return G3Err.Ok;
        }

        [UnmanagedCallersOnly(EntryPoint = "g3_boolean")]
        public static int Boolean(IntPtr a, IntPtr b, int op, G3Options* opt, IntPtr* outResult)
        {
            if (outResult == null) return G3Err.NullArgument;
            *outResult = IntPtr.Zero;
            int rc = Core.Boolean(get<MeshBox>(a), get<MeshBox>(b), op, opt, out var r);
            if (r != null) *outResult = put(r);   // also on a failure status: carries Error
            return rc;
        }

        [UnmanagedCallersOnly(EntryPoint = "g3_imprint")]
        public static int Imprint(IntPtr target, IntPtr cutter, G3Options* opt, IntPtr* outResult)
        {
            if (outResult == null) return G3Err.NullArgument;
            *outResult = IntPtr.Zero;
            int rc = Core.Imprint(get<MeshBox>(target), get<MeshBox>(cutter), opt, out var r);
            if (r != null) *outResult = put(r);
            return rc;
        }

        [UnmanagedCallersOnly(EntryPoint = "g3_result_free")]
        public static void ResultFree(IntPtr r)
        {
            if (r == IntPtr.Zero) return;
            var b = (ResultBox)GCHandle.FromIntPtr(r).Target;
            free(ref b.ReasonUtf8); free(ref b.ErrorUtf8);
            GCHandle.FromIntPtr(r).Free();
        }

        [UnmanagedCallersOnly(EntryPoint = "g3_result_counts")]
        public static int ResultCounts(IntPtr r, long* nv, long* nt)
        {
            var b = get<ResultBox>(r); if (b == null || nv == null || nt == null) return G3Err.NullArgument;
            *nv = b.Mesh == null ? 0 : b.Mesh.VertexCount; *nt = b.Mesh == null ? 0 : b.Mesh.TriangleCount; return G3Err.Ok;
        }

        /// <summary> copies the result into caller arrays of 3*nv doubles and 3*nt ints (sizes from g3_result_counts) </summary>
        [UnmanagedCallersOnly(EntryPoint = "g3_result_copy")]
        public static int ResultCopy(IntPtr r, double* xyz, int* tri)
        {
            var b = get<ResultBox>(r); if (b == null) return G3Err.NullArgument;
            if (b.Mesh == null) return G3Err.Ok;
            if (xyz == null || tri == null) return G3Err.NullArgument;
            Core.CopyOut(b.Mesh, xyz, tri); return G3Err.Ok;
        }

        /// <summary> the status of the operation (a G3Err code) </summary>
        [UnmanagedCallersOnly(EntryPoint = "g3_result_status")]
        public static int ResultStatus(IntPtr r) { var b = get<ResultBox>(r); return b == null ? G3Err.NullArgument : b.Status; }

        /// <summary> the library's error text ("" if none); owned by the result </summary>
        [UnmanagedCallersOnly(EntryPoint = "g3_result_error")]
        public static IntPtr ResultError(IntPtr r)
        {
            var b = get<ResultBox>(r); if (b == null) return IntPtr.Zero;
            if (b.ErrorUtf8 == IntPtr.Zero) b.ErrorUtf8 = utf8(b.Error);
            return b.ErrorUtf8;
        }

        /// <summary> MeshRobustRepair.NotCleanReason of the result: "" if clean, else the reason; owned by the result </summary>
        [UnmanagedCallersOnly(EntryPoint = "g3_result_not_clean_reason")]
        public static IntPtr ResultReason(IntPtr r)
        {
            var b = get<ResultBox>(r); if (b == null) return IntPtr.Zero;
            try { if (b.ReasonUtf8 == IntPtr.Zero) b.ReasonUtf8 = utf8(Core.Reason(b)); }
            catch (Exception e) { Core.SetError(e.Message); return IntPtr.Zero; }
            return b.ReasonUtf8;
        }

        // ---- test helpers of the spike (not part of a supported ABI) ----
        [UnmanagedCallersOnly(EntryPoint = "g3_test_make_sphere")]
        public static int MakeSphere(int edgeVertices, double radius, double cx, double cy, double cz, IntPtr* outMesh)
        {
            try { *outMesh = put(Core.MakeSphere(edgeVertices, radius, cx, cy, cz)); return 0; }
            catch (Exception e) { Core.SetError(e.Message); return G3Err.Exception; }
        }
        [UnmanagedCallersOnly(EntryPoint = "g3_test_result_hash")]
        public static ulong ResultHash(IntPtr r) { var b = get<ResultBox>(r); return b == null || b.Mesh == null ? 0 : Core.Hash(b.Mesh); }
    }
}
