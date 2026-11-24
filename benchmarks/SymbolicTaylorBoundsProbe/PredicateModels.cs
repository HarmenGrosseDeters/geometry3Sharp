// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;

/// <summary>
/// Exact transcriptions of geometry3Sharp predicate and filter DAGs from
/// tools/PredicateCompiler/indirect.spec, math/ExactPredicates.cs, and comp_geom/SnapRounding2.cs.
/// </summary>
internal static class PredicateModels
{
    private static (int x, int y, int z) Pt3(ExprDag d, string p) =>
        (d.Input(p + ".x"), d.Input(p + ".y"), d.Input(p + ".z"));

    private static (int x, int y) Pt2(ExprDag d, string p) =>
        (d.Input(p + ".x"), d.Input(p + ".y"));

    // --- FPTaylorPilot subexpressions (tools/FPTaylorPilot/compare_filter_expressions.py) ---

    public static ExprDag BuildLpiPrx()
    {
        var g = new ExprDag("LPI prx");
        int px = g.Input("p.x"), rx = g.Input("r.x");
        g.FinalizeRoot(g.Sub(px, rx, "prx"));
        return g;
    }

    public static ExprDag BuildLpiNy()
    {
        var g = new ExprDag("LPI ny");
        int sx = g.Input("s.x"), sz = g.Input("s.z");
        int tx = g.Input("t.x"), tz = g.Input("t.z");
        int rx = g.Input("r.x"), rz = g.Input("r.z");
        int ux = g.Sub(sx, rx, "ux"), uz = g.Sub(sz, rz, "uz");
        int wx = g.Sub(tx, rx, "wx"), wz = g.Sub(tz, rz, "wz");
        g.FinalizeRoot(g.Sub(g.Mul(uz, wx), g.Mul(ux, wz), "ny"));
        return g;
    }

    public static ExprDag BuildLpiNz()
    {
        var g = new ExprDag("LPI nz");
        int sx = g.Input("s.x"), sy = g.Input("s.y");
        int tx = g.Input("t.x"), ty = g.Input("t.y");
        int rx = g.Input("r.x"), ry = g.Input("r.y");
        int ux = g.Sub(sx, rx, "ux"), uy = g.Sub(sy, ry, "uy");
        int wx = g.Sub(tx, rx, "wx"), wy = g.Sub(ty, ry, "wy");
        g.FinalizeRoot(g.Sub(g.Mul(ux, wy), g.Mul(uy, wx), "nz"));
        return g;
    }

    private static (int d, int lp, int lq, int nx, int ny, int nz) AddLpiCore(ExprDag g)
    {
        var p = Pt3(g, "p"); var q = Pt3(g, "q");
        var r = Pt3(g, "r"); var s = Pt3(g, "s"); var t = Pt3(g, "t");
        int ux = g.Sub(s.x, r.x), uy = g.Sub(s.y, r.y), uz = g.Sub(s.z, r.z);
        int wx = g.Sub(t.x, r.x), wy = g.Sub(t.y, r.y), wz = g.Sub(t.z, r.z);
        int nx = g.Sub(g.Mul(uy, wz), g.Mul(uz, wy), "nx");
        int ny = g.Sub(g.Mul(uz, wx), g.Mul(ux, wz), "ny");
        int nz = g.Sub(g.Mul(ux, wy), g.Mul(uy, wx), "nz");
        int prx = g.Sub(p.x, r.x), pry = g.Sub(p.y, r.y), prz = g.Sub(p.z, r.z);
        int qrx = g.Sub(q.x, r.x), qry = g.Sub(q.y, r.y), qrz = g.Sub(q.z, r.z);
        int lp = g.Add(g.Add(g.Mul(nx, prx), g.Mul(ny, pry)), g.Mul(nz, prz), "lp");
        int lq = g.Add(g.Add(g.Mul(nx, qrx), g.Mul(ny, qry)), g.Mul(nz, qrz), "lq");
        int d = g.Sub(lq, lp, "d");
        return (d, lp, lq, nx, ny, nz);
    }

