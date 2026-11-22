// Procedural implicit fields with enclosures of the value and of the (generalized) gradient over a box. Inside is f < 0.
// "smooth" is false if the box may hold a point where f is not C1 (two active pieces of a min / max, a square root at 0):
// the gradient enclosure is then the hull over all pieces that can be active (Clarke), decided from enclosures over the
// box, never from a point value (requirement (i) of CONTOURING_TOPOLOGY.md 7.8).
using System;
using g3;

struct Enc
{
    public Iv F, Gx, Gy, Gz;
    public bool Smooth;
    public static Enc Unknown(Iv f) { return new Enc { F = f, Gx = Iv.Whole, Gy = Iv.Whole, Gz = Iv.Whole, Smooth = false }; }
}

abstract class Field : ImplicitFunction3d
{
    public abstract Enc Eval(Iv x, Iv y, Iv z);
    public Enc At(Vector3d p) { return Eval(Iv.Of(p.x), Iv.Of(p.y), Iv.Of(p.z)); }
    /// <summary> the value the contouring sees: the midpoint of the enclosure at the point, so a certified sign is its sign </summary>
    public double Value(ref Vector3d p) { return At(p).F.Mid; }
}

sealed class Lambda : Field
{
    public Func<Iv, Iv, Iv, Enc> F;
    public override Enc Eval(Iv x, Iv y, Iv z) { return F(x, y, z); }
}

static class Fields
{
    static Iv C(double v) { return Iv.Of(v); }
    // a quotient that is known to lie in [-1, 1] (a component of a unit vector: |a| <= sqrt(a^2 + b^2)); cuts the overestimate of the division
    static Iv U(Iv q) { return new Iv(Math.Max(q.Lo, -1), Math.Min(q.Hi, 1)); }

    /// <summary> the distance to a sphere, |p - c| - r </summary>
    public static Field Sphere(Vector3d c, double r)
    {
        return new Lambda { F = (x, y, z) => {
            Iv dx = x - C(c.x), dy = y - C(c.y), dz = z - C(c.z);
            Iv rho = Iv.Sqrt(Iv.Sqr(dx) + Iv.Sqr(dy) + Iv.Sqr(dz));
            if (!(rho.Lo > 0)) return Enc.Unknown(rho - C(r));
            return new Enc { F = rho - C(r), Gx = U(dx / rho), Gy = U(dy / rho), Gz = U(dz / rho), Smooth = true };
        } };
    }

    /// <summary> the polynomial sphere x^2 + y^2 + z^2 - r^2 </summary>
    public static Field SpherePoly(double r)
    {
        return new Lambda { F = (x, y, z) => new Enc { F = Iv.Sqr(x) + Iv.Sqr(y) + Iv.Sqr(z) - C(r * r), Gx = 2 * x, Gy = 2 * y, Gz = 2 * z, Smooth = true } };
    }

    /// <summary> the distance to a torus around the z axis, ring radius R, tube radius t </summary>
    public static Field Torus(double R, double t)
    {
        return new Lambda { F = (x, y, z) => {
            Iv s = Iv.Sqrt(Iv.Sqr(x) + Iv.Sqr(y));
            Iv a = s - C(R);
            Iv rho = Iv.Sqrt(Iv.Sqr(a) + Iv.Sqr(z));
            if (!(s.Lo > 0) || !(rho.Lo > 0)) return Enc.Unknown(rho - C(t));
            Iv ar = U(a / rho);
            return new Enc { F = rho - C(t), Gx = ar * U(x / s), Gy = ar * U(y / s), Gz = U(z / rho), Smooth = true };
        } };
    }

    /// <summary> the distance to the z axis minus r (an infinite cylinder) </summary>
    public static Field Cylinder(double r)
    {
        return new Lambda { F = (x, y, z) => {
            Iv s = Iv.Sqrt(Iv.Sqr(x) + Iv.Sqr(y));
            if (!(s.Lo > 0)) return Enc.Unknown(s - C(r));
            return new Enc { F = s - C(r), Gx = U(x / s), Gy = U(y / s), Gz = C(0), Smooth = true };
        } };
    }

    /// <summary> the distance to the segment from (-a, 0, 0) to (a, 0, 0) minus r (a capsule; C1 off the segment) </summary>
    public static Field Capsule(double a, double r)
    {
        return new Lambda { F = (x, y, z) => {
            Iv ax = Iv.Abs(x) - C(a);
            Iv xp = new Iv(Math.Max(ax.Lo, 0), Math.Max(ax.Hi, 0));
            Iv rho = Iv.Sqrt(Iv.Sqr(xp) + Iv.Sqr(y) + Iv.Sqr(z));
            if (!(rho.Lo > 0)) return Enc.Unknown(rho - C(r));
            Iv sgn = x.Lo > 0 ? C(1) : x.Hi < 0 ? C(-1) : Iv.Unit;       // where x straddles 0 with a > 0, xp is 0 near x = 0
            return new Enc { F = rho - C(r), Gx = sgn * U(xp / rho), Gy = U(y / rho), Gz = U(z / rho), Smooth = true };
        } };
    }

