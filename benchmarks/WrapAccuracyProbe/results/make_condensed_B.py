import json,collections
by=collections.defaultdict(dict)
for l in open('results/spikeB_proc.jsonl'):
    r=json.loads(l)
    if 'variant' in r: by[(r['input'],r['alpha_den'],r['offset_rel'])][r['variant']]=r
def p(x): return '-' if not isinstance(x,(int,float)) or x!=x else '%.1f%%'%(100*x)
print('| input | hole share of the area | alpha | wrap added / lost | wrap vol / solid | patches (matched) | patch / hole area | snap added / lost | auto added / lost (verdict) | robust added / lost (path) |')
print('|---|---:|---|---|---:|---|---:|---|---|---|')
names=['hole_box_face','hole_box_p_0.01','hole_box_p_0.1','hole_box_p_0.3','hole_box_e_0.1','hole_box_slit','hole_cylinder_p_0.1','hole_sphere_p_0.01','hole_sphere_p_0.1','hole_sphere_p_0.3','sheet']
for n in names:
    for a in (4,20,100):
        v=by.get((n,a,1e-4))
        if not v or 'wrap' not in v or 'added_share' not in v['wrap']: continue
        w=v['wrap']; s=v['snap']; au=v['auto']; ro=v['robust']
        vr=w.get('vol_ratio'); vr='-' if not isinstance(vr,float) or vr!=vr else '%.2f'%vr
        pho=w.get('patch_over_hole_area'); pho='-' if not isinstance(pho,float) or pho!=pho else '%.2g'%pho
        print('| %s | %s | d/%d | %s / %s | %s | %d (%d) | %s | %s / %s | %s / %s (%s) | %s / %s (%s) |'%(n,p(w['hole_area_share']) if n!='sheet' else '-',a,p(w['added_share']),p(w['lost_share']),vr,w['patches'],w['patches_matched'],pho,p(s['added_share']),p(s['lost_share']),p(au.get('added_share',float('nan'))),p(au.get('lost_share',float('nan'))),au.get('verdict','-'),p(ro['added_share']),p(ro['lost_share']),ro.get('result_path','-')))
