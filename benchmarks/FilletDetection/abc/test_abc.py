#!/usr/bin/env python3
"""Tests of the sample rule and the feature conversion (python3 -m unittest discover -s benchmarks/FilletDetection/abc -p 'test_*.py')."""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import feat_to_json
import sample


class SampleTests(unittest.TestCase):
    NAMES = ['00000002/00000002_aaaa_features_001.yml', '00000002/00000002_aaaa_features_000.yml',
             '00000007/00000007_bb12_features_000.yml', 'readme.txt', '00000009/00000009_cc34_features_000.yml']

    def test_id(self):
        self.assertEqual(sample.model_id('00000002/00000002_aaaa_features_001.yml'), '00000002_aaaa')
        self.assertEqual(sample.model_id('00000002_aaaa_trimesh_000.obj'), '00000002_aaaa')
        self.assertIsNone(sample.model_id('readme.txt'))

    def test_order_is_by_hash_one_file_per_model_and_independent_of_input_order(self):
        a = sample.order(self.NAMES)
        b = sample.order(list(reversed(self.NAMES)))
        self.assertEqual(a, b)
        self.assertEqual(len(a), 3)
        self.assertIn(('00000002_aaaa', '00000002/00000002_aaaa_features_000.yml'), a)
        import hashlib
        keys = [hashlib.sha256(mid.encode()).hexdigest() for mid, _ in a]
        self.assertEqual(keys, sorted(keys))
        self.assertEqual(sample.order(self.NAMES, 2), a[:2])

    def test_match(self):
        objs = ['00000007/00000007_bb12_trimesh_000.obj', '00000002/00000002_aaaa_trimesh_000.obj']
        self.assertEqual(sample.match(objs, ['00000002_aaaa', '00000099_zz']), {'00000002_aaaa': objs[1]})


class ConvertTests(unittest.TestCase):
    YML = """curves:
- sharp: true
  type: Line
  vert_indices: [0, 1]
- sharp: false
  type: Circle
  vert_indices:
  - 1
  - 2
surfaces:
- type: Plane
  face_indices: [0]
  location: [0.0, 0.0, 0.0]
  z_axis: [0.0, 0.0, 1.0]
- face_indices:
  - 1
  - 2
  type: Cylinder
"""

    def write(self, text):
        f = tempfile.NamedTemporaryFile('w', suffix='.yml', delete=False)
        f.write(text)
        f.close()
        self.addCleanup(os.unlink, f.name)
        return f.name

    def test_convert_keeps_types_faces_axes_and_only_sharp_curves(self):
        out = feat_to_json.convert(self.write(self.YML))
        self.assertEqual(out['sharp_curves'], [[0, 1]])
        self.assertEqual([p['type'] for p in out['patches']], ['Plane', 'Cylinder'])
        self.assertEqual(out['patches'][1]['faces'], [1, 2])
        self.assertEqual(out['patches'][0]['z_axis'], [0.0, 0.0, 1.0])
        self.assertNotIn('z_axis', out['patches'][1])
        json.dumps(out)

    def test_patches_key_is_accepted_and_a_wrong_layout_fails(self):
        out = feat_to_json.convert(self.write(self.YML.replace('surfaces:', 'patches:')))
        self.assertEqual(len(out['patches']), 2)
        empty = feat_to_json.convert(self.write('curves:\nsurfaces:\n- type: Plane\n  face_indices: [0]\n'))
        self.assertEqual(empty['sharp_curves'], [])
        with self.assertRaises(ValueError):
            feat_to_json.convert(self.write('faces: []\n'))


if __name__ == '__main__':
    unittest.main()
