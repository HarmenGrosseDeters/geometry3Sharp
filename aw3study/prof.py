import re, collections, glob, sys, json
def short(s):
    m = re.search(r'([\w.`+<>]+)::([\w.<>|`]+)\(', s)
    if m: return m.group(1).split('.')[-1] + '::' + m.group(2)
    return s[:60]
def load(d):
    out = []
    for p in sorted(glob.glob(d + '/*.folded')):
        st = []
        for l in open(p):
            n, tid, s = l.rstrip('\n').split('\t'); st.append((int(n), tid, [short(x) for x in s.split(';')]))
        out.append((p.split('/')[-1][:-7], st))
    return out
if __name__ == '__main__':
    data = load(sys.argv[1]); leaf = collections.Counter(); tot = 0
    for name, st in data:
        for n, tid, fr in st: leaf[fr[0]] += n; tot += n
    for k, v in leaf.most_common(int(sys.argv[2])): print('%5.2f%% %s' % (100 * v / tot, k))
