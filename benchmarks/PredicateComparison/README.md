# Predicate comparison

Compares implementations of orient2d, incircle and orient3d with an exact reference: the doubles of a case
are written as integers `m * 2^e` on a common exponent and the determinant is evaluated in `BigInteger`.
Exceptions and NaN results count as wrong signs. Results and discussion: `docs/PREDICATES.md`.

Scenarios (20 000 cases each, fixed seeds):
- nearly degenerate: c = a + t (b - a) / four points on a circle / d = a + u (b - a) + v (c - a), every
  coordinate moved by up to 4 ulp
- exactly degenerate on an integer grid (collinear, on circles of radius 5k through integer points, coplanar)
- the grid of Kettner et al. ("Classroom examples of robustness problems in geometric computations"):
  (0.5 + i ulp, 0.5 + j ulp) against a line, a circle and a plane through it
- all of them multiplied by 1, 1e6, 1e-10, 1e-160, 1e-300, 1e150, 1e200

Then the time for 200 000 calls in general position and nearly degenerate.

    dotnet run -c Release                      # g3 and naive double
    dotnet run -c Release -- 5000              # fewer cases
    dotnet run -c Release -p:TriangleNetProject=/path/to/triangle.net/src/Triangle/Triangle.csproj

The last one also runs Triangle.NET's `RobustPredicates` (not part of this repository; only referenced when
the property is given).
