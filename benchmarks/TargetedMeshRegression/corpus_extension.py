"""Run compact ABC and Objaverse cohorts through the production geometry3Sharp CLI."""
import argparse
import gzip
import hashlib
import json
import math
import os
import subprocess
import time
from pathlib import Path

from common import cross, dot, json_write, load, mesh_stats, save, sub

ABC = {
    '00000177': 'thin plate with inward cylindrical walls',
    '00000960': 'very thin planar part',
    '00000382': 'conical transitions and toroidal fillets',
    '00000342': 'B-spline and surfaces of revolution',
    '00000325': 'planar and sharp-edged control',
    '00000684': 'small faces mixed with several curved surface types',
}
OBJAVERSE = {
    '263c5c3fd3ba4770bf580bc1a89c4cc3': 'thin paper geometry with 75 primitives',
    '02543290444e41a3babfbb894e7d0909': 'room with many surfaces and contact regions',
    'b3f6c576821c48f18aa292b16d7df01b': 'multipart object with two materials',
    '37176fdd7c924e56b04c319e1f251cbd': 'scan with open boundaries and degenerate faces',
    '04d9983ba04242fe99182d4506ea0714': 'open seashell with overlapping regions',
}
OBJAVERSE_REV = '556637099bf4fa79ea7b239d0d9c328b8a2e9ac8'
ABC_MIRROR = 'https://concept-collection.github.io/abc-step-1000/'


def weld(mesh, tolerance=0):
    points, faces = mesh
    unique, remap, index = [], [], {}
    for p in points:
        key = tuple(round(x/tolerance) for x in p) if tolerance else p
        if key not in index:
            index[key] = len(unique)
            unique.append(p)
        remap.append(index[key])
    return unique,[tuple(remap[i] for i in f) for f in faces]


