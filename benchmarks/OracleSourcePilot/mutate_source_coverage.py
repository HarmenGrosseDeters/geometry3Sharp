import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description='Targeted source coverage mutants; restores original source bytes in finally.')
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--work', type=Path, required=True)
args = parser.parse_args()
args.work = args.work.resolve()
args.work.mkdir(parents=True, exist_ok=True)
root = Path(__file__).resolve().parents[2]
cover = root/'benchmarks/Meshing2Benchmark/Oracle/Oracle.SourceCoverage.cs'
oracle = root/'benchmarks/Meshing2Benchmark/Oracle/Oracle.cs'
original = {p: p.read_bytes() for p in (cover, oracle)}
env = dict(os.environ)
env.pop('G3_ORACLE_SOURCE_INDEX', None)
mutants = [
    ('disable-grid', cover, 'if (!indexSourceCoverage || !Fast)', 'if (true)', 'Lens_SourceCandidateWorkGrowsBelowQuadratic'),
    ('ignore-second-endpoint', cover, 'if (WithinTube(s, v[a]) && WithinTube(s, v[b])) { covers', 'if (WithinTube(s, v[a])) { covers', 'CoveringRequiresBothEndpoints_AndReportedEmptyListsStayEmpty'),
    ('drop-unknown-boxes', cover, 'var s = sources[k];', 'if (double.IsNaN(sBox[at])) continue;\n                    var s = sources[k];', 'FullReportsAgree_UnknownBoxesAndReferenceFallbacks'),
    ('ignore-scan-control', oracle, 'indexSourceCoverage = SourceIndexSetting() != "scan";', 'indexSourceCoverage = true;', 'Selection_DefaultsAndAcceptedValues'),
    ('ignore-query-budget', cover, 'const int SourceGridQueryWorkLimit = 4096;', 'const int SourceGridQueryWorkLimit = int.MaxValue;', 'UnknownSources_KeepTheScanEarlyExitWithoutMaterializingAllCandidates'),
    ('ignore-bucket-budget', oracle, 'foreach (int i in list) {\n                                    if (++work > maxWork) return null;', 'foreach (int i in list) {\n                                    if (++work > int.MaxValue) return null;', 'DenseKnownBucket_HasBoundedEnumerationAndReusableStamps')
]
results = []
try:
    for name, path, before, after, test in mutants:
        text = original[path].decode()
        assert text.count(before)==1, name
        path.write_text(text.replace(before, after))
        log = args.work/('mutation-'+name+'.log')
        cmd = [args.dotnet, 'test', 'tests/geometry3Sharp.Tests', '-c', 'Release', '-f', 'net10.0',
               '-m:1', '-nr:false', '--artifacts-path', str(args.work/'artifacts'),
               '--filter', 'FullyQualifiedName~Meshing2OracleSourceCoverageTests.'+test,
               '--logger', 'console;verbosity=normal']
        with log.open('w') as output:
            run = subprocess.run(cmd, cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        output = log.read_text()
        killed = run.returncode != 0 and 'Test Run Failed.' in output and test in output and 'error CS' not in output
        record = dict(mutant=name, test=test, exit_code=run.returncode, killed=killed,
                      log_sha256=hashlib.sha256(log.read_bytes()).hexdigest())
        results.append(record)
        (args.work/'mutations.json').write_text(json.dumps(results,indent=2)+'\n')
        print(json.dumps(record),flush=True)
        assert killed, name
        path.write_bytes(original[path])
finally:
    for path, content in original.items():
        path.write_bytes(content)
    assert all(p.read_bytes()==content for p,content in original.items())
