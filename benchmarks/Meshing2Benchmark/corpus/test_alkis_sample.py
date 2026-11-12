"""Offline tests of the ALKIS sample rule (no network, no data): python3 -m unittest discover -s benchmarks/Meshing2Benchmark/corpus -p 'test_*.py'"""
import hashlib
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
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


if __name__ == "__main__":
    unittest.main()
