"""Compare new-deformer and same-instance strict certificate timings on changing targets."""
import argparse
import hashlib
from pathlib import Path
import subprocess
import tempfile

parser=argparse.ArgumentParser()
parser.add_argument('dotnet');parser.add_argument('producer');parser.add_argument('checker')
args=parser.parse_args()
for name,path in [('producer',Path(args.producer)),('checker',Path(args.checker)),('library',Path(args.producer).parent/'geometry3Sharp.dll')]:
 print(name+'_sha256='+hashlib.sha256(path.read_bytes()).hexdigest(),flush=True)
print('first_compute_including_jit is not a warmed cold/preparation-only metric',flush=True)
with tempfile.TemporaryDirectory(prefix='bih-prepared-profile-') as tmp:
 for command,orientation in [('--certificate-profile','aligned'),('--certificate-reuse-profile','aligned'),('--certificate-reuse-profile','rotated')]:
  path=Path(tmp)/(command+'-'+orientation+'.json')
  print('case='+command+' '+orientation+' strict',flush=True)
  r=subprocess.run([args.dotnet,args.producer,command,'20',orientation,'strict',str(path)],capture_output=True,text=True)
  print(r.stdout+r.stderr,end='',flush=True)
  if r.returncode or 'status=Success' not in r.stdout:raise RuntimeError('profile failed')
  digest=hashlib.sha256(path.read_bytes()).hexdigest();print('candidate_sha256='+digest,flush=True)
  c=subprocess.run([args.dotnet,args.checker,'mesh','256',str(path)],capture_output=True,text=True)
  for line in c.stdout.splitlines():
   if line.startswith(('euclidean_','position_certificate=')):print(line,flush=True)
  if c.returncode or 'position_certificate=within_tolerance' not in c.stdout or 'input_sha256='+digest not in c.stdout:
   raise RuntimeError('certificate failed')
