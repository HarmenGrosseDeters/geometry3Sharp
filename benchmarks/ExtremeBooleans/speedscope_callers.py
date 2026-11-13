# Which geometry3Sharp frames lead to a given frame: the CPU time of the samples whose stack contains <target>,
# grouped by the deepest geometry3Sharp frame above it (Speedscope file of dotnet-trace, evented profiles).
#   python3 speedscope_callers.py <trace.speedscope.json> <target> [n]
import json, sys, collections
d = json.load(open(sys.argv[1])); target = sys.argv[2]; n = int(sys.argv[3]) if len(sys.argv) > 3 else 10
frames = [f["name"] for f in d["shared"]["frames"]]
c, total = collections.Counter(), 0.0
for p in d["profiles"]:
    stack, last = [], None
    for e in p["events"]:
        t = e["at"]
        if stack and last is not None and t > last and frames[stack[-1]] == "CPU_TIME":
            names = [frames[i] for i in stack]
            total += t - last
            hit = [k for k, x in enumerate(names) if target in x]
            if hit:
                g = [x for x in names[:hit[0]] if "geometry3Sharp" in x]
                c[g[-1] if g else "-"] += t - last
        last = t
        if e["type"] == "O": stack.append(e["frame"])
        elif stack: stack.pop()
for k, v in c.most_common(n): print("%5.1f %%  %s" % (100 * v / total, k[:150]))
