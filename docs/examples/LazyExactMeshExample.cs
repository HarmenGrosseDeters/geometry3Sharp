using g3;

namespace g3.Examples
{
    /// <summary>
    /// A chain of booleans with exact intermediate results (ExactMesh): the operations build an expression, and Round()
    /// computes it as one exact boolean over the original meshes, with one rounding at the end. A chain of ExactMeshBoolean
    /// calls instead rounds every intermediate result to doubles.
    /// </summary>
    public static class LazyExactMeshExample
    {
        /// <summary>
        /// ((cube ∪ bar) − hole) ∩ cube: the bar and the hole stick out of the cube, the last intersection trims them off
        /// again. Returns null and the error if the boolean fails.
        /// </summary>
        public static DMesh3 Run(out string error)
        {
            DMesh3 cube = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            DMesh3 bar = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.25, 0.25, 0.5), new Vector3d(0.25, 0.25, 1)) }.Generate().MakeDMesh();
            DMesh3 hole = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.75, 0.5, 0.5), new Vector3d(0.125, 0.125, 1)) }.Generate().MakeDMesh();

            ExactMesh c = ExactMesh.FromDMesh(cube);              // a source (a copy of the mesh is kept)
            ExactMesh solid = c.Union(ExactMesh.FromDMesh(bar)).Difference(ExactMesh.FromDMesh(hole)).Intersection(c);   // nothing computed yet

            ExactMeshBoolean boolean = solid.ToBoolean();         // or solid.Round() for the result only
            if (!boolean.Compute()) {
                error = boolean.Error;
                return null;
            }
            error = null;
            return boolean.Result;                                // check boolean.RoundingClean or NotCleanReason like any boolean result
        }
    }
}
