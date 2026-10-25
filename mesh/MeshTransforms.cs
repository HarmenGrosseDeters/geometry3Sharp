using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    /// <summary>
    /// In-place transformations of mesh vertices (translate, rotate, scale, frame changes, Y-up / Z-up conversion,
    /// arbitrary per-vertex functions). All mesh functions modify the given mesh directly and return nothing;
    /// copy the mesh first (eg new DMesh3(mesh)) if you need to keep the original. Triangles are never changed,
    /// except by FlipLeftRightCoordSystems. Vertex normals are updated where noted.
    /// </summary>
    public static class MeshTransforms
    {

        /// <summary> Translate all vertices of mesh by v (in place) </summary>
        public static void Translate(IDeformableMesh mesh, Vector3d v) {
            Translate(mesh, v.x, v.y, v.z);
        }
        /// <summary> Translate all vertices of mesh by (tx,ty,tz) (in place) </summary>
        public static void Translate(IDeformableMesh mesh, double tx, double ty, double tz)
        {
            int NV = mesh.MaxVertexID;
            for ( int vid = 0; vid < NV; ++vid ) {
                if (mesh.IsVertex(vid)) {
                    Vector3d v = mesh.GetVertex(vid);
                    v.x += tx; v.y += ty; v.z += tz;
                    mesh.SetVertex(vid, v);
                }
            }
        }


        /// <summary> Rotate point pos around origin </summary>
        public static Vector3d Rotate(Vector3d pos, Vector3d origin, Quaternionf rotation)
        {
            // rotate in double precision; going through Vector3f loses precision far from the origin
            Quaterniond q = rotation;
            return q * (pos - origin) + origin;
        }
        public static Frame3f Rotate(Frame3f f, Vector3d origin, Quaternionf rotation)
        {
            f.Rotate(rotation);
            f.Origin = (Vector3f)Rotate(f.Origin, origin, rotation);
            return f;
        }
        public static Frame3f Rotate(Frame3f f, Vector3d origin, Quaterniond rotation)
        {
            f.Rotate((Quaternionf)rotation);
            f.Origin = (Vector3f)Rotate(f.Origin, origin, rotation);
            return f;
        }
        /// <summary> Rotate mesh vertices (and vertex normals, if present) around origin, in place </summary>
        public static void Rotate(IDeformableMesh mesh, Vector3d origin, Quaternionf rotation)
        {
            bool bHasNormals = mesh.HasVertexNormals;
            int NV = mesh.MaxVertexID;
            for ( int vid = 0; vid < NV; ++vid ) {
                if (mesh.IsVertex(vid)) {
                    // rotate in double precision; going through Vector3f loses precision far from the origin
                    Quaterniond q = rotation;
                    mesh.SetVertex(vid, q * (mesh.GetVertex(vid) - origin) + origin);
                    if ( bHasNormals )
                        mesh.SetVertexNormal(vid, rotation * mesh.GetVertexNormal(vid));
                }
            }
        }


        public static Vector3d Rotate(Vector3d pos, Vector3d origin, Quaterniond rotation) {
            return rotation * (pos - origin) + origin;
        }
        /// <summary> Rotate mesh vertices (and vertex normals, if present) around origin, in place </summary>
        public static void Rotate(IDeformableMesh mesh, Vector3d origin, Quaterniond rotation)
        {
            bool bHasNormals = mesh.HasVertexNormals;
            int NV = mesh.MaxVertexID;
            for (int vid = 0; vid < NV; ++vid) {
                if (mesh.IsVertex(vid)) {
                    Vector3d v = rotation * (mesh.GetVertex(vid) - origin) + origin;
                    mesh.SetVertex(vid, v);
                    if ( bHasNormals )
                        mesh.SetVertexNormal(vid, (Vector3f)(rotation * mesh.GetVertexNormal(vid)) );
                }
            }
        }


        /// <summary>
        /// Scale mesh vertices per axis relative to origin, in place. Vertex normals are not updated, and a negative
        /// scale factor does not flip the triangle orientation (the mesh then becomes inside-out).
        /// </summary>
        public static void Scale(IDeformableMesh mesh, Vector3d scale, Vector3d origin)
        {
            int NV = mesh.MaxVertexID;
            for (int vid = 0; vid < NV; ++vid) {
                if (mesh.IsVertex(vid)) {
                    Vector3d v = mesh.GetVertex(vid);
                    v.x -= origin.x; v.y -= origin.y; v.z -= origin.z;
                    v.x *= scale.x; v.y *= scale.y; v.z *= scale.z;
                    v.x += origin.x; v.y += origin.y; v.z += origin.z;
                    mesh.SetVertex(vid, v);
                }
            }
        }
        /// <summary>
        /// Scale mesh vertices per axis relative to origin, in place. With bFixOrientation, vertex normals are
        /// transformed too (inverse-transpose, renormalized), and a mirroring scale (negative product of the
        /// factors) reverses the triangle orientation so that the mesh is not turned inside out; that case
        /// throws if mesh is not a DMesh3. Without it this is the same as Scale(mesh, scale, origin).
        /// </summary>
        public static void Scale(IDeformableMesh mesh, Vector3d scale, Vector3d origin, bool bFixOrientation)
        {
            Scale(mesh, scale, origin);
            if (bFixOrientation == false)
                return;

            if (mesh.HasVertexNormals) {
                int NV = mesh.MaxVertexID;
                for (int vid = 0; vid < NV; ++vid) {
                    if (mesh.IsVertex(vid)) {
                        Vector3f n = mesh.GetVertexNormal(vid);
                        Vector3d nd = new Vector3d(n.x / scale.x, n.y / scale.y, n.z / scale.z);
                        mesh.SetVertexNormal(vid, (Vector3f)nd.Normalized);
                    }
                }
            }
            if (scale.x * scale.y * scale.z < 0) {
                if (mesh is DMesh3 == false)
                    throw new ArgumentException("MeshTransforms.Scale: can only reverse the orientation of a DMesh3");
                (mesh as DMesh3).ReverseOrientation(false);     // keep the normals set above
            }
        }
        /// <summary> Scale mesh vertices per axis relative to the world origin, in place (normals not updated) </summary>
        public static void Scale(IDeformableMesh mesh, double sx, double sy, double sz)
        {
            Scale(mesh, new Vector3d(sx, sy, sz), Vector3d.Zero);
        }
        /// <summary> Uniformly scale mesh vertices relative to the world origin, in place </summary>
        public static void Scale(IDeformableMesh mesh, double s)
        {
            Scale(mesh, s, s, s);
        }

        ///<summary>Map mesh *into* local coordinates of Frame (in place, including vertex normals) </summary>
        public static void ToFrame(IDeformableMesh mesh, Frame3f f)
        {
            int NV = mesh.MaxVertexID;
            bool bHasNormals = mesh.HasVertexNormals;
            for ( int vid = 0; vid < NV; ++vid ) {
                if (mesh.IsVertex(vid)) {
                    Vector3d v = mesh.GetVertex(vid);
                    Vector3d vf = f.ToFrameP(ref v);
                    mesh.SetVertex(vid, vf);
                    if ( bHasNormals ) {
                        Vector3f n = mesh.GetVertexNormal(vid);
                        Vector3f nf = f.ToFrameV(ref n);
                        mesh.SetVertexNormal(vid, nf);
                    }
                }
            }
        }

        /// <summary> Map mesh *from* local frame coordinates into "world" coordinates (in place, including vertex normals) </summary>
        public static void FromFrame(IDeformableMesh mesh, Frame3f f)
        {
            int NV = mesh.MaxVertexID;
            bool bHasNormals = mesh.HasVertexNormals;
            for ( int vid = 0; vid < NV; ++vid ) {
                if (mesh.IsVertex(vid)) {
                    Vector3d vf = mesh.GetVertex(vid);
                    Vector3d v = f.FromFrameP(ref vf);
                    mesh.SetVertex(vid, v);
                    if ( bHasNormals ) {
                        Vector3f n = mesh.GetVertexNormal(vid);
                        Vector3f nf = f.FromFrameV(ref n);
                        mesh.SetVertexNormal(vid, nf);
                    }
                }
            }
        }


        public static Vector3d ConvertZUpToYUp(Vector3d v)
        {
            return new Vector3d(v.x, v.z, -v.y);
        }
        public static Vector3f ConvertZUpToYUp(Vector3f v)
        {
            return new Vector3f(v.x, v.z, -v.y);
        }
        public static Frame3f ConvertZUpToYUp(Frame3f f)
        {
            return new Frame3f(
                ConvertZUpToYUp(f.Origin),
                ConvertZUpToYUp(f.X),
                ConvertZUpToYUp(f.Y),
                ConvertZUpToYUp(f.Z));
        }
        /// <summary> Convert mesh from a Z-up to a Y-up coordinate system, (x,y,z) -&gt; (x,z,-y), in place (a rotation, orientation is kept) </summary>
        public static void ConvertZUpToYUp(IDeformableMesh mesh)
        {
            int NV = mesh.MaxVertexID;
            bool bHasNormals = mesh.HasVertexNormals;
            for ( int vid = 0; vid < NV; ++vid ) {
                if ( mesh.IsVertex(vid) ) {
                    Vector3d v = mesh.GetVertex(vid);
                    mesh.SetVertex(vid, new Vector3d(v.x, v.z, -v.y));
                    if ( bHasNormals ) {
                        Vector3f n = mesh.GetVertexNormal(vid);
                        mesh.SetVertexNormal(vid, new Vector3f(n.x, n.z, -n.y));
                    }
                }
            }
        }

        public static Vector3d ConvertYUpToZUp(Vector3d v)
        {
            return new Vector3d(v.x, -v.z, v.y);
        }
        public static Vector3f ConvertYUpToZUp(Vector3f v)
        {
            return new Vector3f(v.x, -v.z, v.y);
        }
        public static Frame3f ConvertYUpToZUp(Frame3f f)
        {
            return new Frame3f(
                ConvertYUpToZUp(f.Origin),
                ConvertYUpToZUp(f.X),
                ConvertYUpToZUp(f.Y),
                ConvertYUpToZUp(f.Z));
        }
        /// <summary> Convert mesh from a Y-up to a Z-up coordinate system, (x,y,z) -&gt; (x,-z,y), in place (a rotation, orientation is kept) </summary>
        public static void ConvertYUpToZUp(IDeformableMesh mesh)
        {
            int NV = mesh.MaxVertexID;
            bool bHasNormals = mesh.HasVertexNormals;
            for ( int vid = 0; vid < NV; ++vid ) {
                if ( mesh.IsVertex(vid) ) {
                    Vector3d v = mesh.GetVertex(vid);
                    mesh.SetVertex(vid, new Vector3d(v.x, -v.z, v.y));
                    if ( bHasNormals ) {
                        Vector3f n = mesh.GetVertexNormal(vid);
                        mesh.SetVertexNormal(vid, new Vector3f(n.x, -n.z, n.y));
                    }
                }
            }
        }


        public static Vector3d FlipLeftRightCoordSystems(Vector3d v)
        {
            return new Vector3d(v.x, v.y, -v.z);
        }
        public static Vector3f FlipLeftRightCoordSystems(Vector3f v)
        {
            return new Vector3f(v.x, v.y, -v.z);
        }
        /// <summary>
        /// Flip the frame's origin, X and Y axes (negate z), and set Z = X x Y. A Frame3f is a rotation, so it cannot
        /// hold the mirrored (left-handed) axes; this keeps it right-handed, so points in its XY plane map exactly
        /// to the flipped points, while Z points opposite to the flipped Z.
        /// </summary>
        public static Frame3f FlipLeftRightCoordSystems(Frame3f f)
        {
            Vector3f x = FlipLeftRightCoordSystems(f.X), y = FlipLeftRightCoordSystems(f.Y);
            return new Frame3f(FlipLeftRightCoordSystems(f.Origin), x, y, x.Cross(y));
        }
        /// <summary>
        /// Convert between left- and right-handed coordinates by negating z of vertices and normals, in place, and
        /// reversing triangle orientation so that normals still point outward. Throws if mesh is not a DMesh3.
        /// </summary>
        public static void FlipLeftRightCoordSystems(IDeformableMesh mesh)
        {
            int NV = mesh.MaxVertexID;
            for ( int vid = 0; vid < NV; ++vid ) {
                if ( mesh.IsVertex(vid) ) {
                    Vector3d v = mesh.GetVertex(vid);
                    v.z = -v.z;
                    mesh.SetVertex(vid, v);

                    if (mesh.HasVertexNormals) {
                        Vector3f n = mesh.GetVertexNormal(vid);
                        n.z = -n.z;
                        mesh.SetVertexNormal(vid, n);
                    }
                }
            }

            if ( mesh is DMesh3 ) {
                (mesh as DMesh3).ReverseOrientation(false);
            } else {
                throw new Exception("argh don't want this in IDeformableMesh...but then for SimpleMesh??");
            }

        }




        /// <summary>
        /// Move each vertex by offsetDistance along its vertex normal, in place. The mesh must have vertex normals
        /// (eg from MeshNormals.QuickCompute). This is a simple displacement, not a robust offset surface.
        /// </summary>
        public static void VertexNormalOffset(IDeformableMesh mesh, double offsetDistance)
        {
            int NV = mesh.MaxVertexID;
            for (int vid = 0; vid < NV; ++vid) {
                if (mesh.IsVertex(vid)) {
                    Vector3d newPos = mesh.GetVertex(vid) + offsetDistance * (Vector3d)mesh.GetVertexNormal(vid);
                    mesh.SetVertex(vid, newPos);
                }
            }
        }


        /// <summary>
        /// Apply TransformF to vertices of mesh, in place
        /// </summary>
        public static void PerVertexTransform(IDeformableMesh mesh, Func<Vector3d,Vector3d> TransformF )
        {
            int NV = mesh.MaxVertexID;
            for (int vid = 0; vid < NV; ++vid) {
                if (mesh.IsVertex(vid)) {
                    Vector3d newPos = TransformF(mesh.GetVertex(vid));
                    mesh.SetVertex(vid, newPos);
                }
            }
        }
        /// <summary>
        /// Apply TransformF(position, normal) to vertices of mesh, in place. The mesh must have vertex normals.
        /// </summary>
        public static void PerVertexTransform(IDeformableMesh mesh, Func<Vector3d, Vector3f, Vector3d> TransformF)
        {
            int NV = mesh.MaxVertexID;
            for (int vid = 0; vid < NV; ++vid) {
                if (mesh.IsVertex(vid)) {
                    Vector3d newPos = TransformF(mesh.GetVertex(vid), mesh.GetVertexNormal(vid));
                    mesh.SetVertex(vid, newPos);
                }
            }
        }


        /// <summary>
        /// Apply TransformF to vertices and normals of mesh
        /// </summary>
        public static void PerVertexTransform(IDeformableMesh mesh, Func<Vector3d, Vector3f, Vector3dTuple2> TransformF)
        {
            int NV = mesh.MaxVertexID;
            for (int vid = 0; vid < NV; ++vid) {
                if (mesh.IsVertex(vid)) {
                    Vector3dTuple2 newPN = TransformF(mesh.GetVertex(vid), mesh.GetVertexNormal(vid));
                    mesh.SetVertex(vid, newPN.V0);
                    mesh.SetVertexNormal(vid, (Vector3f)newPN.V1);
                }
            }
        }


        /// <summary>
        /// Apply Transform to vertices and normals of mesh, in place
        /// (normals are transformed with TransformV, they are not renormalized)
        /// </summary>
        public static void PerVertexTransform(IDeformableMesh mesh, TransformSequence xform)
        {
            int NV = mesh.MaxVertexID;
            if (mesh.HasVertexNormals) {
                for (int vid = 0; vid < NV; ++vid) {
                    if (mesh.IsVertex(vid)) {
                        mesh.SetVertex(vid, xform.TransformP(mesh.GetVertex(vid)));
                        mesh.SetVertexNormal(vid, (Vector3f)xform.TransformV(mesh.GetVertexNormal(vid)));
                    }
                }
            } else {
                for (int vid = 0; vid < NV; ++vid) {
                    if (mesh.IsVertex(vid)) 
                        mesh.SetVertex(vid, xform.TransformP(mesh.GetVertex(vid)));
                }
            }
        }



        /// <summary>
        /// Apply TransformF to subset of vertices of mesh
        /// </summary>
        public static void PerVertexTransform(IDeformableMesh mesh, IEnumerable<int> vertices, Func<Vector3d, int, Vector3d> TransformF)
        {
            foreach (int vid in vertices) { 
                if (mesh.IsVertex(vid)) {
                    Vector3d newPos = TransformF(mesh.GetVertex(vid), vid);
                    mesh.SetVertex(vid, newPos);
                }
            }
        }

        /// <summary>
        /// Apply TransformF to subset of mesh vertices defined by MapV[vertices] 
        /// </summary>
        public static void PerVertexTransform(IDeformableMesh mesh, IEnumerable<int> vertices, Func<int,int> MapV, Func<Vector3d, int, int, Vector3d> TransformF)
        {
            foreach ( int vid in vertices ) {
                int map_vid = MapV(vid);
                if (mesh.IsVertex(map_vid)) {
                    Vector3d newPos = TransformF(mesh.GetVertex(map_vid), vid, map_vid);
                    mesh.SetVertex(map_vid, newPos);
                }
            }
        }


        /// <summary>
        /// Apply TransformF to subset of mesh vertices defined by MapV[vertices] 
        /// </summary>
        public static void PerVertexTransform(IDeformableMesh targetMesh, IDeformableMesh sourceMesh, int[] mapV, Func<Vector3d, int, int, Vector3d> TransformF)
        {
            foreach (int vid in sourceMesh.VertexIndices()) {
                int map_vid = mapV[vid];
                if (targetMesh.IsVertex(map_vid)) {
                    Vector3d newPos = TransformF(targetMesh.GetVertex(map_vid), vid, map_vid);
                    targetMesh.SetVertex(map_vid, newPos);
                }
            }
        }



    }
}
