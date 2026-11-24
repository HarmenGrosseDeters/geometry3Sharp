using System;
using System.Collections.Generic;
using g3;

namespace g3.Examples
{
    /// <summary>
    /// Union of many overlapping bodies: one ExactMeshBoolean call (one arrangement) or a balanced tree of pairwise unions,
    /// and the cheap overlap degree (from bounding boxes) that tells which one is faster for a given input.
    /// See docs/guides/booleans-and-imprint.md, "Union of many overlapping bodies: one call or a tree".
    /// </summary>
    public static class UnionTreeExample
    {
        /// <summary> A 2 x 2 x 2 block of unit balls whose centres are 1.3 apart: every ball overlaps all the others. </summary>
        public static List<DMesh3> MakeBalls()
        {
            var balls = new List<DMesh3>();
            for (int i = 0; i < 8; ++i) {
                DMesh3 ball = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 5 }.Generate().MakeDMesh();
                MeshTransforms.Translate(ball, new Vector3d(1.3 * (i & 1), 1.3 * ((i >> 1) & 1), 1.3 * ((i >> 2) & 1)));
                balls.Add(ball);
            }
            return balls;
        }

        /// <summary> All bodies in one call: one arrangement. Fastest for sparse or chain-like inputs. Null if Compute() fails. </summary>
        public static DMesh3 UnionOneCall(IList<DMesh3> bodies)
        {
            var op = new ExactMeshBoolean(new List<DMesh3>(bodies), ExactMeshBoolean.Operations.Union) { StrictRounding = true };
            return op.Compute() ? op.Result : null;
        }

        /// <summary>
        /// A balanced binary tree of pairwise unions: level by level, neighbours are united and an odd one is carried on.
        /// Every pairwise union drops the interior surface before the next one sees it. Null if a union fails.
        /// </summary>
        public static DMesh3 UnionTree(IList<DMesh3> bodies)
        {
            var level = new List<DMesh3>(bodies);
            while (level.Count > 1) {
                var next = new List<DMesh3>((level.Count + 1) / 2);
                for (int i = 0; i < level.Count; i += 2) {
                    if (i + 1 == level.Count) { next.Add(level[i]); continue; }
                    var op = new ExactMeshBoolean(level[i], level[i + 1], ExactMeshBoolean.Operations.Union) { StrictRounding = true };
                    if (!op.Compute() || op.Result == null)
                        return null;
                    next.Add(op.Result);
                }
                level = next;
            }
            return level.Count == 1 ? level[0] : null;
        }

        /// <summary>
        /// Mean number of other bodies whose bounding box intersects a body's box (an upper estimate of the true overlap
        /// degree, since boxes over-count; a quadratic loop is fine for a few thousand bodies).
        /// </summary>
        public static double MeanBoxOverlapDegree(IList<DMesh3> bodies)
        {
            var boxes = new AxisAlignedBox3d[bodies.Count];
            for (int i = 0; i < boxes.Length; ++i)
                boxes[i] = bodies[i].GetBounds();
            long pairs = 0;
            for (int i = 0; i < boxes.Length; ++i)
                for (int j = i + 1; j < boxes.Length; ++j)
                    if (boxes[i].Intersects(boxes[j]))
                        ++pairs;
            return bodies.Count == 0 ? 0 : 2.0 * pairs / bodies.Count;
        }
    }
}
