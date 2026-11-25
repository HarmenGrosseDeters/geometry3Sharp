#!/usr/bin/env python3
"""Fold a dotnet-trace speedscope file (thread-time sampling) into the BigInteger share of ExactMeshBoolean.Compute.

Usage: python3 fold_speedscope.py <file.speedscope.json>

Time is attributed to the full stack of every interval between events, per thread. Only stacks under
ExactMeshBoolean.Compute count, and stacks whose leaf frame is a wait (idle workers of the parallel loops) are dropped.
"BigInteger arithmetic" = a frame of System.Runtime.Numerics (BigInteger, BigIntegerCalculator) anywhere on the stack,
inclusive of the GC it triggers below it. "BigInteger-keyed containers" = a frame of a collection or comparer whose type
mentions BigInteger, without a Numerics frame. The caller column is the innermost g3 frame above the BigInteger frame.
"""
import collections
import json
import sys

WAIT = ("Wait", "Sleep", "SpinWait", "LowLevelLifoSemaphore", "Monitor.Enter", "Join", "WaitHandle")


def main(path):
    d = json.load(open(path))
    frames = [f["name"] for f in d["shared"]["frames"]]
    is_compute = [("ExactMeshBoolean.Compute" in n) for n in frames]
    is_num = [n.startswith("System.Runtime.Numerics!") for n in frames]
    is_keyed = [("BigInteger" in n and not n.startswith("System.Runtime.Numerics!")
                 and not n.startswith("geometry3Sharp!")) for n in frames]
    is_g3 = [n.startswith("geometry3Sharp!") for n in frames]
    total = 0.0
    big = 0.0
    keyed = 0.0
    idle = 0.0
    callers = collections.Counter()
    for p in d["profiles"]:
        stack = []
        last = None
        for ev in p["events"]:
            t = ev["at"]
            if last is not None and stack:
                dt = t - last
                if any(is_compute[f] for f in stack):
                    leaf = frames[stack[-1]]
                    if any(w in leaf for w in WAIT):
                        idle += dt
                    else:
                        total += dt
                        ni = next((i for i, f in enumerate(stack) if is_num[f]), -1)
                        if ni >= 0:
                            big += dt
                            caller = next((frames[stack[j]] for j in range(ni - 1, -1, -1) if is_g3[stack[j]]), "?")
                            callers[caller.split("(")[0].replace("geometry3Sharp!", "")] += dt
                        elif any(is_keyed[f] for f in stack):
                            keyed += dt
            last = t
            if ev["type"] == "O":
                stack.append(ev["frame"])
            else:
                if stack:
                    stack.pop()
    print(f"busy time under ExactMeshBoolean.Compute: {total:.0f} ms (idle waits dropped: {idle:.0f} ms)")
    print(f"BigInteger arithmetic (inclusive, incl. GC below it): {big:.0f} ms = {100 * big / total:.1f} %")
    print(f"BigInteger-keyed containers (hashing, equality): {keyed:.0f} ms = {100 * keyed / total:.1f} %")
    print("| innermost g3 caller of the BigInteger frame | ms | share of busy time |")
    print("|---|---|---|")
    for c, ms in callers.most_common(12):
        print(f"| {c} | {ms:.0f} | {100 * ms / total:.1f} % |")



def top(path, n=15):
    """the leaf frames (self time) under ExactMeshBoolean.Compute, for the stop note's next candidates"""
    d = json.load(open(path))
    frames = [f["name"] for f in d["shared"]["frames"]]
    self_t = collections.Counter()
    total = 0.0
    for p in d["profiles"]:
        stack, last = [], None
        for ev in p["events"]:
            t = ev["at"]
            if last is not None and stack and any("ExactMeshBoolean.Compute" in frames[f] for f in stack):
                dt = t - last
                total += dt
                g3 = next((frames[f] for f in reversed(stack) if frames[f].startswith("geometry3Sharp!")), "?")
                self_t[g3.split("(")[0].replace("geometry3Sharp!", "")] += dt
            last = t
            if ev["type"] == "O":
                stack.append(ev["frame"])
            elif stack:
                stack.pop()
    print("| innermost g3 frame (self time incl. callees outside g3) | share |")
    print("|---|---|")
    for c, ms in self_t.most_common(n):
        print(f"| {c} | {100 * ms / total:.1f} % |")


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[2] == "--top":
        top(sys.argv[1])
    else:
        main(sys.argv[1])
