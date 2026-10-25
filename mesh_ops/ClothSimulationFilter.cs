// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Ground filter for airborne / drone LiDAR point clouds, after Zhang et al., "An Easy-to-Use Airborne
    /// LiDAR Data Filtering Method Based on Cloth Simulation" (2016). The cloud is turned upside down and a
    /// cloth (grid of particles moving vertically) falls onto it under gravity; springs between neighbour
    /// particles keep the cloth stiff, so it bridges buildings, vehicles and vegetation (which are holes
    /// in the inverted cloud) and settles on the terrain. Points close to the cloth are ground.
    ///
    /// Each particle collides with the lowest point (highest after inversion) near it. Rigidness is the
    /// number of spring relaxation passes per time step (1 soft .. 3 stiff); the fall speed is limited so
    /// that a long fall (deep pits) does not push the cloth through the springs onto objects. Steep faces,
    /// which the stiff cloth cannot follow, are handled by a post-processing step (SlopeStep).
    ///
    /// Limit: an object on a steep face whose height is less than about the slope times ClothResolution
    /// (eg a 2.5 m object on a 51 degree face with a 1 m cloth) cannot be told from the face.
    /// </summary>
    public class ClothSimulationFilter
    {
        public IList<Vector3d> Points;

        /// <summary> particle spacing of the cloth (about the point spacing or a bit larger) </summary>
        public double ClothResolution = 1.0;
        /// <summary> spring relaxation passes per time step (1 soft .. 3 stiff); 2 works for hills and open pits </summary>
        public int Rigidness = 2;
        /// <summary> points within this vertical distance of the cloth are ground </summary>
        public double ClassThreshold = 0.5;
        public int MaxIterations = 500;
        public double TimeStep = 0.65;
        /// <summary>
        /// Post-processing for steep slopes: unsettled cloth particles next to settled ones are settled on
        /// the terrain if its height there differs by less than SlopeStep from the settled neighbour's
        /// (continuous terrain such as a pit face; objects like buildings and vehicles have larger steps).
        /// About the height change of the steepest terrain over one ClothResolution. 0 = off.
        /// </summary>
        public double SlopeStep = 1.5;

        // share of the height difference each of two movable particles moves per relaxation pass
        // (a movable particle next to a settled one moves twice as far), about as in the original CSF
        const double SpringFactor = 0.4;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        public bool[] IsGround;
        /// <summary> final cloth: height (in original orientation) at grid node (i,j) = ClothOrigin + ClothResolution * (i,j) </summary>
        public double[,] ClothHeight;
        public Vector2d ClothOrigin;
        public int Iterations;


        public ClothSimulationFilter(IList<Vector3d> points)
        {
            Points = points;
        }


        public bool Compute()
        {
            int n = Points.Count;
            if (n == 0 || ClothResolution <= 0)
                return false;

            // bounds; heights relative to a local reference (UTM)
            AxisAlignedBox3d bounds = AxisAlignedBox3d.Empty;
            foreach (Vector3d p in Points)
                bounds.Contain(p);
            double r = ClothResolution;
            const int margin = 2;
            ClothOrigin = new Vector2d(bounds.Min.x - margin * r, bounds.Min.y - margin * r);
            int nx = (int)Math.Ceiling(bounds.Width / r) + 2 * margin + 1;
            int ny = (int)Math.Ceiling(bounds.Height / r) + 2 * margin + 1;
            double zref = bounds.Max.z;

            // inverted heights (z' = zref - z >= 0): the collision height of a particle is the highest
            // inverted point (the lowest point) within half a cell; the cloth falls onto the inverted cloud
            double[] collide = new double[nx * ny];
            for (int k = 0; k < collide.Length; ++k)
                collide[k] = double.NaN;
            int[] cellOf = new int[n];
            for (int k = 0; k < n; ++k) {
                Vector3d p = Points[k];
                int i = clampi((int)Math.Round((p.x - ClothOrigin.x) / r), nx), j = clampi((int)Math.Round((p.y - ClothOrigin.y) / r), ny);
                int c = i + nx * j;
                cellOf[k] = c;
                double zi = zref - p.z;
                if (double.IsNaN(collide[c]) || zi > collide[c])
                    collide[c] = zi;
            }
            fill_empty(collide, nx, ny);
            if (CancelF()) return false;

            // the cloth starts above the inverted cloud and falls (h decreases) until it touches it
            int N = nx * ny;
            double[] h = new double[N], hprev = new double[N];
            bool[] movable = new bool[N];
            double maxCollide = double.MinValue;
            foreach (double c in collide) maxCollide = Math.Max(maxCollide, c);
            double start = maxCollide + r;
            for (int k = 0; k < N; ++k) {
                h[k] = hprev[k] = start;
                movable[k] = true;
            }
            const double damping = 0.01;
            double gravityStep = 0.2 * TimeStep * TimeStep;
            double maxSpeed = 0.5 * r;

            for (Iterations = 0; Iterations < MaxIterations; ++Iterations) {
                if (CancelF()) return false;
                // gravity (Verlet)
                double maxMove = 0;
                for (int k = 0; k < N; ++k) {
                    if (movable[k] == false) continue;
                    double hk = h[k];
                    // terminal velocity: after a long fall (eg 40 m from the inverted pit floor down to the
                    // surroundings) the cloth would otherwise go through the springs onto objects
                    double v = Math.Max((hk - hprev[k]) * (1 - damping) - gravityStep, -maxSpeed);
                    h[k] = hk + v;
                    hprev[k] = hk;
                }
                // springs between 4-neighbours
                for (int pass = 0; pass < Math.Max(1, Rigidness); ++pass) {
                    for (int j = 0; j < ny; ++j)
                        for (int i = 0; i < nx; ++i) {
                            int k = i + nx * j;
                            if (i + 1 < nx) relax(h, movable, k, k + 1);
                            if (j + 1 < ny) relax(h, movable, k, k + nx);
                        }
                }
                // collisions
                for (int k = 0; k < N; ++k) {
                    if (movable[k] == false) continue;
                    maxMove = Math.Max(maxMove, Math.Abs(h[k] - hprev[k]));
                    if (h[k] <= collide[k]) {
                        h[k] = hprev[k] = collide[k];
                        movable[k] = false;
                    }
                }
                if (maxMove < 0.005 && Iterations > 10)
                    break;
            }

            // steep slopes: the stiff cloth stays above concave corners of the inverted terrain (crests of
            // faces). Particles next to settled ones are settled too if the terrain height under them differs
            // from the settled neighbour's by less than SlopeStep (continuous terrain, not an object edge),
            // spreading from particle to particle
            if (SlopeStep > 0) {
                Queue<int> queue = new Queue<int>();
                for (int k = 0; k < N; ++k)
                    if (movable[k] == false) queue.Enqueue(k);
                while (queue.Count > 0) {
                    int k = queue.Dequeue();
                    int i = k % nx, j = k / nx;
                    int[] nb = { (i > 0) ? k - 1 : -1, (i + 1 < nx) ? k + 1 : -1, (j > 0) ? k - nx : -1, (j + 1 < ny) ? k + nx : -1 };
                    foreach (int m in nb) {
                        if (m < 0 || movable[m] == false) continue;
                        if (Math.Abs(collide[m] - collide[k]) < SlopeStep) {
                            h[m] = collide[m];
                            movable[m] = false;
                            queue.Enqueue(m);
                        }
                    }
                }
            }

            // cloth in original orientation, and classification by bilinear cloth height
            ClothHeight = new double[nx, ny];
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i)
                    ClothHeight[i, j] = zref - h[i + nx * j];
            bool[] settled = new bool[N];
            for (int k = 0; k < N; ++k)
                settled[k] = !movable[k];
            IsGround = new bool[n];
            gParallel.ForEach(Interval1i.Range(n), (k) => {
                Vector3d p = Points[k];
                double fx = (p.x - ClothOrigin.x) / r, fy = (p.y - ClothOrigin.y) / r;
                int i0 = clampi((int)Math.Floor(fx), nx - 1), j0 = clampi((int)Math.Floor(fy), ny - 1);
                double tx = MathUtil.Clamp(fx - i0, 0, 1), ty = MathUtil.Clamp(fy - j0, 0, 1);
                double c = (1 - tx) * (1 - ty) * ClothHeight[i0, j0] + tx * (1 - ty) * ClothHeight[i0 + 1, j0]
                         + (1 - tx) * ty * ClothHeight[i0, j0 + 1] + tx * ty * ClothHeight[i0 + 1, j0 + 1];
                double dz = Math.Abs(p.z - c);
                // where the cloth lies on the terrain at all four nodes, the slope is the terrain's: measure
                // perpendicular to it (the vertical distance overstates it on steep faces, and bilinear
                // interpolation between nodes on a face is off by more than the threshold). Where the cloth
                // bridges objects it stays vertical.
                if (dz > ClassThreshold && settled[i0 + nx * j0] && settled[i0 + 1 + nx * j0] && settled[i0 + nx * (j0 + 1)] && settled[i0 + 1 + nx * (j0 + 1)]) {
                    double gx = ((1 - ty) * (ClothHeight[i0 + 1, j0] - ClothHeight[i0, j0]) + ty * (ClothHeight[i0 + 1, j0 + 1] - ClothHeight[i0, j0 + 1])) / r;
                    double gy = ((1 - tx) * (ClothHeight[i0, j0 + 1] - ClothHeight[i0, j0]) + tx * (ClothHeight[i0 + 1, j0 + 1] - ClothHeight[i0 + 1, j0])) / r;
                    dz /= Math.Sqrt(1 + gx * gx + gy * gy);
                }
                IsGround[k] = dz <= ClassThreshold;
            });
            return true;
        }

        // spring between particles a and b: both movable -> each moves half way, one fixed -> the other moves
        void relax(double[] h, bool[] movable, int a, int b)
        {
            double d = h[b] - h[a];
            if (movable[a] && movable[b]) {
                h[a] += SpringFactor * d;
                h[b] -= SpringFactor * d;
            } else if (movable[a]) {
                h[a] += 2 * SpringFactor * d;
            } else if (movable[b]) {
                h[b] -= 2 * SpringFactor * d;
            }
        }

        static int clampi(int v, int n) { return Math.Max(0, Math.Min(n - 1, v)); }

        // cells without points take the collision height of the nearest filled cell (breadth first)
        static void fill_empty(double[] c, int nx, int ny)
        {
            Queue<int> queue = new Queue<int>();
            for (int k = 0; k < c.Length; ++k)
                if (!double.IsNaN(c[k])) queue.Enqueue(k);
            while (queue.Count > 0) {
                int k = queue.Dequeue();
                int i = k % nx, j = k / nx;
                int[] nb = { (i > 0) ? k - 1 : -1, (i + 1 < nx) ? k + 1 : -1, (j > 0) ? k - nx : -1, (j + 1 < ny) ? k + nx : -1 };
                foreach (int m in nb) {
                    if (m >= 0 && double.IsNaN(c[m])) {
                        c[m] = c[k];
                        queue.Enqueue(m);
                    }
                }
            }
        }
    }
}
