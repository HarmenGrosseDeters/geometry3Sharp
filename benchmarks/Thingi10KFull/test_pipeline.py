import hashlib
import json
import subprocess
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import patch
import run_full as legacy
import run_pipeline as pipeline
import prepare_probe
import adopt_legacy
from test_runner import clean_boolean


class PipelineTests(unittest.TestCase):
    def test_single_instance_lease_rejects_a_second_runner_and_releases(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'pipeline.lock'
            with pipeline.RunLease(path):
                with self.assertRaisesRegex(RuntimeError, 'lease'):
                    with pipeline.RunLease(path):
                        self.fail('Second runner admitted')
            with pipeline.RunLease(path):
                pass

    def test_cross_platform_unsafe_names_are_rejected(self):
        for name in ('..', '.', '../outside', '..\\outside', 'C:outside', 'a/b', 'a\\b'):
            self.assertFalse(pipeline.safe_name(name), name)
        self.assertTrue(pipeline.safe_name('123.stl'))

    def test_parallel_pipeline_preserves_wrap_progress_and_resume(self):
        summaries = []
        for evaluators in (1, 2):
            with self.subTest(evaluators=evaluators), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                data = root / 'dataset'
                data.mkdir()
                entries = []
                for name in ('123.off', '456.off', '789.off'):
                    (data / name).write_text('input')
                    entries.append({'path': name, 'size': 5})
                (data / 'inventory.json').write_text(json.dumps({'dataset_revision': 'fixed', 'files': entries}))
                (data / 'geometry_data.csv').write_text('file_id,num_faces\n')
                (root / 'config.json').write_text(json.dumps({'commit': legacy.COMMIT, 'dataset_revision': 'fixed',
                    'binary_sha256': {'library': 'fixed'}, 'pipeline': {'probe_sha256': 'fixed'}}))
                results = root / 'results'
                queue = root / 'queue'
                release = threading.Event()
                wrap_finished = threading.Event()
                wrapped = []
                errors = []
                def wrap(mesh, timeout):
                    wrapped.append(mesh.name)
                    if len(wrapped) == 3:
                        wrap_finished.set()
                    return {'file': mesh.name, 'status': 'closed', 'wrap_status': 'Success', 'ms': 3,
                            'after_finite': True, 'after_valid': True, 'not_clean_reason': '',
                            'not_clean_self_intersections_checked': True}
                def compute(mesh):
                    stage = root / 'scratch' / ('compute-' + mesh.name)
                    stage.mkdir()
                    (stage / 'info.json').write_text('{}')
                    raw = {'file': mesh.name, 'commit': legacy.COMMIT, 'input': {},
                           'compute_wall_ms': 7, 'evaluation_state': 'pending'}
                    return stage, raw, {}
                def evaluate(store, path):
                    if not release.wait(3):
                        raise AssertionError('Wrap did not progress independently')
                    raw = clean_boolean()
                    raw.update(file=path.parent.name, evaluation_state='completed')
                    return raw
                def run():
                    try:
                        pipeline.main()
                    except BaseException as error:
                        errors.append(error)
                with patch.multiple(pipeline, ROOT=root, RESULTS=results, QUEUE=queue,
                        STOP=root / 'STOP', MAX_JOBS=1, RESERVE_BYTES=0, EVALUATORS=evaluators), \
                        patch.object(legacy, 'DATA', data), patch.object(legacy, 'download'), \
                        patch.object(pipeline, 'validate_config'), patch.object(legacy, 'run_wrap', side_effect=wrap), \
                        patch.object(pipeline, 'compute_boolean', side_effect=compute), \
                        patch.object(pipeline, 'evaluate_job', side_effect=evaluate):
                    # Simulate a crash after the manifest but before the computed JSONL append.
                    self.job(root)
                    worker = threading.Thread(target=run)
                    worker.start()
                    try:
                        self.assertTrue(wrap_finished.wait(2), 'Evaluation must not block Wrap')
                        self.assertFalse(release.is_set())
                    finally:
                        release.set()
                        worker.join(5)
                    self.assertFalse(worker.is_alive())
                    self.assertEqual(errors, [])
                    before = (results / 'boolean.jsonl').read_bytes()
                    self.assertEqual(len(pipeline.read_results(results / 'boolean.jsonl')), 3)
                    self.assertFalse(list(queue.glob('*/job.json')))
                    self.assertEqual(len(pipeline.read_results(results / 'computed.jsonl')), 3)
                    # Simulate a crash after the final JSONL append but before queued-artifact cleanup.
                    _, stale = self.job(root, '456.off')
                    pipeline.main()
                    self.assertFalse(stale.exists())
                    self.assertEqual((results / 'boolean.jsonl').read_bytes(), before)
                    self.assertEqual(len(wrapped), 3, 'Resume must not recompute finished Wrap models')
                    summaries.append(sorted([legacy.operation_rows('boolean', raw)
                        for raw in pipeline.read_results(results / 'boolean.jsonl').values()], key=lambda rs: rs[0]['file']))
        self.assertEqual(summaries[0], summaries[1], 'Serial and parallel evaluation must give identical normalized results')

    def job(self, root, name='123.off'):
        directory = root / ('stage-' + name)
        directory.mkdir()
        (directory / 'info.json').write_text('{"file":"' + name + '"}')
        (directory / 'ours_union.off').write_text('OFF\n0 0 0\n')
        raw = clean_boolean()
        raw.update(file=name, commit=legacy.COMMIT, input={'file': name}, compute_wall_ms=17,
                   evaluation_state='pending')
        # A computed-only record must never contain invented evaluation metrics.
        for op in legacy.OPS:
            for suffix in ('finite', 'wclosed', 'manifold_closed', 'degenerate', 'zero_area',
                           'not_clean_reason', 'not_clean_self_intersections_checked', 'selfint', 'tris'):
                raw.pop(op + '_' + suffix)
        runs = {op: {'status': 'ok', 'ms': 5} for op in legacy.OPS}
        store = pipeline.JobStore(root / 'queue', {'library': 'fixed'})
        return store, store.publish(directory, raw, runs)

    def test_durable_publish_recovery_claim_and_integrity(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            store, path = self.job(root)
            recovered = pipeline.JobStore(store.root, store.binary_hashes)
            self.assertEqual(recovered.read(path)['raw']['union_ms'], 5)
            self.assertEqual(recovered.claim(), path)
            self.assertIsNone(recovered.claim())
            recovered.release(path)
            self.assertEqual(recovered.claim(), path)
            bad = pipeline.JobStore(store.root, {'library': 'different'})
            with self.assertRaisesRegex(RuntimeError, 'provenance'):
                bad.read(path)
            artifact = path.parent / 'ours_union.off'
            artifact.write_bytes(b'X' * artifact.stat().st_size)
            with self.assertRaisesRegex(RuntimeError, 'artifact'):
                recovered.read(path)

    def test_incomplete_publication_is_quarantined_not_evaluated(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            store = pipeline.JobStore(root / 'queue', {})
            incomplete = store.root / '456.off'
            incomplete.mkdir()
            (incomplete / 'result.off').write_text('keep')
            self.assertIsNone(store.claim())
            store.recover_incomplete(root / 'quarantine')
            self.assertFalse(incomplete.exists())
            self.assertEqual(next((root / 'quarantine').glob('*/result.off')).read_text(), 'keep')

    def test_capacity_blocks_boolean_not_independent_work_and_can_stop(self):
        with tempfile.TemporaryDirectory() as directory:
            store, path = self.job(Path(directory))
            stop = threading.Event()
            done = threading.Event()
            result = []
            with patch.object(pipeline, 'MAX_JOBS', 1), patch.object(pipeline, 'RESERVE_BYTES', 0):
                worker = threading.Thread(target=lambda: (result.append(store.wait_capacity(stop.is_set, lambda: 0)), done.set()))
                worker.start()
                try:
                    self.assertFalse(done.wait(0.1))
                    # Independent Wrap work can finish while Boolean waits for the queue.
                    independent = threading.Event()
                    threading.Thread(target=independent.set).start()
                    self.assertTrue(independent.wait(1))
                    stop.set()
                    with store.condition:
                        store.condition.notify_all()
                    self.assertTrue(done.wait(2))
                    self.assertEqual(result, [False])
                finally:
                    stop.set()
                    worker.join(2)
            self.assertTrue(path.exists())

    def test_capacity_resumes_after_final_job_removed_and_enforces_byte_budget(self):
        with tempfile.TemporaryDirectory() as directory:
            store, path = self.job(Path(directory))
            done = threading.Event()
            with patch.object(pipeline, 'MAX_QUEUE_BYTES', 1), patch.object(pipeline, 'RESERVE_BYTES', 0):
                worker = threading.Thread(target=lambda: (store.wait_capacity(lambda: False, lambda: 0), done.set()))
                worker.start()
                self.assertFalse(done.wait(0.1))
                store.remove(path)
                self.assertTrue(done.wait(2))
                worker.join(2)

    def test_eval_failure_retains_algorithm_times_and_durable_artifacts(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            store, path = self.job(root)
            with patch.object(pipeline, 'RESULTS', root), patch.object(legacy.cmp, 'evaluate',
                    side_effect=subprocess.TimeoutExpired('eval', 3600)):
                raw = pipeline.evaluate_job(store, path)
            self.assertEqual(raw['evaluation_state'], 'failed')
            for op in legacy.OPS:
                self.assertEqual(raw[op + '_ms'], 5)
                self.assertEqual(legacy.normalize_boolean(raw, op)[0], 'evaluation_failed')
            self.assertTrue(path.exists())
            self.assertEqual(store.read(path)['raw']['evaluation_state'], 'pending')

    def test_pending_is_not_clean_and_success_uses_unchanged_normalizer(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            store, path = self.job(root)
            self.assertNotEqual(legacy.normalize_boolean(store.read(path)['raw'], 'union')[0], 'ok')
            result = subprocess.CompletedProcess([], 0, stdout='', stderr='')
            with patch.object(pipeline, 'RESULTS', root), patch.object(legacy.cmp, 'evaluate', return_value=[clean_boolean()]):
                legacy.LOCAL.last_process = result
                raw = pipeline.evaluate_job(store, path)
            self.assertEqual(raw['evaluation_state'], 'completed')
            self.assertEqual(raw['wall_ms'], 17 + raw['eval_wall_ms'])
            for op in legacy.OPS:
                self.assertEqual(legacy.normalize_boolean(raw, op), ('ok', ''))
            self.assertTrue(path.exists())

    def test_resume_retains_bytes_and_rejects_corruption_or_foreign_commit(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'results.jsonl'
            data = (json.dumps({'file': '123.off', 'commit': legacy.COMMIT}) + '\n').encode()
            path.write_bytes(data)
            self.assertEqual(set(pipeline.read_results(path)), {'123.off'})
            self.assertEqual(path.read_bytes(), data)
            path.write_bytes(data + b'{"file":')
            pipeline.read_results(path)
            self.assertEqual(path.read_bytes(), data)
            path.write_bytes(data.rstrip(b'\n'))
            pipeline.read_results(path)
            self.assertEqual(path.read_bytes(), data)
            for bad in (b'bad\n' + data, b'{"file":"456.off","commit":"foreign"}\n'):
                path.write_bytes(bad)
                with self.assertRaises(RuntimeError):
                    pipeline.read_results(path)

    def test_only_evaluation_command_is_routed_to_probe(self):
        with tempfile.TemporaryDirectory() as directory:
            legacy.LOCAL.logs = Path(directory)
            with patch.object(legacy.LoggedSubprocess, 'run', return_value='result') as run:
                pipeline.ProbeSubprocess.run(['dotnet', 'frozen.dll', 'run', 'A', 'B'])
                self.assertEqual(run.call_args.args[0][1], 'frozen.dll')
                pipeline.ProbeSubprocess.run(['dotnet', 'frozen.dll', 'eval', 'job'])
                self.assertEqual(run.call_args.args[0][1], str(pipeline.PROBE))
                self.assertIn('THINGI_EVAL_PHASE_LOG', run.call_args.kwargs['env'])

    def test_compute_does_not_wait_for_evaluation_and_preserves_outputs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'scratch').mkdir()
            result = subprocess.CompletedProcess([], 0, stdout='{"file":"123.off","tris":4}\n', stderr='')
            with patch.object(pipeline, 'ROOT', root), patch.object(legacy.LoggedSubprocess, 'run', return_value=result), \
                 patch.object(legacy.cmp, 'run_one', return_value={'status': 'ok', 'ms': 7}), \
                 patch.object(legacy.cmp, 'evaluate', side_effect=AssertionError('must be deferred')):
                legacy.LOCAL.last_process = result
                path, raw, runs = pipeline.compute_boolean(Path('123.off'))
            self.assertEqual(raw['evaluation_state'], 'pending')
            self.assertTrue(path.is_dir())
            self.assertEqual(set(runs), set(legacy.OPS))
            self.assertTrue(all(raw[op + '_ms'] == 7 for op in legacy.OPS))


class LegacyAdoptionTests(unittest.TestCase):
    def test_only_complete_proven_outputs_are_adoptable_and_times_are_not_invented(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            job = root / 'scratch'
            logs = root / 'logs'
            job.mkdir()
            logs.mkdir()
            info = {'file': '123.off', 'tris': 4}
            (job / 'info.json').write_text(json.dumps(info))
            (logs / 'export.stdout.log').write_text(json.dumps(info))
            tool = root / 'BooleanComparison.dll'
            pipeline.durable_json(logs / 'export.command.json', {'command': ['dotnet', str(tool), 'export',
                str(root / '123.off'), str(job)], 'wall_ms': 12})
            for op in legacy.OPS:
                (job / ('ours_' + op + '.off')).write_text('OFF\n0 0 0\n')
                (logs / (op + '.stdout.log')).write_text('{"ok":true,"ms":5}')
                pipeline.durable_json(logs / (op + '.command.json'), {'command': ['dotnet', str(tool), 'run',
                    str(job / 'A.off'), str(job / 'B.off'), op, str(job / ('ours_' + op + '.off'))], 'wall_ms': 8})
            raw, runs = adopt_legacy.inspect_job(job, logs, tool)
            self.assertIsNone(raw['compute_wall_ms'])
            self.assertEqual(raw['union_ms'], 5)
            self.assertEqual(raw['union_wall_ms'], 8)
            self.assertEqual(raw['evaluation_state'], 'pending')
            self.assertTrue(all(r['status'] == 'ok' for r in runs.values()))
            (job / 'ours_union.off').unlink()
            with self.assertRaisesRegex(ValueError, 'complete'):
                adopt_legacy.inspect_job(job, logs, tool)
            (job / 'ours_union.off').write_text('OFF\n0 0 0\n')
            (logs / 'union.stdout.log').write_text('{"ok":false,"error":"timeout"}')
            with self.assertRaisesRegex(ValueError, 'complete'):
                adopt_legacy.inspect_job(job, logs, tool)
            with self.assertRaisesRegex(ValueError, 'provenance'):
                adopt_legacy.inspect_job(job, logs, root / 'different.dll')


class ProbePreparationTests(unittest.TestCase):
    def test_instrumentation_is_eval_only_and_anchors_are_guarded(self):
        source = prepare_probe.SOURCE.read_text(encoding='utf-8')
        changed = prepare_probe.instrument(source)
        self.assertEqual(source[:source.index('    static int Eval(')], changed[:changed.index(prepare_probe.TRACE)])
        for name in ('read_soup', 'reconstruct_mesh', 'clean_bowties', 'clean_inverted_components', 'clean_self_intersections'):
            self.assertIn('"' + name + '"', changed)
        self.assertIn('mesh.TriangleCount <= 400000', changed)
        with self.assertRaises(ValueError):
            prepare_probe.instrument(source.replace('    static int Eval(string dir)', '    static int Changed(string dir)'))

    def test_edge_hash_change_preserves_keys_direction_counts_and_frozen_driver(self):
        source = prepare_probe.SOURCE.read_text(encoding='utf-8')
        changed = prepare_probe.instrument(source)
        self.assertIn('new Dictionary<long, int>(EdgeKeyComparer.Instance)', changed)
        self.assertNotIn('new Dictionary<long, int>();', changed)
        start = source.index('                int degenerate = 0;')
        end = source.index('                DMesh3 mesh = new DMesh3();', start)
        self.assertIn(source[start:end], changed)
        with self.assertRaises(ValueError):
            prepare_probe.instrument(source.replace('var edgeUse = new Dictionary<long, int>();',
                                                     'var edgeUse = new Dictionary<long, int>(null);'))

    def test_prepare_requires_new_external_path_and_matching_source(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'probe.cs'
            expected = hashlib.sha256(prepare_probe.SOURCE.read_bytes()).hexdigest()
            with self.assertRaises(ValueError):
                prepare_probe.prepare(path, 'bad')
            self.assertFalse(path.exists())
            prepare_probe.prepare(path, expected)
            self.assertIn('FinishPhase', path.read_text())
            with self.assertRaises(ValueError):
                prepare_probe.prepare(path, expected)


if __name__ == '__main__':
    unittest.main()
