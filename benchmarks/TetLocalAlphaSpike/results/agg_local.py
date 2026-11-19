import csv,sys,math,statistics as st
rows=list(csv.DictReader(open(sys.argv[1])))
metric=sys.argv[2] if len(sys.argv)>2 else 'max'
by={}
for r in rows: by.setdefault(r['name'],{})[r['variant']]=r
def fl(x):
    try: return float(x)
    except: return float('nan')
def equiv_tets(m,u):
    # log-log interpolation over uniform 40/80/160 of (dev, tets); clamped
    pts=[(fl(u['uniform%d'%a][metric]),fl(u['uniform%d'%a]['tets'])) for a in (40,80,160)]
    # deviations decrease with alpha; sort by dev descending (40 first)
    if m>=pts[0][0]: return pts[0][1]
    if m<=pts[2][0]: return pts[2][1]
    for (d0,t0),(d1,t1) in zip(pts,pts[1:]):
        if d1<=m<=d0:
            if d0==d1: return t0
            f=(math.log(m)-math.log(d1))/(math.log(d0)-math.log(d1))
            return math.exp(math.log(t1)+f*(math.log(t0)-math.log(t1)))
    return pts[0][1]
variants=['uniform40','uniform80','uniform160','oracle_p95_80','post_p95_80','post2_p95_80','postLhalf_p95_80','post_p95_160','post2_p95_160','lfs_g3']
res={v:{k:[] for k in ('tets80','dev80','time80','eff','valid','self','floor10','n')} for v in variants}
names=[n for n in by if all(v in by[n] for v in variants)]
for n in names:
    u=by[n]
    t80=fl(u['uniform80']['tets']); d80=fl(u['uniform80'][metric]); tm80=fl(u['uniform80']['time_s'])
    for v in variants:
        r=u[v]
        res[v]['tets80'].append(fl(r['tets'])/t80)
        res[v]['dev80'].append(fl(r[metric])/d80)
        res[v]['time80'].append(fl(r['time_s'])/tm80)
        res[v]['eff'].append(fl(r['tets'])/equiv_tets(fl(r[metric]),u))
        res[v]['valid'].append(r['valid']=='yes' and r['status']=='Success'); res[v]['self'].append(r['selfint']=='no')
        res[v]['floor10'].append(fl(r['worst_dihedral'])>=10)
print("meshes",len(names),"metric",metric)
print("%-14s %8s %8s %8s %8s | %s"%("variant","tets/80","dev/80","time/80","tets/eq","valid selfint-free all>=10deg"))
for v in variants:
    R=res[v]
    print("%-14s %8.2f %8.2f %8.2f %8.2f | %d %d %d of %d"%(v,st.median(R['tets80']),st.median(R['dev80']),st.median(R['time80']),st.median(R['eff']),sum(R['valid']),sum(R['self']),sum(R['floor10']),len(names)))
if len(sys.argv)>3:
    print()
    for n in names:
        u=by[n]
        print(n,' '.join("%s:%s/%s"%(v.split('_')[0][:7],u[v]['tets'],u[v][metric]) for v in variants))

print()
print("absolute medians over meshes: tets, time_s, max-dev, p95-dev (diagonal), worst dihedral min, frac tets<10deg max")
for v in variants:
    rs=[by[n][v] for n in names]
    print("%-14s tets %7.0f time %6.2f max %.4f p95 %.4f  worst-dihedral-min %.2f  tets<10deg-max %.5f out_max-med %.4f"%(v,st.median(fl(r['tets']) for r in rs),st.median(fl(r['time_s']) for r in rs),st.median(fl(r['max']) for r in rs),st.median(fl(r['p95']) for r in rs),min(fl(r['worst_dihedral']) for r in rs),max(fl(r['frac_tets_lt10']) for r in rs),st.median(fl(r['out_max']) for r in rs)))
# wins vs uniform80: dev <= 1.0x and tets lower
for v in variants[3:]:
    w=sum(1 for n in names if fl(by[n][v][metric])<=fl(by[n]['uniform80'][metric])*1.0001 and fl(by[n][v]['tets'])<fl(by[n]['uniform80']['tets']))
    w2=sum(1 for n in names if fl(by[n][v][metric])<=fl(by[n]['uniform80'][metric])*1.1 and fl(by[n][v]['tets'])<0.8*fl(by[n]['uniform80']['tets']))
    print(v,"meshes with dev<=uniform80 and fewer tets:",w,"; dev<=1.1x uniform80 and tets<0.8x:",w2,"of",len(names))
