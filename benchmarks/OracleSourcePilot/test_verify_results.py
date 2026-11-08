import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from verify_results import verify


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value))


class ResultAcceptanceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.runs = Path(self.temp.name)/'runs'; self.runs.mkdir()
        self.boundary = Path(self.temp.name)/'boundary'; self.boundary.mkdir()
        inp = b'procedural four-point lens fixture\n'
        mesh = b'mesh2-output 1\nsubsegments -\n'
        report = b'{"Ok":true,"TieEdges":0}'
        (self.runs/'lens-3.input.txt').write_bytes(inp)
        (self.runs/'lens-3.mesh.txt').write_bytes(mesh)
        fixture = dict(input_sha256=sha(inp), mesh_sha256=sha(mesh), mesher_runtime={'mesh.dll':'producer'})
        write(self.runs/'lens-3.fixture.json',fixture)
        expected = sha(b'1\n3\n4294967298\n8589934595')
        for label, mode in [('baseline-unmodified',None),('grid-final','grid'),('scan-final','scan')]:
            runtime = {'oracle.dll':'original' if mode is None else 'indexed'}
            write(self.runs/(label+'.metadata.json'), dict(controller_sha256='driver',mesher_runtime=fixture['mesher_runtime'],
                  sizes=[3],full_max_points=4,repeats=1,mode=mode,pilot_runtime=runtime))
            common=dict(m=3,points=4,repeat=0,input_sha256=sha(inp),mesh_sha256=sha(mesh),exit_code=0)
            phase=dict(common,kind='phase',output=[dict(phase='sources-begin',judgment_complete=False),
                       dict(phase='sources-complete',judgment_complete=False,constraint_edges=4,fingerprint=expected)])
            full=dict(common,kind='full',output=[dict(judgment_complete=True,Ok=True)],report_sha256=sha(report))
            (self.runs/(label+'.jsonl')).write_text(json.dumps(phase)+'\n'+json.dumps(full)+'\n')
            (self.runs/f'{label}-3-0.report.json').write_bytes(report)
        overlay=mesh.replace(b'subsegments -\n',b'subsegments 4\n0 1 0\n1 2 0\n2 3 0\n3 0 0\n')
        (self.boundary/'reported-boundary.mesh.txt').write_bytes(overlay)
        write(self.boundary/'boundary-control.metadata.json',dict(before_runtime={'oracle.dll':'original'},
              after_runtime={'oracle.dll':'indexed'},input_sha256=sha(inp),original_mesh_sha256=sha(mesh),reported_mesh_sha256=sha(overlay)))
        result={'reports_equal':True}
        for name in ('original_oracle_reported_boundary','indexed_oracle_original_mesh'):
            (self.boundary/(name+'.report.json')).write_bytes(report)
            result[name]=dict(exit_code=0,output=[dict(judgment_complete=True,Ok=True)],report_sha256=sha(report))
        write(self.boundary/'boundary-control.json',result)

    def rows(self):
        return [json.loads(l) for l in (self.runs/'grid-final.jsonl').read_text().splitlines()]

    def save_rows(self, rows):
        (self.runs/'grid-final.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in rows))

    def test_equal_reports_and_expected_boundary_are_accepted(self):
        result=verify(self.runs,self.boundary)
        self.assertEqual(result['complete_small_report_cases'],1)
        self.assertTrue(result['large_semantic_reports_equal'])

    def test_changed_diagnostic_is_rejected_even_with_updated_integrity_hash(self):
        report=b'{"Ok":true,"TieEdges":1}'
        (self.runs/'grid-final-3-0.report.json').write_bytes(report)
        rows=self.rows(); rows[1]['report_sha256']=sha(report); self.save_rows(rows)
        with self.assertRaisesRegex(ValueError,'Full report differs across source variants'):
            verify(self.runs,self.boundary)

    def test_wrong_phase_boundary_is_rejected_even_with_success_status(self):
        rows=self.rows(); rows[0]['output'][-1]['fingerprint']='0'*64; self.save_rows(rows)
        with self.assertRaisesRegex(ValueError,'Phase inferred boundary differs'):
            verify(self.runs,self.boundary)

    def test_duplicate_records_are_rejected(self):
        rows=self.rows(); rows.append(rows[0]); self.save_rows(rows)
        with self.assertRaisesRegex(ValueError,'Missing/duplicate/unexpected records'):
            verify(self.runs,self.boundary)

    def test_boundary_control_cannot_change_unreported_geometry(self):
        path=self.boundary/'reported-boundary.mesh.txt'
        path.write_bytes(path.read_bytes()+b'unexpected change\n')
        meta=json.loads((self.boundary/'boundary-control.metadata.json').read_text())
        meta['reported_mesh_sha256']=sha(path.read_bytes()); write(self.boundary/'boundary-control.metadata.json',meta)
        with self.assertRaisesRegex(ValueError,'Boundary overlay differs'):
            verify(self.runs,self.boundary)


if __name__ == '__main__':
    unittest.main()
