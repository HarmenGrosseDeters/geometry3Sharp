# Self and inclusive time per frame of a dotnet-trace Speedscope file (evented profiles, as `dotnet-trace collect
# --format Speedscope` writes them), over all threads; only time inside a frame whose name contains <filter> counts
# (default: all). Only CPU time counts (stacks that end in the CPU_TIME pseudo frame; waiting threads are left out);
# the self time goes to the last real frame. Prints the top <n> frames by self and by inclusive share of that time.
#   python3 speedscope_top.py <trace.speedscope.json> [n] [filter]
import json, sys, collections
d = json.load(open(sys.argv[1])); n = int(sys.argv[2]) if len(sys.argv) > 2 else 25
flt = sys.argv[3] if len(sys.argv) > 3 else None
frames = [f["name"] for f in d["shared"]["frames"]]
self_t, incl, total = collections.Counter(), collections.Counter(), 0.0
for p in d["profiles"]:
    stack, last = [], None
    for e in p["events"]:
        t = e["at"]
        if stack and last is not None and t > last:
            names = [frames[i] for i in stack]
            if names and names[-1] == "CPU_TIME" and (not flt or any(flt in x for x in names)):
                names = names[:-1]
                w = t - last; total += w; self_t[names[-1]] += w
                for x in set(names): incl[x] += w
        last = t
        if e["type"] == "O": stack.append(e["frame"])
        elif stack: stack.pop()
print("total ms", round(total))
for title, c in (("self", self_t), ("inclusive", incl)):
    print("--", title)
    for k, v in c.most_common(n): print("%5.1f %%  %s" % (100 * v / total, k[:160]))
