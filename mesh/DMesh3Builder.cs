using System;
using System.Collections.Generic;
using System.Linq;


namespace g3
{
    /// <summary>
    /// IMeshBuilder implementation that builds DMesh3 objects, used by the mesh readers (StandardMeshReader etc)
    /// to collect one or more meshes plus materials and metadata. Unlike calling DMesh3.AppendTriangle directly,
    /// AppendTriangle here does not drop non-manifold triangles by default: it duplicates their vertices
    /// (see NonManifoldTriBehavior), and duplicate triangles are discarded (see DuplicateTriBehavior).
    /// For building a single mesh from vertex and index arrays, use the static Build() function.
    /// </summary>
    public class DMesh3Builder : IMeshBuilder
    {
        /// <summary> What AppendTriangle does with a triangle that cannot be added as-is </summary>
        public enum AddTriangleFailBehaviors
        {
            DiscardTriangle = 0,
            DuplicateAllVertices = 1
        }

        /// <summary>
        /// What should we do when AddTriangle() fails because triangle is non-manifold?
        /// </summary>
        public AddTriangleFailBehaviors NonManifoldTriBehavior = AddTriangleFailBehaviors.DuplicateAllVertices;

        /// <summary>
        /// What should we do when AddTriangle() fails because the triangle already exists?
        /// </summary>
        public AddTriangleFailBehaviors DuplicateTriBehavior = AddTriangleFailBehaviors.DiscardTriangle;




        /// <summary> the meshes built so far, in order of AppendNewMesh() </summary>
        public List<DMesh3> Meshes;
        /// <summary> materials passed to BuildMaterial() </summary>
        public List<GenericMaterial> Materials;

        /// <summary>
        /// This is a map from index into Meshes to index into Materials (-1 if no material)
        ///  (so, currently we can only have 1 material per mesh!)
        /// </summary>
        public List<int> MaterialAssignment;

        /// <summary> per-mesh metadata added with AppendMetaData() </summary>
        public List<Dictionary<string, object>> Metadata;

        int nActiveMesh;

        public DMesh3Builder()
        {
            Meshes = new List<DMesh3>();
            Materials = new List<GenericMaterial>();
            MaterialAssignment = new List<int>();
            Metadata = new List<Dictionary<string, object>>();
            nActiveMesh = -1;
        }

        /// <summary>
        /// Start a new empty mesh with the given per-vertex/per-triangle attributes and make it the active mesh
        /// (the target of AppendVertex / AppendTriangle). Returns its index in Meshes.
        /// </summary>
        public int AppendNewMesh(bool bHaveVtxNormals, bool bHaveVtxColors, bool bHaveVtxUVs, bool bHaveFaceGroups)
        {
            int index = Meshes.Count;
            DMesh3 m = new DMesh3(bHaveVtxNormals, bHaveVtxColors, bHaveVtxUVs, bHaveFaceGroups);
            Meshes.Add(m);
            MaterialAssignment.Add(-1);     // no material is known
            Metadata.Add(new Dictionary<string, object>());
            nActiveMesh = index;
            return index;
        }

        /// <summary> Add an existing mesh and make it the active mesh. Returns its index in Meshes. </summary>
        public int AppendNewMesh(DMesh3 existingMesh)
        {
            int index = Meshes.Count;
            Meshes.Add(existingMesh);
            MaterialAssignment.Add(-1);     // no material is known
            Metadata.Add(new Dictionary<string, object>());
            nActiveMesh = index;
            return index;
        }


        /// <summary> Make Meshes[id] the active mesh. Throws if id is out of range. </summary>
        public void SetActiveMesh(int id)
        {
            if (id >= 0 && id < Meshes.Count)
                nActiveMesh = id;
            else
                throw new ArgumentOutOfRangeException("active mesh id is out of range");
        }

        /// <summary> Append triangle (i,j,k) without a group to the active mesh, see AppendTriangle(int,int,int,int) </summary>
        public int AppendTriangle(int i, int j, int k)
        {
            return AppendTriangle(i, j, k, -1);
        }