    public static ExprDag BuildLpiD()
    {
        var g = new ExprDag("LPI d");
        var (d, _, _, _, _, _) = AddLpiCore(g);
        g.FinalizeRoot(d);
        return g;
    }

    public static ExprDag BuildLpiNx()
    {
        var g = new ExprDag("LPI Nx");
        var (_, lp, lq, _, _, _) = AddLpiCore(g);
        int px = g.Input("p.x"), qx = g.Input("q.x"), cx = g.Input("c.x");
        int pcx = g.Sub(px, cx), qcx = g.Sub(qx, cx);
        g.FinalizeRoot(g.Sub(g.Mul(pcx, lq), g.Mul(qcx, lp), "Nx"));
        return g;
    }

    public static ExprDag BuildOrient3dLpiDet()
    {
        var g = new ExprDag("Orient3dLPI det");
        var (_, lp, lq, _, _, _) = AddLpiCore(g);
        var p = Pt3(g, "p"); var q = Pt3(g, "q");
        var a = Pt3(g, "a"); var b = Pt3(g, "b"); var c = Pt3(g, "c");
        int pcx = g.Sub(p.x, c.x), pcy = g.Sub(p.y, c.y), pcz = g.Sub(p.z, c.z);
        int qcx = g.Sub(q.x, c.x), qcy = g.Sub(q.y, c.y), qcz = g.Sub(q.z, c.z);
        int Nx = g.Sub(g.Mul(pcx, lq), g.Mul(qcx, lp), "Nx");
        int Ny = g.Sub(g.Mul(pcy, lq), g.Mul(qcy, lp), "Ny");
        int Nz = g.Sub(g.Mul(pcz, lq), g.Mul(qcz, lp), "Nz");
        int acx = g.Sub(a.x, c.x), acy = g.Sub(a.y, c.y), acz = g.Sub(a.z, c.z);
        int bcx = g.Sub(b.x, c.x), bcy = g.Sub(b.y, c.y), bcz = g.Sub(b.z, c.z);
        int m1 = g.Sub(g.Mul(acy, bcz), g.Mul(acz, bcy), "m1");
        int m2 = g.Sub(g.Mul(acx, bcz), g.Mul(acz, bcx), "m2");
        int m3 = g.Sub(g.Mul(acx, bcy), g.Mul(acy, bcx), "m3");
        int t1 = g.Mul(Nx, m1), t2 = g.Mul(Ny, m2), t3 = g.Mul(Nz, m3);
        g.FinalizeRoot(g.Add(g.Sub(t1, t2), t3, "det"));
        return g;
    }

    public static ExprDag BuildTpiU1x()
    {
        var g = new ExprDag("TPI u1_x");
        g.FinalizeRoot(g.Sub(g.Input("b1.x"), g.Input("a1.x"), "u1_x"));
        return g;
    }

    public static ExprDag BuildTpiN1x()
    {
        var g = new ExprDag("TPI n1x");
        int a1y = g.Input("a1.y"), a1z = g.Input("a1.z");
        int b1y = g.Input("b1.y"), b1z = g.Input("b1.z");
        int c1y = g.Input("c1.y"), c1z = g.Input("c1.z");
        int u1y = g.Sub(b1y, a1y), u1z = g.Sub(b1z, a1z);
        int w1y = g.Sub(c1y, a1y), w1z = g.Sub(c1z, a1z);
        g.FinalizeRoot(g.Sub(g.Mul(u1y, w1z), g.Mul(u1z, w1y), "n1x"));
        return g;
    }

