"""Run the targeted mesh cases through the geometry3Sharp CLI."""
import argparse
import itertools
import math
import os
from collections import Counter
from fractions import Fraction
from pathlib import Path
import json
import subprocess
import time

from common import cross, dot, json_write, load, mesh_stats, operands, sub

OPS = {
    'union': lambda a,b: a or b, 'intersection': lambda a,b: a and b,
    'difference_ab': lambda a,b: a and not b, 'difference_ba': lambda a,b: b and not a,
    'xor': lambda a,b: a != b,
}


def contains(mesh, point):
    points, faces = mesh
    angles = []
    for face in faces:
        a,b,c = [sub(points[index],point) for index in face]
        x,y,z = (math.sqrt(dot(v,v)) for v in (a,b,c))
        angles.append(math.atan2(dot(a,cross(b,c)), x*y*z+dot(a,b)*z+dot(b,c)*x+dot(c,a)*y))
    return abs(math.fsum(angles)) > math.pi


def box_cells(boxes, op):
    intervals = []
    for axis in range(3):
        cuts = sorted({point[axis] for box in boxes for point in box})
        intervals.append(list(zip(cuts, cuts[1:])))
    for cell in itertools.product(*intervals):
        point = [(lo+hi)/2 for lo,hi in cell]
        a,b = [all(lo<x<hi for lo,x,hi in zip(low,point,high)) for low,high in boxes]
        yield cell, point, OPS[op](a,b)


def box_probes(mesh, boxes, op):
    return [point for _,point,occupied in box_cells(boxes,op) if contains(mesh,point)!=occupied]


def box_bounds_match(mesh, boxes, op):
    """Check the occupied cells' outer extent as well as their interior samples."""
    occupied = [cell for cell,_,inside in box_cells(boxes,op) if inside]
    points, faces = mesh
    if not occupied:
        return not faces
    if not faces:
        return False
    points = [points[i] for i in {i for face in faces for i in face}]
    if not all(math.isfinite(value) for point in points for value in point):
        return False
    for axis in range(3):
        lo = min(cell[axis][0] for cell in occupied)
        hi = max(cell[axis][1] for cell in occupied)
        domain = max(box[1][axis] for box in boxes) - min(box[0][axis] for box in boxes)
        tolerance = max(1e-6*domain, 8*math.ulp(max(abs(lo),abs(hi))))
        if abs(min(p[axis] for p in points)-lo)>tolerance or abs(max(p[axis] for p in points)-hi)>tolerance:
            return False
    return True


def point_triangle_distance2(p, a, b, c):
    ab, ac, ap = sub(b,a),sub(c,a),sub(p,a)
    aa, bb, abac = dot(ab,ab),dot(ac,ac),dot(ab,ac)
    denominator = aa*bb-abac*abac
    if denominator > 0:
        apab, apac = dot(ap,ab),dot(ap,ac)
        x = (bb*apab-abac*apac)/denominator
        y = (aa*apac-abac*apab)/denominator
        if x >= 0 and y >= 0 and x+y <= 1:
            normal = cross(ab,ac)
            return dot(ap,normal)**2/dot(normal,normal)
    distances = []
    for x,y in ((a,b),(b,c),(c,a)):
        segment, delta = sub(y,x),sub(p,x)
        length2 = dot(segment,segment)
        t = min(1,max(0,dot(delta,segment)/length2)) if length2 else 0
        diff = tuple(d-t*v for d,v in zip(delta,segment))
        distances.append(dot(diff,diff))
    return min(distances)


def sampled_distance(left,right):
    def one_sided(src,dst):
        points,faces = src
        targets = [[dst[0][i] for i in face] for face in dst[1]]
        probes = list(points)
        for face in faces:
            a,b,c = [points[i] for i in face]
            probes.append(tuple((x+y+z)/3 for x,y,z in zip(a,b,c)))
        return math.sqrt(max(min(point_triangle_distance2(p,*tri) for tri in targets) for p in probes))
    return max(one_sided(left,right),one_sided(right,left))


