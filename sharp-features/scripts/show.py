import json,sys
rows=[json.loads(l) for f in sys.argv[1:] for l in open(f)]
def g(r,k,p=2):
    v=r.get(k)
    return '-' if v is None else (f"{v:.{p}f}" if isinstance(v,float) else str(v))
print("file variant tris ms clean hitsIn vmin amin p01 >170 | ecd_off eF.1 crN | cvx_in cvx_mi(w1) ccv_in ccv_mi(w1) prec_mi(w3) | extra")
for r in rows:
    if r.get('status')!='ok': print(r['file'],r['variant'],r.get('status'),r.get('error','')); continue
    ex=' '.join(f"{k[2:]}={r[k]}" for k in r if k.startswith('k_') and k!='k_ms')
    print(r['file'][:14],r['variant'],r['tris'],r['ms'],r['clean'],g(r,'fin_cross_input') if 'fin_cross_input' in r else r['hits_input'],g(r,'vdist_min_over_offset'),g(r,'angle_min',1),g(r,'angle_p01',1),r['angle_gt170'],'|',
      f"{r['fm_ecd']*r['fm_size']/r['delta']:.2f}",g(r,'fm_ef_0001'),g(r,'crease_nd_d2_mean',1),'|',
      g(r,'ec_cvx_in_mean'),f"{g(r,'ec_cvx_mi_mean')}({g(r,'ec_cvx_mi_w1')})",g(r,'ec_ccv_in_mean'),f"{g(r,'ec_ccv_mi_mean')}({g(r,'ec_ccv_mi_w1')})",f"{g(r,'ec_prec_mi_mean')}({g(r,'ec_prec_mi_w3')})",'|',ex)
