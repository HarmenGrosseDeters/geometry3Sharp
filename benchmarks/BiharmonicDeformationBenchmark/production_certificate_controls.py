"""Exercise the production deformer and independent interval verifier on generated grids."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile

root=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser()
parser.add_argument('dotnet');parser.add_argument('producer');parser.add_argument('checker')
args=parser.parse_args()
env=dict(os.environ)
for name in ('producer','checker'):
 path=Path(getattr(args,name)).resolve()
 print(f'{name}_sha256={hashlib.sha256(path.read_bytes()).hexdigest()}')
library=Path(args.producer).resolve().parent/'geometry3Sharp.dll'
print('library_sha256='+hashlib.sha256(library.read_bytes()).hexdigest())
print('exporter_source_sha256='+hashlib.sha256((root/'benchmarks/BiharmonicDeformationBenchmark/ProductionCandidateExport.cs').read_bytes()).hexdigest())
print('diagnostic_source_sha256='+hashlib.sha256((root/'benchmarks/BiharmonicDeformationBenchmark/ProductionAssemblyDiagnostic.cs').read_bytes()).hexdigest())
print('checker_source_sha256='+hashlib.sha256((root/'benchmarks/BiharmonicCertificateArithmetic/Program.cs').read_bytes()).hexdigest())
print('git_head='+subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip())
print('git_status_begin')
print(subprocess.check_output(['git','status','--short'],cwd=root,text=True).rstrip())
print('git_status_end')
print('dotnet_sdk='+subprocess.check_output([args.dotnet,'--version'],cwd=root,text=True).strip())


def run(command, expected, label, required=()):
 result=subprocess.run(command,cwd=root,env=env,capture_output=True,text=True)
 print(f'{label} exit={result.returncode}')
 for line in result.stdout.splitlines():
  if line.startswith(('vertices=', 'replayed_assembly_guard=', 'euclidean_', 'position_certificate=', 'mesh_total_ms=', 'status=uncertain','status=positive_interval_pivots')):print('  '+line)
 if result.returncode!=expected:
  print(result.stdout+result.stderr,file=sys.stderr)
  raise RuntimeError(label+' unexpected exit')
 for marker in required:
  if marker not in result.stdout: raise RuntimeError(label+' missing output: '+marker)


def bits(value):return struct.unpack('>q',struct.pack('>d',value))[0]
def number(raw):return struct.unpack('>d',struct.pack('>q',raw))[0]
with tempfile.TemporaryDirectory(prefix='bih-production-') as directory:
 directory=Path(directory)
 for n,orientation,mode,expected in [(4,'aligned','normal',0),(4,'rotated','normal',0),(4,'rotated','strict',0),(20,'aligned','normal',0),(20,'rotated','normal',1),(20,'rotated','strict',1)]:
  path=directory/f'{n}-{orientation}-{mode}.json'
  status='NumericalRange' if mode=='normal' else 'AccuracyNotEstablished'
  required=(f'mode={mode} orientation={orientation} status={status}',) if expected else (f'mode={mode} orientation={orientation} status=Success',)
  if expected and mode == 'normal': required += ('replayed_assembly_guard=row triangle=0 row=0 col=0',)
  run([args.dotnet,args.producer,'--certificate',str(n),orientation,mode,str(path)],expected,f'export {n} {orientation} {mode}',required)
  if expected:
   if path.exists():raise RuntimeError('failed solve wrote a candidate')
   continue
  digest=hashlib.sha256(path.read_bytes()).hexdigest();print('candidate_sha256='+digest)
  run([args.dotnet,args.checker,'mesh','256',str(path)],0,f'certificate {n} {orientation} {mode}',('euclidean_absolute=True euclidean_relative=True','position_certificate=within_tolerance','input_sha256='+digest))
  if n==20:
   data=json.loads(path.read_text())
   for axis in range(3):
    changed=json.loads(path.read_text());changed['candidate_bits'][1][axis]=bits(number(changed['candidate_bits'][1][axis])+0.01)
    changed_path=directory/f'bad-{axis}.json';changed_path.write_text(json.dumps(changed))
    changed_digest=hashlib.sha256(changed_path.read_bytes()).hexdigest()
    run([args.dotnet,args.checker,'mesh','256',str(changed_path)],2,f'changed axis {axis}',('position_certificate=not_established','input_sha256='+changed_digest))
   data['shift_bits']=bits(1e9);bad=directory/'bad-shift.json';bad.write_text(json.dumps(data))
   shift_digest=hashlib.sha256(bad.read_bytes()).hexdigest()
   run([args.dotnet,args.checker,'mesh','256',str(bad)],1,'excessive shift',('status=uncertain pivot=','input_sha256='+shift_digest))
   original=hashlib.sha256(path.read_bytes()).hexdigest()
   run([args.dotnet,args.producer,'--certificate',str(n),orientation,mode,str(path)],2,'existing export rejected')
   if hashlib.sha256(path.read_bytes()).hexdigest()!=original:raise RuntimeError('existing file changed')
