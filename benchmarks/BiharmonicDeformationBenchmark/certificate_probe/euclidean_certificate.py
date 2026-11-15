"""Exact comparisons for an already-proven, single-component residual bound.

This module cannot establish the input coordinate bounds. The caller must prove
them for the final rounded output and the same normalization before calling it.
Inputs must contain exactly one connected component in ascending original vertex
ID order, so index zero is the contract's rest-radius anchor. Free indices refer
to that local order. Component membership and target identity are caller premises.
"""
from fractions import Fraction as F
from scalar_cholesky_probe import rational


def assess(original, candidate, free, scale, coordinate_bounds,
           position_tolerance=F.from_float(1e-8), response_tolerance=F.from_float(1e-8)):
    if len(original) != len(candidate) or not original or len(coordinate_bounds) != 3:
        raise ValueError('inconsistent dimensions')
    s = rational(float(scale))
    bounds = [F(value) for value in coordinate_bounds]
    pt, rt = F(position_tolerance), F(response_tolerance)
    if s <= 0 or min(bounds) < 0 or pt <= 0 or rt <= 0:
        raise ValueError('invalid bounds or tolerances')
    free = list(free)
    if not free or len(set(free)) != len(free) or any(i < 0 or i >= len(original) for i in free):
        raise ValueError('invalid free indices')
    p = [[rational(float(v)) for v in row] for row in original]
    x = [[rational(float(v)) for v in row] for row in candidate]
    if any(len(row) != 3 for row in p + x):
        raise ValueError('coordinates must have three axes')
    # Every axis bound controls every free coordinate. Combining their squares
    # controls the Euclidean error of each free vertex, without taking a sqrt.
    error_squared = s * s * sum(b * b for b in bounds)
    rest_squared = max(sum((v[k] - p[0][k])**2 for k in range(3)) for v in p)
    response_squared = max(sum((x[i][k] - p[i][k])**2 for k in range(3)) for i in free)
    if rest_squared == 0:
        raise ValueError('zero rest radius')
    absolute_ok = error_squared <= pt * pt * rest_squared
    relative_ok = error_squared * (1 + rt)**2 <= rt * rt * response_squared
    return absolute_ok, relative_ok, error_squared, rest_squared, response_squared
