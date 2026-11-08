import contextlib
import hashlib
import io
import json
import os
import pathlib
import subprocess
import tempfile
import types
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

import prepare
import run as runner
import count_boxes


class InputTests(unittest.TestCase):
    def test_box_candidate_counts_match_exhaustive_pairs(self):
        # Includes coincident points, shared boundaries and zero-width boxes.
        count_boxes.selftest()

    def test_linework_preserves_order_and_shared_vertices(self):
        case=ET.fromstring('<case><a>POLYGON ((0 0,2 0,0 2,0 0))</a><b>LINESTRING (0 0,2 2)</b></case>')
        text=prepare.canonical(case)
        self.assertIn('points 4\n',text)
        self.assertIn('segments 4\n0 1 0 0\n1 2 0 0\n2 0 0 0\n0 3 0 0\nrings 0\n',text)
        self.assertIn('0x4000000000000000 0x4000000000000000\n',text)

    def test_wkb_and_empty_geometries(self):
        from shapely import Point, to_wkb
        token=to_wkb(Point(1,2),hex=True)
        text=prepare.canonical(ET.fromstring(f'<case><a>{token}</a><b>POLYGON EMPTY</b></case>'))
        self.assertIn('points 1\n0x3ff0000000000000 0x4000000000000000\nsegments 0\n',text)

    def test_historical_hash_is_required_and_modes_are_distinct(self):
        base=prepare.canonical(ET.fromstring('<case><a>POINT (1 2)</a></case>'))
        job=dict(case='test/q20',mode='conforming',angle=20,expected_base_hash=hashlib.sha256(base.encode()).hexdigest())
        text,sha=prepare.variant(base,job)
        constrained,cqsha=prepare.variant(base,dict(job,mode='constrained'))
        self.assertNotEqual(sha,cqsha)
        self.assertIn('steiner_limit 200000\n',text)
        self.assertIn('mode constrained\n',constrained)
        with self.assertRaises(ValueError):prepare.variant(base,dict(job,expected_base_hash='wrong'))
        with self.assertRaises(ValueError):prepare.variant(base,dict(job,expected_input_hash='wrong'))


class RunnerTests(unittest.TestCase):
    def test_exit_code_alone_is_not_validation(self):
        row=dict(input_sha256='good')
        for code,output,data in [(0,'',row),(0,'INVALID x',row),(1,'VALID x',row),(0,'VALID x',None),(0,'VALID x',dict(input_sha256='wrong'))]:
            self.assertEqual('ORACLE_ERROR',runner.classify(code,output,data,'good'))
        self.assertEqual('VALID',runner.classify(0,'VALID x',row,'good'))
        self.assertEqual('INVALID',runner.classify(1,'INVALID x',row,'good'))

    def test_findings_incomplete_or_empty_batches_do_not_pass(self):
        self.assertEqual(0,runner.acceptance_exit(dict(planned=1,completed=1,verdicts={'VALID':1})))
        for summary in [dict(planned=1,completed=1,verdicts={'INVALID':1}),
                        dict(planned=1,completed=1,verdicts={'ORACLE_TIMEOUT':1}),
                        dict(planned=2,completed=1,verdicts={'VALID':1}),
                        dict(planned=0,completed=0,verdicts={})]:
            self.assertEqual(1,runner.acceptance_exit(summary))

    def test_empty_or_duplicate_manifest_is_rejected(self):
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ,{},clear=True):
            path=pathlib.Path(d)/'manifest.json'
            args=types.SimpleNamespace(manifest=path)
            for jobs in ([],[dict(case='same',mode='constrained')]*2):
                path.write_text(json.dumps(dict(jobs=jobs)))
                with self.assertRaises(ValueError):runner.run(args)

    def test_timeouts_are_explicit(self):
        with patch.object(runner.subprocess,'run',side_effect=subprocess.TimeoutExpired(['fake'],1,output=b'partial')):
            result=runner.stage(['fake'],1)
        self.assertEqual(1,result['timeout']);self.assertEqual('partial',result['stdout'])

    def test_resume_binds_versions_options_and_rejects_variants(self):
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ,{},clear=True):
            root=pathlib.Path(d)
            for name in ('bench.dll','Meshing2Oracle.dll','geometry3Sharp.dll'):(root/name).write_bytes(name.encode())
            source=root/'input.txt';source.write_text('pinned input')
            sha=runner.digest(source)
            manifest=root/'manifest.json';manifest.write_text(json.dumps(dict(reference_commit='ref',historical_g3_commit='old',jobs=[dict(case='case',mode='constrained',reference_verdict='VALID',old_verdict='ORACLE-TIMEOUT',old_triangles='1',input_path=str(source),input_sha256=sha)])))
            args=types.SimpleNamespace(manifest=manifest,dotnet=pathlib.Path('dotnet'),benchmark=root/'bench.dll',work=root/'work',mesh_timeout=1,oracle_timeout=1)
            def stage(command,timeout):
                output=pathlib.Path(command[command.index('--output')+1])
                if 'mesh' in command:
                    output.write_text('new mesh')
                    stdout='status Success, 1 triangles'
                else:
                    row=pathlib.Path(command[command.index('--row')+1])
                    self.assertFalse(row.exists(),'stale row must be removed before retry')
                    row.write_text(json.dumps(dict(input_sha256=sha,steiner_limit_reached=True)))
                    stdout='VALID test'
                return dict(command=command,exit_code=0,stdout=stdout,stderr='')
            args.work.mkdir()
            key=hashlib.sha256(b'case|constrained').hexdigest()[:20]
            (args.work/(key+'.row.json')).write_text('{"input_sha256":"stale"}')
            with patch.object(runner,'stage',stage),contextlib.redirect_stdout(io.StringIO()):runner.run(args)
            self.assertEqual(1,json.loads((args.work/'summary.json').read_text())['valid_with_steiner_limit'])
            with patch.object(runner,'stage',side_effect=AssertionError('completed job rerun')),contextlib.redirect_stdout(io.StringIO()):runner.run(args)
            args.oracle_timeout=2
            with self.assertRaises(ValueError):runner.run(args)
            args.oracle_timeout=1
            with patch.dict(os.environ,{'G3_M2_CONFORMING':'0'}):
                with self.assertRaises(ValueError):runner.run(args)
            (root/'bench.dll').write_bytes(b'changed binary')
            with self.assertRaises(ValueError):runner.run(args)


if __name__=='__main__':unittest.main()