    private static (int nx, int ny, int nz, int D) AddTpiPlane(ExprDag g, string idx, (int x, int y, int z) c)
    {
        var a = Pt3(g, "a" + idx); var b = Pt3(g, "b" + idx); var q = Pt3(g, "c" + idx);
        int ux = g.Sub(b.x, a.x), uy = g.Sub(b.y, a.y), uz = g.Sub(b.z, a.z);
        int wx = g.Sub(q.x, a.x), wy = g.Sub(q.y, a.y), wz = g.Sub(q.z, a.z);
        int nx = g.Sub(g.Mul(uy, wz), g.Mul(uz, wy));
        int ny = g.Sub(g.Mul(uz, wx), g.Mul(ux, wz));
        int nz = g.Sub(g.Mul(ux, wy), g.Mul(uy, wx));
        int acx = g.Sub(a.x, c.x), acy = g.Sub(a.y, c.y), acz = g.Sub(a.z, c.z);
        int D = g.Add(g.Add(g.Mul(nx, acx), g.Mul(ny, acy)), g.Mul(nz, acz), "D" + idx);
        return (nx, ny, nz, D);
    }

    public static ExprDag BuildTpiD1()
    {
        var g = new ExprDag("TPI D1");
        var c = Pt3(g, "c");
        var (_, _, _, D1) = AddTpiPlane(g, "1", c);
        g.FinalizeRoot(D1);
        return g;
    }

    public static ExprDag BuildOrient3dTpiD()
    {
        var g = new ExprDag("Orient3dTPI d");
        var c = Pt3(g, "c");
        var p1 = AddTpiPlane(g, "1", c);
        var p2 = AddTpiPlane(g, "2", c);
        var p3 = AddTpiPlane(g, "3", c);
        int e23x = g.Sub(g.Mul(p2.ny, p3.nz), g.Mul(p2.nz, p3.ny));
        int e23y = g.Sub(g.Mul(p2.nz, p3.nx), g.Mul(p2.nx, p3.nz));
        int e23z = g.Sub(g.Mul(p2.nx, p3.ny), g.Mul(p2.ny, p3.nx));
        int d = g.Add(g.Add(g.Mul(p1.nx, e23x), g.Mul(p1.ny, e23y)), g.Mul(p1.nz, e23z), "d");
        g.FinalizeRoot(d);
        return g;
    }

    public static ExprDag BuildOrient3dTpiDet()
    {
        var g = new ExprDag("Orient3dTPI det");
        var c = Pt3(g, "c");
        var p1 = AddTpiPlane(g, "1", c);
        var p2 = AddTpiPlane(g, "2", c);
        var p3 = AddTpiPlane(g, "3", c);
        int e23x = g.Sub(g.Mul(p2.ny, p3.nz), g.Mul(p2.nz, p3.ny));
        int e23y = g.Sub(g.Mul(p2.nz, p3.nx), g.Mul(p2.nx, p3.nz));
        int e23z = g.Sub(g.Mul(p2.nx, p3.ny), g.Mul(p2.ny, p3.nx));
        int e31x = g.Sub(g.Mul(p3.ny, p1.nz), g.Mul(p3.nz, p1.ny));
        int e31y = g.Sub(g.Mul(p3.nz, p1.nx), g.Mul(p3.nx, p1.nz));
        int e31z = g.Sub(g.Mul(p3.nx, p1.ny), g.Mul(p3.ny, p1.nx));
        int e12x = g.Sub(g.Mul(p1.ny, p2.nz), g.Mul(p1.nz, p2.ny));
        int e12y = g.Sub(g.Mul(p1.nz, p2.nx), g.Mul(p1.nx, p2.nz));
        int e12z = g.Sub(g.Mul(p1.nx, p2.ny), g.Mul(p1.ny, p2.nx));
        int Nx = g.Add(g.Add(g.Mul(p1.D, e23x), g.Mul(p2.D, e31x)), g.Mul(p3.D, e12x), "Nx");
        int Ny = g.Add(g.Add(g.Mul(p1.D, e23y), g.Mul(p2.D, e31y)), g.Mul(p3.D, e12y), "Ny");
        int Nz = g.Add(g.Add(g.Mul(p1.D, e23z), g.Mul(p2.D, e31z)), g.Mul(p3.D, e12z), "Nz");
        var a = Pt3(g, "a"); var b = Pt3(g, "b");
        int acx = g.Sub(a.x, c.x), acy = g.Sub(a.y, c.y), acz = g.Sub(a.z, c.z);
        int bcx = g.Sub(b.x, c.x), bcy = g.Sub(b.y, c.y), bcz = g.Sub(b.z, c.z);
        int m1 = g.Sub(g.Mul(acy, bcz), g.Mul(acz, bcy));
        int m2 = g.Sub(g.Mul(acx, bcz), g.Mul(acz, bcx));
        int m3 = g.Sub(g.Mul(acx, bcy), g.Mul(acy, bcx));
        int t1 = g.Mul(Nx, m1), t2 = g.Mul(Ny, m2), t3 = g.Mul(Nz, m3);
        g.FinalizeRoot(g.Add(g.Sub(t1, t2), t3, "det"));
        return g;
    }

