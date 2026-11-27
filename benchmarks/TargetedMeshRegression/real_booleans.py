"""Measure real Boolean pairs and published ThingiCSG arrangements."""
import argparse
from importlib.metadata import version
import hashlib
import json
import math
import re
import struct
import subprocess
from pathlib import Path

from common import json_write, load, mesh_stats, save
from corpus_extension import Run
from generate import box

EMBERS = (16,84,260,406,667)
CSG_REV = 'f362674851a06f783070ab8c09d7257c0d59e8f4'


def read_stl(path):
    data=path.read_bytes()
    count=int.from_bytes(data[80:84],'little')
    if len(data)==84+50*count:
        triangles=[v[3:12] for v in struct.iter_unpack('<12fH',data[84:])]
        vertices=[tri[i:i+3] for tri in triangles for i in (0,3,6)]
    else:
        vertices=[tuple(map(float,line.split()[1:])) for line in data.splitlines() if line.lstrip().startswith(b'vertex ')]
    points=list(dict.fromkeys(vertices))
    index={p:i for i,p in enumerate(points)}
    faces=[tuple(index[p] for p in vertices[i:i+3]) for i in range(0,len(vertices),3)]
    return points,faces


def read_off(path):
    lines=[line.split('#')[0].strip() for line in path.read_text().splitlines()]
    lines=[line for line in lines if line]
    signature, *counts = lines[0].split()
    if signature != 'OFF':
        raise ValueError(f'not OFF: {path}')
    start = 1 if counts else 2
    vertices,faces,*_=map(int,counts or lines[1].split())
    points=[tuple(map(float,line.split()[:3])) for line in lines[start:start+vertices]]
    triangles=[]
    for line in lines[start+vertices:start+vertices+faces]:
        n,*indices=map(int,line.split())
        triangles.extend((indices[0],indices[i],indices[i+1]) for i in range(1,n-1))
    return points,triangles


def transform(mesh,cols):
    pts,faces=mesh
    return [tuple(math.fsum([cols['col3'][axis],*(p[i]*cols[f'col{i}'][axis] for i in range(3))])
                  for axis in 'xyz') for p in pts],faces


def joined(meshes):
    points,faces=[],[]
    for vertices,triangles in meshes:
        faces.extend(tuple(i+len(points) for i in f) for f in triangles)
        points.extend(vertices)
    return points,faces


def manifold_reference(a,b):
    import manifold3d as mf
    import numpy as np
    def construct(mesh):
        m=mf.Manifold(mf.Mesh64(np.array(mesh[0],dtype=np.float64),np.array(mesh[1],dtype=np.uint64)))
        if m.status()!=mf.Error.NoError:
            raise ValueError(str(m.status()))
        return m
    A,B=map(construct,(a,b))
    return {op:result.volume() for op,result in [('union',A+B),('difference',A-B),('intersection',A^B)]}


