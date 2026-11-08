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
            args=types.SimpleNamespace(manifest=path,prepared=pathlib.Path(d)/'prepared.json')
            for jobs in ([],[dict(case='same',mode='constrained')]*2):
                path.write_text(json.dumps(dict(jobs=jobs)))
                with self.assertRaises(ValueError):runner.run(args)

    def test_prepared_map_must_list_the_manifest_jobs(self):
        with tempfile.TemporaryDirectory() as d:
            manifest=pathlib.Path(d)/'manifest.json';prepared=pathlib.Path(d)/'prepared.json'
            manifest.write_text(json.dumps(dict(jobs=[dict(case='a',mode='conforming'),dict(case='a',mode='constrained')])))
            for hashes in ({'a|conforming':'x'},{'a|constrained':'y','a|conforming':'x'},{'a|conforming':'x','a|constrained':'y','b|conforming':'z'}):
                prepared.write_text(json.dumps(hashes))
                with self.assertRaises(ValueError):runner.load_jobs(manifest,prepared)
            prepared.write_text(json.dumps({'a|conforming':'x','a|constrained':'y'}))
            _,jobs=runner.load_jobs(manifest,prepared)
            self.assertEqual(['x','y'],[j['input_sha256'] for j in jobs])

    def test_timeouts_are_explicit(self):
        with patch.object(runner.subprocess,'run',side_effect=subprocess.TimeoutExpired(['fake'],1,output=b'partial')):
            result=runner.stage(['fake'],1)
        self.assertEqual(1,result['timeout']);self.assertEqual('partial',result['stdout'])

    def test_compaction_counts_violations_and_drops_captured_text(self):
        stdout=('oracle self-check: 3 checks passed\nVIOLATION AngleBelowBound: triangle 1 2 3 is below 20 degrees\n'
                'VIOLATION NotDelaunayLocal: edge 4 5\nVIOLATION AngleBelowBound: triangle 6 7 8 is below 20 degrees\n'
                'INVALID g3-quality: 9 triangles, twice the area 7/2\n')
        stage=dict(command=['/root/sdk/dotnet','/root/bin/B.dll','oracle','--input','/root/in/x.txt','--row','/root/w/r.json','--case','c'],
                   exit_code=1,seconds=0.5,stdout=stdout,stderr='warn\n')
        out=runner.compact_stage(stage,{'/root/sdk/dotnet':'<dotnet>','/root/bin/B.dll':'<benchmark>','/root/in':'<inputs>','/root/w/':'<work>'})
        self.assertEqual({'AngleBelowBound':2,'NotDelaunayLocal':1},out['violations'])
        self.assertEqual(['<dotnet>','<benchmark>','oracle','--input','<inputs>/x.txt','--row','<work>/r.json','--case','c'],out['command'])
        self.assertEqual((5,hashlib.sha256(stdout.encode()).hexdigest()),(out['stdout_lines'],out['stdout_sha256']))
        self.assertEqual((1,hashlib.sha256(b'warn\n').hexdigest()),(out['stderr_lines'],out['stderr_sha256']))
        self.assertNotIn('stdout',out);self.assertNotIn('stderr',out)
        self.assertNotIn('triangle 1 2 3',json.dumps(out));self.assertNotIn('7/2',json.dumps(out))
        row=runner.compact_row(dict(triangles=9,area2='7/2',reason='AngleBelowBound: triangle 1 2 3; AngleBelowBound: triangle 6 7 8; NotDelaunayGlobal: x'))
        self.assertEqual(dict(triangles=9,area2_sha256=hashlib.sha256(b'7/2').hexdigest(),
                              reason_violations={'AngleBelowBound':2,'NotDelaunayGlobal':1},
                              reason_sha256=hashlib.sha256(b'AngleBelowBound: triangle 1 2 3; AngleBelowBound: triangle 6 7 8; NotDelaunayGlobal: x').hexdigest()),row)
        self.assertEqual({},runner.compact_row(dict(reason=''))['reason_violations'])
        self.assertIsNone(runner.compact_row(None))

    def test_resume_binds_versions_options_and_rejects_variants(self):
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ,{},clear=True):
            root=pathlib.Path(d)
            for name in ('bench.dll','Meshing2Oracle.dll','geometry3Sharp.dll'):(root/name).write_bytes(name.encode())
            inputs=root/'inputs';inputs.mkdir()
            source=inputs/runner.input_name('case','constrained');source.write_text('pinned input')
            sha=runner.digest(source)
            manifest=root/'manifest.json';manifest.write_text(json.dumps(dict(reference_commit='ref',historical_g3_commit='old',jobs=[dict(case='case',mode='constrained',reference_verdict='VALID',old_verdict='ORACLE-TIMEOUT',old_triangles='1')])))
            prepared=root/'prepared.json';prepared.write_text(json.dumps({'case|constrained':sha}))
            args=types.SimpleNamespace(manifest=manifest,prepared=prepared,inputs=inputs,dotnet=pathlib.Path('dotnet'),benchmark=root/'bench.dll',work=root/'work',mesh_timeout=1,oracle_timeout=1)
            def stage(command,timeout):
                self.assertEqual(str(source),command[command.index('--input')+1])
                output=pathlib.Path(command[command.index('--output')+1])
                if 'mesh' in command:
                    output.write_text('new mesh')
                    stdout='status Success, 1 triangles'
                else:
                    row=pathlib.Path(command[command.index('--row')+1])
                    self.assertFalse(row.exists(),'stale row must be removed before retry')
                    row.write_text(json.dumps(dict(input_sha256=sha,steiner_limit_reached=True,triangles=1,area2='1/2',reason='')))
                    stdout='VALID test, twice the area 1/2'
                return dict(command=command,exit_code=0,stdout=stdout,stderr='')
            args.work.mkdir()
            key=hashlib.sha256(b'case|constrained').hexdigest()[:20]
            (args.work/(key+'.row.json')).write_text('{"input_sha256":"stale"}')
            with patch.object(runner,'stage',stage),contextlib.redirect_stdout(io.StringIO()):runner.run(args)
            self.assertEqual(1,json.loads((args.work/'summary.json').read_text())['valid_with_steiner_limit'])
            results=(args.work/'results.jsonl').read_text()
            self.assertNotIn(d,results);self.assertNotIn('1/2',results);self.assertNotIn('"stdout"',results)
            record=json.loads(results)
            self.assertEqual(['<dotnet>','<benchmark>','oracle','--input','<inputs>/'+source.name],record['oracle']['command'][:5])
            self.assertEqual(('Success',{}),(record['mesh']['status'],record['oracle']['violations']))
            self.assertEqual('VALID',record['verdict'])
            self.assertIn('case,constrained,ORACLE-TIMEOUT,VALID,VALID,1,True,,'+sha,(args.work/'verdicts.csv').read_text())
            with patch.object(runner,'stage',side_effect=AssertionError('completed job rerun')),contextlib.redirect_stdout(io.StringIO()):runner.run(args)
            args.oracle_timeout=2
            with self.assertRaises(ValueError):runner.run(args)
            args.oracle_timeout=1
            with patch.dict(os.environ,{'G3_M2_CONFORMING':'0'}):
                with self.assertRaises(ValueError):runner.run(args)
            prepared.write_text(json.dumps({'case|constrained':'0'*64}))
            with self.assertRaises(ValueError):runner.run(args)
            prepared.write_text(json.dumps({'case|constrained':sha}))
            (root/'bench.dll').write_bytes(b'changed binary')
            with self.assertRaises(ValueError):runner.run(args)


