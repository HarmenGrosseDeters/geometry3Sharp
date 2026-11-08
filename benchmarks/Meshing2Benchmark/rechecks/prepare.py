"""Prepare pinned public-data inputs without reading any reference implementation.
Coordinates/order are preserved; canonical hashes are checked before any run.
"""
import argparse
import hashlib
import json
import pathlib
import re
import struct
import subprocess
import urllib.request
import xml.etree.ElementTree as ET
from shapely import from_wkt, from_wkb
from run import input_name, job_key

def canonical(case):
 points=[]; ids={}; segments=[]
 def vertex(c):
  key=(float(c[0]),float(c[1]))
  if key not in ids: ids[key]=len(points);points.append(key)
  return ids[key]
 def geometry(g):
  if g.is_empty:return
  if g.geom_type=='Polygon':
   for ring in [g.exterior,*g.interiors]: geometry(ring)
  elif hasattr(g,'geoms'):
   for part in g.geoms:geometry(part)
  else:
   vertices=[vertex(c) for c in g.coords]
   for a,b in zip(vertices,vertices[1:]):
    if a!=b: segments.append((a,b))
 for name in ('a','b'):
  node=case.find(name)
  if node is None or not (node.text or '').strip():continue
  value=node.text.strip()
  geometry(from_wkb(bytes.fromhex(value)) if value[0] in '0123456789' else from_wkt(value))
 token=lambda v:'0x'+struct.pack('>d',v).hex()
 lines=['mesh2-input 1',f'points {len(points)}',*[token(x)+' '+token(y) for x,y in points],f'segments {len(segments)}',*[f'{a} {b} 0 0' for a,b in segments], 'rings 0','holeseeds 0','regions 0','min_angle_deg -','max_area -','steiner_limit -','snap_tolerance 0x0000000000000000','size_constant -','resolve_crossings 1','conforming 0','end']
 return '\n'.join(lines)+'\n'

def variant(base, job):
    if hashlib.sha256(base.encode()).hexdigest() != job['expected_base_hash']:
        raise ValueError('Historical base input hash differs: '+job['case'])
    if job['mode'] not in ('conforming', 'constrained') or job['angle'] not in (20,25,30):
        raise ValueError('Unknown historical mode/angle')
    text = re.sub(r'^min_angle_deg .*$', 'min_angle_deg 0x'+struct.pack('>d',job['angle']).hex(), base, flags=re.M)
    text = re.sub(r'^steiner_limit .*$', 'steiner_limit 200000',text,flags=re.M)
    if job['mode']=='constrained':
        if 'conforming 1\n' in text: raise ValueError('Constrained mode contradicts explicit conforming input')
        text=text.replace('end\n','mode constrained\nend\n')
    sha=hashlib.sha256(text.encode()).hexdigest()
    if job.get('expected_input_hash') and sha!=job['expected_input_hash']:
        raise ValueError('Historical variant input hash differs: '+job['case'])
    return text, sha


def prepare(args):
    spec=json.loads(args.manifest.read_text())
    args.data.mkdir(parents=True,exist_ok=True);args.inputs.mkdir(parents=True,exist_ok=True)
    trees={};golden={};seen=set();prepared={}
    for job in spec['jobs']:
        key=(job['case'],job['mode'])
        if key in seen:raise ValueError('Duplicate job: '+str(key))
        seen.add(key)
        pin=job.get('pin')
        if pin:
            name=pin['url'].split('/')[-1]
            path=args.data/name
            if not path.exists():
                if not pin['url'].startswith('https://raw.githubusercontent.com/libgeos/geos/'):
                    raise ValueError('Unexpected source for GEOS data')
                path.write_bytes(urllib.request.urlopen(pin['url'],timeout=60).read())
            if hashlib.sha256(path.read_bytes()).hexdigest()!=pin['sha256']:
                raise ValueError('Pinned source checksum differs: '+name)
            if name not in trees:trees[name]=ET.parse(path).getroot().findall('case')
            label,index=pin['parameters'].split('=')
            if label!='case' or not 1<=int(index)<=len(trees[name]):raise ValueError('Invalid XML case index')
            base=canonical(trees[name][int(index)-1])
        else:
            id=job['base_case']
            if id not in golden:
                path=args.inputs/('base-'+hashlib.sha256(id.encode()).hexdigest()[:16]+'.txt')
                subprocess.run([str(args.dotnet),str(args.benchmark),'case','--case',id,'--output',str(path)],check=True,capture_output=True,text=True)
                golden[id]=path.read_text()
            base=golden[id]
        text,sha=variant(base,job)
        (args.inputs/input_name(job['case'],job['mode'])).write_text(text)
        prepared[job_key(job)]=sha
    # Only case|mode -> input_sha256: everything else is in the historical manifest, and the
    # inputs directory (named by input_name) stays local, so no local path is recorded.
    args.out.write_text(json.dumps(prepared,indent=2)+'\n')
    print('Prepared',len(seen),'jobs;',sum(bool(j.get('expected_input_hash')) for j in spec['jobs']),'full historical variant hashes checked')


if __name__=='__main__':
    p=argparse.ArgumentParser()
    for key in ('manifest','data','inputs','out','dotnet','benchmark'):p.add_argument('--'+key,type=pathlib.Path,required=True)
    prepare(p.parse_args())
