using g3;

namespace g3.Examples
{
    /// <summary> Measurements of a mesh: volume, surface area, bounding box, edge lengths, genus. </summary>
    public static class MeasurementsExample
    {
        public class Result
        {
            public double Volume, Area, MinEdge, MaxEdge, AverageEdge;
            public AxisAlignedBox3d Bounds;
            public int Genus;
        }

        /// <summary> Measures a closed mesh (the volume is only meaningful for closed, outward-oriented meshes). </summary>
        public static Result Run(DMesh3 mesh)
        {
            var result = new Result();
            Vector2d volumeArea = MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex);
            result.Volume = volumeArea.x;
            result.Area = volumeArea.y;
            result.Bounds = mesh.GetBounds();     // or mesh.CachedBounds (recomputed after edits)
            MeshQueries.EdgeLengthStats(mesh, out result.MinEdge, out result.MaxEdge, out result.AverageEdge);
            MeshMeasurements.GenusResult genus = MeshMeasurements.Genus(mesh);
            result.Genus = genus.Valid ? genus.Genus : -1;   // not valid for open, multi-component or bowtie meshes
            return result;
        }
    }
}
