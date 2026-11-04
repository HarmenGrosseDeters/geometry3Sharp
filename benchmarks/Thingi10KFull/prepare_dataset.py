import argparse
import csv
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
from urllib.parse import urlsplit
from urllib.request import urlopen


REVISION = '2d5d3b2f3cd3711028ad75b12788c13b25559ec6'
TREE = f'https://huggingface.co/api/datasets/Thingi10K/Thingi10K/tree/{REVISION}/raw_meshes'
METADATA = f'https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/{REVISION}/metadata/geometry_data.csv'
INVENTORY_SHA256 = 'fbf3a0e3ac83ad8809da08855ea6882fb6b2d279aa7762c32b316204178fc029'
PROJECTION_SHA256 = '48083b3c78cf673610897c821f1b798f91034f0595fb11f9a24c7deec5edbfb3'
METADATA_GIT_SHA1 = '71eaa6d9041d44c0ef7d5473c51e371518ed951b'
EXPECTED_FILES = 10000
EXPECTED_BYTES = 38849490179
EXPECTED_METADATA = 9997


def fetch(url):
    with urlopen(url, timeout=60) as response:
        data = response.read(8 * 1024 * 1024 + 1)
        if len(data) > 8 * 1024 * 1024:
            raise ValueError('Metadata response exceeds 8 MiB')
        return data, response.headers.get('Link', '')


def inventory():
    url = TREE + '?recursive=false&limit=1000'
    seen_urls, seen_paths, files = set(), set(), []
    while url:
        parsed = urlsplit(url)
        if parsed.scheme != 'https' or parsed.netloc != 'huggingface.co' or parsed.path != urlsplit(TREE).path:
            raise ValueError('Unexpected pagination URL')
        if url in seen_urls or len(seen_urls) >= 20:
            raise ValueError('Pagination cycle or page limit exceeded')
        seen_urls.add(url)
        data, link = fetch(url)
        page = json.loads(data)
        if not isinstance(page, list) or len(page) > 1000:
            raise ValueError('Invalid inventory page')
        for entry in page:
            path = entry.get('path', '')
            pure = PurePosixPath(path)
            size = entry.get('size')
            lfs = entry.get('lfs') or {}
            if (entry.get('type') != 'file' or str(pure) != path or len(pure.parts) != 2
                    or pure.parts[0] != 'raw_meshes' or '\\' in path or ':' in path
                    or pure.name in ('.', '..') or pure.suffix.lower() not in ('.stl', '.obj', '.off', '.ply')
                    or type(size) is not int or size < 0 or lfs.get('size') != size
                    or re.fullmatch(r'[0-9a-f]{64}', lfs.get('oid', '')) is None):
                raise ValueError('Invalid raw inventory entry')
            if path in seen_paths:
                raise ValueError('Duplicate raw inventory path')
            seen_paths.add(path)
            files.append(entry)
        match = re.search(r'<([^>]+)>;\s*rel="next"', link)
        url = match.group(1) if match else None
    files.sort(key=lambda entry: entry['path'])
    if len(files) != EXPECTED_FILES or sum(entry['size'] for entry in files) != EXPECTED_BYTES:
        raise ValueError('Pinned inventory count or total size mismatch')
    data = json.dumps({'dataset_revision': REVISION, 'files': files}, indent=2).replace('\n', '\r\n').encode('utf-8')
    if hashlib.sha256(data).hexdigest() != INVENTORY_SHA256:
        raise ValueError('Pinned inventory SHA256 mismatch')
    return data


def projection():
    data, _ = fetch(METADATA)
    blob = b'blob ' + str(len(data)).encode('ascii') + b'\0' + data
    if hashlib.sha1(blob).hexdigest() != METADATA_GIT_SHA1:
        raise ValueError('Pinned metadata Git blob mismatch')
    reader = csv.DictReader(io.StringIO(data.decode('utf-8'), newline=''))
    if not {'file_id', 'num_faces'}.issubset(reader.fieldnames or ()):
        raise ValueError('Missing geometry metadata fields')
    stream = io.StringIO(newline='')
    writer = csv.writer(stream)
    writer.writerow(('file_id', 'num_faces'))
    seen = set()
    for row in reader:
        identifier, faces = row['file_id'], row['num_faces']
        if not identifier.isdecimal() or not faces.isdecimal() or identifier in seen:
            raise ValueError('Invalid or duplicate geometry metadata row')
        seen.add(identifier)
        writer.writerow((identifier, faces))
    result = stream.getvalue().encode('utf-8')
    if len(seen) != EXPECTED_METADATA or hashlib.sha256(result).hexdigest() != PROJECTION_SHA256:
        raise ValueError('Pinned projection count or SHA256 mismatch')
    return result


def prepare(destination, revision=REVISION):
    destination = Path(destination).resolve()
    if revision != REVISION:
        raise ValueError('Only the measured pinned revision is supported')
    if destination.exists():
        raise FileExistsError(destination)
    if any((parent / '.git').exists() for parent in destination.parents):
        raise ValueError('Use a destination outside Git checkouts')
    files = {'inventory.json': inventory(), 'geometry_data.csv': projection()}
    destination.mkdir(parents=True, exist_ok=False)
    written = []
    try:
        for name, data in files.items():
            path = destination / name
            with path.open('xb') as stream:
                written.append(path)
                stream.write(data)
    except Exception:
        for path in written:
            path.unlink()
        destination.rmdir()
        raise
    return {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Regenerate pinned metadata outside Git; never download meshes.')
    parser.add_argument('destination', type=Path)
    parser.add_argument('--revision', default=REVISION)
    args = parser.parse_args()
    print(json.dumps(prepare(args.destination, args.revision), indent=2))
