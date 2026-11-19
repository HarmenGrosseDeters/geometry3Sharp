#!/usr/bin/env python3
"""Summary of run_local_alpha.sh (TET_MESHING.md 17.13): per configuration, the medians over the meshes of the per-mesh ratios against uniform alpha / 80
(tets, input-to-boundary max distance, time), the absolute medians, and the counts of valid results and of meshes with every tet >= 10 degrees."""
import csv, statistics as st, sys
rows = [r for r in csv.reader(open(sys.argv[1])) if len(r) > 25 and r[0] != 'skipped']
hdr = "name,time_s,threads,tets,vertices,neg_tets,zero_tets,inverted,valid,boundary_tris,boundary_self_intersecting,re_worst,re_p5,re_median,re_gt2_share,mindih_worst,mindih_p1,mindih_p5,mindih_median,dih_lt10_share,dih_ge10_all,dev_out_max_rel,dev_out_mean_rel,dev_in_max_rel,dev_in_mean_rel,status".split(',')
by = {}
for r in rows:
    d = dict(zip(['cfg'] + hdr, r[:len(hdr) + 1]))
    by.setdefault(d['name'], {})[d['cfg']] = d
cfgs = ['u40', 'u80', 'u160', 'd150p1', 'd250p1', 'd250p2']
names = [n for n in by if all(c in by[n] and by[n][c]['status'] == 'Success' for c in cfgs)]
f = float
print("meshes with every configuration Success:", len(names), "of", len(by))
print("%-8s %9s %9s %9s | %8s %9s %9s | valid  selfint-free  all>=10deg" % ("config", "tets/u80", "dev/u80", "time/u80", "tets", "in-max", "time s"))
for c in cfgs:
    rt = [f(by[n][c]['tets']) / f(by[n]['u80']['tets']) for n in names]
    rd = [f(by[n][c]['dev_in_max_rel']) / f(by[n]['u80']['dev_in_max_rel']) for n in names]
    rm = [f(by[n][c]['time_s']) / f(by[n]['u80']['time_s']) for n in names]
    print("%-8s %9.2f %9.2f %9.2f | %8.0f %9.4f %9.2f | %d %d %d of %d" % (c, st.median(rt), st.median(rd), st.median(rm),
          st.median(f(by[n][c]['tets']) for n in names), st.median(f(by[n][c]['dev_in_max_rel']) for n in names), st.median(f(by[n][c]['time_s']) for n in names),
          sum(by[n][c]['valid'] == 'yes' for n in names), sum(by[n][c]['boundary_self_intersecting'] == 'no' for n in names), sum(by[n][c]['dih_ge10_all'] == 'yes' for n in names), len(names)))
for c in cfgs[3:]:
    w = sum(1 for n in names if f(by[n][c]['dev_in_max_rel']) <= 1.0001 * f(by[n]['u80']['dev_in_max_rel']) and f(by[n][c]['tets']) < f(by[n]['u80']['tets']))
    print("%s: input-to-boundary max <= uniform /80 with fewer tets on %d of %d meshes" % (c, w, len(names)))
