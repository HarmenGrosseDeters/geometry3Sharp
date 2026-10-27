using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace g3
{
    public class MeshICP
    {
        public IPointSet Source; 
        public DMeshAABBTree3 TargetSurface;

        public Vector3d Translation;
        public Quaterniond Rotation;

        public Action<string> VerboseF = null;

        public int MaxIterations = 50;

        public bool UseNormals = false;
        public double MaxAllowableDistance = double.MaxValue;


        /// <summary>
        /// change of the mean distance below which an iteration counts as converged, in absolute units. Left at its
        /// default (1e-5), RelativeConvergeTolerance is used instead (an absolute 1e-5 stopped millimetre models after a
        /// few iterations and never converged on kilometre ones, docs/ROBUSTNESS_SWEEP.md R8); any other value is used as given.
        /// </summary>
        public double ConvergeTolerance = 0.00001;
        /// <summary> the convergence tolerance as a fraction of the diagonal of the target's bounds (while ConvergeTolerance has its default) </summary>
        public double RelativeConvergeTolerance = 5e-6;
        /// <summary> the tolerance the last Solve() used </summary>
        public double EffectiveConvergeTolerance { get; private set; } = double.NaN;
        public bool Converged = false;

        bool is_initialized = false;
        int[] MapV;
        int[] FromV;        // vertex id of each From[i]
        Vector3d[] From;
        Vector3d[] To;
        double[] Weights;
        double LastError;

        public MeshICP(IPointSet source, DMeshAABBTree3 target)
        {
            Source = source;
            TargetSurface = target;

            Translation = Vector3d.Zero;
            Rotation = Quaterniond.Identity;
        }


        /// <summary>
        /// Solve MaxIterations steps, or until convergence.
        /// If bUpdate = true, will improve on previous solution.
        /// </summary>
        public void Solve(bool bUpdate = false)
        {
            if (bUpdate == false)
                is_initialized = false;
            if (is_initialized == false) {
                initialize();
                is_initialized = true;
            }

            update_from();
            update_to();


            LastError = measure_error();

            EffectiveConvergeTolerance = ConvergeTolerance;
            if (ConvergeTolerance == 0.00001) {
                double diag = TargetSurface.Mesh.CachedBounds.DiagonalLength;
                if (diag > 0 && double.IsInfinity(diag) == false)
                    EffectiveConvergeTolerance = RelativeConvergeTolerance * diag;
            }

            int nTolPasssed = 0;
            int nMaxTolPassed = 5;      // if we get this many iterations without
                                        // making an improvement > SolveTolerance, we have converged

            for (int i = 0; i < MaxIterations && nTolPasssed < nMaxTolPassed; ++i) {

                if (VerboseF != null)
                    VerboseF(string.Format("[ICP] iter {0} : error {1}", i, LastError));

                update_transformation();
                update_from();
                update_to();

                double err = measure_error();
                if ( Math.Abs(LastError - err) < EffectiveConvergeTolerance ) {
                    nTolPasssed++;
                } else {
                    LastError = err;
                    nTolPasssed = 0;
                }
            }

            Converged = (nTolPasssed >= nMaxTolPassed);
        }


        /// <summary>
        /// returns last measured deviation error metric (currently mean distance)
        /// </summary>
        public double Error
        {
            get { return LastError; }
        }



        /// <summary>
        /// Transfer new vertex positions to target
        /// </summary>
        public void UpdateVertices(IDeformableMesh target)
        {
            bool bNormals = target.HasVertexNormals;

            update_from();
            foreach (int vid in target.VertexIndices()) {
                int i = MapV[vid];
                target.SetVertex(vid, From[i]);

                if ( bNormals ) {
                    target.SetVertexNormal(vid,
                        (Vector3f)(Rotation * target.GetVertexNormal(vid)) );
                }
            }
        }


        // set up internal data structures
        void initialize()
        {
            From = new Vector3d[Source.VertexCount];
            To = new Vector3d[Source.VertexCount];
            Weights = new double[Source.VertexCount];

            MapV = new int[Source.MaxVertexID];
            FromV = new int[Source.VertexCount];

            int i = 0;
            foreach (int vid in Source.VertexIndices()) {
                MapV[vid] = i;
                FromV[i] = vid;
                Weights[i] = 1.0f;
                From[i++] = Source.GetVertex(vid);
            }
        }




        // apply transformation to source vertices, store in From array
        void update_from()
        {
            int i = 0;
            foreach (int vid in Source.VertexIndices()) {
                Weights[i] = 1.0f;
                Vector3d v = Source.GetVertex(vid);

                From[i++] = (Rotation * v) + Translation;
            }
        }

        // for each From[i], find closest point on TargetSurface
        void update_to()
        {
            double max_dist = double.MaxValue;

            bool bNormals = (UseNormals && Source.HasVertexNormals);

            Interval1i range = Interval1i.Range(From.Length);
            gParallel.ForEach(range, (vi) => {
                // (a non-finite source point is ignored: weight 0, skipped in the sums)
                if (From[vi].IsFinite == false) {
                    Weights[vi] = 0;
                    return;
                }
                int tid = TargetSurface.FindNearestTriangle(From[vi], max_dist);
                if (tid == DMesh3.InvalidID) {
                    Weights[vi] = 0;
                    return;
                }

                DistPoint3Triangle3 d = MeshQueries.TriangleDistance(TargetSurface.Mesh, tid, From[vi]);
                if ( d.DistanceSquared > MaxAllowableDistance*MaxAllowableDistance ) {
                    Weights[vi] = 0;
                    return;
                }

                To[vi] = d.TriangleClosest;
                Weights[vi] = 1.0f;

                if ( bNormals ) {
                    Vector3d fromN = Rotation * Source.GetVertexNormal(FromV[vi]);
                    Vector3d toN = TargetSurface.Mesh.GetTriNormal(tid);
                    double fDot = fromN.Dot(toN);
                    Debug.Assert(MathUtil.IsFinite(fDot));
                    if (fDot < 0)
                        Weights[vi] = 0;
                    else
                        Weights[vi] += Math.Sqrt(fDot);
                }

            });
        }



        // measure deviation between From and To
        double measure_error()
        {
            double sum = 0;
            double wsum = 0;
            for ( int i = 0; i < From.Length; ++i ) {
                if (Weights[i] == 0) continue;
                sum += Weights[i] * From[i].Distance(To[i]);
                wsum += Weights[i];
            }
            return (wsum > 0) ? sum / wsum : double.NaN;
        }


        /// <summary>
        /// Solve for Translate/Rotate update, based on current From and To
        /// Note: From and To are invalidated, will need to be re-computed after calling this
        /// </summary>
        void update_transformation()
        {
            int N = From.Length;

            // normalize weights
            double wSum = 0;
            for (int i = 0; i < N; ++i) {
                wSum += Weights[i];
            }

            if (!(wSum > 0))
                return;         // (no usable point pairs: no update)
            double wSumInv = 1.0 / wSum;

            // compute means
            Vector3d MeanX = Vector3d.Zero;
            Vector3d MeanY = Vector3d.Zero;
            for ( int i = 0; i < N; ++i ) {
                if (Weights[i] == 0) continue;
                MeanX += (Weights[i] * wSumInv) * From[i];
                MeanY += (Weights[i] * wSumInv) * To[i];
            }


            // subtract means
            for ( int i = 0; i < N; ++i ) {
                From[i] -= MeanX;
                To[i] -= MeanY;
            }

            // construct matrix 3x3 Matrix  From*Transpose(To)
            // (vectors are columns)
            double[] M = new double[9];
            for (int k = 0; k < 3; ++k) {
                int r = 3 * k;
                for (int i = 0; i < N; ++i) {
                    if (Weights[i] == 0) continue;
                    double lhs = (Weights[i] * wSumInv) * From[i][k];
                    M[r + 0] += lhs * To[i].x;
                    M[r + 1] += lhs * To[i].y;
                    M[r + 2] += lhs * To[i].z;
                }
            }

            // compute SVD of M
            SingularValueDecomposition svd = new SingularValueDecomposition(3, 3, 100);
            uint ok = svd.Solve(M, -1);     // sort in decreasing order, like Eigen
            Debug.Assert(ok < 9999999);
            double[] U = new double[9], V = new double[9], Tmp = new double[9]; ;
            svd.GetU(U);
            svd.GetV(V);

            // this is our rotation update
            double[] RotUpdate = new double[9];

            // U*V gives us desired rotation
            double detU = MatrixUtil.Determinant3x3(U);
            double detV = MatrixUtil.Determinant3x3(V);
            if ( detU*detV < 0 ) {
                double[] S = MatrixUtil.MakeDiagonal3x3(1, 1, -1);
                MatrixUtil.Multiply3x3(V, S, Tmp);
                MatrixUtil.Transpose3x3(U);
                MatrixUtil.Multiply3x3(Tmp, U, RotUpdate);
            } else {
                MatrixUtil.Transpose3x3(U);
                MatrixUtil.Multiply3x3(V, U, RotUpdate);
            }

            // convert matrix to quaternion
            Matrix3d RotUpdateM = new Matrix3d(RotUpdate);
            Quaterniond RotUpdateQ = new Quaterniond(RotUpdateM);

            // the update maps the current From = Rotation*v + Translation to RotUpdate*From + TransUpdate, so it composes
            // as Rotation' = RotUpdate*Rotation, Translation' = RotUpdate*Translation + TransUpdate. Upstream added
            // TransUpdate alone (its TODO asked whether that was right): the missing (RotUpdate - I)*Translation is small
            // near the coordinate origin, but at UTM coordinates (Translation ~ 1e6 after the first rotation about the
            // origin) the iteration diverged (docs/ROBUSTNESS_SWEEP.md R8)
            Vector3d TransUpdate = MeanY - RotUpdateQ * MeanX;
            Translation = RotUpdateQ * Translation + TransUpdate;
            Rotation = RotUpdateQ * Rotation;


            // above was ported from this code...still not supporting weights though.
            // need to figure out exactly what this code does (like, what does
            // transpose() do to a vector??)

            //            /// Normalize weight vector
            //Eigen::VectorXd w_normalized = w/w.sum();
            ///// De-mean
            //Eigen::Vector3d X_mean, Y_mean;
            //for(int i=0; i<3; ++i) {
            //    X_mean(i) = (X.row(i).array()*w_normalized.transpose().array()).sum();
            //    Y_mean(i) = (Y.row(i).array()*w_normalized.transpose().array()).sum();

            //Eigen::Matrix3d sigma = X * w_normalized.asDiagonal() * Y.transpose();

        }








    }
}
