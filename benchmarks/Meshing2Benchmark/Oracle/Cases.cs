using System;
using System.Collections.Generic;
using System.Linq;

namespace Meshing2Acceptance
{
    /// <summary>The features a row can wait for. A candidate registers those it implements (Candidates/CandidateRegistry.cs).</summary>
    public static class Feature
    {
        public const string Kernel = "m1.kernel", Api = "m1.api";
        public const string Cdt = "m2.cdt", Noding = "m2.noding", Snap = "m2.snap", Domain = "m2.domain", Regions = "m2.regions", RingInterior = "m2.ring-interior";
        public const string Refine = "m3.refine", Locks = "m3.locks", RefineAgain = "m3.refine-again";
        public const string Grid = "m4.grid", Wedge = "m4.wedge", Steiner = "m4.steiner-postprocess";
        public static readonly string[] All = { Kernel, Api, Cdt, Noding, Snap, Domain, Regions, RingInterior, Refine, Locks, RefineAgain, Grid, Wedge, Steiner };
    }

    /// <summary>SplitMix64 (Steele, Lea, Flood 2014): the one random source of all cases, the same in every runtime and language.</summary>
    public sealed class SplitMix64
    {
        ulong state;
        public SplitMix64(ulong seed) { state = seed; }
        public ulong Next()
        {
            ulong z = unchecked(state += 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }
        /// <summary>The top bits of the next value, as an integer in [0, 2^bits).</summary>
        public long Bits(int bits) => (long)(Next() >> (64 - bits));
    }

    /// <summary>A named input. <see cref="Generator"/> is the description of docs/design/MESHING2D_GOLDEN_FORMAT.md, section 2.</summary>
    public sealed class Mesh2Case
    {
        public string Id, Generator;
        public Milestone Milestone;
        public Mesh2Input Input;
        public OracleExpect Expect;
        /// <summary>The input is tied by construction (cocircular): only a valid result is required of a differential.</summary>
        public bool Ties;
        public override string ToString() => Id;
    }

    /// <summary>The procedural generators. No trigonometry and no decimal fractions: all inputs are integer or dyadic.</summary>
    public static class Generators
    {
        public static Mesh2Input Literal(params double[] xy)
        {
            var input = new Mesh2Input { X = new double[xy.Length / 2], Y = new double[xy.Length / 2] };
            for (int i = 0; i < input.Count; ++i) { input.X[i] = xy[2 * i]; input.Y[i] = xy[2 * i + 1]; }
            return input;
        }

        static Mesh2Input From(IEnumerable<(double x, double y)> points)
        {
            var list = points.ToList();
            return new Mesh2Input { X = list.Select(p => p.x).ToArray(), Y = list.Select(p => p.y).ToArray() };
        }

        public static Mesh2Input UniformInt(int n, int bits, ulong seed)
        {
            var random = new SplitMix64(seed);
            var points = new List<(double, double)>(n);
            for (int i = 0; i < n; ++i) { long x = random.Bits(bits); long y = random.Bits(bits); points.Add((x, y)); }
            return From(points);
        }

        /// <summary>x -> x * 2^exponent + dx, the same for y; exact as long as the case is built for it.</summary>
        public static Mesh2Input Transform(Mesh2Input input, int exponent, double dx = 0, double dy = 0)
        {
            double scale = Math.Pow(2, exponent);
            return new Mesh2Input { X = input.X.Select(x => x * scale + dx).ToArray(), Y = input.Y.Select(y => y * scale + dy).ToArray() };
        }

        public static Mesh2Input Grid(int nx, int ny)
        {
            var points = new List<(double, double)>();
            for (int x = 0; x < nx; ++x) for (int y = 0; y < ny; ++y) points.Add((x, y));
            return From(points);
        }

        /// <summary>All integer points on the circle of radius r around the origin, by increasing x, then y; optionally the centre last.</summary>
        public static Mesh2Input LatticeCircle(int r, bool centre)
        {
            var points = new List<(double, double)>();
            long r2 = (long)r * r;
            for (long x = -r; x <= r; ++x) {
                long rest = r2 - x * x;
                long y = (long)Math.Round(Math.Sqrt(rest)); // exact: rest is below 2^53, and the square is verified
                if (y * y != rest) continue;
                if (y != 0) points.Add((x, -y));
                points.Add((x, y));
            }
            if (centre) points.Add((0, 0));
            return From(points);
        }

