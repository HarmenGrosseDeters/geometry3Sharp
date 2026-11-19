// SPIKE (runs/clipper-gc): switches for the GC / allocation levers, so a benchmark can A/B them in one process. Not for master.
using System;
using g3.Clipper2.Vendored;

namespace g3.Clipper2
{
    internal static class Clipper2Levers
    {
        /// <summary> Regions: one engine for all polygons of a call, cleared between them (the pools of the first stay) </summary>
        public static bool ReuseRegions;
        /// <summary> one engine per thread kept across calls (pools, lists and free Actives stay) </summary>
        public static bool ReuseEngine;
        /// <summary> the free Actives survive Execute (engine change: ClearSolutionOnly keeps _freeActives) </summary>
        public static bool KeepActives { get => ClipperBase.KeepFreeActives; set => ClipperBase.KeepFreeActives = value; }
        /// <summary> Polygon2d to Path64 by index (no boxed enumerator), Path64 to Polygon2d without the second copy </summary>
        public static bool LeanBridge;

        public static void SetAll(bool on) { ReuseRegions = ReuseEngine = KeepActives = LeanBridge = on; }
    }

    internal static class EnginePool
    {
        [ThreadStatic] static Clipper64 cached;

        public static Clipper64 Rent(bool preserveCollinear)
        {
            Clipper64 c = null;
            if (Clipper2Levers.ReuseEngine) { c = cached; cached = null; }
            if (c == null) c = new Clipper64();
            else { c.Clear(); c._using_polytree = false; c.ReverseSolution = false; }
            c.PreserveCollinear = preserveCollinear;
            return c;
        }

        public static void Return(Clipper64 c)
        {
            if (Clipper2Levers.ReuseEngine) cached = c;
        }
    }
}
