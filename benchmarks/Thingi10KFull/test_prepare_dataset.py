import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import prepare_dataset as prepare


class PrepareDatasetTests(unittest.TestCase):
    def fixture(self):
        files = [dict(type='file', path=f'raw_meshes/{number}.stl', size=1,
                      lfs=dict(size=1, oid='a' * 64)) for number in (1, 2)]
        inventory = json.dumps(dict(dataset_revision=prepare.REVISION, files=files), indent=2).replace('\n', '\r\n').encode()
        metadata = b'file_id,num_faces,unused\r\n1,4,x\r\n'
        projection = b'file_id,num_faces\r\n1,4\r\n'
        expected = dict(EXPECTED_FILES=2, EXPECTED_BYTES=2, EXPECTED_METADATA=1,
                        INVENTORY_SHA256=hashlib.sha256(inventory).hexdigest(),
                        PROJECTION_SHA256=hashlib.sha256(projection).hexdigest(),
                        METADATA_GIT_SHA1=hashlib.sha1(b'blob ' + str(len(metadata)).encode() + b'\0' + metadata).hexdigest())
        return files, inventory, metadata, projection, expected

    def test_paginated_regeneration_is_exact_and_does_not_download_meshes(self):
        files, inventory, metadata, projection, expected = self.fixture()
        next_url = prepare.TREE + '?cursor=opaque'
        replies = [(json.dumps(files[:1]).encode(), f'<{next_url}>; rel="next"'),
                   (json.dumps(files[1:]).encode(), ''), (metadata, '')]
        with tempfile.TemporaryDirectory() as directory, patch.multiple(prepare, **expected), \
                patch.object(prepare, 'fetch', side_effect=replies) as fetch:
            destination = Path(directory) / 'dataset'
            hashes = prepare.prepare(destination)
            self.assertEqual((destination / 'inventory.json').read_bytes(), inventory)
            self.assertEqual((destination / 'geometry_data.csv').read_bytes(), projection)
            self.assertEqual(hashes['inventory.json'], expected['INVENTORY_SHA256'])
            self.assertEqual(fetch.call_args_list[1].args[0], next_url)
            self.assertEqual(fetch.call_args_list[2].args[0], prepare.METADATA)
            self.assertEqual(fetch.call_count, 3)

    def test_wrong_revision_existing_destination_and_git_checkout_refuse_before_network(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(prepare, 'fetch') as fetch:
            root = Path(directory)
            with self.assertRaises(ValueError):
                prepare.prepare(root / 'new', 'main')
            with self.assertRaises(FileExistsError):
                prepare.prepare(root)
            (root / '.git').mkdir()
            with self.assertRaises(ValueError):
                prepare.prepare(root / 'new')
            fetch.assert_not_called()

    def test_inventory_rejects_missing_duplicate_paths_and_pagination_anomalies(self):
        files, _, _, _, expected = self.fixture()
        invalid = dict(files[0], path='raw_meshes/../1.stl')
        for page, link in ((files[:1], ''), (files + files[:1], ''), ([invalid], ''),
                           (files, '<https://example.com/next>; rel="next"'),
                           (files, f'<{prepare.TREE}?recursive=false&limit=1000>; rel="next"')):
            with self.subTest(page=page, link=link), patch.multiple(prepare, **expected), \
                    patch.object(prepare, 'fetch', return_value=(json.dumps(page).encode(), link)):
                with self.assertRaises(ValueError):
                    prepare.inventory()

    def test_projection_rejects_blob_mismatch_and_duplicate_ids(self):
        _, _, metadata, _, expected = self.fixture()
        with patch.multiple(prepare, **expected), patch.object(prepare, 'fetch', return_value=(metadata + b'x', '')):
            with self.assertRaises(ValueError):
                prepare.projection()
        duplicated = metadata + b'1,4,x\r\n'
        expected['METADATA_GIT_SHA1'] = hashlib.sha1(b'blob ' + str(len(duplicated)).encode() + b'\0' + duplicated).hexdigest()
        with patch.multiple(prepare, **expected), patch.object(prepare, 'fetch', return_value=(duplicated, '')):
            with self.assertRaises(ValueError):
                prepare.projection()


if __name__ == '__main__':
    unittest.main()