class Benchmark:
    def __init__(self, args):
        self.args = args
        self.fixtures = args.fixtures.resolve()
        self.root = args.output.resolve()
        self.root.mkdir(parents=True,exist_ok=True)
        self.mesh_dir = self.root/'meshes'
        self.mesh_dir.mkdir(exist_ok=True)
        self.rows = []
        self.env = {**os.environ, 'DOTNET_PROCESSOR_COUNT': '2'}

    def run(self, case, check, *argv, output=None, timeout=None):
        timeout = timeout or self.args.timeout
        argv = [str(arg) for arg in argv]
        start = time.monotonic()
        row = dict(case=case, check=check, argv=argv, timeout_seconds=timeout)
        if output is not None:
            if output.exists():
                output.unlink()
            row['output'] = str(output.relative_to(self.root))
        try:
            p = subprocess.run([str(self.args.dotnet),str(self.args.cli),*argv,'--compact'],
                               capture_output=True,text=True,timeout=timeout,env=self.env)
            row.update(exit_code=p.returncode,wall_seconds=time.monotonic()-start)
            if p.stderr:
                row['stderr'] = p.stderr[-4000:]
            try:
                row['cli'] = json.loads(p.stdout)
                row['status'] = 'ok' if p.returncode == 0 and row['cli'].get('status')=='ok' else 'cli_error'
            except json.JSONDecodeError:
                row.update(status='process_error',stdout=p.stdout[-4000:])
        except subprocess.TimeoutExpired:
            row.update(status='timeout',wall_seconds=time.monotonic()-start)
        self.rows.append(row)
        return row

    def checkpoint(self):
        json_write(self.root/'results.json',self.rows)

    def path(self,case,extension='obj'):
        return self.mesh_dir/f'{case}.{extension}'

    def wrap(self, case):
        name = case['id']
        path = self.fixtures/case['inputs']['input']
        points, _ = load(path)
        diagonal = math.hypot(*(max(axis)-min(axis) for axis in zip(*points)))
        configs = {'baseline': (diagonal/50,diagonal/1500)}
        measure = case['measure'] if isinstance(case['measure'],dict) else {}
        probe = measure.get('probe',measure.get('cavity_probe'))
        if name=='W02-open-box':
            probe = [0.5,0.5,0.5]
        width = measure.get('gap') or 2*measure.get('bore_radius',0)
        if self.args.sweep and case['sweep']:
            configs.update({f'alpha-D{n}':(diagonal/n,diagonal/1500) for n in (20,100,200)})
            configs.update({f'offset-alpha{n}':(diagonal/50,diagonal/50/n) for n in (100,10)})
            if width:
                configs.update({f'local-{i}':(ratio*width,width/80)
                                for i,ratio in enumerate((1/8,1/4,1/2,1,2))})
        if self.args.transition and width:
            configs.update({'below_width':(width/4,width/40),'above_width':(width,width/40)})
        unknown = {config for case_id, config in self.args.job if case_id==name} - configs.keys()
        if unknown:
            raise ValueError(f'{name}: unknown wrap configurations {sorted(unknown)}')
        for config,(alpha,offset) in configs.items():
            if self.args.job and (name,config) not in self.args.job:
                continue
            out = self.path(f'{name}-{config}')
            row = self.run(name,config,'wrap',path,out,'--alpha',alpha,'--offset',offset,output=out)
            if row['status']=='ok':
                metrics = row['cli']['mesh']
                row['status'] = 'topology_checks_pass' if metrics['clean'] and metrics['volume']>0 else 'wrap_topology_issue'
                if probe is not None:
                    row['probe_inside'] = contains(load(out),probe)
            self.checkpoint()

    def repair(self, case):
        name = case['id']
        out = self.path(name)
        row = self.run(name,'repair','repair',self.fixtures/case['inputs']['input'],out,output=out)
        if row['status']!='ok':
            return
        mesh = load(out)
        stats = mesh_stats(mesh)
        row['output_stats'] = stats
        if not row['cli']['mesh']['clean']:
            row['status'] = 'invalid_topology'
            return
        refs = operands(case,self.fixtures)
        reference = refs.get('reference')
        if reference is None or name=='R10-coplanar-shells':
            row['status'] = 'policy_dependent'
            return
        ref = mesh_stats(reference)
        distance = sampled_distance(mesh,reference)
        row['sampled_symmetric_distance'] = distance
        if distance<=1e-6 and stats['components']==ref['components'] and abs(stats['volume']-ref['volume'])<=1e-6:
            row['status'] = 'checks_pass'
        else:
            row['status'] = 'reference_changed'

    def boolean(self, case):
        name = case['id']
        a,b = [self.fixtures/case['inputs'][key] for key in ('a','b')]
        for op in OPS:
            out = self.path(f'{name}-{op}')
            left,right = (b,a) if op=='difference_ba' else (a,b)
            row = self.run(name,op,'boolean',left,right,out,'--op',op.split('_')[0],output=out)
            expected = float(Fraction(case['regularized_volumes'][op]))
            row['expected_volume'] = expected
            if row['status']!='ok':
                continue
            mesh = load(out)
            stats = mesh_stats(mesh)
            row['output_stats'] = stats
            row['volume_ok'] = abs(stats['volume']-expected)<=max(case.get('scale',1)**3*1e-14,abs(expected)*1e-6)
            row['empty_ok'] = bool(stats['faces']) == (op not in case['empty_operations'])
            row['cell_failures'] = box_probes(mesh,case['reference_boxes'],op) if 'reference_boxes' in case else None
            metrics = row['cli']['mesh']
            row['bounds_ok'] = box_bounds_match(mesh,case['reference_boxes'],op) if 'reference_boxes' in case else None
            if not row['volume_ok'] or not row['empty_ok'] or row['cell_failures'] or row['bounds_ok'] is False:
                row['status'] = 'wrong_geometry'
            elif not stats['faces'] or metrics['clean']:
                row['status'] = 'checks_pass'
            elif (op=='xor' or (op=='union' and case['contact'] in ('edge','point'))) and row['cli']['boolean']['rounding_clean']:
                row['status'] = 'contact_topology'
            else:
                row['status'] = 'invalid_topology'
            self.checkpoint()

    def pipeline(self, case):
        name = case['id']
        a,b = [self.fixtures/case['inputs'][key] for key in ('a','b')]

        def run(check,op,left,right):
            out = self.path(f'{name}-{check}')
            row = self.run(name,check,'boolean',left,right,out,'--op',op,output=out)
            return out if row['status']=='ok' else None

        if name.startswith('C01'):
            repaired = self.path(f'{name}-repair')
            repair = self.run(name,'repair_cutter','repair',b,repaired,output=repaired)
            control = run('control','difference',a,self.fixtures/case['inputs']['repair_reference_b'])
            result = run('actual','difference',a,repaired) if repair['status']=='ok' else None
            expected = float(Fraction(case['repair_then_difference_volume']))
        else:
            c = self.fixtures/case['inputs']['c']
            ab = run('ab','difference',a,b)
            ac = run('ac','union',a,c)
            bc = run('bc','difference',b,c)
            control = run('left','union',ab,c) if ab else None
            result = run('right','difference',ac,bc) if ac and bc else None
            expected = float(Fraction(case['regularized_volume']))
        summary = dict(case=name,check='comparison',status='no_output',expected_volume=expected)
        if control and result:
            ref, actual = load(control), load(result)
            stats = [mesh_stats(m) for m in (ref,actual)]
            distance = sampled_distance(ref,actual) if all(s['faces'] for s in stats) else None
            counts = case.get('solid_components',stats[0]['components'])
            clean = all(not any(s[key] for key in ('boundary_edges','nonmanifold_edges',
                        'inconsistent_edges','disconnected_vertex_links','degenerate_faces')) for s in stats)
            passed = clean and distance is not None and distance<=1e-6 and all(
                s['components']==counts and abs(s['volume']-expected)<=1e-6 for s in stats)
            summary.update(status='checks_pass' if passed else 'wrong_geometry',outputs=stats,
                           sampled_symmetric_distance=distance)
        self.rows.append(summary)

    def importer(self, case):
        name = case['id']
        row = self.run(name,'measure_glb','measure',self.fixtures/case['inputs']['glb'])
        if row['status']=='ok':
            m = row['cli']['mesh']
            passed = (m['clean'] and m['triangles']==case['expected_faces'] and
                      m['components']==case['expected_components'] and
                      [m['bounds']['min'],m['bounds']['max']]==case['expected_bbox'] and m['volume']==2)
            if passed:
                converted = self.path(f'{name}-imported')
                exported = self.run(name,'convert_glb','convert',self.fixtures/case['inputs']['glb'],
                                    converted,output=converted)
                if exported['status']=='ok':
                    actual = load(converted)
                    reference = load(self.fixtures/case['inputs']['reference'])
                    row['sampled_symmetric_distance'] = sampled_distance(actual,reference) if actual[1] else None
                    passed = row['sampled_symmetric_distance'] is not None and row['sampled_symmetric_distance']<=1e-6
                else:
                    passed = False
            row['status'] = 'checks_pass' if passed else 'wrong_geometry'

    def export(self):
        for name in ('B13-far-origin','B14-rational-cut'):
            if self.args.case and name not in self.args.case:
                continue
            obj = self.path(f'{name}-intersection')
            if not obj.exists():
                continue
            stl = self.path(f'{name}-intersection','stl')
            row = self.run(name,'stl_export','convert',obj,stl,output=stl)
            if row['status']=='ok':
                reimport = self.run(name,'stl_reimport','measure',stl)
                if reimport['status']!='ok':
                    continue
                stats = reimport['cli']['mesh']
                expected = mesh_stats(load(obj))['volume']
                reimport['expected_volume'] = expected
                if stats['volume'] is not None:
                    reimport['volume_error'] = abs(stats['volume']-expected)
                reimport['status'] = ('checks_pass' if stats['clean'] and stats['volume'] is not None
                                      and reimport['volume_error'] <= abs(expected)*1e-6
                                      else 'roundtrip_geometry_loss')

    def thingi(self):
        for id in ('356074','996816','101633'):
            path = self.args.thingi/f'{id}.stl'
            if not path.exists():
                continue
            for command,options in [('wrap',('--relative-alpha',50,'--relative-offset',1500)),('repair',())]:
                out = self.path(f'T{id}-{command}')
                self.run(f'T{id}',command,command,path,out,*options,output=out,
                         timeout=self.args.thingi_timeout)
                self.checkpoint()