    /// <summary> Cassini surface of revolution ((x-a)^2 + y^2 + z^2)((x+a)^2 + y^2 + z^2) - b^4: one sphere-like component for b > a,
    /// two for b < a, a singular point at the origin for b = a </summary>
    public static Field Cassini(double a, double b)
    {
        return new Lambda { F = (x, y, z) => {
            Iv q = Iv.Sqr(y) + Iv.Sqr(z);
            Iv xm = x - C(a), xp = x + C(a);
            Iv A = Iv.Sqr(xm) + q, B = Iv.Sqr(xp) + q;
            Iv b2 = Iv.Sqr(C(b));
            Iv s = A + B;
            return new Enc { F = A * B - Iv.Sqr(b2), Gx = 2 * (xm * B + xp * A), Gy = 2 * (y * s), Gz = 2 * (z * s), Smooth = true };
        } };
    }

    public enum Tpms { Gyroid, SchwarzP, Diamond }

    /// <summary> g(k p) - level + cap (|p|^2 / R^2)^3: a TPMS with a smooth cap that closes the surface inside the ball of radius R </summary>
    public static Field CappedTpms(Tpms type, double k, double level, double R, double cap)
    {
        return new Lambda { F = (x, y, z) => {
            Iv sx, cx, sy, cy, sz, cz;
            Iv.SinCos(k * x, out sx, out cx); Iv.SinCos(k * y, out sy, out cy); Iv.SinCos(k * z, out sz, out cz);
            Iv g, gx, gy, gz;
            switch (type) {
                case Tpms.Gyroid:
                    g = sx * cy + sy * cz + sz * cx;
                    gx = cx * cy - sz * sx; gy = cy * cz - sx * sy; gz = cz * cx - sy * sz; break;
                case Tpms.SchwarzP:
                    g = cx + cy + cz; gx = -sx; gy = -sy; gz = -sz; break;
                default:
                    g = sx * sy * sz + sx * cy * cz + cx * sy * cz + cx * cy * sz;
                    gx = cx * sy * sz + cx * cy * cz - sx * sy * cz - sx * cy * sz;
                    gy = sx * cy * sz - sx * sy * cz + cx * cy * cz - cx * sy * sz;
                    gz = sx * sy * cz - sx * cy * sz - cx * sy * sz + cx * cy * cz; break;
            }
            Iv u = (Iv.Sqr(x) + Iv.Sqr(y) + Iv.Sqr(z)) / Iv.Sqr(C(R));
            Iv u2 = Iv.Sqr(u);
            Iv d = C(6) * C(cap) * u2 / Iv.Sqr(C(R));           // cap * 3 u^2 * 2 p / R^2
            return new Enc { F = g - C(level) + C(cap) * (u2 * u), Gx = C(k) * gx + d * x, Gy = C(k) * gy + d * y, Gz = C(k) * gz + d * z, Smooth = true };
        } };
    }

    public static Field Neg(Field a)
    {
        return new Lambda { F = (x, y, z) => { Enc e = a.Eval(x, y, z); return new Enc { F = -e.F, Gx = -e.Gx, Gy = -e.Gy, Gz = -e.Gz, Smooth = e.Smooth }; } };
    }

    public static Field Min(Field a, Field b)
    {
        return new Lambda { F = (x, y, z) => {
            Enc p = a.Eval(x, y, z), q = b.Eval(x, y, z);
            if (p.F.Hi < q.F.Lo) return p;
            if (q.F.Hi < p.F.Lo) return q;
            return new Enc { F = Iv.Min(p.F, q.F), Gx = Iv.Hull(p.Gx, q.Gx), Gy = Iv.Hull(p.Gy, q.Gy), Gz = Iv.Hull(p.Gz, q.Gz), Smooth = false };
        } };
    }

    public static Field Max(Field a, Field b) { return Neg(Min(Neg(a), Neg(b))); }

    /// <summary> f(R (p - t)) for a rotation R: the box is enclosed in the rotated frame (this widens it), the gradient is R^T g </summary>
    public static Field Moved(Field a, Matrix3d R, Vector3d t)
    {
        return new Lambda { F = (x, y, z) => {
            Iv px = x - C(t.x), py = y - C(t.y), pz = z - C(t.z);
            Iv qx = R[0, 0] * px + R[0, 1] * py + R[0, 2] * pz;
            Iv qy = R[1, 0] * px + R[1, 1] * py + R[1, 2] * pz;
            Iv qz = R[2, 0] * px + R[2, 1] * py + R[2, 2] * pz;
            Enc e = a.Eval(qx, qy, qz);
            if (!e.Gx.IsFinite || !e.Gy.IsFinite || !e.Gz.IsFinite) return Enc.Unknown(e.F);
            return new Enc { F = e.F, Smooth = e.Smooth,
                Gx = R[0, 0] * e.Gx + R[1, 0] * e.Gy + R[2, 0] * e.Gz,
                Gy = R[0, 1] * e.Gx + R[1, 1] * e.Gy + R[2, 1] * e.Gz,
                Gz = R[0, 2] * e.Gx + R[1, 2] * e.Gy + R[2, 2] * e.Gz };
        } };
    }
}
