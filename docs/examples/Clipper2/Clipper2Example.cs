using System.Collections.Generic;
using g3;
using g3.Clipper2;

namespace g3.Examples
{
    /// <summary>
    /// 2D polygon booleans and offsets with the optional package geometry3Sharp.Clipper2: merge building footprints,
    /// add a setback zone around them, and clip a break line to the free area of a parcel. Works at UTM coordinates.
    /// </summary>
    public static class Clipper2Example
    {
        public static Clipper2Result Run(GeneralPolygon2d parcel, List<GeneralPolygon2d> footprints, PolyLine2d breakLine,
                                         double setback, out Clipper2Result clippedLine)
        {
            // overlapping footprints become one polygon each (holes where they enclose a courtyard)
            Clipper2Result buildings = Clipper2Polygons.Union(footprints);

            // the setback zone: the footprints grown by setback, with mitered corners
            var miter = new Clipper2Options { JoinType = Clipper2Lib.JoinType.Miter };
            Clipper2Result zone = Clipper2Polygons.Offset(buildings.Polygons, setback, miter);

            // the free area: parcel minus the zone; Status tells whether it worked
            Clipper2Result free = Clipper2Polygons.Difference(new[] { parcel }, zone.Polygons);

            // the pieces of the break line that lie in the free area
            clippedLine = Clipper2Polygons.ClipPolyLines(Clipper2Lib.ClipType.Intersection, new[] { breakLine }, free.Polygons);
            return free;
        }
    }
}
