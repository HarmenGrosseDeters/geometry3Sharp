using g3;

namespace g3.Examples
{
    /// <summary>
    /// Exact mesh booleans: union, intersection and difference of two closed meshes with exact arithmetic.
    /// Coplanar and touching inputs give closed results; only the output coordinates are rounded to double.
    /// </summary>
    public static class ExactMeshBooleanExample
    {
        /// <summary> Unit cube minus a box that cuts through it (coplanar faces included). Returns null and the error if Compute() fails. </summary>
        public static DMesh3 Run(ExactMeshBoolean.Operations operation, out string error)
        {
            DMesh3 cube = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            // 0.5 x 0.5 x 2 bar through the cube; its top and bottom are outside, two of its sides are coplanar with the cube's
            DMesh3 bar = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.25, 0.25, 0.5), new Vector3d(0.25, 0.25, 1)) }.Generate().MakeDMesh();

            var boolean = new ExactMeshBoolean(cube, bar, operation);   // the inputs are not modified
            if (!boolean.Compute()) {
                error = boolean.Error;                                   // why it failed (eg an input is not closed and AllowOpenMeshes = false)
                return null;
            }
            error = null;
            return boolean.Result;                                       // a new mesh

            // ExactMeshBoolean.Resolve(mesh) does the same for one self-intersecting mesh (the boundary of its solid)
        }

        /// <summary>
        /// Three overlapping boxes in one operation: the region inside at least two of them (a winding number
        /// predicate over all inputs). Status says why Compute() failed, if it did.
        /// </summary>
        public static DMesh3 AtLeastTwoOfThree(out ExactMeshBoolean.BooleanStatus status)
        {
            var boxes = new System.Collections.Generic.List<DMesh3>();
            foreach (double x in new[] { 0.0, 0.5, 1.0 })      // unit cubes along x, each overlapping the next by 0.5
                boxes.Add(new TrivialBox3Generator() { Box = new Box3d(new Vector3d(x + 0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh());

            var boolean = new ExactMeshBoolean(boxes, ExactMeshBoolean.AtLeast(2));
            // the same with any predicate on the winding numbers w[i] of the inputs, eg the symmetric difference:
            //   new ExactMeshBoolean(boxes, w => (w[0] > 0) ^ (w[1] > 0) ^ (w[2] > 0))
            // and the n-ary operations: new ExactMeshBoolean(boxes, ExactMeshBoolean.Operations.Union)
            boolean.Compute();
            status = boolean.Status;                                     // Success, InvalidInput or OpenInput
            return boolean.Result;                                       // null unless Success
        }
    }
}
