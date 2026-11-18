# aggregate a speedscope evented trace: self and inclusive ms per frame name (all threads; recursion counted once)
import json, sys, collections
def load(path):
    d = json.load(open(path)); names = [f['name'] for f in d['shared']['frames']]
    selfms = collections.Counter(); incl = collections.Counter(); total = 0.0
    for p in d['profiles']:
        stack = []; last = None
        for e in p['events']:
            t = e['at']
            if last is not None and stack:
                dt = t - last
                selfms[stack[-1]] += dt
                for f in set(stack): incl[f] += dt
                if names[stack[0]].startswith('Process') or True: total += dt
            last = t
            if e['type'] == 'O': stack.append(e['frame'])
            else:
                while stack and stack[-1] != e['frame']: stack.pop()
                if stack: stack.pop()
    return names, selfms, incl
if __name__ == '__main__':
    names, s, i = load(sys.argv[1])
    pat = sys.argv[2] if len(sys.argv) > 2 else ''
    for f, v in i.most_common(int(sys.argv[3]) if len(sys.argv) > 3 else 60):
        if pat in names[f]: print(f"{v:9.0f} incl {s[f]:9.0f} self  {names[f][:150]}")
