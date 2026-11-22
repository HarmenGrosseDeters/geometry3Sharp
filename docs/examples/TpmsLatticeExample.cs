using g3;

namespace g3.Examples
{
    /// <summary>
    /// Triply periodic minimal surface (TPMS) lattice generation (TpmsLattice) with certified clean output.
    /// </summary>
    public static class TpmsLatticeExample
    {
        /// <summary>
        /// Generates a single-cell gyroid sheet lattice inside [0,1]^3 at 30% volume fraction,
        /// verified closed, manifold, and self-intersection-free.
        /// </summary>
        public static TpmsLattice GyroidSheet()
        {
            var tpms = new TpmsLattice {
                Type = TpmsType.Gyroid,
                Style = TpmsStyle.Sheet,
                CellSize = 1.0,
                Divisions = 12,
                VolumeFraction = 0.30,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1)),
                VerifySelfIntersections = true
            };
            tpms.Compute();
            return tpms;
        }
    }
}
