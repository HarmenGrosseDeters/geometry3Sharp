"""Offline tests of the ALKIS sample rule (no network, no data): python3 -m unittest discover -s benchmarks/Meshing2Benchmark/corpus -p 'test_*.py'"""
import contextlib
import errno
import hashlib
import io
import json
import os
import socket
import sys
import tempfile
import types
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import alkis_run as runner  # noqa: E402
import alkis_tiles as tiles  # noqa: E402
import alkis_sample as sample  # noqa: E402


class FakeStats:
    """A district of 40 x 40 cells whose measures come from a hash, so that every stratum has candidates."""

    def __init__(self, tag):
        self.nb, self.np, self.cells, self._use, self._big = {}, {}, [], {}, {}
        uses = ["Wohnbaufläche", "Fläche gemischter Nutzung", "Landwirtschaft", "Wald", "Fließgewässer",
                "Industrie- und Gewerbefläche", "Tagebau, Grube, Steinbruch", "Weg"]
        for i in range(40):
            for j in range(40):
                cell = (300000 + 200 * i, 5600000 + 200 * j)
                h = int(hashlib.sha256(f"{tag}{cell}".encode()).hexdigest(), 16)
                self.cells.append(cell)
                self.nb[cell] = (h >> 8) % 260 if h % 3 else (h >> 8) % 3
                self.np[cell] = (h >> 16) % 180
                self._use[cell] = uses[(h >> 24) % len(uses)]
                self._big[cell] = 2_000_000 if (h >> 32) % 20 == 0 else 0

    def use(self, cell):
        return self._use[cell]

    def big(self, cell):
        return self._big[cell]


class SampleRule(unittest.TestCase):
    def setUp(self):
        self.stats = {}
        self.stats_of = lambda d: self.stats.setdefault(d, FakeStats(d))

    def draw(self, **kw):
        return sample.select_tiles(self.stats_of, **kw)

    def test_full_sample_has_every_stratum_and_no_cell_twice(self):
        full = self.draw()
        self.assertEqual({p[0] for p in full}, set(sample.STRATA))
        self.assertEqual(len(full), sum(k for k, _, _ in sample.STRATA.values()))
        cells = [(p[1], p[2]) for p in full]
        self.assertEqual(len(cells), len(set(cells)))
        for name, d, cell, nb, np_, use, big in full:      # every tile is in the stratum it was drawn for
            self.assertEqual(sample.classify(nb, np_, use, big), name)

    def test_only_is_a_subset_of_the_full_sample(self):
        full = self.draw()
        for only in (["centre"], ["suburb", "forest"], ["tiny", "huge", "river"], ["industrial"]):
            part = self.draw(only=only)
            self.assertEqual(part, [p for p in full if p[0] in only], only)

    def test_first_is_a_subset_of_the_full_sample(self):
        full = self.draw()
        for n in (1, 2, 3):
            part = self.draw(first=n)
            expected = [p for name in sample.STRATA for p in [q for q in full if q[0] == name][:n]]
            self.assertEqual(part, expected, n)
        self.assertEqual(self.draw(only=["centre"], first=1), [p for p in full if p[0] == "centre"][:1])

    def test_draw_is_deterministic(self):
        self.assertEqual(self.draw(), sample.select_tiles(lambda d: FakeStats(d)))

    def test_seed_changes_the_draw(self):
        full = self.draw()
        old = sample.SEED
        try:
            sample.SEED = old + "-other"
            self.assertNotEqual(self.draw(), full)
        finally:
            sample.SEED = old


