"""Check curved CAD bores and Boolean cuts against Open Cascade solids."""
import argparse
import json
import math
from pathlib import Path

import cadquery as cq
from OCP.BRepAdaptor import BRepAdaptor_Surface

from common import json_write, load, mesh_stats, save
from corpus_extension import Run
from g3_run import contains
from generate import box


def inward_bore_points(shape):
    for face in shape.Faces():
        if face.geomType()!='CYLINDER':
            continue
        surface=BRepAdaptor_Surface(face.wrapped).Cylinder()
        loc=cq.Vector(surface.Location().Coord())
        axis=cq.Vector(surface.Axis().Direction().Coord())
        a,b,c,d=face.uvBounds()
        normal,point=face.normalAt((a+b)/2,(c+d)/2)
        delta=point-loc
        on_axis=loc+axis.multiply(delta.dot(axis))
        if normal.dot(point-on_axis)<0 and not shape.isInside(on_axis,1e-9):
            yield tuple(on_axis.toTuple()), surface.Radius()


def main(args):
    run=Run(args)
    measurements=json.loads((run.output/'abc.json').read_text())['commands']
    completed={(row['case'],row['stage']) for row in measurements if row['status']=='ok'}
    for model in ('00000177','00000382','00000342','00000325'):
        name=f'ABC-{model}'
        part=cq.importers.importStep(str(run.root/'abc'/f'{model}.step')).solids().val()
        bounds=part.BoundingBox()
        dx,dy,dz=bounds.xlen,bounds.ylen,bounds.zlen
        cut=bounds.xmin+0.431*dx
        (lo,hi)=((cut,bounds.ymin-dy,bounds.zmin-dz),(bounds.xmax+dx,bounds.ymax+dy,bounds.zmax+dz))
        tool=cq.Solid.makeBox(*(y-x for x,y in zip(lo,hi)),cq.Vector(lo))
        references={'intersection':part.intersect(tool).Volume(),'difference':part.cut(tool).Volume()}
        expected=part.Volume()
        # Axis-aligned cutter generated once per B-rep, shared by both tessellations.
        cutter=run.work/f'{name}-cutter.obj'
        save(cutter,box(lo,hi))
        entry=dict(id=name,reference_volumes=references,brep_volume=expected,cut_plane_x=cut,variants=[])
        run.report['cases'].append(entry)
        probes=list(inward_bore_points(part))
        for variant in ('coarse','fine'):
            path=run.work/f'{name}-{variant}.obj'
            wrap=run.work/f'{name}-{variant}-wrap.obj'
            solid=load(path)
            data=dict(name=variant,input_volume=mesh_stats(solid)['volume'])
            entry['variants'].append(data)
            if probes and (name,f'{variant}-wrap') in completed and wrap.is_file():
                outer=load(wrap)
                data['bore_axis_probes']=[dict(point=p,radius=r,input_inside=contains(solid,p),
                                              wrap_inside=contains(outer,p)) for p,r in probes]
            for op in ('intersection','difference'):
                out=run.work/f'{name}-{variant}-{op}.obj'
                row=run.run(name,f'{variant}-{op}','boolean',path,cutter,out,'--op',op)
                if row['status']=='ok':
                    value=mesh_stats(load(out))['volume']
                    data[op]=dict(volume=value,error_over_brep_volume=abs(value-references[op])/abs(expected))
            if all(op in data for op in references):
                data['mesh_volume_balance']=abs(sum(data[op]['volume'] for op in references)-data['input_volume'])
            run.snapshot()


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for key in ('data','output','dotnet','cli'):
        parser.add_argument('--'+key,type=Path,required=True)
    args=parser.parse_args()
    args.corpus='abc-checks'
    main(args)