class BooleanRun(Run):
    def data_for(self, label, mesh):
        output=self.work/f'{label}.obj'
        save(output,mesh)
        return output

    def compare(self, entry, op, inputs, timeout=25):
        name=entry['id']
        out=self.work/f'{name}-{op}.obj'
        extra=['--more',';'.join(map(str,inputs[2:]))] if len(inputs)>2 else []
        row=self.run(name,op,'boolean',inputs[0],inputs[1],out,'--op',op,*extra,timeout=timeout)
        if row['status']=='ok':
            mesh=mesh_stats(load(out))
            expected=entry.get('references',{}).get(op)
            check=dict(observed_volume=mesh['volume'],faces=mesh['faces'],components=mesh['components'],
                       expected_volume=expected)
            if expected is not None:
                check['absolute_volume_error']=abs(mesh['volume']-expected)
                normalizer=entry.get('operand_volume_scale') or abs(expected) or 1
                check['relative_to_operand_volume_error']=check['absolute_volume_error']/normalizer
            entry.setdefault('outputs',{})[op]=check
        self.snapshot()
        return out if row['status']=='ok' else None

    def ember(self):
        files=self.root/'ember'
        source=files/'cases.json'
        cases=json.loads(source.read_text())
        self.report['transform_manifest_sha256']=hashlib.sha256(source.read_bytes()).hexdigest()
        self.report['transform_source']='https://github.com/elalish/manifold/tree/master/extras/ember_tests'
        self.report['reference']=f'Manifold {version("manifold3d")}, Mesh64, transformed triangles'
        for i in EMBERS:
            row=cases[i]
            name=f'EMBER-{i:04d}'
            paths=[files/f'{row[f"id_{s}"]}.stl' for s in 'ab']
            if not all(p.exists() for p in paths):
                self.report['cases'].append(dict(id=name,status='missing_source',source=row))
                self.snapshot()
                continue
            meshes=[transform(read_stl(p),row[f'transform_{s}']) for p,s in zip(paths,'ab')]
            stats=[mesh_stats(m) for m in meshes]
            entry=dict(id=name,source=row,index_base=0,input_stats=stats,
                       source_sha256=[hashlib.sha256(p.read_bytes()).hexdigest() for p in paths],
                       operand_volume_scale=max(abs(s['volume']) for s in stats))
            self.report['cases'].append(entry)
            try:
                entry['references']=manifold_reference(*meshes)
                entry['overlap_fraction_of_smaller_operand']=entry['references']['intersection']/min(abs(s['volume']) for s in stats)
            except (ValueError,AttributeError,TypeError) as exc:
                entry['reference_error']=repr(exc)
            inputs=[self.data_for(f'{name}-{s}',m) for s,m in zip('ab',meshes)]
            for op in ('difference','intersection','union'):
                self.compare(entry,op,inputs)
            volumes=entry.get('outputs',{})
            if len(volumes)==3:
                va,vb=(m['volume'] for m in stats)
                u,i,d=(volumes[op]['observed_volume'] for op in ('union','intersection','difference'))
                entry['volume_identity_errors']=dict(union=abs(u+i-va-vb),difference=abs(d+i-va))
            self.snapshot()

    def csg(self):
        repo=self.root/'thingiCSG'
        self.report['thingicsg_revision']=CSG_REV
        directory=repo/'DATABASE/Basic'
        for label,file,op in [('two-cylinders','two_cylinders_1.scad','union'),
                              ('grid-difference','20x20.scad','difference'),
                              ('rotated-cubes','multi_rot_cube.scad','union'),
                              ('rotated-cubes-first20','multi_rot_cube.scad','union')]:
            name='CSG-'+label
            if self.args.case and name not in self.args.case:
                continue
            scad=directory/file
            oracle=self.work/f'{name}-oracle.off'
            entry=dict(id=name,source_file=file,source_url=f'https://github.com/BrunoLevy/thingiCSG/blob/{CSG_REV}/DATABASE/Basic/{file}')
            self.report['cases'].append(entry)
            try:
                if label=='rotated-cubes-first20':
                    # Preserve the source case's angular step while limiting the operand count.
                    source=self.work/f'{name}.scad'
                    source.write_text(scad.read_text().replace('for(i=[0:n-1])','for(i=[0:19])'))
                else:
                    source=scad
                p=subprocess.run(['openscad','-o',str(oracle),str(source)],capture_output=True,text=True,timeout=35)
                if p.returncode==0:
                    entry['openscad']=mesh_stats(read_off(oracle))
                else:
                    entry['openscad_error']=p.stderr[-1500:]
            except (ValueError,subprocess.TimeoutExpired) as exc:
                entry['openscad_error']=repr(exc)
            if label=='two-cylinders':
                pair=repo/'DATABASE/QuickCSG/TwoCylinders'
                # The committed operands include the published placement and rounded coordinates.
                meshes=[read_off(pair/'cyl1.off'),read_off(pair/'cyl2.off')]
                if any(mesh_stats(m)['volume']<0 for m in meshes):
                    meshes=[(points,[f[::-1] for f in faces]) if mesh_stats((points,faces))['volume']<0 else (points,faces)
                            for points,faces in meshes]
                try:
                    entry['references']=manifold_reference(*meshes)
                except (ValueError,AttributeError,TypeError) as exc:
                    entry['reference_error']=repr(exc)
            elif label=='grid-difference':
                w=0.025
                meshes=[joined(box((i/20,0,0),(i/20+w,1,w)) for i in range(20)),
                        joined(box((0,i/20,0),(1,i/20+w,w)) for i in range(20))]
                entry.update(references={'difference':0.00625},analytical_components=400)
            else:
                meshes=[]
                points,faces=box((-0.5,)*3,(0.5,)*3)
                for i in range(20 if label=='rotated-cubes-first20' else 70):
                    angle=i*math.pi/140
                    c,s=math.cos(angle),math.sin(angle)
                    meshes.append(([(c*x-s*y,s*x+c*y,z) for x,y,z in points],faces))
                if 'openscad' in entry:
                    entry['references']={'union':entry['openscad']['volume']}
            entry['operand_volume_scale']=sum(abs(mesh_stats(m)['volume']) for m in meshes)
            inputs=[self.data_for(f'{name}-{i:02d}',m) for i,m in enumerate(meshes)]
            self.compare(entry,op,inputs,timeout=self.args.csg_budget)
            self.snapshot()


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('corpus',choices=['ember','csg'])
    parser.add_argument('--data',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--dotnet',type=Path,required=True)
    parser.add_argument('--cli',type=Path,required=True)
    parser.add_argument('--case',action='append',default=[])
    parser.add_argument('--csg-budget',type=int,default=40)
    args=parser.parse_args()
    run=BooleanRun(args)
    getattr(run,args.corpus)()
    run.snapshot()
