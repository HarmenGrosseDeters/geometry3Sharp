"""Compare strict production cold cost and exact output bytes across two libraries.

Build the preceding producer/library and current producer separately. Each
process warms up once, then measures five fresh deformers on a procedural mesh.
This measures complete Compute, including certificate work and allocation/GC.
No mesh is committed. Run without concurrent task builds/profiles.
"""
import argparse
import hashlib
from pathlib import Path
import subprocess
import tempfile

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('dotnet');parser.add_argument('baseline_producer');parser.add_argument('producer')
args=parser.parse_args()
for name in ('baseline_producer','producer'):
    path=Path(getattr(args,name)).resolve()
    print(name+'_sha256='+hashlib.sha256(path.read_bytes()).hexdigest(),flush=True)
    print(name+'_library_sha256='+hashlib.sha256((path.parent/'geometry3Sharp.dll').read_bytes()).hexdigest(),flush=True)
with tempfile.TemporaryDirectory(prefix='bih-row-production-') as directory:
    for case,orientation in enumerate(('aligned','rotated')):
        order=[('baseline',args.baseline_producer),('indexed',args.producer)]
        if case%2:order.reverse()
        outputs=[]
        for label,producer in order:
            output=Path(directory)/f'{label}-{orientation}.json'
            command=[args.dotnet,producer,'--certificate-profile','50',orientation,'strict',str(output)]
            result=subprocess.run(command,capture_output=True,text=True,timeout=120)
            print(f'orientation={orientation} library={label} exit={result.returncode}',flush=True)
            print(result.stdout+result.stderr,end='',flush=True)
            if result.returncode or 'status=Success accuracy=Certified' not in result.stdout:
                raise RuntimeError('production profile failed')
            print('candidate_sha256='+hashlib.sha256(output.read_bytes()).hexdigest(),flush=True)
            outputs.append(output.read_bytes())
        assert outputs[0]==outputs[1], 'production output metadata or bits changed'
        print(f'orientation={orientation} production_export_bits_identical=True',flush=True)
