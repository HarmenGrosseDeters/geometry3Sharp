using g3;

namespace g3.Examples
{
    /// <summary> Cut a mesh with a plane and cap the hole, in place. The side the normal points to is removed. </summary>
    public static class PlaneCutExample
    {
        public static MeshPlaneCut Run(DMesh3 mesh, Vector3d origin, Vector3d normal)
        {
            var cut = new MeshPlaneCut(mesh, origin, normal);
            cut.Cut();             // CutLoops / CutSpans: the new boundary along the plane
            cut.FillHoles();       // planar caps for the closed loops (LoopFillTriangles)
            return cut;
        }
    }
}