def file_hash(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Run:
    def __init__(self, args):
        self.args = args
        self.root = args.data.resolve()
        self.output = args.output.resolve()
        self.output.mkdir(parents=True,exist_ok=True)
        self.work = self.root/'derived'
        self.work.mkdir(exist_ok=True)
        self.report = {'cases': [], 'commands': []}

    def snapshot(self):
        json_write(self.output/f'{self.args.corpus}.json',self.report)

    def run(self, case, stage, *args, timeout=25):
        argv = [str(x) for x in args]
        output_index = {'wrap': 2, 'repair': 2, 'convert': 2, 'boolean': 3}.get(argv[0])
        output = Path(argv[output_index]) if output_index is not None else None
        if output is not None:
            output.unlink(missing_ok=True)
        t = time.monotonic()
        row = {'case':case,'stage':stage,'argv':argv,'timeout_seconds':timeout}
        try:
            p = subprocess.run([str(self.args.dotnet),str(self.args.cli),*argv,'--compact'],
                               capture_output=True,text=True,timeout=timeout,
                               env={**os.environ,'DOTNET_PROCESSOR_COUNT':'2'})
            row.update(returncode=p.returncode,cli=json.loads(p.stdout))
            row['status'] = row['cli']['status']
            if row['status']=='ok' and (p.returncode or (output is not None and not output.is_file())):
                row['status']='process_error'
        except subprocess.TimeoutExpired:
            row['status']='timeout'
        except json.JSONDecodeError:
            row.update(status='process_error',stderr=p.stderr[-1500:],stdout=p.stdout[-1500:])
        if row['status']!='ok' and output is not None:
            output.unlink(missing_ok=True)
        row['wall_seconds']=time.monotonic()-t
        self.report['commands'].append(row)
        self.snapshot()
        return row

    def abc(self):
        import cadquery as cq
        self.report['cadquery_version']=cq.__version__
        index={x['id']:x for x in json.loads((self.root/'abc/index.json').read_text())['files']}
        screen={x['id']:x for x in json.loads((self.root/'abc/screen.json').read_text())}
        for model,feature in ABC.items():
            path=self.root/'abc'/f'{model}.step'
            name=f'ABC-{model}'
            source=cq.importers.importStep(str(path)).solids().val()
            if not source.isValid():
                raise ValueError(f'invalid STEP {model}')
            diagonal=source.BoundingBox().DiagonalLength
            shell=cq.Compound.makeCompound(source.Faces())
            entry=dict(id=name,feature=feature,source_url=ABC_MIRROR+index[model]['path'],
                       step_sha256=file_hash(path),brep=screen[model],resolutions=[])
            self.report['cases'].append(entry)
            for variant,relative,angle in [('coarse',0.02,0.45),('fine',0.001,0.12)]:
                fresh=cq.importers.importStep(str(path)).solids().val()
                pts,tri=fresh.tessellate(diagonal*relative,angle)
                mesh=weld(([tuple(p.toTuple()) for p in pts],tri),diagonal*1e-10)
                ref=self.work/f'{name}-{variant}.obj'
                save(ref,mesh)
                resolution=dict(name=variant,linear_deflection_over_diagonal=relative,angular_deflection_rad=angle,
                                input_stats=mesh_stats(mesh),derived_sha256=file_hash(ref))
                entry['resolutions'].append(resolution)
                self.run(name,f'{variant}-measure','measure',ref)
                wrap=self.work/f'{name}-{variant}-wrap.obj'
                result=self.run(name,f'{variant}-wrap','wrap',ref,wrap,'--relative-alpha',50,'--relative-offset',1500)
                if result['status']=='ok':
                    points,_=load(wrap)
                    # A deterministic spread of sampled wrap vertices, with distance to CAD faces.
                    samples=[points[i*len(points)//48] for i in range(48)]
                    distances=[cq.Vertex.makeVertex(*p).distance(shell)/diagonal for p in samples]
                    resolution['wrap_to_brep_sample_max_over_diagonal']=max(distances)
                    resolution['wrap_to_brep_sample_count']=len(samples)
                    resolution['wrap_volume_relative_to_brep']=mesh_stats(load(wrap))['volume']/source.Volume()
                if variant=='coarse':
                    points,triangles=mesh
                    def area2(triangle):
                        a,b,c=[points[i] for i in triangle]
                        n=cross(sub(b,a),sub(c,a))
                        return dot(n,n)
                    remove=max(range(len(triangles)),key=lambda i:area2(triangles[i]))
                    damaged=self.work/f'{name}-hole.obj'
                    save(damaged,(points,triangles[:remove]+triangles[remove+1:]))
                    repaired=self.work/f'{name}-repaired.obj'
                    entry['removed_triangle']=remove
                    result=self.run(name,'repair-hole','repair',damaged,repaired)
                    if result['status']=='ok':
                        before=mesh_stats(mesh)['volume']
                        after=mesh_stats(load(repaired))['volume']
                        entry['repair_volume_relative_error_to_intact_coarse']=abs(after-before)/abs(before)
                self.snapshot()

    def objaverse(self):
        metadata=json.load(gzip.open(self.root/'objaverse/metadata.json.gz'))
        screen={x['uid']:x for x in json.loads((self.root/'objaverse/screen.json').read_text())}
        for uid,feature in OBJAVERSE.items():
            record=metadata[uid]
            path=self.root/'objaverse'/f'{uid}.glb'
            name=f'OBJ-{uid[:8]}'
            entry=dict(id=name,asset_uid=uid,name=record['name'],feature=feature,license=record['license'],
                       author=(record.get('user') or {}).get('displayName'),source_url=record['viewerUrl'],
                       download_url=f'https://huggingface.co/datasets/allenai/objaverse/resolve/{OBJAVERSE_REV}/glbs/000-000/{uid}.glb',
                       sha256=file_hash(path),scene={key:val for key,val in screen[uid].items() if key!='info'})
            self.report['cases'].append(entry)
            self.run(name,'raw-measure','measure',path)
            obj=self.work/f'{name}-imported.obj'
            if self.run(name,'convert','convert',path,obj)['status']!='ok':
                continue
            raw=load(obj)
            fixed=weld(raw)
            entry['raw_export_stats']=mesh_stats(raw)
            entry['exact_position_weld_stats']=mesh_stats(fixed)
            welded=self.work/f'{name}-welded.obj'
            save(welded,fixed)
            self.run(name,'welded-measure','measure',welded)
            for tag,source in [('raw',path),('welded',welded)]:
                self.run(name,f'{tag}-repair','repair',source,self.work/f'{name}-{tag}-repair.obj')
            self.run(name,'raw-wrap','wrap',path,self.work/f'{name}-wrap.obj','--relative-alpha',50,'--relative-offset',1500)
            self.snapshot()


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('corpus',choices=['abc','objaverse'])
    parser.add_argument('--data',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--dotnet',type=Path,required=True)
    parser.add_argument('--cli',type=Path,required=True)
    args=parser.parse_args()
    run=Run(args)
    getattr(run,args.corpus)()
    run.snapshot()
