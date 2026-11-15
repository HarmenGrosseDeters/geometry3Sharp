"""Generate procedural fixtures and exercise the independent .NET mesh pilot."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
pilot = root / 'benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py'
parser = argparse.ArgumentParser()
parser.add_argument('dotnet', help='path to dotnet executable')
parser.add_argument('dll', help='path to built BiharmonicCertificateArithmetic.dll')
args = parser.parse_args()
env = dict(os.environ, OPENBLAS_NUM_THREADS='1')


def run(command, expected, label):
    result = subprocess.run(command, cwd=root, env=env, capture_output=True, text=True)
    print(f'{label}: exit={result.returncode}')
    for line in result.stdout.splitlines():
        if line.startswith(('euclidean_', 'position_certificate=', 'mesh_total_ms=', 'status=uncertain')):
            print('  ' + line)
    if result.returncode != expected:
        print(result.stdout + result.stderr, file=sys.stderr)
        raise RuntimeError(label + ' returned unexpected exit code')


with tempfile.TemporaryDirectory(prefix='bih-euclidean-') as tmp:
    tmp = Path(tmp)
    grid, tiny, disconnected = (tmp / name for name in ('grid.json', 'tiny.json', 'disconnected.json'))
    run([sys.executable,str(pilot),'20','40','0.5','0','indexed',str(grid),'--motion=xyz','--euclidean'],0,'python xyz')
    run([args.dotnet,args.dll,'mesh','256',str(grid)],0,'dotnet xyz')
    run([sys.executable,str(pilot),'3','700','0.5','0','scalar-audit',str(tiny),'--fixture=tiny200','--wrong-known-candidate','--euclidean'],2,'python tiny200')
    run([args.dotnet,args.dll,'mesh','2048',str(tiny)],2,'dotnet tiny200')
    data=json.loads(grid.read_text())
    for key in ('vertices_bits','candidate_bits','target_bits'):
        data[key].append([0,0,0])
    data['fixed'].append(True)
    disconnected.write_text(json.dumps(data))
    run([args.dotnet,args.dll,'mesh','256',str(disconnected)],1,'dotnet disconnected')