class CacheKey(unittest.TestCase):
    """The key of the CI cache depends on the sample's districts and on nothing else of the host's listing."""

    def listing(self, d, **changes):
        districts = sorted({x for _, ds, _ in sample.STRATA.values() for x in ds})
        files = [{"name": f"gru_vereinf_{x}_City{x}_EPSG25832_GeoPackage.zip", "size": "1000", "timestamp": "2026-07-29T12:00:00"} for x in districts]
        for f in files:
            f.update(changes.get(f["name"].split("_")[2], {}))
        json.dump({"datasets": [{"files": files}]}, open(os.path.join(d, "index.json"), "w"))

    def key(self, d, first=1):
        return sample.cache_key(types.SimpleNamespace(cache=d, first=first))

    def test_depends_on_the_sample_districts_only(self):
        used = sample.STRATA["tiny"][1][0]                                  # tile 0 of a stratum: in the --first 1 sample
        unused = sample.STRATA["tiny"][1][3]                                # only tile 3 (DORTMUND): not in --first 1 by tiny alone
        only_late = [x for x in {d for _, ds, _ in sample.STRATA.values() for d in ds}
                     if x not in {ds[0] for _, ds, _ in sample.STRATA.values()}]
        self.assertTrue(only_late)
        with tempfile.TemporaryDirectory() as d:
            self.listing(d)
            base = self.key(d)
            self.assertEqual(base, self.key(d))
            self.listing(d, **{only_late[0]: {"size": "2000"}})
            self.assertEqual(base, self.key(d), "a district outside the --first 1 sample must not change the key")
            self.listing(d, **{used: {"timestamp": "2027-01-01T00:00:00"}})
            self.assertNotEqual(base, self.key(d), "a republished district of the sample must change the key")
            self.listing(d)
            self.assertNotEqual(base, self.key(d, first=0), "the full sample uses more districts")
            del unused


class KnownFindings(unittest.TestCase):
    """The ratchet of the scheduled job: new finding, fixed finding, missing result, empty draw."""

    def setUp(self):
        self.dir = tempfile.mkdtemp()
        names = ["m2_1_2s", "base_1_2_q20s"]
        self.manifest = [{"stratum": "a", "tile": "1_2", "files": {n: "x" for n in names}}]
        self.names = names

    def run_check(self, results, known, expect=None, only=None):
        path = os.path.join(self.dir, "results.jsonl")
        with open(path, "w") as f:
            for name, verdict, flags in results:
                f.write(json.dumps({"stratum": "a", "name": name, "verdict": verdict, "flags": flags, "detail": ""}) + "\n")
        kp = os.path.join(self.dir, "known.json")
        json.dump(known, open(kp, "w"))
        args = types.SimpleNamespace(known=kp, only=only, expect_runs=expect)
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = runner.check_known(args, self.manifest, path)
        return code, out.getvalue()

    def valid(self):
        return [(n, "VALID", []) for n in self.names]

    def test_all_valid_holds(self):
        self.assertEqual(self.run_check(self.valid(), {}, expect=2)[0], 0)

    def test_new_finding_fails(self):
        code, out = self.run_check([(self.names[0], "VALID", []), (self.names[1], "INVALID", ["INVALID"])], {})
        self.assertEqual(code, 1)
        self.assertIn("NEW FINDING base_1_2_q20s", out)

    def test_known_finding_holds_and_a_fixed_one_fails(self):
        bad = [(self.names[0], "VALID", []), (self.names[1], "INVALID", ["INVALID"])]
        self.assertEqual(self.run_check(bad, {self.names[1]: "#1"})[0], 0)
        code, out = self.run_check(self.valid(), {self.names[1]: "#1"})
        self.assertEqual(code, 1)
        self.assertIn("FIXED base_1_2_q20s", out)

    def test_slow_mesher_and_steiner_hit_are_findings(self):
        self.assertEqual(self.run_check([(self.names[0], "VALID", ["SLOW"]), (self.names[1], "VALID", [])], {})[0], 1)
        self.assertEqual(self.run_check([(self.names[0], "VALID", []), (self.names[1], "VALID", ["STEINER"])], {})[0], 1)
        self.assertEqual(self.run_check([(self.names[0], "VALID", ["SLOW-ORACLE"]), (self.names[1], "VALID", [])], {})[0], 0)

    def test_missing_result_fails(self):
        code, out = self.run_check([(self.names[0], "VALID", [])], {})
        self.assertEqual(code, 1)
        self.assertIn("no result", out)

    def test_empty_or_partial_draw_fails(self):
        self.manifest = []
        self.assertEqual(self.run_check([], {})[0], 1)
        self.manifest = [{"stratum": "a", "tile": "1_2", "files": {n: "x" for n in self.names}}]
        self.assertEqual(self.run_check(self.valid(), {}, expect=50)[0], 1)


