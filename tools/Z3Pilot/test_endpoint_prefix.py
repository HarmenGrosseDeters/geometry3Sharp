from unittest import TestCase

from check_pilot_obligations import run_test
from endpoint_prefix import run


class EndpointPrefixTests(TestCase):
    def test_weighted_prefix_decisions(self):
        run_test(self, run, 'Z3_ENDPOINT_PREFIX_REPORT')
