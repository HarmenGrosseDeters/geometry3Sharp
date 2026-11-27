"""Check original and mirrored Objaverse hierarchies with independent glTF geometry."""
import argparse
import copy
import json
import struct
from collections import Counter
from pathlib import Path

import numpy as np

from common import load
from corpus_extension import Run

UID='b3f6c576821c48f18aa292b16d7df01b'


def unpack_glb(path):
    raw=path.read_bytes()
    size=struct.unpack_from('<I',raw,12)[0]
    return json.loads(raw[20:20+size]),raw[28+size:],raw[20+size:]


def geometry(doc,binary):
    def read(index):
        a=doc['accessors'][index]
        if 'sparse' in a:
            raise ValueError('sparse accessor is outside this fixture')
        v=doc['bufferViews'][a['bufferView']]
        dtype=np.dtype({5123:'<u2',5125:'<u4',5126:'<f4'}[a['componentType']])
        width={'SCALAR':1,'VEC3':3}[a['type']]
        offset=v.get('byteOffset',0)+a.get('byteOffset',0)
        stride=v.get('byteStride',dtype.itemsize*width)
        return np.ndarray((a['count'],width),dtype=dtype,buffer=binary,offset=offset,
                          strides=(stride,dtype.itemsize))
    all_triangles=[]
    def visit(i,parent):
        n=doc['nodes'][i]
        if 'rotation' in n:
            raise ValueError('fixture uses matrices or translation/scale')
        local=np.array(n['matrix']).reshape((4,4),order='F') if 'matrix' in n else np.eye(4)
        if 'matrix' not in n:
            for axis,s in enumerate(n.get('scale',[1,1,1])):
                local[axis,axis]=s
            local[:3,3]=n.get('translation',[0,0,0])
        m=parent@local
        if 'mesh' in n:
            for prim in doc['meshes'][n['mesh']]['primitives']:
                assert prim.get('mode',4)==4
                pts=read(prim['attributes']['POSITION']).astype(np.float64)@m[:3,:3].T+m[:3,3]
                faces=read(prim['indices']).reshape((-1,3))
                if np.linalg.det(m[:3,:3])<0:
                    faces=faces[:,::-1]
                all_triangles.extend(pts[faces])
        for child in n.get('children',[]):
            visit(child,m)
    for root in doc['scenes'][doc.get('scene',0)]['nodes']:
        visit(root,np.eye(4))
    return np.array(all_triangles)


def oriented_keys(triangles,unit):
    keys=Counter()
    for tri in np.rint(triangles/unit).astype(np.int64):
        a,b,c=map(tuple,tri)
        keys[min((a,b,c),(b,c,a),(c,a,b))]+=1
    return keys


def main(args):
    run=Run(args)
    path=run.root/'objaverse'/f'{UID}.glb'
    doc,binary,last_chunk=unpack_glb(path)
    original=geometry(doc,binary)
    bounds=original.reshape(-1,3)
    diag=float(np.linalg.norm(np.ptp(bounds,axis=0)))
    shift=float(bounds[:,0].max()*2+diag)
    scene=doc['scenes'][doc.get('scene',0)]
    old_roots=list(scene['nodes'])
    old_nodes=copy.deepcopy(doc['nodes'])
    n=len(old_nodes)
    for node in old_nodes:
        if 'children' in node:
            node['children']=[i+n for i in node['children']]
    doc['nodes'].extend(old_nodes)
    mirrored_root=len(doc['nodes'])
    doc['nodes'].append({'children':[i+n for i in old_roots],'translation':[shift,0,0],'scale':[-1,1,1]})
    scene['nodes']=old_roots+[mirrored_root]
    encoded=json.dumps(doc,separators=(',',':')).encode()
    encoded+=b' ' * (-len(encoded)%4)
    derived=run.work/'mushroom-mirrored.glb'
    derived.write_bytes(struct.pack('<4sII',b'glTF',2,20+len(encoded)+len(last_chunk))+
                        struct.pack('<I4s',len(encoded),b'JSON')+encoded+last_chunk)
    expected=geometry(doc,binary)
    for name,input_path,reference in [('original',path,original),('mirrored',derived,expected)]:
        out=run.work/f'mushroom-{name}-hierarchy.obj'
        row=run.run('OBJ-b3f6c576',name,'convert',input_path,out)
        entry=dict(id=name,triangle_count_expected=len(reference),derived_translation_x=shift if name=='mirrored' else None)
        if row['status']=='ok':
            p,f=load(out)
            triangles=np.asarray(p)[np.asarray(f)]
            expected_keys=oriented_keys(reference,diag*1e-7)
            actual=oriented_keys(triangles,diag*1e-7)
            entry.update(triangle_count_observed=len(f),missing_oriented_triangles=sum((expected_keys-actual).values()),
                         extra_oriented_triangles=sum((actual-expected_keys).values()))
        run.report['cases'].append(entry)
        run.snapshot()


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for key in ('data','output','dotnet','cli'):
        parser.add_argument('--'+key,type=Path,required=True)
    args=parser.parse_args()
    args.corpus='scene-checks'
    main(args)
