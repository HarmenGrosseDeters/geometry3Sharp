"""Focused mutation smoke; runtime patches never modify the measured library or source."""
import argparse
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import run_pipeline as pipeline
import test_pipeline


def main(report):
    cases = [
        ('artifact hashes ignored', patch.object(pipeline, 'digest', return_value='ignored'),
         'test_durable_publish_recovery_claim_and_integrity'),
        ('pending validation reported successful', patch.object(pipeline.legacy, 'normalize_boolean', return_value=('ok', '')),
         'test_pending_is_not_clean_and_success_uses_unchanged_normalizer'),
        ('backpressure bypassed', patch.object(pipeline.JobStore, 'wait_capacity', return_value=True),
         'test_capacity_blocks_boolean_not_independent_work_and_can_stop'),
        ('manifest persistence omitted', patch.object(pipeline, 'durable_json'),
         'test_durable_publish_recovery_claim_and_integrity'),
    ]
    results = []
    for name, mutation, test in cases:
        with mutation:
            result = unittest.TextTestRunner(stream=io.StringIO()).run(
                unittest.TestSuite([test_pipeline.PipelineTests(test)]))
        assert not result.wasSuccessful(), 'Mutation survived: ' + name
        results.append({'mutation': name, 'test': test, 'detected': True})
        print('Detected mutation: ' + name)
    report.write_text(json.dumps({'command': 'python pipeline_mutations.py --report <external-json>',
                                 'mutations': results}, indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    main(args.report)
