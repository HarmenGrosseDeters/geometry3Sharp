// Copyright (c) Ryan Schmidt (rms@gradientspace.com) - All Rights Reserved
// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using g3;

namespace gs
{
	/// <summary>
	/// Remove "occluded" triangles, ie triangles on the "inside" of the mesh. 
    /// This is a fuzzy definition, current implementation is basically computing
    /// something akin to ambient occlusion, and if face is fully occluded, then
    /// we classify it as inside and remove it.
    ///
    /// Each triangle (centroid) or vertex (PerVertex) is tested at a point nudged out along its normal by
    /// NormalOffset, with the test chosen by InsideMode. Triangles classified inside are removed from the
    /// mesh in place (vertices left unused are removed too); their ids are in RemovedT. Used by
    /// MeshAutoRepair.RemoveMode.
	/// </summary>
	public class RemoveOccludedTriangles
	{
		public DMesh3 Mesh;
        /// <summary> optional prebuilt AABB tree of Mesh (not used in RayParity mode if the mesh has holes) </summary>
        public DMeshAABBTree3 Spatial;

        /// <summary> ids of the removed triangles (unordered). Empty if nothing was removed. Not meaningful if Apply() returned false </summary>
        public List<int> RemovedT = null;

        /// <summary> true if removing some of the triangles failed </summary>
        public bool RemoveFailed = false;

        /// <summary>
        /// if true, then we discard tris if any vertex is occluded.
        /// Otherwise we discard based on tri centroids
        /// </summary>
        public bool PerVertex = false;

        /// <summary> we nudge points out along the normal by this amount to try to counteract numerical issues </summary>
        public double NormalOffset = MathUtil.ZeroTolerance;

        /// <summary> a point is inside if its winding number is above this (winding number modes) </summary>
        public double WindingIsoValue = 0.5;

        /// <summary>
        /// Inside test. RayParity: ray-crossing parity against the mesh (holes are filled with a fan in a copy
        /// first). AnalyticWindingNumber / FastWindingNumber: exact or fast generalized winding number above
        /// WindingIsoValue (robust for open meshes and soups). SimpleOcclusionTest: occluded if rays along all six
        /// axis directions hit the mesh.
        /// </summary>
        public enum CalculationMode
        {
            RayParity = 0,
            AnalyticWindingNumber = 1,
            FastWindingNumber = 2,
            SimpleOcclusionTest = 3
        }
        /// <summary> inside test, see CalculationMode; default RayParity </summary>
        public CalculationMode InsideMode = CalculationMode.RayParity;


        /// <summary>
        /// Set this to be able to cancel the running computation
        /// </summary>
        public ProgressCancel Progress = null;

        /// <summary>
        /// if this returns true, abort computation. 
        /// </summary>
        protected virtual bool Cancelled() {
            return (Progress == null) ? false : Progress.Cancelled();
        }



        public RemoveOccludedTriangles(DMesh3 mesh)
		{
			Mesh = mesh;
		}

        public RemoveOccludedTriangles(DMesh3 mesh, DMeshAABBTree3 spatial)
        {
            Mesh = mesh;
            Spatial = spatial;
        }