class FakeResponse:
    """What urlopen returns: a context manager with read(); `fail_after` bytes are delivered, then the error is raised."""

    def __init__(self, data, error=None):
        self.data, self.error, self.pos = data, error, 0

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False

    def read(self, n=-1):
        if self.error is not None and self.pos >= len(self.data) // 2:
            raise self.error
        chunk = self.data[self.pos:self.pos + (n if n and n > 0 else len(self.data))]
        self.pos += len(chunk)
        return chunk


class Download(unittest.TestCase):
    """The district archives are large; a transient network error must not fail the night (run 43312208041: connection reset), and a
    stalled download must not hang the job."""

    def run_get(self, outcomes, attempts=5):
        """outcomes: one per call of urlopen, an exception to raise at the open, ("read", exception) to raise in the middle of the
        read, or None for success."""
        calls, pauses, timeouts = [], [], []

        def fake(url, timeout=None):
            timeouts.append(timeout)
            outcome = outcomes[len(calls)] if len(calls) < len(outcomes) else None
            calls.append(url)
            if isinstance(outcome, BaseException):
                raise outcome
            if isinstance(outcome, tuple):
                return FakeResponse(b"x" * 100, outcome[1])
            return FakeResponse(b"zip")

        old = tiles.urllib.request.urlopen
        tiles.urllib.request.urlopen = fake
        try:
            with tempfile.TemporaryDirectory() as d:
                path = os.path.join(d, "sub", "a.zip")
                try:
                    tiles.get("https://example.invalid/a.zip", path, attempts=attempts, pause=5, sleep=pauses.append)
                    result = open(path, "rb").read()
                except Exception as e:                  # noqa: BLE001
                    result = e
                leftover = os.path.exists(path + ".part")
        finally:
            tiles.urllib.request.urlopen = old
        return result, len(calls), pauses, leftover, timeouts

    def http(self, code):
        return tiles.urllib.error.HTTPError("https://example.invalid", code, "x", {}, None)

    def test_retries_a_reset_connection(self):
        result, calls, pauses, leftover, _ = self.run_get([ConnectionResetError(104, "reset"), tiles.urllib.error.URLError("reset")])
        self.assertEqual(result, b"zip")
        self.assertEqual((calls, pauses, leftover), (3, [5, 10], False))

    def test_a_connection_reset_in_the_middle_of_the_body_is_retried_from_the_start(self):
        result, calls, pauses, leftover, _ = self.run_get([("read", ConnectionResetError(104, "reset"))])
        self.assertEqual((result, calls, pauses, leftover), (b"zip", 2, [5], False))

    def test_a_stalled_download_times_out_and_is_retried(self):
        # urlopen gets a socket timeout (120 s), so a download without bytes raises TimeoutError instead of hanging
        result, calls, pauses, leftover, timeouts = self.run_get([("read", TimeoutError("timed out")), socket.timeout("timed out")])
        self.assertEqual((result, calls, pauses, leftover), (b"zip", 3, [5, 10], False))
        self.assertEqual(set(timeouts), {120})

    def test_gives_up_after_the_last_attempt(self):
        result, calls, pauses, leftover, _ = self.run_get([ConnectionResetError(104, "reset")] * 5, attempts=3)
        self.assertIsInstance(result, ConnectionResetError)
        self.assertEqual((calls, len(pauses), leftover), (3, 2, False))

    def test_a_client_error_is_not_retried(self):
        result, calls, pauses, _, _ = self.run_get([self.http(404)])
        self.assertIsInstance(result, tiles.urllib.error.HTTPError)
        self.assertEqual((calls, pauses), (1, []))

    def test_a_server_error_and_too_many_requests_are_retried(self):
        for code in (503, 429):
            result, calls, _, _, _ = self.run_get([self.http(code)])
            self.assertEqual((result, calls), (b"zip", 2), code)

    def test_a_full_disk_is_not_retried(self):
        for where in (OSError(errno.ENOSPC, "No space left on device"), ("read", OSError(errno.ENOSPC, "No space left on device"))):
            result, calls, pauses, leftover, _ = self.run_get([where])
            self.assertIsInstance(result, OSError)
            self.assertEqual(result.errno, errno.ENOSPC)
            self.assertEqual((calls, pauses, leftover), (1, [], False))


if __name__ == "__main__":
    unittest.main()
