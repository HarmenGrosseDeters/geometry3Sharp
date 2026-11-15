"""Experimental scalar verifier for exported shifted interval matrices.

Not an end-to-end mesh certificate. Entries and their structural completeness are
trusted input. Bound derivation and runtime assumptions require external review.
No source code port: scalar recurrence and trace bound from Rump (2006).
"""
import json, math, struct, sys, time
from fractions import Fraction as F

AUDIT = False


def bits(x):
    return struct.unpack('>Q', struct.pack('>d', x))[0]


def rational(x):
    raw = bits(x)
    exponent, mantissa = (raw >> 52) & 2047, raw & (2**52 - 1)
    if exponent == 2047:
        raise ArithmeticError('nonfinite binary64')
    power = -1074 if exponent == 0 else exponent - 1075
    integer = mantissa if exponent == 0 else mantissa + 2**52
    if raw >> 63:
        integer = -integer
    return F(integer * 2**power) if power >= 0 else F(integer, 2**(-power))


def neighbor(x, upward):
    raw = bits(x)
    if raw & (2**63 - 1) == 0:
        raw = 1 if upward else (2**63 | 1)
    else:
        raw += 1 if upward != bool(raw >> 63) else -1
    return struct.unpack('>d', struct.pack('>Q', raw))[0]


def rounding_cell(x):
    """Exact nearest-even cell of a finite binary64 result."""
    if not math.isfinite(x):
        raise ArithmeticError('nonfinite rounding candidate')
    before, after = neighbor(x, False), neighbor(x, True)
    if not math.isfinite(before) or not math.isfinite(after):
        raise ArithmeticError('rounding audit excludes overflow boundary')
    lo, hi = (rational(before) + rational(x)) / 2, (rational(x) + rational(after)) / 2
    even = struct.unpack('>Q', struct.pack('>d', x))[0] % 2 == 0
    return lo, hi, even


def audit_round(x, exact, squared=False):
    lo, hi, even = rounding_cell(x)
    if squared:
        if lo < 0:
            raise ArithmeticError('invalid square-root rounding cell')
        lo, hi = lo * lo, hi * hi
    if not (lo < exact < hi or (even and exact in (lo, hi))):
        raise ArithmeticError('incorrect nearest-even rounding')
    return x


def difference(a, b):
    x = checked(a - b)
    return audit_round(x, rational(a) - rational(b)) if AUDIT else x


def root(a):
    x = checked(math.sqrt(a))
    return audit_round(x, rational(a), squared=True) if AUDIT else x


def upper(x):
    y = float(x)
    if rational(y) < x:
        y = neighbor(y, True)
    if rational(y) < x:
        raise ArithmeticError('incorrect outward conversion')
    if not math.isfinite(y):
        raise ArithmeticError('bound overflow')
    return y


def checked(x):
    raw = bits(x) & (2**63 - 1)
    if raw >= 2047 * 2**52 or (0 < raw < 2**52):
        raise ArithmeticError('nonfinite or subnormal intermediate')
    return x


def product(a, b):
    x = checked(a * b)
    if AUDIT:
        audit_round(x, rational(a) * rational(b))
    if rational(x) == 0 and rational(a) != 0 and rational(b) != 0:
        raise ArithmeticError('product underflow')
    if abs(rational(x)) == F(1, 2**1022) and abs(rational(a) * rational(b)) < F(1, 2**1022):
        raise ArithmeticError('exact product is subnormal')
    return x


def quotient(a, b):
    x = checked(a / b)
    if AUDIT:
        audit_round(x, rational(a) / rational(b))
    if rational(x) == 0 and rational(a) != 0:
        raise ArithmeticError('division underflow')
    if abs(rational(x)) == F(1, 2**1022) and abs(rational(a) / rational(b)) < F(1, 2**1022):
        raise ArithmeticError('exact quotient is subnormal')
    return x


def verify(data):
    n = data['size']
    if not 0 < n <= 2500:
        raise ValueError('experimental size cap')
    a = {}; radius = [F(0) for _ in range(n)]
    for i, j, lo, hi in data['entries']:
        if not 0 <= j <= i < n or (i, j) in a:
            raise ValueError('invalid or duplicate entry')
        lo, hi = F(lo), F(hi)
        if lo > hi:
            raise ValueError('inverted interval')
        mid = checked(float((lo + hi) / 2))
        a[i, j] = mid
        r = max(abs(rational(mid) - lo), abs(hi - rational(mid)))
        radius[i] += r
        if i != j:
            radius[j] += r
    diagonal = [a[i, i] for i in range(n)]
    if min(diagonal) <= 0:
        return False, 'nonpositive midpoint diagonal'
    eps = F(1, 2**53)
    gamma = (n + 1) * eps / (1 - (n + 1) * eps)
    # Underflow is excluded by checked arithmetic below, so no eta term is used.
    c = upper(gamma / (1 - gamma) * sum(map(rational, diagonal)))
    r = upper(max(radius))
    for i in range(n):
        exact = rational(diagonal[i]) - rational(c) - rational(r)
        v = float(exact)
        if rational(v) > exact:
            v = neighbor(v, False)
        if rational(v) > exact:
            raise ArithmeticError('incorrect downward conversion')
        a[i, i] = checked(v)
    # Symbolic fill comes from structure, even where numeric values are zero.
    columns = [set() for _ in range(n)]
    for i, j in a:
        if i != j:
            columns[j].add(i)
    rows = [dict() for _ in range(n)]
    updates = 0
    for j in range(n):
        neighbors = sorted(columns[j])
        for p, i in enumerate(neighbors):
            columns[i].update(neighbors[p+1:])
        diagonal_value = a[j, j]
        for k, v in rows[j].items():
            diagonal_value = difference(diagonal_value, product(v, v))
        if diagonal_value <= 0:
            return False, f'failed_pivot={j} c={c:.12g} radius={r:.12g}'
        pivot = root(diagonal_value)
        for i in neighbors:
            value = a.get((i, j), 0.0)
            for k in sorted(rows[i].keys() & rows[j].keys()):
                value = difference(value, product(rows[i][k], rows[j][k]))
                updates += 1
            rows[i][j] = quotient(value, pivot)
    return True, f'c={c:.12g} radius={r:.12g} products={updates}'


if __name__ == '__main__':
    AUDIT = '--audit' in sys.argv[2:]
    start = time.perf_counter()
    with open(sys.argv[1], encoding='utf-8') as stream:
        data = json.load(stream)
    ok, detail = verify(data)
    print(f'scalar_shifted_matrix_check={ok} {detail} elapsed_seconds={time.perf_counter()-start:.3f}')
    print(f'scope=imported_intervals_only arithmetic_audited={AUDIT} bounds_review=ongoing position_certificate=not_checked')
    sys.exit(0 if ok else 1)
