using g3;

namespace g3.Examples
{
    /// <summary>
    /// Simplification with Reducer (quadric error edge collapses), in place. Targets: triangle count, vertex
    /// count, edge length or quadric error. MaxDeviation guarantees that the result stays within a distance of
    /// the input.
    /// </summary>
    public static class SimplificationExample
    {
        /// <summary> Reduces mesh in place to at most triangleCount triangles (fewer collapses if none is allowed). </summary>
        public static void ToTriangleCount(DMesh3 mesh, int triangleCount)
        {
            var reducer = new Reducer(mesh) {
                PreventSelfIntersections = true     // slower, but no collapse creates a self-intersection
            };
            reducer.ReduceToTriangleCount(triangleCount);
        }

        /// <summary>
        /// Reduces as far as possible while every point of the result stays within maxDeviation of the input
        /// (one-sided: the result is near the input, thin parts of the input can still be cut off).
        /// </summary>
        public static void WithMaxDeviation(DMesh3 mesh, double maxDeviation)
        {
            var reducer = new Reducer(mesh) { MaxDeviation = maxDeviation };
            reducer.ReduceToTriangleCount(4);    // as far as the deviation bound allows
        }
    }
}
