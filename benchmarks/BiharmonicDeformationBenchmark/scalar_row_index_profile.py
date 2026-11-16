"""Compare indexed/scan scalar recurrences with identical inputs and operation traces.

Timings are fresh processes including JIT and phase allocations/GC. Phase
allocation counters exclude setup, input reconstruction and JSON parsing. This
is scalar matrix verification, not a production deformation cost/certificate.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import statistics
import subprocess
import tempfile
from scalar_certificate_controls import audit

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('dotnet');parser.add_argument('producer');parser.add_argument('checker')
args=parser.parse_args()
for name in ('producer','checker'):
    print(name+'_sha256='+hashlib.sha256(Path(getattr(args,name)).read_bytes()).hexdigest(),flush=True)

def run(command):
    result=subprocess.run(command,capture_output=True,text=True,timeout=30)
    if result.returncode:raise RuntimeError(result.stdout+result.stderr)
    return result.stdout

with tempfile.TemporaryDirectory(prefix='bih-row-index-') as directory:
    directory=Path(directory)
    for n in (3,5,7):
        fixture=directory/f'control-{n}.json'
        run([args.dotnet,args.producer,'--certificate-structure',str(n),'solver','0.125','rotated',str(fixture)])
        traces=[]
        for mode in ('mesh-envelope-scan','mesh-envelope'):
            trace=directory/f'{n}-{mode}.jsonl'
            run([args.dotnet,args.checker,mode,'256',str(fixture),str(trace)])
            records=[json.loads(line) for line in trace.read_text().splitlines()]
            assert records[0]['input_sha256']==hashlib.sha256(fixture.read_bytes()).hexdigest()
            audit(records)
            traces.append(trace.read_bytes())
        assert traces[0]==traces[1], 'complete scalar trace changed'
        print(f'grid={n} exact_audit=True full_trace_bits_identical=True',flush=True)
    for n in (20,50):
        fixture=directory/f'profile-{n}.json'
        run([args.dotnet,args.producer,'--certificate-structure',str(n),'solver','0.125','rotated',str(fixture)])
        digest=hashlib.sha256(fixture.read_bytes()).hexdigest()
        samples={mode:[] for mode in ('mesh-envelope-scan','mesh-envelope')}
        for repetition in range(5):
            modes=list(samples)
            if repetition%2:modes.reverse()
            for mode in modes:
                print(f'grid={n} repetition={repetition+1} mode={mode} input_sha256={digest}',flush=True)
                output=run([args.dotnet,args.checker,mode,'256',str(fixture)])
                assert 'status=positive_scalar_pivots' in output and 'input_sha256='+digest in output
                assert 'position_certificate=not_checked' in output
                print(output,end='',flush=True)
                row={name:float(re.search(r'(?<![A-Za-z_])'+name+r'=([0-9.]+)',output)[1]) for name in
                     ('symbolic_ms','numeric_ms','elapsed_ms','mesh_total_ms')}
                samples[mode].append(row)
        for mode,rows in samples.items():
            print(f'summary grid={n} mode={mode} runs=5 '+ ' '.join(f'{name}_median={statistics.median(row[name] for row in rows):.3f}' for name in rows[0]),flush=True)