if __name__=='__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet',type=Path,required=True)
    parser.add_argument('--cli',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--fixtures',type=Path,required=True)
    parser.add_argument('--timeout',type=int,default=25)
    parser.add_argument('--sweep',action='store_true')
    parser.add_argument('--transition',action='store_true')
    parser.add_argument('--case',action='append',default=[])
    parser.add_argument('--job',action='append',nargs=2,metavar=('CASE','CONFIG'),default=[])
    parser.add_argument('--thingi',type=Path)
    parser.add_argument('--thingi-timeout',type=int,default=25)
    parser.add_argument('--only',choices=['wrap','repair','boolean','pipeline','import','thingi'])
    args = parser.parse_args()
    if args.only=='thingi' and not args.thingi:
        parser.error('--only thingi requires --thingi with the input directory')
    args.job = {tuple(job) for job in args.job}
    bench = Benchmark(args)
    cases = json.loads((bench.fixtures/'manifest.json').read_text())['cases']
    selected = [case for case in cases if args.only in (None,case['suite'])]
    unknown_cases = set(args.case) - {case['id'] for case in selected}
    if unknown_cases:
        parser.error(f'unknown cases for this suite: {sorted(unknown_cases)}')
    wrap_ids = {case['id'] for case in selected if case['suite']=='wrap' and
                (not args.case or case['id'] in args.case)}
    unknown_jobs = {case_id for case_id,_ in args.job} - wrap_ids
    if unknown_jobs:
        parser.error(f'unknown wrap cases for --job: {sorted(unknown_jobs)}')
    for case in selected:
        suite = case['suite']
        if (args.only is None or suite==args.only) and (not args.case or case['id'] in args.case):
            phases = {'wrap': bench.wrap,'repair': bench.repair,'boolean': bench.boolean,
                      'pipeline': bench.pipeline,'import': bench.importer}
            phases[suite](case)
            bench.checkpoint()
    if args.only in (None,'boolean'):
        bench.export()
    if args.thingi and args.only in (None,'thingi'):
        bench.thingi()
    bench.checkpoint()
    print(Counter(row['status'] for row in bench.rows))
