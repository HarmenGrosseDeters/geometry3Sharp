using System;
using System.Collections.Generic;
using g3;
using gs;

namespace ReferenceParts
{
    /// <summary> what one check measured at one step of a chain </summary>
    public sealed class CheckResult
    {
        public string Name;
        /// <summary> a functional check (what the part must still do); the others describe the mesh and do not decide the first failing step </summary>
        public bool Functional;
        public bool Pass;
        /// <summary> the measured value in the part's canonical units (NaN: could not be measured) </summary>
        public double Value;
        /// <summary> the requirement, as text (&gt;= 7.76) </summary>
        public string Requirement = "";
        public string Detail = "";
    }

    public delegate CheckResult CheckFunc(ReferencePart part, DMesh3 mesh, bool afterCut);

    public sealed class PartCheck
    {
        public string Name;
        public bool Functional;
        public CheckFunc Run;
    }

    /// <summary>
    /// A functional technical part, built analytically with known target values (docs/design is not needed: the construction
    /// states them), its deliberate defects, the cut-out of the chain and the checks that say what the part must still do.
    /// All lengths are in the canonical frame (before Frame); the meshes are in the frame.
    /// </summary>
    public sealed class ReferencePart
    {
        public string Name;
        public PartFrame Frame = PartFrame.Identity;
        public DefectSet Defects;
        /// <summary> the canonical size of the part (the housing's edge, the flange's width) </summary>
        public double Size;
        /// <summary> the soup with its defects, in the frame </summary>
        public DMesh3 Input;
        /// <summary> the closed cut-out body to subtract, in the frame </summary>
        public DMesh3 Cutter;
        /// <summary> the volume of the construction, before and after the cut-out (canonical units cubed) </summary>
        public double VolumeBefore, VolumeAfter;
        /// <summary> the area of the construction (canonical), for the volume tolerance of a deviation-bounded reduction </summary>
        public double Area;
        /// <summary> the deviation bound of the chain's reduction (canonical units) </summary>
        public double Deviation;
        public readonly List<PartCheck> Checks = new List<PartCheck>();
        /// <summary> numbers of the construction that the checks use, by name (canonical units) </summary>
        public readonly Dictionary<string, double> Targets = new Dictionary<string, double>();
        /// <summary> the segments of the pin checks: name, start, end (canonical), radius (canonical) </summary>
        public readonly List<Pin> Pins = new List<Pin>();
        /// <summary> the sampling lines of the rib thickness: (point on the mid plane minus 5 along the thickness direction, direction), canonical </summary>
        public readonly List<Ray3d> ThicknessLines = new List<Ray3d>();
        /// <summary> the screw: the box extents (canonical) </summary>
        public Vector3d ScrewExtent;

        public double Scale { get { return Frame.Scale; } }

        public sealed class Pin
        {
            public string Name;
            public Vector3d A, B;
            public double Radius;
        }
    }

    /// <summary> the three reference parts, canonical sizes in millimetres around (1000, 1000, 1000) so that float32 loses visibly </summary>
    public static class Parts
    {
        public static readonly Vector3d Offset = new Vector3d(1000, 1000, 1000);

        // ---------------- 1. flange with a cross bore ----------------

