using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    /// <summary>
    /// Sample implicit fields into a dense grid
    /// </summary>
    public class ImplicitFieldSampler3d
    {
        public DenseGrid3f Grid;
        public double CellSize;
        public Vector3d GridOrigin;
        public ShiftGridIndexer3 Indexer;
        public AxisAlignedBox3i GridBounds;

        public float BackgroundValue;


        public enum CombineModes
        {
            DistanceMinUnion = 0
        }
        public CombineModes CombineMode = CombineModes.DistanceMinUnion;


        public ImplicitFieldSampler3d(AxisAlignedBox3d fieldBounds, double cellSize)
        {
            Vector3i dims = SamplingGridSize.Dimensions(fieldBounds.Min, fieldBounds.Max + cellSize, cellSize);
            if (dims.x < 0)
                throw new ArgumentException("ImplicitFieldSampler3d: the cell size and bounds must be finite and give a grid that fits in an array");
            CellSize = cellSize;
            GridOrigin = fieldBounds.Min;
            Indexer = new ShiftGridIndexer3(GridOrigin, CellSize);

            int ni = dims.x, nj = dims.y, nk = dims.z;

            GridBounds = new AxisAlignedBox3i(0, 0, 0, ni, nj, nk);

            BackgroundValue = (float)((ni + nj + nk) * CellSize);
            Grid = new DenseGrid3f(ni, nj, nk, BackgroundValue);
        }



        public DenseGridTrilinearImplicit ToImplicit() {
            return new DenseGridTrilinearImplicit(Grid, GridOrigin, CellSize);
        }



        public void Clear(float f)
        {
            BackgroundValue = f;
            Grid.assign(BackgroundValue);
        }



        public void Sample(BoundedImplicitFunction3d f, double expandRadius = 0)
        {
            AxisAlignedBox3d bounds = f.Bounds();

            Vector3d expand = expandRadius * Vector3d.One;
            Vector3i gridMin = Indexer.ToGrid(bounds.Min-expand), 
                     gridMax = Indexer.ToGrid(bounds.Max+expand) + Vector3i.One;
            gridMin = GridBounds.ClampExclusive(gridMin);
            gridMax = GridBounds.ClampExclusive(gridMax);

            AxisAlignedBox3i gridbox = new AxisAlignedBox3i(gridMin, gridMax);
            switch (CombineMode) {
                case CombineModes.DistanceMinUnion:
                    sample_min(f, gridbox.IndicesInclusive());
                    break;
            }
        }


        void sample_min(BoundedImplicitFunction3d f, IEnumerable<Vector3i> indices)
        {
            gParallel.ForEach(indices, (idx) => {
                Vector3d v = Indexer.FromGrid(idx);
                double d = f.Value(ref v);
                Grid.set_min(ref idx, (float)d);
            });
        }

    }
}
