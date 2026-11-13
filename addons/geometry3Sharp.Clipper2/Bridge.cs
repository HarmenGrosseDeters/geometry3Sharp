// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using V = g3.Clipper2.Vendored;
using Pkg = Clipper2Lib;

namespace g3.Clipper2
{
    /// <summary>
    /// Conversions between the types of the Clipper2 package (Clipper2Lib, used by the public API of this package) and
    /// the vendored Clipper2 2.0.0 sources (g3.Clipper2.Vendored, internal, which do the work). Both come from the same
    /// sources, so points are copied field by field and the enums have the same values.
    /// </summary>
    internal static class Bridge
    {
        internal static V.Point64 ToVendored(Pkg.Point64 q) => new V.Point64(q.X, q.Y);

        internal static Pkg.Point64 ToPackage(V.Point64 q) => new Pkg.Point64(q.X, q.Y);

        internal static V.Path64 ToVendored(Pkg.Path64 path)
        {
            var result = new V.Path64(path.Count);
            foreach (Pkg.Point64 q in path)
                result.Add(new V.Point64(q.X, q.Y));
            return result;
        }

        internal static Pkg.Path64 ToPackage(V.Path64 path)
        {
            var result = new Pkg.Path64(path.Count);
            foreach (V.Point64 q in path)
                result.Add(new Pkg.Point64(q.X, q.Y));
            return result;
        }

        internal static Pkg.Paths64 ToPackage(V.Paths64 paths)
        {
            var result = new Pkg.Paths64(paths.Count);
            foreach (V.Path64 path in paths)
                result.Add(ToPackage(path));
            return result;
        }

        /// <summary> the same tree (same nesting and paths, in order) </summary>
        internal static V.PolyTree64 ToVendored(Pkg.PolyTree64 tree)
        {
            var result = new V.PolyTree64();
            void Copy(Pkg.PolyPath64 from, V.PolyPath64 to)
            {
                for (int i = 0; i < from.Count; ++i) {
                    Pkg.PolyPath64 child = from.Child(i);
                    Copy(child, (V.PolyPath64)to.AddChild(ToVendored(child.Polygon)));
                }
            }
            Copy(tree, result);
            return result;
        }

        internal static V.ClipType ToVendored(Pkg.ClipType op) => (V.ClipType)(int)op;

        internal static V.FillRule ToVendored(Pkg.FillRule rule) => (V.FillRule)(int)rule;

        internal static V.JoinType ToVendored(Pkg.JoinType join) => (V.JoinType)(int)join;

        internal static V.EndType ToVendored(Pkg.EndType end) => (V.EndType)(int)end;
    }
}
