using g3;

namespace g3.Examples
{
    /// <summary>
    /// Building a DMesh3: by hand from vertices and triangles, or with one of the mesh generators.
    /// DMesh3 is the editable, manifold-edge triangle mesh that all operations work on.
    /// </summary>
    public static class MeshConstructionExample
    {
        /// <summary> A closed tetrahedron, built vertex by vertex. Triangles are oriented counter-clockwise seen from outside. </summary>
        public static DMesh3 Tetrahedron()
        {
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
            int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
            int d = mesh.AppendVertex(new Vector3d(0, 0, 1));
            mesh.AppendTriangle(a, c, b);   // returns the triangle id, or a negative error code (eg DMesh3.NonManifoldID)
            mesh.AppendTriangle(a, b, d);
            mesh.AppendTriangle(a, d, c);
            mesh.AppendTriangle(b, c, d);
            return mesh;
        }

        /// <summary> Generators return a MeshGenerator; MakeDMesh() turns it into a DMesh3. </summary>
        public static DMesh3 Box()
        {
            // axis-aligned box from (-1,-1,-1) to (1,1,1), 4 x 4 quads per face, closed (shared corner vertices)
            return new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One), EdgeVertices = 5 }.Generate().MakeDMesh();
        }
    }
}
