// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace g3
{
    /// <summary>
    /// A solid given as a boolean expression (union, difference, intersection) over double meshes, evaluated lazily:
    /// chained operations build the expression without computing anything, and Round() computes the whole expression
    /// as one exact n-ary ExactMeshBoolean over the original meshes, with one rounding at the end (Exact Boolean 4.0
    /// stage 1, docs/design/EXACT_BOOLEAN_40_DESIGN.md). The result is the solid an exact chain of booleans would give:
    /// the intermediate results are never rounded, so their faces stay exactly in the planes of the original triangles.
    /// A chain of ExactMeshBoolean calls instead rounds every intermediate result to doubles.
    ///
    /// Opt-in and additive: nothing else in the library uses it, and ExactMeshBoolean's behaviour is unchanged. The
    /// operations are regularized as in ExactMeshBoolean (a point is inside a source where its winding number is
    /// positive). Where the expression has the shape of a built-in operation (two sources, a union or an intersection of
    /// sources, or a source minus a union of other sources), Round() uses that operation, so the result is the same,
    /// bit for bit, as ExactMeshBoolean with that operation on the sources; otherwise the expression is the
    /// WindingPredicate of an n-ary ExactMeshBoolean.
    ///
    /// Cost: one arrangement of all sources. A chain that shrinks the solid early (an intersection first) can be cheaper
    /// as a chain, since the n-ary arrangement contains every source in full (the measurements in the design note).
    /// Long chains of near-coincident sources (copies one ulp apart) can leave more to the single final rounding than
    /// the chain's roundings had each (the design note, section 4.1): check Rounding / NotCleanReason, or round an
    /// intermediate result explicitly (FromDMesh(x.Round())).
    ///
    /// Immutable: a value can be used in several expressions and from several threads. The source meshes are copied
    /// by FromDMesh, so later changes to the caller's mesh do not change the expression.
    ///
    /// (The file is named LazyExactMesh.cs: the class does no arithmetic and is not part of the exact code, which
    /// .editorconfig names as mesh_ops/ExactMesh*.cs.)
    /// </summary>
    public sealed class ExactMesh
    {
        // a node: a source (op = Leaf, mesh set) or an operation on two nodes
        enum Op : byte { Leaf, Union, Intersection, Difference }
        readonly Op op;
        readonly DMesh3 mesh;
        readonly ExactMesh left, right;
        // distinct sources in the order of their first appearance, left to right
        readonly List<ExactMesh> leaves;

        ExactMesh(DMesh3 m)
        {
            op = Op.Leaf;
            mesh = m;
            leaves = new List<ExactMesh> { this };
        }

        ExactMesh(Op o, ExactMesh a, ExactMesh b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            op = o; left = a; right = b;
            leaves = new List<ExactMesh>(a.leaves);
            var seen = new HashSet<ExactMesh>(a.leaves);
            foreach (ExactMesh l in b.leaves)
                if (seen.Add(l))
                    leaves.Add(l);
        }

        /// <summary>
        /// a source: the solid of the mesh (a copy is kept, the mesh is not referenced). Every call makes a new source,
        /// also for the same mesh; use the same ExactMesh value to refer to one source more than once.
        /// </summary>
        public static ExactMesh FromDMesh(DMesh3 mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            return new ExactMesh(new DMesh3(mesh, false, false, false, false));
        }

        /// <summary> this ∪ b (nothing is computed) </summary>
        public ExactMesh Union(ExactMesh b) { return new ExactMesh(Op.Union, this, b); }
        /// <summary> this ∩ b (nothing is computed) </summary>
        public ExactMesh Intersection(ExactMesh b) { return new ExactMesh(Op.Intersection, this, b); }
        /// <summary> this − b (nothing is computed) </summary>
        public ExactMesh Difference(ExactMesh b) { return new ExactMesh(Op.Difference, this, b); }

        /// <summary> the distinct source meshes of the expression, in the order of their first appearance (do not modify them) </summary>
        public IReadOnlyList<DMesh3> Sources
        {
            get {
                var s = new List<DMesh3>(leaves.Count);
                foreach (ExactMesh l in leaves)
                    s.Add(l.mesh);
                return s;
            }
        }

        /// <summary> number of distinct sources </summary>
        public int SourceCount { get { return leaves.Count; } }

        /// <summary> true if this is a source (FromDMesh), not an operation </summary>
        public bool IsSource { get { return op == Op.Leaf; } }

        /// <summary>
        /// the expression's indicator for winding numbers of the sources (entry i for Sources[i]): true inside. The
        /// predicate returned by WindingPredicate() computes the same.
        /// </summary>
        public bool Inside(int[] windingNumbers)
        {
            if (windingNumbers == null) throw new ArgumentNullException(nameof(windingNumbers));
            if (windingNumbers.Length != leaves.Count)
                throw new ArgumentException($"ExactMesh.Inside: {windingNumbers.Length} winding numbers for {leaves.Count} sources");
            return new Program(this).Eval(windingNumbers);
        }

        /// <summary>
        /// the expression as a winding number predicate over Sources (for ExactMeshBoolean.WindingPredicate). Each call
        /// returns a new delegate with its own evaluation stack: one delegate must not be called from two threads at once.
        /// </summary>
        public Func<int[], bool> WindingPredicate()
        {
            var p = new Program(this);
            return p.Eval;
        }

        /// <summary>
        /// an ExactMeshBoolean that computes the expression, not yet computed: set its options (eg SafeRounding,
        /// ComputeExactVertices) and call Compute(). A built-in operation where the expression has its shape (see the
        /// class comment), otherwise the n-ary boolean over Sources with WindingPredicate().
        /// </summary>
        public ExactMeshBoolean ToBoolean()
        {
            if (op == Op.Leaf)
                return new ExactMeshBoolean(new List<DMesh3> { mesh }, ExactMeshBoolean.Operations.Union);
            if (leaves.Count == 2 && left.op == Op.Leaf && right.op == Op.Leaf)
                return new ExactMeshBoolean(left.mesh, right.mesh, builtin(op));
            List<DMesh3> flat;
            if ((op == Op.Union || op == Op.Intersection) && flatten(this, op, out flat))
                return new ExactMeshBoolean(flat, builtin(op));
            if (op == Op.Difference && left.op == Op.Leaf && flatten(right, Op.Union, out flat)) {
                // (A − (A ∪ B) as Difference over A, A, B: no point is inside the first mesh only, the empty result)
                flat.Insert(0, left.mesh);
                return new ExactMeshBoolean(flat, ExactMeshBoolean.Operations.Difference);
            }
            return new ExactMeshBoolean(Sources, WindingPredicate());
        }

        /// <summary>
        /// compute the expression (ToBoolean(), then configure if given, then Compute()); the result mesh, or null if
        /// Compute() fails (use ToBoolean() for the status, the error and the rounding's counts)
        /// </summary>
        public DMesh3 Round(Action<ExactMeshBoolean> configure = null)
        {
            ExactMeshBoolean b = ToBoolean();
            configure?.Invoke(b);
            return b.Compute() ? b.Result : null;
        }

        static ExactMeshBoolean.Operations builtin(Op o)
        {
            switch (o) {
                case Op.Union: return ExactMeshBoolean.Operations.Union;
                case Op.Intersection: return ExactMeshBoolean.Operations.Intersection;
                default: return ExactMeshBoolean.Operations.Difference;
            }
        }

        // the distinct sources of a tree of only `o` nodes over sources, in the order of Sources; false if another
        // operation occurs
        static bool flatten(ExactMesh e, Op o, out List<DMesh3> meshes)
        {
            meshes = null;
            var stack = new Stack<ExactMesh>();
            stack.Push(e);
            while (stack.Count > 0) {
                ExactMesh n = stack.Pop();
                if (n.op == Op.Leaf)
                    continue;
                if (n.op != o)
                    return false;
                stack.Push(n.left);
                stack.Push(n.right);
            }
            meshes = new List<DMesh3>(e.leaves.Count);
            foreach (ExactMesh l in e.leaves)
                meshes.Add(l.mesh);
            return true;
        }

        // the expression in postfix: code >= 0 pushes "winding[code] > 0", the negative codes combine the top two
        sealed class Program
        {
            const int Union = -1, Intersection = -2, Difference = -3;
            readonly int[] code;
            readonly bool[] stack;
            readonly int sources;

            public Program(ExactMesh e)
            {
                var slot = new Dictionary<ExactMesh, int>();
                for (int i = 0; i < e.leaves.Count; ++i)
                    slot[e.leaves[i]] = i;
                sources = e.leaves.Count;
                var output = new List<int>();
                // iterative post-order (a chain of thousands of operations is a deep tree)
                var todo = new Stack<KeyValuePair<ExactMesh, bool>>();
                todo.Push(new KeyValuePair<ExactMesh, bool>(e, false));
                int depth = 0, maxDepth = 0;
                while (todo.Count > 0) {
                    KeyValuePair<ExactMesh, bool> t = todo.Pop();
                    ExactMesh n = t.Key;
                    if (n.op == Op.Leaf) {
                        output.Add(slot[n]);
                        maxDepth = Math.Max(maxDepth, ++depth);
                    } else if (t.Value) {
                        output.Add(n.op == Op.Union ? Union : n.op == Op.Intersection ? Intersection : Difference);
                        depth--;
                    } else {
                        todo.Push(new KeyValuePair<ExactMesh, bool>(n, true));
                        todo.Push(new KeyValuePair<ExactMesh, bool>(n.right, false));
                        todo.Push(new KeyValuePair<ExactMesh, bool>(n.left, false));
                    }
                }
                code = output.ToArray();
                stack = new bool[Math.Max(1, maxDepth)];
            }

            public bool Eval(int[] w)
            {
                if (w == null || w.Length != sources)
                    throw new ArgumentException($"ExactMesh: {(w == null ? 0 : w.Length)} winding numbers for {sources} sources");
                int top = 0;
                foreach (int c in code) {
                    if (c >= 0) {
                        stack[top++] = w[c] > 0;
                        continue;
                    }
                    bool b = stack[--top], a = stack[top - 1];
                    stack[top - 1] = c == Union ? (a || b) : c == Intersection ? (a && b) : (a && !b);
                }
                return stack[0];
            }
        }
    }
}
