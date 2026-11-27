import json
import os
import pathlib
import shutil
import tempfile
import unittest

import may_meet_box

TIMEOUT_MS = int(os.environ.get('Z3_PILOT_TIMEOUT_MS', '120000'))
ROOT = pathlib.Path(__file__).resolve().parents[2]


class MayMeetBoxTests(unittest.TestCase):
    def test_separation_lemmas_mutants_and_replays(self):
        report = may_meet_box.run(TIMEOUT_MS)
        out = os.environ.get('Z3_MAY_MEET_BOX_REPORT')
        if out:
            pathlib.Path(out).write_text(json.dumps(report, indent=2) + '\n')
        self.assertTrue(report['passed'], json.dumps(report, indent=1))
        self.assertEqual(dict(factor=3, eta_units=4), report['contract'])
        self.assertEqual(3, report['smallest_factor'])

    def test_source_coupling(self):
        d = pathlib.Path(tempfile.mkdtemp())
        try:
            (d / may_meet_box.SOURCE).parent.mkdir(parents=True)
            text = (ROOT / may_meet_box.SOURCE).read_text(encoding='utf-8')
            (d / may_meet_box.SOURCE).write_text(text, encoding='utf-8')
            self.assertEqual(3, may_meet_box.source_contract(d)['factor'])
            # a smaller err is read from the source, and the lemma with it fails
            old = 'double err = 4 * Epsilon * 3 * M + 4 * double.Epsilon;'
            (d / may_meet_box.SOURCE).write_text(text.replace(old, old.replace('* 3 *', '* 2 *')), encoding='utf-8')
            c = may_meet_box.source_contract(d)
            m = may_meet_box.Model(c['factor'], c['eta_units'], False)
            _, r, _ = may_meet_box._check(m.c + [m.decides, may_meet_box.z.Not(m.claim)], TIMEOUT_MS)
            self.assertEqual('sat', str(r))
            # a changed comparison is not described by the model
            (d / may_meet_box.SOURCE).write_text(text.replace('bmax + err < tmin - err', 'bmax < tmin - err'), encoding='utf-8')
            with self.assertRaises(ValueError):
                may_meet_box.source_contract(d)
        finally:
            shutil.rmtree(d)


if __name__ == '__main__':
    unittest.main()
