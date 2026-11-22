#!/usr/bin/env python3
"""Summary tables of results/run1.txt (the raw output of the benchmark's default mode), as markdown.
   python3 benchmarks/SelfIntersectionUntangle/summarize.py benchmarks/SelfIntersectionUntangle/results/run1.txt"""
import statistics
import sys


def num(s):
    return None if s.strip() == "-" else float(s)


def main(path):
    rows = []
    for line in open(path, encoding="utf-8"):
        p = [x.strip() for x in line.split(" | ")]
        if len(p) < 14 or p[0] == "class":
            continue
        topo, intent = p[12].split(" (")
        rows.append(dict(cls=p[0], case=p[1], frame=p[2], method=p[5], clean=p[6] == "clean", ms=float(p[7]), to=num(p[8]), frm=num(p[9]),
                         intent=num(p[10]), vol=num(p[11]), topo=topo, itopo=intent.rstrip(")")))
    methods = ["untangle local+d0", "untangle local+d4", "untangle signed+d0", "untangle signed+d4", "robust repair", "exact resolve", "solidify", "alpha wrap"]
    classes = sorted({r["cls"] for r in rows})

    def med(xs):
        xs = [x for x in xs if x is not None]
        return "-" if not xs else "%.4f" % statistics.median(xs)

    print("| method | " + " | ".join(classes) + " | all |")
    print("|---|" + "---|" * (len(classes) + 1))
    for m in methods:
        cells = []
        for c in classes + [None]:
            sel = [r for r in rows if r["method"] == m and (c is None or r["cls"] == c)]
            cells.append("%d / %d" % (sum(r["clean"] for r in sel), len(sel)))
        print("| " + m + " | " + " | ".join(cells) + " |")
    print()
    print("| method | clean | median h(result->input) | median h(input->result) | max h(result->input) | median H(intent) | topology of the intent kept | median ms |")
    print("|---|---|---|---|---|---|---|---|")
    for m in methods:
        sel = [r for r in rows if r["method"] == m and r["clean"]]
        withintent = [r for r in sel if r["itopo"] != "-"]
        same = sum(r["topo"] == r["itopo"] for r in withintent)
        mx = max([r["to"] for r in sel if r["to"] is not None], default=float("nan"))
        print("| %s | %d | %s | %s | %.4f | %s | %d / %d | %s |" % (m, len(sel), med([r["to"] for r in sel]), med([r["frm"] for r in sel]), mx,
              med([r["intent"] for r in sel]), same, len(withintent), "%.0f" % statistics.median([r["ms"] for r in rows if r["method"] == m])))
    print()
    # the runs both the signed untangler and the repair finish clean, with an intent: who is closer to it
    a = {(r["case"], r["frame"]): r for r in rows if r["method"] == "untangle signed+d0" and r["clean"] and r["intent"] is not None}
    b = {(r["case"], r["frame"]): r for r in rows if r["method"] == "robust repair" and r["clean"] and r["intent"] is not None}
    both = sorted(set(a) & set(b))
    closer = sum(a[k]["intent"] < 0.9 * b[k]["intent"] for k in both)
    farther = sum(a[k]["intent"] > 1.1 * b[k]["intent"] for k in both)
    print("runs with an intent where both are clean: %d; untangled closer to the intent by more than 10 %%: %d; farther by more than 10 %%: %d; within 10 %%: %d"
          % (len(both), closer, farther, len(both) - closer - farther))
    va = [abs(a[k]["vol"] - 1) for k in both]
    vb = [abs(b[k]["vol"] - 1) for k in both]
    print("median |volume / intent - 1|: untangled %.4f, repair %.4f" % (statistics.median(va), statistics.median(vb)))
    print()
    # per case (canonical frame): does the repair (exact resolve) give the solid of the intent, and what does untangling add
    print("| class | case | repair: topology of the intent | repair: volume / intent | repair: H(intent) | untangled: volume / intent | untangled: H(intent) |")
    print("|---|---|---|---|---|---|---|")
    f = lambda x: "-" if x is None else "%.4f" % x
    for r in rows:
        if r["method"] != "robust repair" or r["frame"] != "id":
            continue
        u = [x for x in rows if x["method"] == "untangle signed+d0" and x["frame"] == "id" and x["case"] == r["case"]][0]
        topo = "no intent" if r["itopo"] == "-" else ("kept" if r["topo"] == r["itopo"] else "changed (%s, intent %s)" % (r["topo"], r["itopo"]))
        print("| %s | %s | %s | %s | %s | %s | %s |" % (r["cls"], r["case"], topo, f(r["vol"]), f(r["intent"]),
              f(u["vol"]) if u["clean"] else "not untangled", f(u["intent"]) if u["clean"] else "-"))


if __name__ == "__main__":
    main(sys.argv[1])
