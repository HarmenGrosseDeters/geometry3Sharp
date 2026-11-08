"""Fail closed on missing, corrupt or unequal source-pilot results and full reports."""
import argparse
import hashlib
import json
from pathlib import Path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def verify(runs, boundary):
    labels = ['baseline-unmodified', 'grid-final', 'scan-final']
    metadata = {label: json.loads((runs/(label+'.metadata.json')).read_text()) for label in labels}
    records = {label: [json.loads(line) for line in (runs/(label+'.jsonl')).read_text().splitlines()] for label in labels}
    base = metadata[labels[0]]
    for label in labels:
        meta, rows = metadata[label], records[label]
        for field in ('controller_sha256', 'mesher_runtime', 'sizes', 'full_max_points'):
            require(meta[field] == base[field], 'Campaign metadata differs: '+label+'/'+field)
        expected = {(m, repeat, kind) for m in base['sizes'] for repeat in range(meta['repeats'])
                    for kind in ['phase']+(['full'] if 2*m-2<=base['full_max_points'] else [])}
        keys = [(r['m'], r['repeat'], r['kind']) for r in rows]
        require(len(set(keys)) == len(keys) and set(keys) == expected, 'Missing/duplicate/unexpected records: '+label)
    require(metadata['grid-final']['mode']=='grid' and metadata['scan-final']['mode']=='scan', 'Control modes differ')
    require(metadata['grid-final']['pilot_runtime']==metadata['scan-final']['pilot_runtime'], 'Grid/scan binaries differ')
    full_cases = phase_cases = 0
    for m in base['sizes']:
        n = 2*m-2
        fixture = json.loads((runs/f'lens-{m}.fixture.json').read_text())
        require(digest(runs/f'lens-{m}.input.txt')==fixture['input_sha256'], 'Input hash differs')
        require(digest(runs/f'lens-{m}.mesh.txt')==fixture['mesh_sha256'], 'Mesh hash differs')
        require(fixture['mesher_runtime']==base['mesher_runtime'], 'Fixture producer differs')
        edge_keys = sorted((min(i,(i+1)%n)<<32)|max(i,(i+1)%n) for i in range(n))
        expected_fp = hashlib.sha256('\n'.join(map(str,edge_keys)).encode()).hexdigest()
        reports = []
        for label in labels:
            for row in (r for r in records[label] if r['m']==m):
                require(row['points']==n and row['input_sha256']==fixture['input_sha256']
                        and row['mesh_sha256']==fixture['mesh_sha256'], 'Record fixture differs')
                if row['kind']=='phase':
                    require(row['exit_code'] in (0,'TIMEOUT'), 'Phase failed')
                    require(row['output'] and row['output'][0]['phase']=='sources-begin'
                            and row['output'][0]['judgment_complete'] is False, 'Missing phase preparation')
                    if row['exit_code']=='TIMEOUT':
                        require(label!='grid-final', 'Indexed phase did not finish')
                    else:
                        result = row['output'][-1]
                        require(result['phase']=='sources-complete' and result['judgment_complete'] is False
                                and result['constraint_edges']==n and result['fingerprint']==expected_fp,
                                'Phase inferred boundary differs: '+label+'/'+str(n))
                    phase_cases += 1
                else:
                    require(row['exit_code']==0 and row['output'][-1]['judgment_complete'] is True
                            and row['output'][-1]['Ok'] is True, 'Full checker failed or incomplete')
                    report = runs/f'{label}-{m}-{row["repeat"]}.report.json'
                    require(digest(report)==row['report_sha256'], 'Full report integrity differs')
                    reports.append(report.read_bytes())
        if reports:
            require(all(r==reports[0] for r in reports), 'Full report differs across source variants: '+str(n))
            full_cases += 1
    meta = json.loads((boundary/'boundary-control.metadata.json').read_text())
    result = json.loads((boundary/'boundary-control.json').read_text())
    require(meta['before_runtime']==metadata['baseline-unmodified']['pilot_runtime']
            and meta['after_runtime']==metadata['grid-final']['pilot_runtime'], 'Boundary control binaries differ')
    large_m = max(base['sizes'])
    fixture = json.loads((runs/f'lens-{large_m}.fixture.json').read_text())
    require(meta['input_sha256']==fixture['input_sha256'] and meta['original_mesh_sha256']==fixture['mesh_sha256'],
            'Boundary control fixture differs')
    control_mesh = boundary/'reported-boundary.mesh.txt'
    require(digest(control_mesh)==meta['reported_mesh_sha256'], 'Reported boundary mesh hash differs')
    n = 2*large_m-2
    original = (runs/f'lens-{large_m}.mesh.txt').read_text()
    expected = original.replace('subsegments -\n',f'subsegments {n}\n'+''.join(f'{i} {(i+1)%n} 0\n' for i in range(n)))
    require(original.count('subsegments -\n')==1 and control_mesh.read_text()==expected, 'Boundary overlay differs')
    reports=[]
    for name in ('original_oracle_reported_boundary','indexed_oracle_original_mesh'):
        row=result[name]
        require(row['exit_code']==0 and row['output'][-1]['judgment_complete'] is True
                and row['output'][-1]['Ok'] is True, 'Boundary full checker failed')
        report=boundary/(name+'.report.json')
        require(digest(report)==row['report_sha256'], 'Boundary full report integrity differs')
        reports.append(report.read_bytes())
    require(reports[0]==reports[1] and result['reports_equal'] is True, 'Large semantic report differs')
    return dict(complete_small_report_cases=full_cases, phase_records=phase_cases,
                all_completed_boundaries_equal=True, large_semantic_reports_equal=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runs', type=Path, required=True)
    parser.add_argument('--boundary', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(verify(args.runs, args.boundary)))
