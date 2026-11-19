import csv,sys,statistics as st
rows=list(csv.DictReader(open(sys.argv[1])))
by={}
for r in rows: by.setdefault(r['name'],[]).append(r)
f=float
first={n:rs[0] for n,rs in by.items()}
def q(vals,p): v=sorted(vals); return v[min(len(v)-1,int(p*(len(v)-1)+0.5))]
print("meshes",len(by))
for k in ['frac_d40_gt_half_alpha','frac_d40_gt_alpha','area_gap','area_thin_only','gap_share_of_d40_gt_half_alpha','thin_share_of_d40_gt_half_alpha','gap_share_of_worst5pct','thin_share_of_worst5pct','irreducible_share_of_gt_half_alpha']:
    vals=[f(r[k]) for r in first.values()]
    print("%-40s median %.3f p10 %.3f p90 %.3f"%(k,st.median(vals),q(vals,.1),q(vals,.9)))
# weight: meshes with frac>a/2 > 1%
sig=[n for n,r in first.items() if f(r['frac_d40_gt_half_alpha'])>0.01]
print("meshes with >1%% of area beyond alpha/2: %d"%len(sig))
for k in ['gap_share_of_d40_gt_half_alpha','thin_share_of_d40_gt_half_alpha','irreducible_share_of_gt_half_alpha']:
    vals=[f(first[n][k]) for n in sig]; print("  among them %-36s median %.3f"%(k,st.median(vals)))
print()
for tgt in ['p95_80','max_80','p95_160','max_160']:
    rs=[by[n][[r['target'] for r in by[n]].index(tgt)] for n in by]
    print(tgt,"fine area strict med %.3f  dilated med %.3f | tets_local/tets80 strict med %.2f dilated med %.2f | /tets160 dilated med %.2f | achieved p95/T med %.2f max/T med %.2f"%(
      st.median(f(r['frac_fine_strict']) for r in rs),st.median(f(r['frac_fine_dilated']) for r in rs),st.median(f(r['ratio_vs80_strict']) for r in rs),st.median(f(r['ratio_vs80_dilated']) for r in rs),st.median(f(r['ratio_vs160_dilated']) for r in rs),st.median(f(r['ach_p95_over_T']) for r in rs),st.median(f(r['ach_max_over_T']) for r in rs)))
    # only meshes where uniform 80/160 improve on 40 (reducible): max40 > 1.3*max80
