using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace g3
{
    /// <summary>
    /// Global Laplacian smoothing by a sparse least-squares solve: finds vertex positions x that minimize
    /// |L x|^2 + sum over constrained vertices of (w_i |x_i - p_i|)^2, where L is a (uniform-like) graph
    /// Laplacian of the mesh and p_i, w_i are soft constraints set with SetConstraint(). The target Laplacian
    /// is zero, so unconstrained regions become as smooth (flat/minimal) as the constraints allow; the weights
    /// control how strongly vertices are held. Without any constraints the problem is singular and the
    /// result degenerates, so always constrain some vertices (eg the boundary, or all vertices with a small
    /// weight to their current position for mild smoothing).
    ///
    /// Solve() writes the result to an array; SolveAndUpdateMesh() writes it to the mesh in place. Both return
    /// false if the conjugate-gradient solve does not converge (the mesh is then not changed). Initialize()
    /// (called automatically by the first solve) captures the mesh topology and positions, so do not change
    /// the mesh topology afterwards. RegionSmooth() smooths a set of triangles with a fixed border.
    /// For simple local smoothing with a fixed number of steps see <see cref="MeshIterativeSmooth"/>.
    /// </summary>
    public class LaplacianMeshSmoother
    {
        /// <summary> the mesh (SolveAndUpdateMesh() modifies it in place) </summary>
        public DMesh3 Mesh;

        // info that is fixed based on mesh
        PackedSparseMatrix PackedM;
        int N;
        int[] ToMeshV, ToIndex;
        double[] Px, Py, Pz;
        int[] nbr_counts;
        double[] MLx, MLy, MLz;

        /// <summary> a soft position constraint (see SetConstraint) </summary>
        public struct SoftConstraintV
        {
            public Vector3d Position;
            public double Weight;
            public bool PostFix;
        }
        Dictionary<int, SoftConstraintV> SoftConstraints = new Dictionary<int, SoftConstraintV>();
        bool HavePostFixedConstraints = false;


        // positions are solved relative to this point (the bounds center at Initialize): the CG tolerance is relative to
        // the right-hand side, which grew with the distance from the coordinate origin (upstream lost about 1e-8 of the
        // UTM coordinates, ie centimetres, docs/ROBUSTNESS_SWEEP.md R2)
        Vector3d origin = Vector3d.Zero;

        // needs to be updated after constraints
        bool need_solve_update;
        DiagonalMatrix WeightsM;
        double[] Cx, Cy, Cz;
        double[] Bx, By, Bz;
        DiagonalMatrix Preconditioner;


        /// <summary>
        /// Solve the normal equations (L^T L + W^2) x = W^2 p of the least-squares problem (default). Appendix C from
        /// http://sites.fas.harvard.edu/~cs277/papers/deformation_survey.pdf . Must be set before Initialize().
        /// </summary>
        public bool UseSoftConstraintNormalEquations = true;


        // result
        double[] Sx, Sy, Sz;


        /// <summary> smoother for mesh; set constraints, then call Solve() or SolveAndUpdateMesh() </summary>
        public LaplacianMeshSmoother(DMesh3 mesh)
        {
            Mesh = mesh;
        }


        /// <summary>
        /// Soft constraint: pull vertex vID towards targetPos with the given weight (larger = stronger; 0 = none).
        /// If bForceToFixedPos, the vertex is also set exactly to targetPos after the solve.
        /// Replaces an existing constraint of the vertex.
        /// </summary>
        public void SetConstraint(int vID, Vector3d targetPos, double weight, bool bForceToFixedPos = false)
        {
            SoftConstraints[vID] = new SoftConstraintV() { Position = targetPos, Weight = weight, PostFix = bForceToFixedPos };
            HavePostFixedConstraints = HavePostFixedConstraints || bForceToFixedPos;
            need_solve_update = true;
        }

        /// <summary> true if SetConstraint() was called for vertex vID </summary>
        public bool IsConstrained(int vID) {
            return SoftConstraints.ContainsKey(vID);
        }

        /// <summary> remove all soft constraints </summary>
        public void ClearConstraints()
        {
            SoftConstraints.Clear();
            HavePostFixedConstraints = false;
            need_solve_update = true;
        }


        /// <summary>
        /// Build the Laplacian system from the current mesh topology and vertex positions (the CG start values).
        /// Called by the first solve if not called explicitly; call again if the mesh changed.
        /// </summary>
        public void Initialize()
        {
            ToMeshV = new int[Mesh.MaxVertexID];
            ToIndex = new int[Mesh.MaxVertexID];
            N = 0;
            foreach (int vid in Mesh.VertexIndices()) {
                ToMeshV[N] = vid;
                ToIndex[vid] = N;
                N++;
            }

            Px = new double[N];
            Py = new double[N];
            Pz = new double[N];
            nbr_counts = new int[N];
            SymmetricSparseMatrix M = new SymmetricSparseMatrix();

            origin = Mesh.CachedBounds.Center;
            if (origin.IsFinite == false)
                origin = Vector3d.Zero;
            for (int i = 0; i < N; ++i) {
                int vid = ToMeshV[i];
                Vector3d v = Mesh.GetVertex(vid) - origin;
                Px[i] = v.x; Py[i] = v.y; Pz[i] = v.z;
                nbr_counts[i] = Mesh.GetVtxEdgeCount(vid);
            }

            // construct laplacian matrix
            for (int i = 0; i < N; ++i) {
                int vid = ToMeshV[i];
                int n = nbr_counts[i];

                double sum_w = 0;
                foreach (int nbrvid in Mesh.VtxVerticesItr(vid)) {
                    int j = ToIndex[nbrvid];
                    int n2 = nbr_counts[j];

                    // weight options
                    //double w = -1;
                    double w = -1.0 / Math.Sqrt(n + n2);
                    //double w = -1.0 / n;

                    M.Set(i, j, w);
                    sum_w += w;
                }
                sum_w = -sum_w;
                M.Set(i, i, sum_w);
            }

            // transpose(L) * L, but matrix is symmetric...
            if (UseSoftConstraintNormalEquations) {
                //M = M.Multiply(M);
                // only works if M is symmetric!!
                PackedM = M.SquarePackedParallel();
            } else {
                PackedM = new PackedSparseMatrix(M);
            }

            // compute laplacian vectors of initial mesh positions
            MLx = new double[N];
            MLy = new double[N];
            MLz = new double[N];
            PackedM.Multiply(Px, MLx);
            PackedM.Multiply(Py, MLy);
            PackedM.Multiply(Pz, MLz);

            // zero out...this is the smoothing bit!
            for (int i = 0; i < Px.Length; ++i) {
                MLx[i] = 0;
                MLy[i] = 0;
                MLz[i] = 0;
            }

            // allocate memory for internal buffers
            Preconditioner = new DiagonalMatrix(N);
            WeightsM = new DiagonalMatrix(N);
            Cx = new double[N]; Cy = new double[N]; Cz = new double[N];
            Bx = new double[N]; By = new double[N]; Bz = new double[N];
            Sx = new double[N]; Sy = new double[N]; Sz = new double[N];

            need_solve_update = true;
            UpdateForSolve();
        }




        void UpdateForSolve()
        {
            if (need_solve_update == false)
                return;

            // construct constraints matrix and RHS
            WeightsM.Clear();
            Array.Clear(Cx, 0, N);
            Array.Clear(Cy, 0, N);
            Array.Clear(Cz, 0, N);
            foreach ( var constraint in SoftConstraints ) {
                int vid = constraint.Key;
                int i = ToIndex[vid];
                double w = constraint.Value.Weight;

                if ( UseSoftConstraintNormalEquations )
                    w = w * w;

                WeightsM.Set(i, i, w);
                Vector3d pos = constraint.Value.Position - origin;
                Cx[i] = w * pos.x;
                Cy[i] = w * pos.y;
                Cz[i] = w * pos.z;
            }

            // add RHS vectors
            for (int i = 0; i < N; ++i) {
                Bx[i] = MLx[i] + Cx[i];
                By[i] = MLy[i] + Cy[i];
                Bz[i] = MLz[i] + Cz[i];
            }

            // update basic Jacobi preconditioner
            for ( int i = 0; i < N; i++ ) {
                double diag_value = PackedM[i, i] + WeightsM[i, i];
                Preconditioner.Set(i, i, 1.0 / diag_value);
            }

            need_solve_update = false;
        }




        /// <summary>
        /// Solve with three separate preconditioned CG solves (x, y, z in parallel) and write the new position of
        /// each vertex to Result[vid]. Result must be as large as Mesh.MaxVertexID. Returns false if a solve
        /// does not converge (Result is then not written). Does not modify the mesh.
        /// </summary>
        public bool SolveMultipleCG(Vector3d[] Result)
        {
            if (WeightsM == null)
                Initialize();       // force initialize...

            UpdateForSolve();

            // use initial positions as initial solution. 
            Array.Copy(Px, Sx, N);
            Array.Copy(Py, Sy, N);
            Array.Copy(Pz, Sz, N);

            Action<double[], double[]> CombinedMultiply = (X, B) => {
                //PackedM.Multiply(X, B);
                PackedM.Multiply_Parallel(X, B);

                for (int i = 0; i < N; ++i)
                    B[i] += WeightsM.D[i] * X[i];
            };

            SparseSymmetricCG SolverX = new SparseSymmetricCG() { B = Bx, X = Sx,
                MultiplyF = CombinedMultiply, PreconditionMultiplyF = Preconditioner.Multiply,
                UseXAsInitialGuess = true };
            SparseSymmetricCG SolverY = new SparseSymmetricCG() { B = By, X = Sy,
                MultiplyF = CombinedMultiply, PreconditionMultiplyF = Preconditioner.Multiply,
                UseXAsInitialGuess = true };
            SparseSymmetricCG SolverZ = new SparseSymmetricCG() { B = Bz, X = Sz,
                MultiplyF = CombinedMultiply, PreconditionMultiplyF = Preconditioner.Multiply,
                UseXAsInitialGuess = true };

            SparseSymmetricCG[] solvers = new SparseSymmetricCG[3] { SolverX, SolverY, SolverZ };
            bool[] ok = new bool[3];
            int[] indices = new int[3] { 0, 1, 2 };

            // preconditioned solve is marginally faster
            Action<int> SolveF = (i) => {  ok[i] = solvers[i].SolvePreconditioned(); };
            //Action<int> SolveF = (i) => {  ok[i] = solvers[i].Solve(); };
            gParallel.ForEach(indices, SolveF);

            if (ok[0] == false || ok[1] == false || ok[2] == false)
                return false;

            for ( int i = 0; i < N; ++i ) {
                int vid = ToMeshV[i];
                Result[vid] = new Vector3d(Sx[i], Sy[i], Sz[i]) + origin;
            }

            // apply post-fixed constraints
            if (HavePostFixedConstraints) {
                foreach (var constraint in SoftConstraints) {
                    if (constraint.Value.PostFix) {
                        int vid = constraint.Key;
                        Result[vid] = constraint.Value.Position;
                    }
                }
            }

            return true;
        }



        /// <summary>
        /// Same as SolveMultipleCG(), with one CG solver for the three right-hand sides.
        /// Result must be as large as Mesh.MaxVertexID. Does not modify the mesh.
        /// </summary>
        public bool SolveMultipleRHS(Vector3d[] Result)
        {
            if (WeightsM == null)
                Initialize();       // force initialize...

            UpdateForSolve();

            // use initial positions as initial solution. 
            double[][] B = BufferUtil.InitNxM(3, N, new double[][] { Bx, By, Bz } );
            double[][] X = BufferUtil.InitNxM(3, N, new double[][] { Px, Py, Pz } );

            Action<double[][], double[][]> CombinedMultiply = (Xt, Bt) => {
                PackedM.Multiply_Parallel_3(Xt, Bt);
                gParallel.ForEach(Interval1i.Range(3), (j) => {
                    BufferUtil.MultiplyAdd(Bt[j], WeightsM.D, Xt[j]);
                });
            };

            Action<double[][], double[][]> CombinedPreconditionerMultiply = (Xt, Bt) => {
                gParallel.ForEach(Interval1i.Range(3), (j) => {
                    Preconditioner.Multiply(Xt[j], Bt[j]);
                });
            };

            SparseSymmetricCGMultipleRHS Solver = new SparseSymmetricCGMultipleRHS() { 
                B = B, X = X,
                MultiplyF = CombinedMultiply, PreconditionMultiplyF = CombinedPreconditionerMultiply,
                UseXAsInitialGuess = true
            };

            // preconditioned solve is marginally faster
            //bool ok = Solver.Solve();
            bool ok = Solver.SolvePreconditioned();

            if (ok == false)
                return false;

            for (int i = 0; i < N; ++i) {
                int vid = ToMeshV[i];
                Result[vid] = new Vector3d(X[0][i], X[1][i], X[2][i]) + origin;
            }

            // apply post-fixed constraints
            if (HavePostFixedConstraints) {
                foreach (var constraint in SoftConstraints) {
                    if (constraint.Value.PostFix) {
                        int vid = constraint.Key;
                        Result[vid] = constraint.Value.Position;
                    }
                }
            }

            return true;
        }


        /// <summary>
        /// Solve for the smoothed positions into Result (indexed by vertex id, size at least Mesh.MaxVertexID),
        /// using SolveMultipleCG() below 10000 vertices and SolveMultipleRHS() above. Returns false if the
        /// solve does not converge. Does not modify the mesh.
        /// </summary>
        public bool Solve(Vector3d[] Result)
        {
            // for small problems, faster to use separate CGs?
            if ( Mesh.VertexCount < 10000 )
                return SolveMultipleCG(Result);
            else
                return SolveMultipleRHS(Result);
        }




        /// <summary>
        /// Solve() and set the mesh vertices to the result (in place). Returns false, leaving the mesh
        /// unchanged, if the solve does not converge.
        /// </summary>
        public bool SolveAndUpdateMesh()
        {
            int N = Mesh.MaxVertexID;
            Vector3d[] Result = new Vector3d[N];
            if ( Solve(Result) == false )
                return false;
            for (int i = 0; i < N; ++i ) {
                if ( Mesh.IsVertex(i) ) {
                    Mesh.SetVertex(i, Result[i]);
                }
            }
            return true;
        }




        /// <summary>
        /// Apply LaplacianMeshSmoother to subset of mesh triangles. 
        /// border of subset always has soft constraint with borderWeight, 
        /// but is then snapped back to original vtx pos after solve.
        /// nConstrainLoops inner loops are also soft-constrained, with weight falloff via square roots (defines continuity)
        /// interiorWeight is soft constraint added to all vertices.
        /// nIncludeExteriorRings expands the region by that many one-rings first; if bPreserveExteriorRings, the
        /// added vertices are snapped back to their original positions after the solve.
        /// The mesh is modified in place (only the region's vertex positions).
        /// </summary>
        public static void RegionSmooth(DMesh3 mesh, IEnumerable<int> triangles, 
            int nConstrainLoops, 
            int nIncludeExteriorRings,
            bool bPreserveExteriorRings,
            double borderWeight = 10.0, double interiorWeight = 0.0)
        {
            HashSet<int> fixedVerts = new HashSet<int>();
            if ( nIncludeExteriorRings > 0 ) {
                MeshFaceSelection expandTris = new MeshFaceSelection(mesh);
                expandTris.Select(triangles);
                if (bPreserveExteriorRings) {
                    MeshEdgeSelection bdryEdges = new MeshEdgeSelection(mesh);
                    bdryEdges.SelectBoundaryTriEdges(expandTris);
                    expandTris.ExpandToOneRingNeighbours(nIncludeExteriorRings);
                    MeshVertexSelection startVerts = new MeshVertexSelection(mesh);
                    startVerts.SelectTriangleVertices(triangles);
                    startVerts.DeselectEdges(bdryEdges);
                    MeshVertexSelection expandVerts = new MeshVertexSelection(mesh, expandTris);
                    foreach (int vid in expandVerts) {
                        if (startVerts.IsSelected(vid) == false)
                            fixedVerts.Add(vid);
                    }
                } else {
                    expandTris.ExpandToOneRingNeighbours(nIncludeExteriorRings);
                }
                triangles = expandTris;
            }

            RegionOperator region = new RegionOperator(mesh, triangles);
            DSubmesh3 submesh = region.Region;
            DMesh3 smoothMesh = submesh.SubMesh;
            LaplacianMeshSmoother smoother = new LaplacianMeshSmoother(smoothMesh);

            // map fixed verts to submesh
            HashSet<int> subFixedVerts = new HashSet<int>();
            foreach (int base_vid in fixedVerts)
                subFixedVerts.Add(submesh.MapVertexToSubmesh(base_vid));

            // constrain borders
            double w = borderWeight;

            HashSet<int> constrained = (submesh.BaseBorderV.Count > 0) ? new HashSet<int>() : null;
            foreach (int base_vid in submesh.BaseBorderV) {
                int sub_vid = submesh.BaseToSubV[base_vid];
                smoother.SetConstraint(sub_vid, smoothMesh.GetVertex(sub_vid), w, true);
                if (constrained != null)
                    constrained.Add(sub_vid);
            }

            if (constrained != null && constrained.Count > 0) {
                w = Math.Sqrt(w);
                for (int k = 0; k < nConstrainLoops; ++k) {
                    HashSet<int> next_layer = new HashSet<int>();

                    foreach (int sub_vid in constrained) {
                        foreach (int nbr_vid in smoothMesh.VtxVerticesItr(sub_vid)) {
                            if (constrained.Contains(nbr_vid) == false) {
                                if ( smoother.IsConstrained(nbr_vid) == false )
                                    smoother.SetConstraint(nbr_vid, smoothMesh.GetVertex(nbr_vid), w, subFixedVerts.Contains(nbr_vid));
                                next_layer.Add(nbr_vid);
                            }
                        }
                    }

                    constrained.UnionWith(next_layer);
                    w = Math.Sqrt(w);
                }
            }

            // soft constraint on all interior vertices, if requested
            if (interiorWeight > 0) {
                foreach (int vid in smoothMesh.VertexIndices()) {
                    if ( smoother.IsConstrained(vid) == false )
                        smoother.SetConstraint(vid, smoothMesh.GetVertex(vid), interiorWeight, subFixedVerts.Contains(vid));
                }
            } else if ( subFixedVerts.Count > 0 ) { 
                foreach (int vid in subFixedVerts) {
                    if (smoother.IsConstrained(vid) == false)
                        smoother.SetConstraint(vid, smoothMesh.GetVertex(vid), 0, true);
                }
            }

            smoother.SolveAndUpdateMesh();
            region.BackPropropagateVertices(true);
        }


    }
}