        /// <summary>
        /// Append triangle (i,j,k) with group g to the active mesh. If the triangle already exists, returns the existing
        /// triangle ID (or appends a copy with duplicated vertices, see DuplicateTriBehavior). If it would be
        /// non-manifold, appends it with duplicated vertices (default) or returns DMesh3.NonManifoldID,
        /// see NonManifoldTriBehavior.
        /// </summary>
        public int AppendTriangle(int i, int j, int k, int g)
        {
            // [RMS] What to do here? We definitely do not want to add a duplicate triangle!!
            //   But is silently ignoring the right thing to do?
            int existing_tid = Meshes[nActiveMesh].FindTriangle(i, j, k);
            if (existing_tid != DMesh3.InvalidID) {
                if (DuplicateTriBehavior == AddTriangleFailBehaviors.DuplicateAllVertices)
                    return append_duplicate_triangle(i, j, k, g);
                else
                    return existing_tid;
            }

            int tid = Meshes[nActiveMesh].AppendTriangle(i, j, k, g);
            if ( tid == DMesh3.NonManifoldID ) {
                if (NonManifoldTriBehavior == AddTriangleFailBehaviors.DuplicateAllVertices)
                    return append_duplicate_triangle(i, j, k, g);
                else
                    return DMesh3.NonManifoldID;
            }
            return tid;
        }
        int append_duplicate_triangle(int i, int j, int k, int g)
        {
            NewVertexInfo vinfo = new NewVertexInfo();
            Meshes[nActiveMesh].GetVertex(i, ref vinfo, true, true, true);
            int new_i = Meshes[nActiveMesh].AppendVertex(vinfo);
            Meshes[nActiveMesh].GetVertex(j, ref vinfo, true, true, true);
            int new_j = Meshes[nActiveMesh].AppendVertex(vinfo);
            Meshes[nActiveMesh].GetVertex(k, ref vinfo, true, true, true);
            int new_k = Meshes[nActiveMesh].AppendVertex(vinfo);
            return Meshes[nActiveMesh].AppendTriangle(new_i, new_j, new_k, g);
        }



        /// <summary> Append a vertex to the active mesh, returns its ID </summary>
        public int AppendVertex(double x, double y, double z)
        {
            return Meshes[nActiveMesh].AppendVertex(new Vector3d(x, y, z));
        }
        public int AppendVertex(NewVertexInfo info)
        {
            return Meshes[nActiveMesh].AppendVertex(info);
        }

        public bool SupportsMetaData { get { return true; } }
        public void AppendMetaData(string identifier, object data)
        {
            Metadata[nActiveMesh].Add(identifier, data);
        }


        /// <summary> Just store GenericMaterial object, we can't use it here. Returns its index in Materials. </summary>
        public int BuildMaterial(GenericMaterial m)
        {
            int id = Materials.Count;
            Materials.Add(m);
            return id;
        }

        /// <summary> Do material assignment to mesh. Throws if either index is out of range. </summary>
        public void AssignMaterial(int materialID, int meshID)
        {
            if (meshID >= MaterialAssignment.Count || materialID >= Materials.Count)
                throw new ArgumentOutOfRangeException("[SimpleMeshBuilder::AssignMaterial] meshID or materialID are out-of-range");
            MaterialAssignment[meshID] = materialID;
        }





        //
        // DMesh3 construction utilities
        //

        /// <summary>
        /// Ultimate generic mesh-builder, pass it arrays of floats/doubles, or lists
        /// of Vector3d, or anything in-between. Will figure out how to interpret.
        /// Vertices: float/double (3 per vertex), Vector3f or Vector3d. Triangles: int (3 per triangle), Index3i or Vector3i.
        /// Normals (optional): one per vertex, same element types as Vertices. TriGroups (optional): one per triangle.
        /// Vertex IDs are the input indices. Triangles that cannot be added (invalid or non-manifold) are silently
        /// skipped, so check TriangleCount if this matters. Throws NotSupportedException for other element types,
        /// and if the normal or group counts do not match.
        /// The type arguments must be given explicitly (NType cannot be inferred when Normals is omitted), eg
        /// <c>DMesh3 m = DMesh3Builder.Build&lt;double, int, double&gt;(new double[] { 0,0,0, 1,0,0, 0,1,0 }, new int[] { 0,1,2 });</c>
        /// </summary>
        public static DMesh3 Build<VType,TType,NType>(IEnumerable<VType> Vertices,  
                                                      IEnumerable<TType> Triangles, 
                                                      IEnumerable<NType> Normals = null,
                                                      IEnumerable<int> TriGroups = null)
        {
            DMesh3 mesh = new DMesh3(Normals != null, false, false, TriGroups != null);

            Vector3d[] v = BufferUtil.ToVector3d(Vertices);
            for (int i = 0; i < v.Length; ++i)
                mesh.AppendVertex(v[i]);

            if ( Normals != null ) {
                Vector3f[] n = BufferUtil.ToVector3f(Normals);
                if ( n.Length != v.Length )
                    throw new Exception("DMesh3Builder.Build: incorrect number of normals provided");
                for (int i = 0; i < n.Length; ++i)
                    mesh.SetVertexNormal(i, n[i]);
            }

            Index3i[] t = BufferUtil.ToIndex3i(Triangles);
            List<int> groups = null;
            if ( TriGroups != null ) {
                groups = new List<int>(TriGroups);
                if (groups.Count != t.Length)
                    throw new Exception("DMesh3Builder.Build: incorect number of triangle groups");
            }
            // pass the group with the triangle: a triangle that cannot be added (eg non-manifold) gets no id,
            // so triangle ids are not the input indices once one has been skipped
            for (int i = 0; i < t.Length; ++i)
                mesh.AppendTriangle(t[i], (groups != null) ? groups[i] : -1);

            return mesh;
        }

         


    }





}
