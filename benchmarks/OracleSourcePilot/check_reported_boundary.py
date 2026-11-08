"""Full-report control: original oracle with the known convex-lens boundary reported explicitly.

This changes only the optional subsegment report. It is a semantic control, not a
timing comparison on identical serialized meshes. The old inference scan remains
separately measured/censored on the original mesh by profile_sources.py.
"""
import argparse
import json
import os
from pathlib import Path
import re
from profile_sources import sha, runtime, run


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--dotnet', required=True)
    p.add_argument('--before', type=Path, required=True)
    p.add_argument('--after', type=Path, required=True)
    p.add_argument('--input', type=Path, required=True)
    p.add_argument('--mesh', type=Path, required=True)
    p.add_argument('--out', type=Path, required=True)
    p.add_argument('--seconds', type=float, default=120)
    args = p.parse_args()
    text = args.mesh.read_text()
    n = int(re.search(r'^vertices (\d+)$', text, re.M)[1])
    mapping = list(map(int, re.search(r'^map (.*)$', text, re.M)[1].split()))
    if mapping != [n] + list(range(n)) or text.count('subsegments -\n') != 1:
        raise ValueError('Expected the generated convex lens with identity mapping and no subsegment report')
    args.out.mkdir(parents=True, exist_ok=True)
    control = args.out/'reported-boundary.mesh.txt'
    control.write_text(text.replace('subsegments -\n', f'subsegments {n}\n'+''.join(f'{i} {(i+1)%n} 0\n' for i in range(n))))
    metadata = dict(input_sha256=sha(args.input), original_mesh_sha256=sha(args.mesh), reported_mesh_sha256=sha(control),
                    controller_sha256=sha(__file__), before_runtime=runtime(args.before), after_runtime=runtime(args.after),
                    source_mode={'original_oracle_reported_boundary': 'scan', 'indexed_oracle_original_mesh': 'grid'},
                    scope='Full report equality across optional boundary-report representations; not identical serialized meshes or a before/after timing claim.')
    (args.out/'boundary-control.metadata.json').write_text(json.dumps(metadata, indent=2)+'\n')
    record = {}
    for name, pilot, mesh, mode in [('original_oracle_reported_boundary', args.before, control, 'scan'),
                                     ('indexed_oracle_original_mesh', args.after, args.mesh, 'grid')]:
        env = dict(os.environ, G3_ORACLE_TIMING='1', G3_ORACLE_SOURCE_INDEX=mode)
        report = args.out/(name+'.report.json')
        record[name] = run([args.dotnet, str(pilot), 'full', str(args.input), str(mesh), str(report)], env, args.seconds)
        if record[name]['exit_code'] == 0:
            record[name]['report_sha256'] = sha(report)
        (args.out/'boundary-control.json').write_text(json.dumps(record, indent=2)+'\n')
        print(json.dumps({name: record[name]}), flush=True)
    record['reports_equal'] = all(v['exit_code']==0 for v in record.values()) and len({v.get('report_sha256') for v in record.values()})==1
    (args.out/'boundary-control.json').write_text(json.dumps(record, indent=2)+'\n')
    if not record['reports_equal']:
        raise RuntimeError('Complete report comparison did not pass; see the retained records')


if __name__ == '__main__':
    main()
