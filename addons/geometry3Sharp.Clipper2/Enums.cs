// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
namespace g3.Clipper2
{
    // The options of the operations. Since 2.0 these are types of this package, with the names, members and values of
    // Clipper2's own (Clipper2Lib); before, the API used Clipper2Lib's types (README, "Breaking change in 2.0").

    /// <summary> the boolean operation of Clipper2Polygons.Boolean, BooleanPaths and ClipPolyLines (NoClip is rejected) </summary>
    public enum ClipType
    {
        /// <summary> no operation (rejected as InvalidParameter) </summary>
        NoClip,
        /// <summary> the region inside both subject and clip </summary>
        Intersection,
        /// <summary> the region inside subject or clip </summary>
        Union,
        /// <summary> subject minus clip </summary>
        Difference,
        /// <summary> the region inside exactly one of subject and clip </summary>
        Xor
    }

    /// <summary> which winding numbers are inside, for each operand </summary>
    public enum FillRule
    {
        /// <summary> odd winding numbers </summary>
        EvenOdd,
        /// <summary> winding numbers other than 0 </summary>
        NonZero,
        /// <summary> winding numbers above 0 </summary>
        Positive,
        /// <summary> winding numbers below 0 </summary>
        Negative
    }

    /// <summary> the corners of an offset </summary>
    public enum JoinType
    {
        /// <summary> extended to a point, up to MiterLimit times the distance </summary>
        Miter,
        /// <summary> squared off at the offset distance </summary>
        Square,
        /// <summary> cut off by a straight line between the offset edges </summary>
        Bevel,
        /// <summary> rounded (ArcTolerance) </summary>
        Round
    }

    /// <summary> the ends of an offset polyline (Polygon: the paths are closed polygons) </summary>
    public enum EndType
    {
        /// <summary> closed polygons (Offset) </summary>
        Polygon,
        /// <summary> polylines taken as closed rings </summary>
        Joined,
        /// <summary> squared off at the end points </summary>
        Butt,
        /// <summary> squared off beyond the end points by the distance </summary>
        Square,
        /// <summary> rounded </summary>
        Round
    }
}
