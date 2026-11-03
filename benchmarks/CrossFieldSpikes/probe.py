"""Exact synthetic feasibility probes, not geometry3Sharp implementations or timing benchmarks."""

from fractions import Fraction as F
from itertools import product
import json


def clearance(lower, move_a, move_b):
    if min(lower, move_a, move_b) < 0:
        raise ValueError("Clearance and displacement budgets must be nonnegative")
    return lower > move_a + move_b


def clearance_probe():
    records = []
    for gap in (F(1, 4), F(1), F(4)):
        # Parallel congruent triangles translated toward one another; equality is contact.
        shifts = [gap * F(i, 8) for i in range(9)]
        pairs = list(product(shifts, repeat=2))
        accepted = [(a, b) for a, b in pairs if clearance(gap, a, b)]
        false_accepts = sum(gap - a - b <= 0 for a, b in accepted)
        ignored_b = [(a, b) for a, b in pairs if gap > a]
        records.append({"gap": str(gap), "queries": len(pairs),
                        "certified": len(accepted), "false_accepts": false_accepts,
                        "ignore_second_motion_false_accepts": sum(
                            gap - a - b <= 0 for a, b in ignored_b)})
    return records


def orientation(a, b, c):
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])


def on_segment(p, a, b):
    return orientation(a, b, p) == 0 and all(
        min(a[i], b[i]) <= p[i] <= max(a[i], b[i]) for i in range(2))


def intersects(a, b, c, d):
    o = (orientation(a, b, c), orientation(a, b, d),
         orientation(c, d, a), orientation(c, d, b))
    return (o[0] * o[1] < 0 and o[2] * o[3] < 0) or any(
        on_segment(p, u, v) for p, u, v in ((a, c, d), (b, c, d), (c, a, b), (d, a, b)))


def overlaps_edge(a, b, c, d):
    if a == b or c == d or orientation(a, b, c) or orientation(a, b, d):
        return False
    axis = 0 if a[0] != b[0] else 1
    return max(min(a[axis], b[axis]), min(c[axis], d[axis])) < min(
        max(a[axis], b[axis]), max(c[axis], d[axis]))


def segments(chain):
    return list(zip(chain, chain[1:]))


def contacts(left, right):
    return sum(intersects(a, b, c, d) for (a, b), (c, d) in product(segments(left), segments(right)))


def pinch_fixture(shared_tip=False):
    # Already-snapped polylines. This does NOT run SRR, insert pixels, or emulate the mesher.
    left = [(F(-2), F(-1)), (F(2), F(-1))]
    right = [(F(-2), F(1)), (F(2), F(1))]
    tip = (F(4), F(0))
    if shared_tip:
        left.append(tip)
        right.append(tip)
    proposed_left = [left[0], (F(0), F(0)), *left[1:]]
    proposed_right = [right[0], (F(0), F(0)), *right[1:]]
    return left, right, proposed_left, proposed_right


def new_common_vertices(left, right, proposed_left, proposed_right):
    return (set(proposed_left) & set(proposed_right)) - (set(left) & set(right))


def q17_probe():
    results = []
    for shared in (False, True):
        a, b, new_a, new_b = pinch_fixture(shared)
        results.append({
            "fixture": "shared_tip" if shared else "separate_chains",
            "components_before": 1 if contacts(a, b) else 2,
            "components_after": 1 if contacts(new_a, new_b) else 2,
            "new_common_vertices": len(new_common_vertices(a, b, new_a, new_b)),
            "positive_length_edge_overlaps": sum(
                overlaps_edge(p, q, r, s) for (p, q), (r, s) in product(segments(new_a), segments(new_b))),
        })
    return results


XY = ((-2, -2), (2, -2), (2, 2), (-2, 2), (0, 0))
FACES = ((0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4))


def fan(collar_height, center_height):
    return tuple((F(x), F(y), F(collar_height if i < 4 else center_height))
                 for i, (x, y) in enumerate(XY))


def fan_pair_is_separated(lower, upper):
    # Same XY triangulation: the height difference is affine on each face.
    if len(lower) != 5 or len(upper) != 5:
        raise ValueError("This checker only handles the declared five-vertex fan pair")
    for i, xy in enumerate(XY):
        if lower[i][:2] != xy or upper[i][:2] != xy:
            raise ValueError("This checker requires the declared common XY projection")
    return all(upper[i][2] > lower[i][2] for i in range(5))


def lattice_probe(budget):
    if budget < 0:
        raise ValueError("Budget must be nonnegative")
    low = F(-1, 4)
    high = F(1, 4)
    # Complete enumeration of allowed integer Z for these two centers, with X/Y frozen.
    candidates_low = range((low - budget).__ceil__(), (low + budget).__floor__() + 1)
    candidates_high = range((high - budget).__ceil__(), (high + budget).__floor__() + 1)
    choices = list(product(candidates_low, candidates_high))
    feasible = []
    for a, b in choices:
        if fan_pair_is_separated(fan(-1, a), fan(1, b)):
            feasible.append({"centers": [a, b], "max_displacement": str(max(abs(a-low), abs(b-high)))})
    return {"budget": str(budget), "candidates": len(choices), "feasible": feasible,
            "nearest_rounding_separated": fan_pair_is_separated(fan(-1, 0), fan(1, 0))}


def run():
    return {"scope": "synthetic exact models; no library integration or speed measurement",
            "clearance": clearance_probe(), "q17": q17_probe(),
            "fixed_collar": [lattice_probe(F(1, 4)), lattice_probe(F(3, 4))]}


if __name__ == "__main__":
    print(json.dumps(run(), indent=2, sort_keys=True))