        /// <summary>
        /// A square block 80 x 80 x 20 with a bore of radius 8 along z (centre (-20, 0)) and a cross bore of radius 4 along y
        /// (through x = 22, z = 10), neither meeting the other (an analytic construction of their intersection is out of scope). Both bores
        /// are n-gons: the targets use the polygon's area and inradius. The cut-out is a pocket in the top face.
        /// </summary>
        public static ReferencePart Flange(int n, DefectSet defects, PartFrame frame)
        {
            const double W = 40, T = 20, R = 8, Rc = 4, xm = -20, xc = 22, zc = 10;
            Vector3d o = Offset;
            var soup = new Soup();
            Func<double, double, double, Vector3d> P = (x, y, z) => new Vector3d(o.x + x, o.y + y, o.z + z);
            Vector3d[] rect(double z) { return new[] { P(-W, -W, z), P(W, -W, z), P(W, W, z), P(-W, W, z) }; }
            Vector3d[] ringTop = Soup.Ring(P(xm, 0, T), Vector3d.AxisX, Vector3d.AxisY, R, n);
            Vector3d[] ringBottom = Soup.Ring(P(xm, 0, 0), Vector3d.AxisX, Vector3d.AxisY, R, n);
            soup.Plate("top", rect(T), new[] { ringTop }, Vector3d.AxisZ);
            soup.Plate("bottom", rect(0), new[] { ringBottom }, -Vector3d.AxisZ);
            soup.Wall("bore", ringTop, ringBottom, P(xm, 0, T), P(xm, 0, 0), false);
            Vector3d[] ringFront = Soup.Ring(P(xc, W, zc), Vector3d.AxisX, Vector3d.AxisZ, Rc, n);
            Vector3d[] ringBack = Soup.Ring(P(xc, -W, zc), Vector3d.AxisX, Vector3d.AxisZ, Rc, n);
            soup.Plate("front", new[] { P(-W, W, 0), P(W, W, 0), P(W, W, T), P(-W, W, T) }, new[] { ringFront }, Vector3d.AxisY);
            soup.Plate("back", new[] { P(-W, -W, 0), P(-W, -W, T), P(W, -W, T), P(W, -W, 0) }, new[] { ringBack }, -Vector3d.AxisY);
            soup.Wall("crossbore", ringFront, ringBack, P(xc, W, zc), P(xc, -W, zc), false);
            soup.Plate("left", new[] { P(-W, -W, 0), P(-W, W, 0), P(-W, W, T), P(-W, -W, T) }, null, -Vector3d.AxisX);
            soup.Plate("right", new[] { P(W, -W, 0), P(W, -W, T), P(W, W, T), P(W, W, 0) }, null, Vector3d.AxisX);

            double volume = 4 * W * W * T - Soup.PolygonArea(R, n) * T - Soup.PolygonArea(Rc, n) * 2 * W;
            var cutter = new Soup();
            cutter.Box("cut", P(-10, 10, 10), P(10, 35, 25));
            const double cutVolume = 20 * 25 * 10;

            var part = new ReferencePart() { Name = "flange", Frame = frame, Defects = defects, Size = 2 * W };
            part.Input = soup.ToMesh(defects, frame, part.Size);
            part.Cutter = cutter.ToMesh(DefectSet.None(), frame, part.Size);
            part.VolumeBefore = volume; part.VolumeAfter = volume - cutVolume;
            part.Area = soup.Area();
            part.Deviation = 0.02;
            part.Targets["boreRadius"] = R; part.Targets["crossRadius"] = Rc;
            part.Targets["boreInradius"] = Soup.PolygonInradius(R, n); part.Targets["crossInradius"] = Soup.PolygonInradius(Rc, n);
            part.Pins.Add(new ReferencePart.Pin() { Name = "pinMain", A = P(xm, 0, -5), B = P(xm, 0, T + 5), Radius = 0.97 * R });
            part.Pins.Add(new ReferencePart.Pin() { Name = "pinCross", A = P(xc, -W - 5, zc), B = P(xc, W + 5, zc), Radius = 0.97 * Rc });
            part.Checks.Add(Checks.Volume());
            part.Checks.Add(Checks.Pin("pinMain"));
            part.Checks.Add(Checks.Pin("pinCross"));
            part.Checks.Add(Checks.Clean());
            return part;
        }

        // ---------------- 2. housing with a thin rib ----------------

