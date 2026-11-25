# Before/after table of two builds of the benchmark: minimum time per case over the given runs of each, and whether
# every run printed the same result (triangle counts, rejections, fingerprint).
# usage: python3 ab_compare.py <before outputs...> -- <after outputs...>
import sys
def load(f):
    d = {}
    for l in open(f):
        c = [x.strip() for x in l.strip().strip('|').split('|')]
        if len(c) >= 7 and c[0] and c[0][0].isalpha() and c[0] != 'case':
            d[c[0]] = (float(c[4]), c[6], c[2], c[3])
    return d
k = sys.argv.index('--')
M = [load(f) for f in sys.argv[1:k]]; C = [load(f) for f in sys.argv[k + 1:]]
print("| case | before ms (each run) | after ms (each run) | change (min) | result |")
print("|---|---:|---:|---:|---|")
for name in M[0]:
    m = min(x[name][0] for x in M); c = min(x[name][0] for x in C)
    same = all(x[name][1:] == M[0][name][1:] for x in M + C)
    print(f"| {name} | {', '.join(f'{x[name][0]:.0f}' for x in M)} | {', '.join(f'{x[name][0]:.0f}' for x in C)} | {100 * (c / m - 1):+.1f} % | {'same' if same else 'DIFFERS'} |")
