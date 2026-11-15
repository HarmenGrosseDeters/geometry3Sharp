// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using V = g3.Clipper2.Vendored;

namespace g3.Clipper2
{
    /// <summary>
    /// The public enums of this package mapped to those of the vendored Clipper2 2.0.0 sources (g3.Clipper2.Vendored,
    /// internal): the same members with the same values.
    /// </summary>
    internal static class Bridge
    {
        internal static V.ClipType ToVendored(ClipType op) => (V.ClipType)(int)op;

        internal static V.FillRule ToVendored(FillRule rule) => (V.FillRule)(int)rule;

        internal static V.JoinType ToVendored(JoinType join) => (V.JoinType)(int)join;

        internal static V.EndType ToVendored(EndType end) => (V.EndType)(int)end;
    }
}