    // --- Standard ExactPredicates ---

    public static ExprDag BuildOrient2d()
    {
        var g = new ExprDag("Orient2d");
        var a = Pt2(g, "a"); var b = Pt2(g, "b"); var c = Pt2(g, "c");
        int acx = g.Sub(a.x, c.x), acy = g.Sub(a.y, c.y);
        int bcx = g.Sub(b.x, c.x), bcy = g.Sub(b.y, c.y);
        g.FinalizeRoot(g.Sub(g.Mul(acx, bcy), g.Mul(acy, bcx), "det"));
        return g;
    }

    public static ExprDag BuildOrient3d()
    {
        var g = new ExprDag("Orient3d");
        var a = Pt3(g, "a"); var b = Pt3(g, "b"); var c = Pt3(g, "c"); var d = Pt3(g, "d");
        int adx = g.Sub(a.x, d.x), ady = g.Sub(a.y, d.y), adz = g.Sub(a.z, d.z);
        int bdx = g.Sub(b.x, d.x), bdy = g.Sub(b.y, d.y), bdz = g.Sub(b.z, d.z);
        int cdx = g.Sub(c.x, d.x), cdy = g.Sub(c.y, d.y), cdz = g.Sub(c.z, d.z);
        int bdxcdy = g.Mul(bdx, cdy), cdxbdy = g.Mul(cdx, bdy);
        int cdxady = g.Mul(cdx, ady), adxcdy = g.Mul(adx, cdy);
        int adxbdy = g.Mul(adx, bdy), bdxady = g.Mul(bdx, ady);
        int t1 = g.Mul(adz, g.Sub(bdxcdy, cdxbdy));
        int t2 = g.Mul(bdz, g.Sub(cdxady, adxcdy));
        int t3 = g.Mul(cdz, g.Sub(adxbdy, bdxady));
        g.FinalizeRoot(g.Add(g.Add(t1, t2), t3, "det"));
        return g;
    }

    public static ExprDag BuildInCircle()
    {
        var g = new ExprDag("InCircle");
        var a = Pt2(g, "a"); var b = Pt2(g, "b"); var c = Pt2(g, "c"); var d = Pt2(g, "d");
        int adx = g.Sub(a.x, d.x), ady = g.Sub(a.y, d.y);
        int bdx = g.Sub(b.x, d.x), bdy = g.Sub(b.y, d.y);
        int cdx = g.Sub(c.x, d.x), cdy = g.Sub(c.y, d.y);
        int alift = g.Add(g.Mul(adx, adx), g.Mul(ady, ady));
        int blift = g.Add(g.Mul(bdx, bdx), g.Mul(bdy, bdy));
        int clift = g.Add(g.Mul(cdx, cdx), g.Mul(cdy, cdy));
        int t1 = g.Mul(alift, g.Sub(g.Mul(bdx, cdy), g.Mul(bdy, cdx)));
        int t2 = g.Mul(blift, g.Sub(g.Mul(cdx, ady), g.Mul(cdy, adx)));
        int t3 = g.Mul(clift, g.Sub(g.Mul(adx, bdy), g.Mul(ady, bdx)));
        g.FinalizeRoot(g.Add(g.Add(t1, t2), t3, "det"));
        return g;
    }

