#!/usr/bin/env python3
"""The sample rule of the ABC measurement, fixed before any result: the feature files of a chunk in the order of the
SHA-256 of their model id (the leading "NNNNNNNN_hash" of the file name), one file per model (the lowest name).

    sample.py order  < names.txt  [count]     paths of the first `count` feature files in that order (default: all)
    sample.py match ids.txt < names.txt       the paths in names.txt (eg of the obj archive) whose model id is in ids.txt
    sample.py id path                         the model id of a path
"""
import hashlib
import re
import sys

ID = re.compile(r'(?:^|/)(\d{8}_[0-9a-zA-Z]+)_[A-Za-z]+_\d+\.[A-Za-z0-9]+$')


def model_id(path):
    m = ID.search(path.strip())
    return m.group(1) if m else None


def order(names, count=None):
    first = {}
    for name in sorted(n.strip() for n in names if n.strip()):
        mid = model_id(name)
        if mid is not None and mid not in first:
            first[mid] = name
    ranked = sorted(first, key=lambda mid: hashlib.sha256(mid.encode()).hexdigest())
    return [(mid, first[mid]) for mid in (ranked if count is None else ranked[:count])]


def match(names, ids):
    wanted = set(ids)
    out = {}
    for name in sorted(n.strip() for n in names if n.strip()):
        mid = model_id(name)
        if mid in wanted and mid not in out:
            out[mid] = name
    return out


if __name__ == '__main__':
    if len(sys.argv) >= 2 and sys.argv[1] == 'order':
        for mid, name in order(sys.stdin, int(sys.argv[2]) if len(sys.argv) > 2 else None):
            print(name)
    elif len(sys.argv) == 3 and sys.argv[1] == 'match':
        with open(sys.argv[2]) as f:
            ids = [line.strip() for line in f if line.strip()]
        found = match(sys.stdin, ids)
        for mid in ids:
            if mid in found:
                print(found[mid])
    elif len(sys.argv) == 3 and sys.argv[1] == 'id':
        print(model_id(sys.argv[2]) or '')
    else:
        sys.exit(__doc__)
