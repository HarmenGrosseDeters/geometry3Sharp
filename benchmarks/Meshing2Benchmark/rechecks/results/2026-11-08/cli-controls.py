import json
import pathlib
import shutil
import subprocess
import tempfile

repo = pathlib.Path('/workspace/geometry3Sharp-meshing-qa')
dll = '/workspace/meshing-qa-final-artifacts/bin/Meshing2Benchmark/release_net10.0/Meshing2Benchmark.dll'
records = []
with tempfile.TemporaryDirectory(prefix='g3-golden-cli-') as tmp:
    root = pathlib.Path(tmp)
    (root/'geometry3Sharp_netstandard.csproj').touch()
    golden = root/'benchmarks/Meshing2Benchmark/golden'
    shutil.copytree(repo/'benchmarks/Meshing2Benchmark/golden', golden)
    def check(name, args, expected, needle=None):
        cmd = ['/workspace/dotnet/dotnet', dll] + args
        r = subprocess.run(cmd, cwd=root, capture_output=True, text=True, timeout=300)
        pathlib.Path('/tmp/meshing-cli-'+name+'.log').write_text(r.stdout+r.stderr)
        records.append(dict(name=name, command=cmd, expected=expected, actual=r.returncode,
                            diagnostic_present=needle is None or needle in r.stdout+r.stderr))
        print(json.dumps(records[-1]), flush=True)
        assert r.returncode == expected and records[-1]['diagnostic_present'], name
    check('missing-mode', ['golden','check','--milestone','M3','--mode'], 2, '--mode')
    check('unknown-mode', ['golden','check','--milestone','M3','--mode','wrong'], 2, '--mode')
    check('wrong-milestone', ['golden','check','--milestone','M2','--mode','constrained'], 2, 'M3')
    check('ci-clean', ['ci'], 0)
    path = golden/'m3-constrained.json'
    original = path.read_bytes()
    data = json.loads(original)
    data['rows'][0]['triangles'] += 1
    path.write_text(json.dumps(data))
    check('ci-constrained-row-mutated', ['ci'], 1, 'f-3/c')
    assert (repo/'benchmarks/Meshing2Benchmark/golden/m3-constrained.json').read_bytes() == original
pathlib.Path('/tmp/meshing-cli-validation.json').write_text(json.dumps(records, indent=2)+'\n')
