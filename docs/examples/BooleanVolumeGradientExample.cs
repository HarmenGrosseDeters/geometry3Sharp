using g3;

namespace g3.Examples
{
    /// <summary>
    /// The gradient of the volume of a Boolean result with respect to the input vertices (BooleanVolumeGradient), for shape
    /// optimization: each entry is a double with a proven error bound, and contacts of positive area (where the volume has a
    /// kink) are reported with the other one-sided derivative.
    /// </summary>
    public static class BooleanVolumeGradientExample
    {
        /// <summary>
        /// Union of the unit cube and a box that touches its +x face (contact area 1/4). Returns the derivative of the volume
        /// when the second box moves along +x (away from the cube), the realized one (the union merges the touching boxes:
        /// 1/4) and the other one-sided derivative (the boxes moving apart: 0). Null if a step fails.
        /// </summary>
        public static double[] TouchingBoxes(out string error)
        {
            DMesh3 cube = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.5, 0.5, 0.5)) }.Generate().MakeDMesh();
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(1.5, 0.5, 0.5), new Vector3d(0.5, 0.25, 0.25)) }.Generate().MakeDMesh();

            // RetainClassification keeps what the gradient needs (off by default: no cost for other uses)
            var boolean = new ExactMeshBoolean(cube, box, ExactMeshBoolean.Operations.Union) { RetainClassification = true };
            if (!boolean.Compute()) { error = boolean.Error; return null; }
            var gradient = new BooleanVolumeGradient(boolean);
            if (!gradient.Compute()) { error = gradient.Error; return null; }

            // dV/dt for the whole second box moving along +x: the sum of the x components of its vertices' entries
            // (|exact - value| <= GradientBound per component)
            double realized = 0;
            foreach (int v in box.VertexIndices())
                realized += gradient.Gradient[1][v].x;

            // the contact region: the other branch is Gradient plus the region's alternative (a sparse change)
            double other = realized;
            foreach (BooleanVolumeGradient.ContactRegion region in gradient.Contacts)
                foreach (BooleanVolumeGradient.GradientTerm t in region.Alternatives[0])
                    if (t.Mesh == 1)
                        other += t.Value.x;
            error = null;
            return new[] { gradient.Volume, realized, other };
        }
    }
}
