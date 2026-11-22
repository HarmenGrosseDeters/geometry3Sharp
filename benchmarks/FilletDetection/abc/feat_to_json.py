#!/usr/bin/env python3
"""Reduce an ABC feature file (YAML: a list of patches and a list of curves, Koch et al. 2019, supplement 2.3) to the small
JSON the corpus run reads: per patch its type and triangle indices (and axis / location where given), and the vertex
indices of the curves marked sharp. No coordinates of the model are written.

    feat_to_json.py features.yml out.json
    feat_to_json.py --probe features.yml      print the structure only (keys, counts, types), for a first look at the format
"""
import json
import sys
from collections import Counter

import yaml

LOADER = getattr(yaml, 'CSafeLoader', yaml.SafeLoader)


def load(path):
    with open(path, 'rb') as f:
        data = yaml.load(f, Loader=LOADER)
    if not isinstance(data, dict):
        raise ValueError('top level is %s, not a mapping' % type(data).__name__)
    key = 'surfaces' if 'surfaces' in data else 'patches'
    if key not in data or 'curves' not in data:
        raise ValueError('expected the keys curves and surfaces (or patches), found %s' % sorted(data.keys()))
    # an empty list is written as a key without a value
    patches = data[key] or []
    curves = data['curves'] or []
    return data, patches, curves


def vec(entry, key):
    v = entry.get(key)
    return [float(x) for x in v] if isinstance(v, (list, tuple)) and len(v) == 3 else None


def convert(path):
    _, patches, curves = load(path)
    out = {'patches': [], 'sharp_curves': []}
    for p in patches:
        item = {'type': str(p['type']), 'faces': [int(i) for i in p['face_indices']]}
        for key in ('z_axis', 'location'):
            v = vec(p, key)
            if v is not None:
                item[key] = v
        out['patches'].append(item)
    for c in curves:
        if c.get('sharp') is True:
            out['sharp_curves'].append([int(i) for i in c['vert_indices']])
    return out


def probe(path):
    data, patches, curves = load(path)
    print('loader:', LOADER.__name__)
    print('top-level keys:', sorted(data.keys()))
    print('patches:', len(patches), 'types', dict(Counter(str(p.get('type')) for p in patches)))
    print('curves:', len(curves), 'types', dict(Counter(str(c.get('type')) for c in curves)),
          'sharp', dict(Counter(repr(c.get('sharp')) for c in curves)))
    seen = set()
    for p in patches:
        t = str(p.get('type'))
        if t not in seen:
            seen.add(t)
            print('  patch %s keys: %s' % (t, sorted(p.keys())))
    if curves:
        print('  curve keys:', sorted(curves[0].keys()))
    faces = [i for p in patches for i in p.get('face_indices', [])]
    print('face indices: %d, distinct %d, min %s, max %s' % (len(faces), len(set(faces)), min(faces, default=None), max(faces, default=None)))


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--probe':
        probe(sys.argv[2])
    elif len(sys.argv) == 3:
        with open(sys.argv[2], 'w') as f:
            json.dump(convert(sys.argv[1]), f, separators=(',', ':'))
    else:
        sys.exit(__doc__)
