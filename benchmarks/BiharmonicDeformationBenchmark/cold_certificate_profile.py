"""Five warmed-process, uncached Computes per fixture, with external accuracy checks."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

parser=argparse.ArgumentParser()
parser.add_argument('dotnet');parser.add_argument('producer');parser.add_argument('checker')
args=parser.parse_args()
for name,path in [('producer',Path(args.producer)),('checker',Path(args.checker)),('library',Path(args.producer).parent/'geometry3Sharp.dll')]:
 print(name+'_sha256='+hashlib.sha256(path.read_bytes()).hexdigest(),flush=True)
print('one warmup then five sequential fresh deformers; fixture generation excluded',flush=True)
with tempfile.TemporaryDirectory(prefix='bih-profile-') as tmp:
 outputs={}
 for orientation,mode in [('aligned','normal'),('aligned','strict'),('rotated','strict')]:
  path=Path(tmp)/(orientation+'-'+mode+'.json');print('case='+orientation+' '+mode,flush=True)
  r=subprocess.run([args.dotnet,args.producer,'--certificate-profile','20',orientation,mode,str(path)],capture_output=True,text=True)
  print(r.stdout+r.stderr,end='',flush=True)
  if r.returncode!=0 or 'status=Success' not in r.stdout:raise RuntimeError('failed compute not comparable')
  outputs[orientation,mode]=json.loads(path.read_text())
  digest=hashlib.sha256(path.read_bytes()).hexdigest();print('candidate_sha256='+digest,flush=True)
  c=subprocess.run([args.dotnet,args.checker,'mesh','256',str(path)],capture_output=True,text=True)
  for line in c.stdout.splitlines():
   if line.startswith(('euclidean_','position_certificate=')):print(line,flush=True)
  if c.returncode!=0 or 'position_certificate=within_tolerance' not in c.stdout or 'input_sha256='+digest not in c.stdout:
   raise RuntimeError('certificate not established for exported candidate')
 a=outputs['aligned','normal'];b=outputs['aligned','strict']
 for key in ('vertices_bits','faces','fixed','target_bits'):
  if a[key]!=b[key]:raise RuntimeError('inputs differ')
 print('aligned_candidate_bits_identical='+str(a['candidate_bits']==b['candidate_bits']))
