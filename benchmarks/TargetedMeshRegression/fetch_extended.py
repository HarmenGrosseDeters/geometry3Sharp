"""Fetch the frozen real-mesh inputs at runtime and verify their recorded hashes."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import gzip
import hashlib
import json
import os
from pathlib import Path
import subprocess

from common import json_write

MANIFEST=Path(__file__).parent/'corpus-sources.json'
MAX_ASSET_BYTES=64*1024*1024


def fetch(data,asset):
    path=data/asset['path']
    path.parent.mkdir(parents=True,exist_ok=True)
    if path.is_file() and path.stat().st_size <= MAX_ASSET_BYTES and hashlib.sha256(path.read_bytes()).hexdigest()==asset['sha256']:
        return
    fetched=path.with_suffix(path.suffix+'.download')
    try:
        subprocess.run(['curl','-fLsS','--retry','2','--max-time','60','--max-filesize',
                        str(MAX_ASSET_BYTES),asset['url'],'-o',str(fetched)],check=True)
        if fetched.stat().st_size>MAX_ASSET_BYTES:
            raise ValueError(f'download exceeds size budget for {asset["path"]}')
        if asset.get('gzip'):
            with gzip.open(fetched,'rb') as stream:
                contents=stream.read(MAX_ASSET_BYTES+1)
        else:
            contents=fetched.read_bytes()
        if len(contents)>MAX_ASSET_BYTES or hashlib.sha256(contents).hexdigest()!=asset['sha256']:
            raise ValueError(f'input exceeds size budget or hash changed for {asset["path"]}')
        path.write_bytes(contents)
    finally:
        fetched.unlink(missing_ok=True)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('data',type=Path)
    parser.add_argument('--abc', action='store_true', help='fetch only ABC on a GitHub-hosted runner')
    args=parser.parse_args()
    if args.abc and os.getenv('RUNNER_ENVIRONMENT') != 'github-hosted':
        parser.error('--abc requires a GitHub-hosted runner')
    data=args.data.resolve()
    data.mkdir(parents=True,exist_ok=True)
    manifest=json.loads(MANIFEST.read_text())
    assets = [a for a in manifest['assets'] if a['path'].startswith('abc/') == args.abc]
    with ThreadPoolExecutor(max_workers=6) as executor:
        list(executor.map(lambda asset:fetch(data,asset),assets))
    if args.abc:
        json_write(data/'abc/index.json',{'files':manifest['abc_index']})
        json_write(data/'abc/screen.json',manifest['screens']['abc'])
        print(f'Verified {len(assets)} ABC inputs')
        return
    json_write(data/'objaverse/screen.json',manifest['screens']['objaverse'])
    repo=data/'thingiCSG'
    pin=manifest['thingicsg']
    if not (repo/'.git').exists():
        subprocess.run(['git','init',str(repo)],check=True)
        subprocess.run(['git','-C',str(repo),'fetch','--depth','1',pin['url'],pin['commit']],check=True)
        subprocess.run(['git','-C',str(repo),'checkout','--detach','FETCH_HEAD'],check=True)
    actual=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()
    modified=subprocess.check_output(['git','-C',str(repo),'status','--porcelain','--untracked-files=no'],text=True)
    if modified.strip():
        raise ValueError('ThingiCSG checkout has modified tracked files')
    if actual!=pin['commit']:
        raise ValueError(f'thingiCSG revision {actual} differs from requested {pin["commit"]}')
    print(f'Verified {len(assets)} inputs and ThingiCSG {actual}')


if __name__=='__main__':
    main()