    public static ExprDag BuildInSphere()
    {
        var g = new ExprDag("InSphere");
        var a = Pt3(g, "a"); var b = Pt3(g, "b"); var c = Pt3(g, "c"); var d = Pt3(g, "d"); var e = Pt3(g, "e");
        int aex = g.Sub(a.x, e.x), aey = g.Sub(a.y, e.y), aez = g.Sub(a.z, e.z);
        int bex = g.Sub(b.x, e.x), bey = g.Sub(b.y, e.y), bez = g.Sub(b.z, e.z);
        int cex = g.Sub(c.x, e.x), cey = g.Sub(c.y, e.y), cez = g.Sub(c.z, e.z);
        int dex = g.Sub(d.x, e.x), dey = g.Sub(d.y, e.y), dez = g.Sub(d.z, e.z);

        int ab = g.Sub(g.Mul(aex, bey), g.Mul(bex, aey));
        int bc = g.Sub(g.Mul(bex, cey), g.Mul(cex, bey));
        int cd = g.Sub(g.Mul(cex, dey), g.Mul(dex, cey));
        int da = g.Sub(g.Mul(dex, aey), g.Mul(aex, dey));
        int ac = g.Sub(g.Mul(aex, cey), g.Mul(cex, aey));
        int bd = g.Sub(g.Mul(bex, dey), g.Mul(dex, bey));

        int abc = g.Add(g.Sub(g.Mul(aez, bc), g.Mul(bez, ac)), g.Mul(cez, ab));
        int bcd = g.Add(g.Sub(g.Mul(bez, cd), g.Mul(cez, bd)), g.Mul(dez, bc));
        int cda = g.Add(g.Add(g.Mul(cez, da), g.Mul(dez, ac)), g.Mul(aez, cd));
        int dab = g.Add(g.Add(g.Mul(dez, ab), g.Mul(aez, bd)), g.Mul(bez, da));

        int alift = g.Add(g.Add(g.Mul(aex, aex), g.Mul(aey, aey)), g.Mul(aez, aez));
        int blift = g.Add(g.Add(g.Mul(bex, bex), g.Mul(bey, bey)), g.Mul(bez, bez));
        int clift = g.Add(g.Add(g.Mul(cex, cex), g.Mul(cey, cey)), g.Mul(cez, cez));
        int dlift = g.Add(g.Add(g.Mul(dex, dex), g.Mul(dey, dey)), g.Mul(dez, dez));

        int det = g.Add(
            g.Sub(g.Mul(dlift, abc), g.Mul(clift, dab)),
            g.Sub(g.Mul(blift, cda), g.Mul(alift, bcd)),
            "det");
        g.FinalizeRoot(det);
        return g;
    }

    // --- Hand-written filter SN-5: SnapGrid.CrossesInteriorFilter (comp_geom/SnapRounding2.cs:707-718) ---
    public static ExprDag BuildSnapCrossesInterior()
    {
        var g = new ExprDag("SN-5 CrossesInterior");
        int ax = g.Input("a.x"), ay = g.Input("a.y");
        int bx = g.Input("b.x"), by = g.Input("b.y");
        int kxh = g.Input("kx_half"), kyh = g.Input("ky_half"), w = g.Input("W");
        int cx = g.Mul(kxh, w, "cx");
        int cy = g.Mul(kyh, w, "cy");
        int dx = g.Sub(bx, ax, "dx"), dy = g.Sub(by, ay, "dy");
        int ex = g.Sub(cx, ax, "ex"), ey = g.Sub(cy, ay, "ey");
        g.FinalizeRoot(g.Sub(g.Mul(dx, ey), g.Mul(dy, ex), "o"));
        return g;
    }
}
