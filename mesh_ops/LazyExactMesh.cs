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
    /// positive). Where the expression has the shape of a built-in operation, ToBoolean() and Round() use that operation
    /// (which keeps its cull of the parts outside the first operand):
    /// - one operation on two different sources: ExactMeshBoolean(a, b, op), the same result bit for bit;
    /// - a union or an intersection of sources only: the n-ary Union / Intersection over Sources;
    /// - a source minus sources (A − B, A − (B ∪ C), (A − B) − C, ...), where A is not among the subtracted: the n-ary
    ///   Difference over Sources;
    /// - a single source: the n-ary Union over that mesh (its resolved solid).
    /// Every other expression is the WindingPredicate of an n-ary ExactMeshBoolean over Sources.
    ///
    /// Cost: one arrangement of all sources. A chain that shrinks the solid early (an intersection first) can be cheaper
    /// as a chain, since the n-ary arrangement contains every source in full (the design note, sections 4 and 8). Long
    /// chains of near-coincident sources (copies one ulp apart) can leave the single final rounding defects that need edits
    /// beyond its drift bound, where the chain's own roundings had resolved them step by step (section 8): check
    /// RoundingClean / NotCleanReason, and round an intermediate result explicitly where needed (FromDMesh(x.Round())).
    /// The booleans use ExactMeshBoolean's defaults, so a source mesh with inconsistently oriented faces makes Round() return
    /// null (InconsistentOrientation, #1210; ToBoolean() for the status), also for a leaf; ExactMeshBoolean.Resolve orients it
    /// by area instead.
    ///
    /// Immutable: a value can be used in several expressions and from several threads at once. The source meshes are
    /// copied by FromDMesh, so later changes to the caller's mesh do not change the expression; the copies are what
    /// Sources and the inputs of ToBoolean() refer to, and must not be modified.
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
        // distinct sources in the order of their first appearance, left to right: computed on first use (a node does not
        // copy its children's lists, so a chain of n operations is O(n), not O(n^2)); a race computes the same list twice
        List<ExactMesh> leaves;

        ExactMesh(DMesh3 m)
        {
            op = Op.Leaf;
            mesh = m;
        }

        ExactMesh(Op o, ExactMesh a, ExactMesh b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            op = o; left = a; right = b;
        }

        /// <summary>
        /// a source: the solid of the mesh (a copy is kept, the mesh is not referenced). Every call makes a new source,
        /// also for the same mesh; use the same ExactMesh value to refer to one source more than once.
        /// </summary>
        public static ExactMesh FromDMesh(DMesh3 mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            // (not compacted: the same vertex and triangle ids, so the arrangement is that of the original mesh)
            return new ExactMesh(new DMesh3(mesh, false, false, false, false));
        }

        /// <summary> this ∪ b (nothing is computed) </summary>
        public ExactMesh Union(ExactMesh b) { return new ExactMesh(Op.Union, this, b); }
        /// <summary> this ∩ b (nothing is computed) </summary>
        public ExactMesh Intersection(ExactMesh b) { return new ExactMesh(Op.Intersection, this, b); }
        /// <summary> this − b (nothing is computed) </summary>
        public ExactMesh Difference(ExactMesh b) { return new ExactMesh(Op.Difference, this, b); }

        List<ExactMesh> Leaves()
        {
            List<ExactMesh> l = System.Threading.Volatile.Read(ref leaves);
            if (l != null)
                return l;
            l = new List<ExactMesh>();
            // (a value can be used twice in one expression, eg e.Union(e): every node is visited once, so the walk is
            // linear in the distinct nodes, not in the size of the expanded tree; a revisit adds no new source)
            var seen = new HashSet<ExactMesh>();
            var stack = new Stack<ExactMesh>();
            stack.Push(this);
            while (stack.Count > 0) {
                ExactMesh n = stack.Pop();
                if (seen.Add(n) == false)
                    continue;
                if (n.op == Op.Leaf) {
                    l.Add(n);
                    continue;
                }
                stack.Push(n.right);
                stack.Push(n.left);
            }
            System.Threading.Volatile.Write(ref leaves, l);
            return l;
        }

        /// <summary> the distinct source meshes of the expression, in the order of their first appearance (do not modify them) </summary>
        public IReadOnlyList<DMesh3> Sources
        {
            get {
                List<ExactMesh> ls = Leaves();
                var s = new List<DMesh3>(ls.Count);
                foreach (ExactMesh l in ls)
                    s.Add(l.mesh);
                return s;
            }
        }

        /// <summary> number of distinct sources </summary>
        public int SourceCount { get { return Leaves().Count; } }

        /// <summary> true if this is a source (FromDMesh), not an operation </summary>
        public bool IsSource { get { return op == Op.Leaf; } }

        /// <summary>
        /// the expression's indicator for winding numbers of the sources (entry i for Sources[i]): true inside. The
        /// predicate returned by WindingPredicate() computes the same; this compiles the expression on every call
        /// (linear in its size), the predicate once.
        /// </summary>
        public bool Inside(int[] windingNumbers)
        {
            if (windingNumbers == null) throw new ArgumentNullException(nameof(windingNumbers));
            int n = Leaves().Count;
            if (windingNumbers.Length != n)
                throw new ArgumentException($"ExactMesh.Inside: {windingNumbers.Length} winding numbers for {n} sources");
            return new Program(this).Eval(windingNumbers);
        }

        /// <summary>
        /// the expression as a winding number predicate over Sources (for ExactMeshBoolean.WindingPredicate). Each call
        /// returns a new delegate with its own buffer: one delegate must not be called from two threads at once
        /// (ExactMeshBoolean calls it from one thread).
        /// </summary>
        public Func<int[], bool> WindingPredicate()
        {
            var p = new Program(this);
            return p.Eval;
        }

        /// <summary>
        /// an ExactMeshBoolean that computes the expression, not yet computed: set its options (eg SafeRounding,
        /// ComputeExactVertices) and call Compute(). A built-in operation where the expression has its shape (see the
        /// class comment), otherwise the n-ary boolean over Sources with WindingPredicate(). Its input meshes are the
        /// sources' copies: do not modify them.
        /// </summary>
        public ExactMeshBoolean ToBoolean()
        {
            if (op == Op.Leaf)
                return new ExactMeshBoolean(new List<DMesh3> { mesh }, ExactMeshBoolean.Operations.Union);
            if (left.op == Op.Leaf && right.op == Op.Leaf && left != right)
                return new ExactMeshBoolean(left.mesh, right.mesh, builtin(op));
            if ((op == Op.Union || op == Op.Intersection) && only(this, op))
                return new ExactMeshBoolean(Sources, builtin(op));
            if (op == Op.Difference && differenceOfSources())
                return new ExactMeshBoolean(Sources, ExactMeshBoolean.Operations.Difference);
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

        // true if the expression has only `o` nodes over sources (each distinct node visited once)
        static bool only(ExactMesh e, Op o)
        {
            var seen = new HashSet<ExactMesh>();
            var stack = new Stack<ExactMesh>();
            stack.Push(e);
            while (stack.Count > 0) {
                ExactMesh n = stack.Pop();
                if (n.op == Op.Leaf || seen.Add(n) == false)
                    continue;
                if (n.op != o)
                    return false;
                stack.Push(n.left);
                stack.Push(n.right);
            }
            return true;
        }

        // A − R1 − R2 ... (a left spine of differences ending in a source A) with every Ri a union of sources and A in none:
        // inside A and outside all others, the built-in n-ary Difference over Sources (A is Sources[0])
        bool differenceOfSources()
        {
            ExactMesh n = this;
            var rights = new List<ExactMesh>();
            while (n.op == Op.Difference) {
                rights.Add(n.right);
                n = n.left;
            }
            if (n.op != Op.Leaf)
                return false;
            foreach (ExactMesh r in rights)
                if (only(r, Op.Union) == false || r.Leaves().Contains(n))
                    return false;
            return true;
        }

        // the expression as a program over its distinct nodes in post-order (a value used twice is computed once):
        // node i is a source (kind[i] = slot >= 0) or an operation (kind[i] < 0) on the nodes a[i], b[i] before it
        sealed class Program
        {
            const int Union = -1, Intersection = -2, Difference = -3;
            readonly int[] kind, a, b;
            readonly bool[] value;
            readonly int sources;

            public Program(ExactMesh e)
            {
                List<ExactMesh> leaves = e.Leaves();
                var slot = new Dictionary<ExactMesh, int>();
                for (int i = 0; i < leaves.Count; ++i)
                    slot[leaves[i]] = i;
                sources = leaves.Count;
                var index = new Dictionary<ExactMesh, int>();
                var k = new List<int>();
                var ka = new List<int>();
                var kb = new List<int>();
                // iterative post-order over the distinct nodes (a chain of thousands of operations is a deep tree)
                var todo = new Stack<KeyValuePair<ExactMesh, bool>>();
                todo.Push(new KeyValuePair<ExactMesh, bool>(e, false));
                while (todo.Count > 0) {
                    KeyValuePair<ExactMesh, bool> t = todo.Pop();
                    ExactMesh n = t.Key;
                    if (index.ContainsKey(n))
                        continue;
                    if (n.op == Op.Leaf) {
                        index[n] = k.Count;
                        k.Add(slot[n]); ka.Add(-1); kb.Add(-1);
                    } else if (t.Value) {
                        index[n] = k.Count;
                        k.Add(n.op == Op.Union ? Union : n.op == Op.Intersection ? Intersection : Difference);
                        ka.Add(index[n.left]); kb.Add(index[n.right]);
                    } else {
                        todo.Push(new KeyValuePair<ExactMesh, bool>(n, true));
                        todo.Push(new KeyValuePair<ExactMesh, bool>(n.right, false));
                        todo.Push(new KeyValuePair<ExactMesh, bool>(n.left, false));
                    }
                }
                kind = k.ToArray(); a = ka.ToArray(); b = kb.ToArray();
                value = new bool[kind.Length];
            }

            public bool Eval(int[] w)
            {
                if (w == null || w.Length != sources)
                    throw new ArgumentException($"ExactMesh: {(w == null ? 0 : w.Length)} winding numbers for {sources} sources");
                for (int i = 0; i < kind.Length; ++i) {
                    int c = kind[i];
                    if (c >= 0) {
                        value[i] = w[c] > 0;
                        continue;
                    }
                    bool x = value[a[i]], y = value[b[i]];
                    value[i] = c == Union ? (x || y) : c == Intersection ? (x && y) : (x && !y);
                }
                return value[kind.Length - 1];
            }
        }
    }
}
