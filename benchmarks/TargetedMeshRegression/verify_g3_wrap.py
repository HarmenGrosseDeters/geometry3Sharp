"""Check geometry3Sharp wrap containment with an independent CGAL AABB tree."""
import argparse
import json
import subprocess
from pathlib import Path

from common import json_write

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('checker',type=Path)
parser.add_argument('runs',type=Path,nargs='+')
args = parser.parse_args()
for root in args.runs:
    rows = json.loads((root/'results.json').read_text())
    checks = []
    for row in rows:
        if row.get('status') not in ('topology_checks_pass','ok') or not row['argv'][:1]==['wrap']:
            continue
        source = row['argv'][1]
        output = root/row['output']
        check = dict(case=row['case'],config=row['check'],output=row['output'])
        try:
            process = subprocess.run([str(args.checker),source,str(output)],timeout=25,
                                     capture_output=True,text=True)
            check.update(status=process.stdout.strip() if process.returncode==0 else 'checker_error',
                         exit_code=process.returncode)
            if process.stderr:
                check['stderr'] = process.stderr[-1000:]
        except subprocess.TimeoutExpired:
            check['status'] = 'checker_timeout'
        checks.append(check)
    json_write(root/'wrap-containment.json',checks)
