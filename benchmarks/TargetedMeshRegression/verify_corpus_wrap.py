"""Check strict containment for the selected ABC or Objaverse wrap outputs."""
import argparse
import json
import subprocess
from pathlib import Path

from common import json_write


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('checker', type=Path)
    parser.add_argument('results', type=Path)
    parser.add_argument('--corpus', choices=['abc', 'objaverse'], required=True)
    args = parser.parse_args()
    rows = json.loads((args.results/f'{args.corpus}.json').read_text())['commands']
    converted = {row['case']:row['argv'][2] for row in rows
                 if row['stage']=='convert' and row['status']=='ok'}
    checks = []
    for row in rows:
        argv = row['argv']
        if argv[0]!='wrap':
            continue
        check = dict(case=row['case'], stage=row['stage'])
        checks.append(check)
        if row['status']!='ok':
            check.update(status='wrap_unavailable', wrap_status=row['status'])
            continue
        source = argv[1] if args.corpus=='abc' else converted.get(row['case'])
        if source is None:
            check['status']='import_unavailable'
            continue
        try:
            process = subprocess.run([str(args.checker),source,argv[2]], capture_output=True,
                                     text=True, timeout=20)
            check.update(status=process.stdout.strip() if process.returncode==0 else 'checker_error',
                         exit_code=process.returncode)
            if process.stderr:
                check['stderr']=process.stderr[-400:]
        except subprocess.TimeoutExpired:
            check['status']='checker_timeout'
    json_write(args.results/f'{args.corpus}-containment.json', checks)


if __name__=='__main__':
    main()
