#!/usr/bin/env python3
"""Download Thingi10K raw meshes by id from the Hugging Face mirror, at a pinned revision.

usage: python3 fetch_thingi.py <out dir> <id>...
The mesh files are not committed (their own licenses); they are fetched for the run only."""
import json, os, re, sys, time, urllib.error, urllib.request

REV = '2d5d3b2f3cd3711028ad75b12788c13b25559ec6'
API = f'https://huggingface.co/api/datasets/Thingi10K/Thingi10K/tree/{REV}/raw_meshes?recursive=false&limit=1000'


def retried(f, what, attempts=7):
    # Hugging Face answers bursts with HTTP 429 (and sometimes 5xx): wait (Retry-After if given, else 30 s doubling,
    # at most 10 min) and try again; any other error, or the last attempt, raises
    wait = 30
    for k in range(attempts):
        try:
            return f()
        except urllib.error.HTTPError as e:
            if (e.code != 429 and e.code < 500) or k == attempts - 1:
                raise
            ra = e.headers.get('Retry-After') if e.headers else None
            delay = min(600, int(ra) if ra and ra.isdigit() else wait)
            print(f'{what}: HTTP {e.code}, retry in {delay} s', file=sys.stderr, flush=True)
            time.sleep(delay)
            wait *= 2


def inventory(cache):
    if os.path.exists(cache):
        return json.load(open(cache))
    files, url = [], API
    while url:
        def page(u=url):
            with urllib.request.urlopen(u, timeout=60) as r:
                return json.load(r), r.headers.get('Link') or ''
        got, link = retried(page, 'inventory')
        files += got
        m = re.search(r'<([^>]+)>;\s*rel="next"', link)
        url = m.group(1) if m else None
    inv = {f['path'].split('/')[-1].split('.')[0]: {'path': f['path'], 'size': f.get('size')}
           for f in files if f.get('type') == 'file'}
    json.dump(inv, open(cache, 'w'))
    return inv


def main():
    out, ids = sys.argv[1], sys.argv[2:]
    os.makedirs(out, exist_ok=True)
    inv = inventory(os.path.join(out, 'inventory.json'))
    for i in ids:
        p = inv[i]['path']
        dst = os.path.join(out, p.split('/')[-1])
        if os.path.exists(dst) and os.path.getsize(dst) == inv[i]['size']:
            continue
        retried(lambda: urllib.request.urlretrieve(f'https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/{REV}/{p}', dst), i)
        print(i, os.path.getsize(dst), flush=True)


if __name__ == '__main__':
    main()