        /// <summary>Every odd point moved one unit in the last place, in x (or in y if x is 0).</summary>
        public static Mesh2Input NudgeOdd(Mesh2Input input)
        {
            var x = (double[])input.X.Clone(); var y = (double[])input.Y.Clone();
            for (int i = 1; i < x.Length; i += 2) {
                if (x[i] != 0) x[i] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x[i]) + 1);
                else y[i] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(y[i]) + 1);
            }
            return new Mesh2Input { X = x, Y = y };
        }

        public static Mesh2Input NearCollinear(int n)
        {
            double slope = Math.Pow(2, -20), offset = Math.Pow(2, -40);
            return From(Enumerable.Range(0, n).Select(i => ((double)i, i * slope + (i % 2 == 0 ? offset : -offset))));
        }

        public static Mesh2Input Parabola(int n) => From(Enumerable.Range(0, n).Select(i => ((double)i, (double)i * i)));

        /// <summary>The integer points of the border of [0,k]^2 in counter-clockwise order from (0,0), and (k/2, k/2) last.</summary>
        public static Mesh2Input SquareBorder(int k)
        {
            var points = new List<(double, double)>();
            for (int i = 0; i < k; ++i) points.Add((i, 0));
            for (int i = 0; i < k; ++i) points.Add((k, i));
            for (int i = 0; i < k; ++i) points.Add((k - i, k));
            for (int i = 0; i < k; ++i) points.Add((0, k - i));
            points.Add((k / 2.0, k / 2.0));
            return From(points);
        }

        /// <summary>Odd points scaled by 2^tiny, even points by 2^large (both integer in [0, 2^bits) before scaling).</summary>
        public static Mesh2Input Mixed(int n, int bits, int tiny, int large, ulong seed)
        {
            var random = new SplitMix64(seed);
            var points = new List<(double, double)>(n);
            for (int i = 0; i < n; ++i) {
                double s = Math.Pow(2, i % 2 == 0 ? large : tiny);
                long x = random.Bits(bits), y = random.Bits(bits);
                points.Add((x * s, y * s));
            }
            return From(points);
        }
    }

    /// <summary>The cases of M1: point sets, run by every gate. The first ones that fit in a second are the CI subset.</summary>
    public static class M1Cases
    {
        static double NegZero => BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000UL));

        static Mesh2Case Case(string id, string generator, Mesh2Input input, bool ties = false) =>
            new Mesh2Case { Id = "m1/" + id, Generator = generator, Milestone = Milestone.M1, Input = input, Ties = ties, Expect = new OracleExpect { Milestone = Milestone.M1 } };

        /// <summary>The fixed subset of the golden master: small, covering each kind of input.</summary>
        public static IEnumerable<Mesh2Case> Ci()
        {
            yield return Case("empty", "literal()", Generators.Literal());
            yield return Case("single", "literal(1 1)", Generators.Literal(1, 1));
            yield return Case("two", "literal(1 1, 2 2)", Generators.Literal(1, 1, 2, 2));
            yield return Case("identical-5", "literal(1 1 x5)", Generators.Literal(1, 1, 1, 1, 1, 1, 1, 1, 1, 1));
            yield return Case("collinear-8", "literal(i i, i=0..7)", Generators.Literal(0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7));
            yield return Case("collinear-duplicates", "literal(-2 -4, 0 0, 2 4, 1 2, -2 -4)", Generators.Literal(-2, -4, 0, 0, 2, 4, 1, 2, -2, -4));
            yield return Case("triangle", "literal(0 0, 8 0, 0 7)", Generators.Literal(0, 0, 8, 0, 0, 7));
            yield return Case("square", "literal(0 0, 8 0, 8 8, 0 8)", Generators.Literal(0, 0, 8, 0, 8, 8, 0, 8), ties: true);
            yield return Case("square-centre", "literal(0 0, 8 0, 8 8, 0 8, 4 4)", Generators.Literal(0, 0, 8, 0, 8, 8, 0, 8, 4, 4));
            yield return Case("hull-edge-point", "literal(0 0, 8 0, 0 7, 4 0)", Generators.Literal(0, 0, 8, 0, 0, 7, 4, 0));
            yield return Case("duplicates-signed-zero", "literal(-0 0, 4 0, 0 4, 0 -0, 4 0, 1 1)", Generators.Literal(NegZero, 0, 4, 0, 0, 4, 0, NegZero, 4, 0, 1, 1));
            yield return Case("collinear-extension", "literal(0 0, 2 0, 1 0, 4 0, 1 1, 3 0, 2 5)", Generators.Literal(0, 0, 2, 0, 1, 0, 4, 0, 1, 1, 3, 0, 2, 5));
            yield return Case("uniform-int-32", "uniform-int(n=32 bits=20 seed=1)", Generators.UniformInt(32, 20, 1));
            yield return Case("uniform-int-128", "uniform-int(n=128 bits=20 seed=2)", Generators.UniformInt(128, 20, 2));
            yield return Case("uniform-small-range-64", "uniform-int(n=64 bits=4 seed=3)", Generators.UniformInt(64, 4, 3), ties: true);
            yield return Case("dyadic-64", "transform(uniform-int(n=64 bits=30 seed=4), exp=-20)", Generators.Transform(Generators.UniformInt(64, 30, 4), -20));
            yield return Case("utm-64", "transform(uniform-int(n=64 bits=27 seed=5), exp=-10, dx=632117.25, dy=5712043.5)", Generators.Transform(Generators.UniformInt(64, 27, 5), -10, 632117.25, 5712043.5));
            yield return Case("tiny-64", "transform(uniform-int(n=64 bits=31 seed=6), exp=-560)", Generators.Transform(Generators.UniformInt(64, 31, 6), -560));
            yield return Case("huge-64", "transform(uniform-int(n=64 bits=31 seed=7), exp=200)", Generators.Transform(Generators.UniformInt(64, 31, 7), 200));
            yield return Case("subnormal-32", "transform(uniform-int(n=32 bits=20 seed=8), exp=-1074)", Generators.Transform(Generators.UniformInt(32, 20, 8), -1074));
            yield return Case("mixed-48", "mixed(n=48 bits=20 tiny=-530 large=200 seed=9)", Generators.Mixed(48, 20, -530, 200, 9));
            yield return Case("grid-8x8", "grid(nx=8 ny=8)", Generators.Grid(8, 8), ties: true);
            yield return Case("lattice-circle-1105", "lattice-circle(r=1105 centre=false)", Generators.LatticeCircle(1105, false), ties: true);
            yield return Case("lattice-circle-centre", "lattice-circle(r=1105 centre=true)", Generators.LatticeCircle(1105, true));
            yield return Case("near-cocircular", "nudge-odd(lattice-circle(r=1105 centre=false))", Generators.NudgeOdd(Generators.LatticeCircle(1105, false)));
            yield return Case("near-collinear-64", "near-collinear(n=64)", Generators.NearCollinear(64));
            yield return Case("parabola-48", "parabola(n=48)", Generators.Parabola(48));
            yield return Case("square-border-16", "square-border(k=16)", Generators.SquareBorder(16), ties: true);
        }

        /// <summary>Larger cases for the differential and the benchmark, outside the CI subset.</summary>
        public static IEnumerable<Mesh2Case> Larger()
        {
            yield return Case("uniform-int-512", "uniform-int(n=512 bits=26 seed=10)", Generators.UniformInt(512, 26, 10));
            yield return Case("uniform-int-1024", "uniform-int(n=1024 bits=26 seed=11)", Generators.UniformInt(1024, 26, 11));
            yield return Case("grid-24x24", "grid(nx=24 ny=24)", Generators.Grid(24, 24), ties: true);
            yield return Case("utm-512", "transform(uniform-int(n=512 bits=27 seed=12), exp=-10, dx=632117.25, dy=5712043.5)", Generators.Transform(Generators.UniformInt(512, 27, 12), -10, 632117.25, 5712043.5));
            yield return Case("near-collinear-256", "near-collinear(n=256)", Generators.NearCollinear(256));
            yield return Case("parabola-256", "parabola(n=256)", Generators.Parabola(256));
            yield return Case("lattice-circle-32045", "lattice-circle(r=32045 centre=false)", Generators.LatticeCircle(32045, false), ties: true);
        }
    }
}
