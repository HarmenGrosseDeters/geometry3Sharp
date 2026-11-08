import hashlib
import json
import pathlib
import tempfile
import types
import unittest
from unittest.mock import patch
import recheck
import recheck_edges


class RecheckTests(unittest.TestCase):
    module = recheck

    def test_runtime_binding_detects_index_or_dependency_manifest_changes(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d)
            for name in ('pilot.dll','NetTopologySuite.dll','pilot.deps.json','pilot.runtimeconfig.json'):(root/name).write_text(name)
            before=self.module.runtime_files(root/'pilot.dll')
            self.assertIn('NetTopologySuite.dll',before)
            (root/'NetTopologySuite.dll').write_text('changed index')
            self.assertNotEqual(before,self.module.runtime_files(root/'pilot.dll'))
            (root/'NetTopologySuite.dll').write_text('NetTopologySuite.dll')
            self.assertEqual(before,self.module.runtime_files(root/'pilot.dll'))
            (root/'pilot.deps.json').write_text('changed dependency graph')
            self.assertNotEqual(before,self.module.runtime_files(root/'pilot.dll'))

    def test_only_complete_baseline_records_are_selected(self):
        with tempfile.TemporaryDirectory() as d:
            path=pathlib.Path(d)/'rows.jsonl'
            line=json.dumps(dict(case='case',mode='constrained',verdict='ORACLE_TIMEOUT'))+'\n'
            path.write_text(line+'{"inflight":')
            rows=self.module.baseline_rows(path)
            self.assertEqual(1,len(rows))
            self.assertEqual(hashlib.sha256(line.encode()).hexdigest(),rows[('case','constrained')][1])
            path.write_text(line+line)
            with self.assertRaises(ValueError):self.module.baseline_rows(path)

    def test_verdict_requires_exit_text_and_same_input_hash(self):
        valid=dict(exit_code=0,stdout='VALID result')
        row=dict(input_sha256='expected')
        self.assertEqual('VALID',self.module.classify(valid,row,'expected'))
        self.assertEqual('ORACLE_ERROR',self.module.classify(valid,None,'expected'))
        self.assertEqual('ORACLE_ERROR',self.module.classify(valid,row,'different'))
        self.assertEqual('ORACLE_ERROR',self.module.classify(dict(valid,exit_code=1),row,'expected'))
        self.assertEqual('ORACLE_TIMEOUT',self.module.classify(dict(timeout=1),row,'expected'))

    def test_identical_cached_mesh_is_judged_with_explicit_pilot_only(self):
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d);(root/'meshes').mkdir()
            source=root/'input.txt';source.write_text('canonical input')
            key=hashlib.sha256(b'case|constrained').hexdigest()[:20]
            mesh=root/'meshes'/(key+'.mesh.txt');mesh.write_text('identical mesh')
            base=dict(case='case',mode='constrained',verdict='ORACLE_TIMEOUT',input_sha256=self.module.digest(source),output_sha256=self.module.digest(mesh),
                      mesh=dict(command=['dotnet','base.dll','mesh','--input',str(source),'--output','old.mesh']),
                      oracle=dict(command=['dotnet','base.dll','oracle','--input',str(source),'--output','old.mesh','--row','old.row']))
            args=types.SimpleNamespace(work=root,dotnet=pathlib.Path('dotnet'),mesh_benchmark=pathlib.Path('base.dll'),pilot_benchmark=pathlib.Path('pilot.dll'),mesh_timeout=1,oracle_timeout=1)
            def stage(command,timeout,environment):
                self.assertEqual('oracle',command[2]);self.assertEqual('pilot.dll',command[1])
                self.assertEqual('strtree',environment['G3_ORACLE_POINT_INDEX'])
                self.assertEqual('strtree-hulls' if self.module is recheck_edges else 'sweep',environment['G3_ORACLE_EDGE_INDEX'])
                pathlib.Path(command[command.index('--row')+1]).write_text(json.dumps(dict(input_sha256=base['input_sha256'])))
                return dict(command=command,exit_code=0,stdout='VALID result',stderr='self-check timing\nORACLE-JUDGE-BEGIN\nORACLE conformity: point-triangle 3.0 ms\n')
            with patch.object(self.module,'stage',stage):result=self.module.judge(base,'baseline-row-hash',args,{})
            self.assertEqual('VALID',result['verdict']);self.assertTrue(result['cached_identical_mesh'])
            self.assertEqual('conformity: point-triangle',result['last_completed_phase'])
            self.assertNotIn('self-check timing',result['oracle']['stderr'])
            mesh.write_text('wrong mesh')
            def wrong_mesh(command,timeout,environment):
                self.assertEqual('mesh',command[2]);mesh.write_text('still different')
                return dict(exit_code=0)
            with patch.object(self.module,'stage',wrong_mesh):result=self.module.judge(base,'baseline-row-hash',args,{})
            self.assertEqual('MESH_MISMATCH',result['verdict'])

    def test_partial_or_empty_summary_does_not_enable_second_worker(self):
        with tempfile.TemporaryDirectory() as d:
            path=pathlib.Path(d)/'summary.json'
            self.assertFalse(self.module.complete(path))
            for text in ('{','{"planned":0,"completed":0}','{"planned":2,"completed":1}'):
                path.write_text(text);self.assertFalse(self.module.complete(path))
            path.write_text('{"planned":2,"completed":2}');self.assertTrue(self.module.complete(path))


class EdgeRecheckTests(RecheckTests):
    module = recheck_edges


if __name__=='__main__':unittest.main()
