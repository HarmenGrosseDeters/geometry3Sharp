"""Mutate temporary copies of the probe; never edit the checkout or mutation CI tooling."""

from pathlib import Path
import os
import subprocess
import sys
import tempfile


MUTATIONS = (
    ("ignore_second_motion", "return lower > move_a + move_b", "return lower > move_a",
     "test_clearance_equality_is_not_safe"),
    ("forget_new_vertex_identification",
     "return (set(proposed_left) & set(proposed_right)) - (set(left) & set(right))",
     "return set()", "test_shared_tip_is_allowed_but_new_identification_is_not"),
    ("permit_touching_sheets", "return all(upper[i][2] > lower[i][2] for i in range(5))",
     "return all(upper[i][2] >= lower[i][2] for i in range(5))",
     "test_small_budget_only_contact_is_rejected"),
)


def run():
    here = Path(__file__).resolve().parent
    source = (here / 'probe.py').read_text(encoding='utf-8')
    env = dict(os.environ, PYTHONDONTWRITEBYTECODE='1')
    subprocess.run([sys.executable, '-m', 'unittest', 'test_probe'], cwd=here, env=env, check=True)
    for name, old, new, test in MUTATIONS:
        if source.count(old) != 1:
            raise RuntimeError(f'{name}: mutation target must occur exactly once')
        with tempfile.TemporaryDirectory(prefix='g3-crossfield-mutation-') as folder:
            root = Path(folder)
            (root / 'probe.py').write_text(source.replace(old, new), encoding='utf-8')
            (root / 'test_probe.py').write_text((here / 'test_probe.py').read_text(encoding='utf-8'), encoding='utf-8')
            result = subprocess.run([sys.executable, '-m', 'unittest', f'test_probe.ProbeTests.{test}'],
                                    cwd=root, env=env, capture_output=True, text=True)
            # A crash/import failure is not evidence that the intended assertion killed the mutant.
            if result.returncode != 1 or 'FAIL:' not in result.stderr or 'FAILED (failures=1)' not in result.stderr:
                raise RuntimeError(f'{name}: not killed by the intended assertion\n{result.stdout}\n{result.stderr}')
            print(f'{name}: killed by {test}')


if __name__ == '__main__':
    run()
