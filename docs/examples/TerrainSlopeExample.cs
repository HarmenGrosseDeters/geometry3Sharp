using System.Collections.Generic;
using g3;
using gs;

namespace g3.Examples
{
    /// <summary>
    /// A pit cut into a tilted terrain (TerrainSlopeDesign): the slope from a closed toe up to the terrain, never steeper
    /// than 1:1.5, the catch line, the composite terrain and the cut volume; then the same in a local frame at UTM.
    /// </summary>
    public static class TerrainSlopeExample
    {
        // a TIN: a 20 x 20 grid on [-50, 50]^2 of the plane z = 10 + 0.05 x, moved by origin
        static DMesh3 Terrain(Vector3d origin)
        {
            var mesh = new DMesh3();
            for (int j = 0; j <= 20; ++j)
                for (int i = 0; i <= 20; ++i) {
                    double x = -50 + 5 * i, y = -50 + 5 * j;
                    mesh.AppendVertex(origin + new Vector3d(x, y, 10 + 0.05 * x));
                }
            for (int j = 0; j < 20; ++j)
                for (int i = 0; i < 20; ++i) {
                    int v = j * 21 + i;
                    mesh.AppendTriangle(v, v + 1, v + 22);
                    mesh.AppendTriangle(v, v + 22, v + 21);
                }
            return mesh;
        }

        public static TerrainSlopeDesign Run(bool atUtm = false)
        {
            Vector3d o = atUtm ? new Vector3d(632117.25, 5712043.5, 0) : Vector3d.Zero;
            // the pit floor: a 16 x 12 rectangle at z = 0, the slope on its outside
            var toe = new List<Vector3d> { o + new Vector3d(-8, -6, 0), o + new Vector3d(8, -6, 0), o + new Vector3d(8, 6, 0), o + new Vector3d(-8, 6, 0) };
            var design = new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(toe, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)) },
                Terrain = Terrain(o),
                Tolerance = 0.05,              // the professional accuracy (m): no library default
                BuildComposite = true,         // the whole terrain with the slope region (CompositeKinds.Replacement: only that part)
                Direction = TerrainSlopeDesign.Directions.Cut,
                LocalFrame = atUtm,            // at UTM: finer guaranteed features; the outputs are then local
            };
            design.Compute();              // false also for NoDaylight (a partial result): check Status
            if (design.Status != TerrainSlopeDesign.Statuses.Success)
                throw new System.InvalidOperationException(design.Status + " (" + design.StatusKind + "): " + design.Message);
            // design.CatchLines: where the slope meets the terrain; design.Composite: the new terrain;
            // design.CutVolume: the volume removed; design.UsedMinFeatureSize: the smallest feature the result guarantees.
            // With LocalFrame every output is local: add design.UsedOrigin to x and y for the input's frame (that rounds
            // by up to half an ulp and is not covered by the exact checks)
            return design;
        }
    }
}
