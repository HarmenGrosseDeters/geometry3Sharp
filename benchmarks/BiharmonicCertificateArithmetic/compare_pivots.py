"""Compare every decimal Python-pilot interval to a binary fixed-point C# interval.

Overlapping intervals are an independent consistency check, not proof of either implementation.
"""
from decimal import Decimal
from fractions import Fraction
from pathlib import Path
import sys
import json
import hashlib

if len(sys.argv)!=5:
 raise SystemExit('usage: compare_pivots.py <decimal-pivots> <binary-pivots> <binary-fraction-bits> <exported-metadata.json>')
def load(path):
 result=[];lines=Path(path).read_text().splitlines()
 if not lines or not lines[0].startswith('# input_sha256='):raise SystemExit('missing input digest')
 digest=lines[0].split('=',1)[1]
 for line in lines[1:]:
  index,lo,hi=line.split(); result.append((int(index),lo,hi))
 return digest,result
pd,p=load(sys.argv[1]);cd,c=load(sys.argv[2]);bits=int(sys.argv[3]);metadata=Path(sys.argv[4]).read_bytes();expected=json.loads(metadata)['size']
digest=hashlib.sha256(metadata).hexdigest()
if pd!=digest or cd!=digest:raise SystemExit('input digest mismatch')
if expected<1 or len(p)!=expected or len(c)!=expected or bits<1:
 raise SystemExit('pivot count does not match the exported matrix size, or invalid precision')
scale=1<<bits
for k,(left,right) in enumerate(zip(p,c)):
 if left[0]!=k or right[0]!=k:raise SystemExit(f'pivot order mismatch at {k}')
 plo,phi=Fraction(Decimal(left[1])),Fraction(Decimal(left[2]))
 clo,chi=Fraction(int(right[1]),scale),Fraction(int(right[2]),scale)
 if plo>phi or clo>chi or clo>phi or plo>chi:raise SystemExit(f'disjoint or invalid intervals at pivot {k}')
print(f'pivot_count={len(p)} bits={bits} status=all_intervals_overlap')