class PrepareOutputTests(unittest.TestCase):
    def test_prepared_manifest_is_only_case_mode_hashes(self):
        base=prepare.canonical(ET.fromstring('<case><a>LINESTRING (0 0,1 1)</a></case>'))
        job=dict(case='f-1/q20',base_case='f-1',mode='constrained',angle=20,expected_base_hash=hashlib.sha256(base.encode()).hexdigest())
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d);manifest=root/'manifest.json';manifest.write_text(json.dumps(dict(jobs=[job])))
            def case(command,**kw):pathlib.Path(command[command.index('--output')+1]).write_text(base)
            args=types.SimpleNamespace(manifest=manifest,data=root/'data',inputs=root/'inputs',out=root/'prepared.json',dotnet='dotnet',benchmark='b.dll')
            with patch.object(prepare.subprocess,'run',case),contextlib.redirect_stdout(io.StringIO()):prepare.prepare(args)
            text,sha=prepare.variant(base,job)
            self.assertEqual({'f-1/q20|constrained':sha},json.loads(args.out.read_text()))
            self.assertEqual(text,(args.inputs/runner.input_name('f-1/q20','constrained')).read_text())


RESULTS=pathlib.Path(__file__).resolve().parent/'results'/'2026-11-08'


class CommittedEvidenceTests(unittest.TestCase):
    def records(self):
        return [json.loads(line) for c in sorted(RESULTS.glob('results-*.jsonl')) for line in c.read_text().splitlines()]

    def test_chunks_match_evidence_hashes(self):
        evidence=json.loads((RESULTS/'evidence.json').read_text())
        chunks=sorted(RESULTS.glob('results-*.jsonl'))
        self.assertEqual([c['path'] for c in evidence['chunks']],[c.name for c in chunks])
        for entry in evidence['chunks']:
            data=(RESULTS/entry['path']).read_bytes()
            self.assertEqual(entry['sha256'],hashlib.sha256(data).hexdigest(),entry['path'])
            self.assertEqual(entry['records'],len(data.splitlines()),entry['path'])
        self.assertEqual(evidence['results_jsonl_sha256'],hashlib.sha256(b''.join(c.read_bytes() for c in chunks)).hexdigest())

    def test_records_are_compact_and_portable(self):
        for record in self.records():
            for name in ('mesh','oracle'):
                if name not in record:continue
                stage=record[name]
                self.assertFalse({'stdout','stderr'}&set(stage),record['case'])
                self.assertTrue({'violations','stdout_lines','stdout_sha256','stderr_lines','stderr_sha256'}<=set(stage))
                self.assertTrue(stage['command'][0]=='<dotnet>' and stage['command'][1]=='<benchmark>')
            self.assertFalse({'area2','reason'}&set(record.get('row') or {}))
        for path in RESULTS.iterdir():
            text=path.read_text()
            self.assertNotIn('/workspace/',text,path.name);self.assertNotIn('VIOLATION',text,path.name)

    def test_summary_and_verdicts_derive_from_records(self):
        records=self.records()
        self.assertEqual(json.loads((RESULTS/'summary.json').read_text()),runner.summarize(records,len(records)))
        with tempfile.TemporaryDirectory() as d:
            path=pathlib.Path(d)/'verdicts.csv';runner.write_verdicts(records,path)
            self.assertEqual((RESULTS/'verdicts.csv').read_bytes(),path.read_bytes())

    def test_prepared_manifest_lists_the_historical_jobs(self):
        historical=json.loads((RESULTS.parent.parent/'historical-exceptions.json').read_text())
        prepared=json.loads((RESULTS/'prepared-manifest.json').read_text())
        _,jobs=runner.load_jobs(RESULTS.parent.parent/'historical-exceptions.json',RESULTS/'prepared-manifest.json')
        self.assertEqual(len(historical['jobs']),len(prepared))
        by_key={(r['case'],r['mode']):r['input_sha256'] for r in self.records()}
        self.assertEqual(by_key,{(j['case'],j['mode']):j['input_sha256'] for j in jobs})
        for job in jobs:
            if job.get('expected_input_hash'):self.assertEqual(job['expected_input_hash'],job['input_sha256'])


if __name__=='__main__':unittest.main()
