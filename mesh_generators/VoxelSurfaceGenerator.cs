using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace g3
{
    public class VoxelSurfaceGenerator
    {
        public IBinaryVoxelGrid Voxels;

        // if true, we do not add triangles that are "inside" outer surface of voxel faces
        public bool SkipInteriorFaces = true;

        // if false, we skip faces along outer boundary
        public bool CapAtBoundary = true;

        // "normal" meshes are counter-clockwise. Unity is CW though...
        public bool Clockwise = false;

		// if set, we assign colors to each block
		public Func<Vector3i, Colorf> ColorSourceF;

		// if the vert-count or tri-count of a mesh would get higher than this value, we start a new mesh: a bound that holds
		// for every mesh (a voxel is never split, so a value below the 24 vertices and 12 triangles of one voxel with six
		// exposed faces cannot be kept, and a mesh then holds that one voxel)
		public int MaxMeshElementCount = int.MaxValue;

        // result
		public List<DMesh3> Meshes;


		DMesh3 cur_mesh;
		void append_mesh() {
			if (Meshes == null || Meshes.Count == 0) {
				Meshes = new List<DMesh3>();
			}
			cur_mesh = new DMesh3(MeshComponents.VertexNormals);
			if (ColorSourceF != null)
				cur_mesh.EnableVertexColors(Colorf.White);
			Meshes.Add(cur_mesh);
		}
		void check_counts_or_append(int newV, int newT) {
			// (an empty mesh takes the voxel whatever the limit: a new mesh would not help)
			if (cur_mesh.MaxVertexID == 0 ||
				(cur_mesh.MaxVertexID + newV <= MaxMeshElementCount && cur_mesh.MaxTriangleID + newT <= MaxMeshElementCount))
				return;
			append_mesh();
		}


        public void Generate()
        {
			Meshes = null;      // start a new result list, do not append to the previous one
			append_mesh();

            AxisAlignedBox3i bounds = Voxels.GridBounds;
            bounds.Max -= Vector3i.One;

            int[] vertices = new int[4];

            bool[] exposed = new bool[6];

            foreach ( Vector3i nz in Voxels.NonZeros() ) {
                // the faces this voxel adds (checks dependent on neighbours), counted first so that the whole voxel fits
                int faces = 0;
                for ( int fi = 0; fi < 6; ++fi ) {
                    Index3i nbr = nz + gIndices.GridOffsets6[fi];
                    exposed[fi] = bounds.Contains(nbr) ? !(SkipInteriorFaces && Voxels.Get(nbr)) : CapAtBoundary;
                    if (exposed[fi]) ++faces;
                }
                if (faces == 0)
                    continue;
				check_counts_or_append(4 * faces, 2 * faces);

                Box3d cube = Box3d.UnitZeroCentered;
                cube.Center = (Vector3d)nz;

                for ( int fi = 0; fi < 6; ++fi ) {

                    if (exposed[fi] == false)
                        continue;


                    int ni = gIndices.BoxFaceNormals[fi];
                    Vector3f n = (Vector3f)(Math.Sign(ni) * cube.Axis(Math.Abs(ni) - 1));
                    NewVertexInfo vi = new NewVertexInfo(Vector3d.Zero, n);
					if (ColorSourceF != null) {
						vi.c = ColorSourceF(nz);
						vi.bHaveC = true;
					}
                    for ( int j = 0; j < 4; ++j ) {
                        vi.v = cube.Corner(gIndices.BoxFaces[fi, j]);
						vertices[j] = cur_mesh.AppendVertex(vi);
                    }

                    Index3i t0 = new Index3i(vertices[0], vertices[1], vertices[2], Clockwise);
                    Index3i t1 = new Index3i(vertices[0], vertices[2], vertices[3], Clockwise);
                    cur_mesh.AppendTriangle(t0);
                    cur_mesh.AppendTriangle(t1);
                }

            }

        }
    }
}
