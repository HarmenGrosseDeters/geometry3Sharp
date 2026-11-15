using System.Collections.Generic;
using g3;
using g3.Clipper2;
using Pkg = Clipper2Lib;
using V = g3.Clipper2.Vendored;

namespace geometry3Sharp.Clipper2.Tests
{
    /// <summary>
    /// The tests' bridge between the Clipper2 2.0.0 package (a reference of this test project only) and the addon: its
    /// internal vendored types (g3.Clipper2.Vendored) and its public enums. Until 2.0 the addon's public ClipperFrame
    /// methods took and returned the package's types; the extension methods here keep those names for the tests.
    /// </summary>
    static class PackageBridge
    {
        public static V.Point64 ToVendored(Pkg.Point64 q) => new V.Point64(q.X, q.Y);

        public static Pkg.Point64 ToPackage(V.Point64 q) => new Pkg.Point64(q.X, q.Y);

        public static V.Path64 ToVendored(Pkg.Path64 path)
        {
            var result = new V.Path64(path.Count);
            foreach (Pkg.Point64 q in path) result.Add(ToVendored(q));
            return result;
        }

        public static Pkg.Path64 ToPackage(V.Path64 path)
        {
            var result = new Pkg.Path64(path.Count);
            foreach (V.Point64 q in path) result.Add(ToPackage(q));
            return result;
        }

        public static V.Paths64 ToVendored(Pkg.Paths64 paths)
        {
            var result = new V.Paths64(paths.Count);
            foreach (Pkg.Path64 p in paths) result.Add(ToVendored(p));
            return result;
        }

        public static Pkg.Paths64 ToPackage(V.Paths64 paths)
        {
            var result = new Pkg.Paths64(paths.Count);
            foreach (V.Path64 p in paths) result.Add(ToPackage(p));
            return result;
        }

        /// <summary> the same tree (same nesting and paths, in order) </summary>
        public static V.PolyTree64 ToVendored(Pkg.PolyTree64 tree)
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

        public static V.ClipType ToVendored(Pkg.ClipType op) => (V.ClipType)(int)op;
        public static V.FillRule ToVendored(Pkg.FillRule rule) => (V.FillRule)(int)rule;
        public static V.JoinType ToVendored(Pkg.JoinType join) => (V.JoinType)(int)join;
        public static V.EndType ToVendored(Pkg.EndType end) => (V.EndType)(int)end;

        public static ClipType ToAddon(Pkg.ClipType op) => (ClipType)(int)op;
        public static FillRule ToAddon(Pkg.FillRule rule) => (FillRule)(int)rule;

        // the frame's conversions on the package's types (the addon's public methods until 2.0)
        public static Pkg.Point64 ToPoint64(this ClipperFrame f, Vector2d p) => ToPackage(f.ToPoint(p));
        public static Pkg.Point64 ToPoint64Displacement(this ClipperFrame f, Vector2d d) => ToPackage(f.ToDisplacement(d));
        public static Vector2d ToVector2d(this ClipperFrame f, Pkg.Point64 q) => f.ToVector2d(ToVendored(q));
        public static Pkg.Path64 ToPath64(this ClipperFrame f, Polygon2d p) => ToPackage(f.ToPath(p));
        public static Pkg.Path64 ToPath64(this ClipperFrame f, PolyLine2d p) => ToPackage(f.ToPath(p));
        public static Pkg.Paths64 ToPaths64(this ClipperFrame f, GeneralPolygon2d g) => ToPackage(f.ToPaths(g));
        public static Pkg.Paths64 ToPaths64(this ClipperFrame f, IEnumerable<GeneralPolygon2d> g) => ToPackage(f.ToPaths(g));
        public static Polygon2d ToPolygon2d(this ClipperFrame f, Pkg.Path64 p) => f.ToPolygon2d(ToVendored(p));
        public static PolyLine2d ToPolyLine2d(this ClipperFrame f, Pkg.Path64 p) => f.ToPolyLine2d(ToVendored(p));
        public static List<GeneralPolygon2d> ToGeneralPolygons(this ClipperFrame f, Pkg.PolyTree64 tree) => f.ToGeneralPolygons(ToVendored(tree));
    }
}
