"""Compare roundoff bounds on represented rotated-grid interval matrices.

The bounds-only mode reconstructs L/M/Q from original binary64 metadata, but
runs no Cholesky and certifies no position. Small full runs exercise both scalar
criteria. Generated fixtures stay in a temporary directory.
"""
import argparse
import hashlib
from pathlib import Path
import re
import subprocess
import tempfile

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('dotnet');parser.add_argument('producer');parser.add_argument('checker')
args=parser.parse_args()
for name in ('producer','checker'):
    path=Path(getattr(args,name)).resolve()
    print(name+'_sha256='+hashlib.sha256(path.read_bytes()).hexdigest(),flush=True)

def run(command):
    result=subprocess.run(command,capture_output=True,text=True,timeout=90)
    print(result.stdout+result.stderr,end='',flush=True)
    if result.returncode:raise RuntimeError('command failed: '+str(command))
    return result.stdout

with tempfile.TemporaryDirectory(prefix='bih-envelope-') as tmp:
    for n in (20,50):
        path=Path(tmp)/f'small-{n}.json'
        run([args.dotnet,args.producer,'--certificate-structure',str(n),'solver','0.125','rotated',str(path)])
        for mode in ('mesh-scalar','mesh-envelope'):
            print(f'grid={n} matrix_mode={mode}',flush=True)
            output=run([args.dotnet,args.checker,mode,'256',str(path)])
            assert 'status=positive_scalar_pivots' in output and 'position_certificate=not_checked' in output
    for nx,ny in ((100,100),(200,125),(250,200),(400,250)):
        path=Path(tmp)/f'{nx}x{ny}.json'
        print(f'grid={nx}x{ny} scope=represented_binary64_bounds_only',flush=True)
        run([args.dotnet,args.producer,'--certificate-growth',f'{nx}x{ny}','solver','0.0001','rotated',str(path)])
        digest=hashlib.sha256(path.read_bytes()).hexdigest()
        output=run([args.dotnet,args.checker,'mesh-envelope-bounds','256',str(path)])
        assert 'input_sha256='+digest in output and 'status=matrix_bounds_only' in output
        assert 'position_certificate=not_checked' in output
        envelope=int(re.search(r'(?<!global_)correction_units=(\d+)',output)[1])
        global_bound=int(re.search(r'global_correction_units=(\d+)',output)[1])
        assert 0<envelope<=global_bound
        print(f'envelope_over_global={envelope/global_bound:.9g} position_certificate=not_checked',flush=True)
        path.unlink()
