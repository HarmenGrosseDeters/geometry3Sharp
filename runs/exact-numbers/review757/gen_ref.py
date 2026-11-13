# reference: the largest double <= tan(a pi / 180) for double angles a, by Decimal Taylor series at 200 digits
import random, struct, sys
from decimal import Decimal, getcontext
from fractions import Fraction as F
getcontext().prec = 220
def pi_dec():
    # Machin: pi = 16 atan(1/5) - 4 atan(1/239)
    def atan_inv(n):
        n = Decimal(n); s = Decimal(0); p = 1 / n; k = 0; n2 = n * n
        while True:
            t = p / (2 * k + 1)
            if t < Decimal(10) ** -215: break
            s += t if k % 2 == 0 else -t
            p /= n2; k += 1
        return s
    return 16 * atan_inv(5) - 4 * atan_inv(239)
PI = pi_dec()
def bits(x): return struct.unpack('<q', struct.pack('<d', x))[0]
def frombits(b): return struct.unpack('<d', struct.pack('<q', b))[0]
def tan_floor(a):
    fa = F(a)
    x = Decimal(fa.numerator) / Decimal(fa.denominator) * PI / 180
    s = Decimal(0); c = Decimal(0); term = Decimal(1); m = 0
    while True:
        if m % 2 == 0: c += term if (m // 2) % 2 == 0 else -term
        else: s += term if (m // 2) % 2 == 0 else -term
        m += 1
        term = term * x / m
        if m > 400 or (term < Decimal(10) ** -330 and m > 10): break
    t = s / c
    ft = F(t)
    try:
        d = float(ft)
    except OverflowError:
        return None
    if d == 0.0:
        return 0.0
    if F(d) > ft: d = frombits(bits(d) - 1)
    return d
random.seed(7)
angles = [30.0, 45.0, 60.0, 10.0, 50.0, 75.0, 85.0, 89.0, 1e-6, 89.99999, frombits(bits(90.0) - 1), frombits(bits(90.0) - 2), 89.99999999999, 89.9999999999999,
          1e-300, 1e-305, 2e-306, 1.2e-306, 1.3e-306, 1e-200, 1e-100, 1e-20, 1e-10, 44.99999999999999, 45.00000000000001, 0.5, 1.0, 5.0, 22.5, 67.5, 80.0, 88.0, 89.9, 89.999]
for _ in range(1500): angles.append(random.uniform(0, 90))
for _ in range(300): angles.append(90 - 10 ** random.uniform(-14, 0))
for _ in range(300): angles.append(10 ** random.uniform(-290, 0))
with open(sys.argv[1], 'w') as f:
    for a in angles:
        if not (0 < a < 90): continue
        t = tan_floor(a)
        if t is None: continue
        f.write("%d %d\n" % (bits(a), bits(t)))
print(len(angles), "angles")