        /// <summary>
        /// Classify and remove the inside triangles.
        /// </summary>
        /// <returns>false if cancelled (nothing is removed then), true otherwise</returns>
        public virtual bool Apply()
        {
            DMesh3 testAgainstMesh = Mesh;
            if (InsideMode == CalculationMode.RayParity) {
                MeshBoundaryLoops loops = new MeshBoundaryLoops(testAgainstMesh);
                if (loops.Count > 0) {
                    testAgainstMesh = new DMesh3(Mesh);
                    foreach (var loop in loops) {
                        if (Cancelled())
                            return false;
                        SimpleHoleFiller filler = new SimpleHoleFiller(testAgainstMesh, loop);
                        filler.Fill();
                    }
                }
            }

            DMeshAABBTree3 spatial = (Spatial != null && testAgainstMesh == Mesh) ? 
                Spatial : new DMeshAABBTree3(testAgainstMesh, true);
            if (InsideMode == CalculationMode.AnalyticWindingNumber)
                spatial.WindingNumber(Vector3d.Zero);
            else if (InsideMode == CalculationMode.FastWindingNumber )
                spatial.FastWindingNumber(Vector3d.Zero);

            if (Cancelled())
                return false;

            // ray directions
            List<Vector3d> ray_dirs = null; int NR = 0;
            if (InsideMode == CalculationMode.SimpleOcclusionTest) {
                ray_dirs = new List<Vector3d>();
                ray_dirs.Add(Vector3d.AxisX); ray_dirs.Add(-Vector3d.AxisX);
                ray_dirs.Add(Vector3d.AxisY); ray_dirs.Add(-Vector3d.AxisY);
                ray_dirs.Add(Vector3d.AxisZ); ray_dirs.Add(-Vector3d.AxisZ);
                NR = ray_dirs.Count;
            }

            Func<Vector3d, bool> isOccludedF = (pt) => {
                // a test point of a non-finite vertex is not occluded (its rays threw deep inside the tree query,
                // eg MeshAutoRepair on a mesh with an infinite vertex, docs/ROBUSTNESS_SWEEP.md R11)
                if (pt.IsFinite == false)
                    return false;

                if (InsideMode == CalculationMode.RayParity) {
                    return spatial.IsInside(pt);
                } else if (InsideMode == CalculationMode.AnalyticWindingNumber) {
                    return spatial.WindingNumber(pt) > WindingIsoValue;
                } else if (InsideMode == CalculationMode.FastWindingNumber) {
                    return spatial.FastWindingNumber(pt) > WindingIsoValue;
                } else {
                    for (int k = 0; k < NR; ++k) {
                        int hit_tid = spatial.FindNearestHitTriangle(new Ray3d(pt, ray_dirs[k]));
                        if (hit_tid == DMesh3.InvalidID)
                            return false;
                    }
                    return true;
                }
            };

            bool cancel = false;

            // bool[] rather than BitArray, which loses updates when written from the parallel loop below
            bool[] vertices = null;
            if ( PerVertex ) {
                vertices = new bool[Mesh.MaxVertexID];

                MeshNormals normals = null;
                if (Mesh.HasVertexNormals == false) {
                    normals = new MeshNormals(Mesh);
                    normals.Compute();
                }

                gParallel.ForEach(Mesh.VertexIndices(), (vid) => {
                    if (cancel) return;
                    if (vid % 10 == 0) cancel = Cancelled();

                    Vector3d c = Mesh.GetVertex(vid);
                    Vector3d n = (normals == null) ? Mesh.GetVertexNormal(vid) : normals[vid];
                    c += n * NormalOffset;
                    vertices[vid] = isOccludedF(c);
                });
            }
            if (Cancelled())
                return false;

            RemovedT = new List<int>();
            SpinLock removeLock = new SpinLock();

            gParallel.ForEach(Mesh.TriangleIndices(), (tid) => {
                if (cancel) return;
                if (tid % 10 == 0) cancel = Cancelled();

                bool inside = false;
                if (PerVertex) {
                    Index3i tri = Mesh.GetTriangle(tid);
                    inside = vertices[tri.a] || vertices[tri.b] || vertices[tri.c];

                } else {
                    Vector3d c = Mesh.GetTriCentroid(tid);
                    Vector3d n = Mesh.GetTriNormal(tid);
                    c += n * NormalOffset;
                    inside = isOccludedF(c);
                }

                if (inside) {
                    bool taken = false;
                    removeLock.Enter(ref taken);
                    RemovedT.Add(tid);
                    removeLock.Exit();
                }
            });

            if (Cancelled())
                return false;

            if (RemovedT.Count > 0) {
                MeshEditor editor = new MeshEditor(Mesh);
                bool bOK = editor.RemoveTriangles(RemovedT, true);
                RemoveFailed = (bOK == false);
            } 

            return true;
		}


        
	}
}