        /// <summary>
        /// A block 100 x 100 x 20 with a rib on its top face: 60 long, thickness t (default 0.4), 15 high, along x. The cut-out is a
        /// pocket in the block's top face away from the rib. The rib shares its footprint with the top face (one closed solid).
        /// </summary>
        public static ReferencePart HousingRib(DefectSet defects, PartFrame frame, double ribThickness = 0.4)
        {
            const double H = 50, T = 20, L = 30, RH = 15;
            double t = ribThickness;
            Vector3d o = Offset;
            Func<double, double, double, Vector3d> P = (x, y, z) => new Vector3d(o.x + x, o.y + y, o.z + z);
            var soup = new Soup();
            soup.Plate("bottom", new[] { P(-H, -H, 0), P(-H, H, 0), P(H, H, 0), P(H, -H, 0) }, null, -Vector3d.AxisZ);
            soup.Plate("front", new[] { P(-H, H, 0), P(H, H, 0), P(H, H, T), P(-H, H, T) }, null, Vector3d.AxisY);
            soup.Plate("back", new[] { P(-H, -H, 0), P(-H, -H, T), P(H, -H, T), P(H, -H, 0) }, null, -Vector3d.AxisY);
            soup.Plate("left", new[] { P(-H, -H, 0), P(-H, H, 0), P(-H, H, T), P(-H, -H, T) }, null, -Vector3d.AxisX);
            soup.Plate("right", new[] { P(H, -H, 0), P(H, -H, T), P(H, H, T), P(H, H, 0) }, null, Vector3d.AxisX);
            soup.Plate("top", new[] { P(-H, -H, T), P(H, -H, T), P(H, H, T), P(-H, H, T) },
                new[] { new[] { P(-L, -t / 2, T), P(L, -t / 2, T), P(L, t / 2, T), P(-L, t / 2, T) } }, Vector3d.AxisZ);
            soup.Plate("rib", new[] { P(-L, t / 2, T), P(L, t / 2, T), P(L, t / 2, T + RH), P(-L, t / 2, T + RH) }, null, Vector3d.AxisY);
            soup.Plate("rib", new[] { P(-L, -t / 2, T), P(-L, -t / 2, T + RH), P(L, -t / 2, T + RH), P(L, -t / 2, T) }, null, -Vector3d.AxisY);
            soup.Plate("rib", new[] { P(-L, -t / 2, T), P(-L, t / 2, T), P(-L, t / 2, T + RH), P(-L, -t / 2, T + RH) }, null, -Vector3d.AxisX);
            soup.Plate("rib", new[] { P(L, -t / 2, T), P(L, -t / 2, T + RH), P(L, t / 2, T + RH), P(L, t / 2, T) }, null, Vector3d.AxisX);
            soup.Plate("rib", new[] { P(-L, -t / 2, T + RH), P(L, -t / 2, T + RH), P(L, t / 2, T + RH), P(-L, t / 2, T + RH) }, null, Vector3d.AxisZ);

            double volume = 2 * H * 2 * H * T + 2 * L * t * RH;
            var cutter = new Soup();
            cutter.Box("cut", P(20, 10, 10), P(40, 40, 25));
            const double cutVolume = 20 * 30 * 10;

            var part = new ReferencePart() { Name = "housing-rib", Frame = frame, Defects = defects, Size = 2 * H };
            part.Input = soup.ToMesh(defects, frame, part.Size);
            part.Cutter = cutter.ToMesh(DefectSet.None(), frame, part.Size);
            part.VolumeBefore = volume; part.VolumeAfter = volume - cutVolume;
            part.Area = soup.Area();
            part.Deviation = 0.02;
            part.Targets["ribThickness"] = t;
            part.Targets["ribTopZ"] = o.z + T + RH;
            part.Targets["baseBottomZ"] = o.z;
            foreach (double x in new[] { -25.3, -12.1, 0.7, 14.9, 27.3 })
                foreach (double z in new[] { T + 3.7, T + 7.1, T + 11.9 })
                    part.ThicknessLines.Add(new Ray3d(P(x, -5, z), Vector3d.AxisY));
            part.Checks.Add(Checks.Volume());
            part.Checks.Add(Checks.RibConnected());
            part.Checks.Add(Checks.RibThickness());
            part.Checks.Add(Checks.Clean());
            return part;
        }

