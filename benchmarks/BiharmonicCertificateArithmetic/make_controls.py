"""Create intentionally invalid candidates for the standalone mesh pilot."""
import copy
import json
import math
import struct
import sys
from pathlib import Path

if len(sys.argv)!=3:
 raise SystemExit('usage: make_controls.py <fixture.json> <output-directory>')
data=json.loads(Path(sys.argv[1]).read_text());out=Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)
def value(bits): return struct.unpack('>d',struct.pack('>q',bits))[0]
def bits(v): return struct.unpack('>q',struct.pack('>d',v))[0]
free=next(i for i,v in enumerate(data['fixed']) if not v)
fixed=next(i for i,v in enumerate(data['fixed']) if v)
for name,index,axis in [('bad-x',free,0),('bad-y',free,1),('bad-z',free,2),('bad-handle',fixed,2)]:
 candidate=copy.deepcopy(data)
 original=candidate['candidate_bits'][index][axis]; v=value(original); changed=v+0.0001
 if not math.isfinite(changed) or bits(changed)==original:
  changed=math.nextafter(v, -math.inf if v>0 else math.inf)
 if not math.isfinite(changed) or bits(changed)==original: raise ValueError('could not make a finite distinct control value')
 candidate['candidate_bits'][index][axis]=bits(changed)
 (out/f'{name}.json').write_text(json.dumps(candidate,separators=(',',':')))
candidate=copy.deepcopy(data);del candidate['candidate_bits']
(out/'no-candidate.json').write_text(json.dumps(candidate,separators=(',',':')))
candidate=copy.deepcopy(data);candidate['entries']=[]
(out/'no-matrix.json').write_text(json.dumps(candidate,separators=(',',':')))
candidate=copy.deepcopy(data);candidate['scale_bits']=1
(out/'tiny-scale.json').write_text(json.dumps(candidate,separators=(',',':')))