        // ---------------- 3. assembly with a separate small screw ----------------

        /// <summary>
        /// A housing block 100 x 100 x 20 and a screw of size s = ratio x 100 standing 0.1 s above its top face (a head of radius 0.3 s and
        /// height 0.2 s on a shaft of radius 0.15 s and length 0.8 s, both 16-gons): two separate bodies in one soup. The reduction's deviation
        /// is 1e-5 of the housing, and at most a tenth of the screw (so the screw is never within the deviation of the housing: its loss is a defect). The cut-out is a pocket in the housing.
        /// </summary>
        public static ReferencePart ScrewAssembly(double ratio, DefectSet defects, PartFrame frame)
        {
            const double H = 50, T = 20;
            const int n = 16;
            double s = ratio * 2 * H;
            Vector3d o = Offset;
            Func<double, double, double, Vector3d> P = (x, y, z) => new Vector3d(o.x + x, o.y + y, o.z + z);
            var soup = new Soup();
            soup.Box("housing", P(-H, -H, 0), P(H, H, T), "top");
            double rh = 0.3 * s, rs = 0.15 * s, hh = 0.2 * s, sl = 0.8 * s;
            double cx = -20, cy = -20, z0 = T + 0.1 * s;
            Vector3d[] headTop = Soup.Ring(P(cx, cy, z0 + s), Vector3d.AxisX, Vector3d.AxisY, rh, n);
            Vector3d[] headBottomOuter = Soup.Ring(P(cx, cy, z0 + sl), Vector3d.AxisX, Vector3d.AxisY, rh, n);
            Vector3d[] headBottomInner = Soup.Ring(P(cx, cy, z0 + sl), Vector3d.AxisX, Vector3d.AxisY, rs, n);
            Vector3d[] shaftBottom = Soup.Ring(P(cx, cy, z0), Vector3d.AxisX, Vector3d.AxisY, rs, n);
            soup.Plate("screw", headTop, null, Vector3d.AxisZ);
            soup.Wall("screw", headTop, headBottomOuter, P(cx, cy, z0 + s), P(cx, cy, z0 + sl), true);
            soup.Plate("screw", headBottomOuter, new[] { headBottomInner }, -Vector3d.AxisZ);
            soup.Wall("screw", headBottomInner, shaftBottom, P(cx, cy, z0 + sl), P(cx, cy, z0), true);
            soup.Plate("screw", shaftBottom, null, -Vector3d.AxisZ);

            double screwVolume = Soup.PolygonArea(rh, n) * hh + Soup.PolygonArea(rs, n) * sl;
            double volume = 2 * H * 2 * H * T + screwVolume;
            var cutter = new Soup();
            cutter.Box("cut", P(20, 10, 10), P(40, 40, 25));
            const double cutVolume = 20 * 30 * 10;

            var part = new ReferencePart() { Name = "screw-assembly", Frame = frame, Defects = defects, Size = 2 * H };
            part.Input = soup.ToMesh(defects, frame, part.Size);
            part.Cutter = cutter.ToMesh(DefectSet.None(), frame, part.Size);
            part.VolumeBefore = volume; part.VolumeAfter = volume - cutVolume;
            part.Area = soup.Area();
            part.Deviation = Math.Min(1e-5 * 2 * H, 0.1 * s);
            part.ScrewExtent = new Vector3d(2 * rh, 2 * rh, s);
            part.Targets["screwSize"] = s;
            part.Targets["screwVolume"] = screwVolume;
            part.Checks.Add(Checks.Volume());
            part.Checks.Add(Checks.ScrewSeparate());
            part.Checks.Add(Checks.ScrewBox());
            part.Checks.Add(Checks.Clean());
            return part;
        }
    }
}
