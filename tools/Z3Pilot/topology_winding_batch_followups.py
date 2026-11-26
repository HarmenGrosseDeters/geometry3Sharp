"""Boundary obligations for edge flips, winding culling and cancelled batches."""
import hashlib
import re

import z3 as z

from pilot import ROOT, check

SHORT_MAX = 2**15 - 1
BLOCK_SIZE = 1024


def pin(path, start, end, fingerprint):
    source = (ROOT / path).read_text(encoding='utf-8-sig')
    first = source.index(start)
    last = source.index(end, first + len(start))
    section = re.sub(r'\s+', '', re.sub(r'//[^\n]*', '', source[first:last]))
    if hashlib.sha256(section.encode()).hexdigest() != fingerprint:
        raise ValueError(f'{path}: review the follow-up model near {start}')


def refcounts():
    pin('core/RefCountVector.cs', 'public int increment(', 'public bool allocate_at(', '1a14906abfed1452cdb889892b44df3df1f58d8e4761c83e12c79c4c8dab5c23')
    pin('mesh/DMesh3_edge_operators.cs', 'public MeshResult FlipEdge(int eab', 'void debug_fail(', '7b86090d22beb41d51f2e7f438315114cb863d374f50dc8faf2c9c39a8d46b75')
    c, d = z.BitVecs('c_before d_before', 16)
    c_new, d_new = c + 1, d + 1
    valid = [c > 0, d > 0]
    over_capacity = z.Or(c == SHORT_MAX, d == SHORT_MAX)
    # Sign extension after the increment is the stored Int16 result.
    # Extending before increment is a nonwrapping exact reference.
    incorrect = z.Or(c_new <= 0, d_new <= 0,
                     z.SignExt(1, c_new) != z.SignExt(1, c) + 1,
                     z.SignExt(1, d_new) != z.SignExt(1, d) + 1)
    # A and B each begin with two local triangles plus the vertex's
    # own reference. Removing one triangle cannot free either vertex.
    a, b = z.Ints('a_before b_before')
    a_b = [3 <= a, a <= SHORT_MAX, 3 <= b, b <= SHORT_MAX]
    return [
        ('valid_refcount_wraps_on_flip', valid + [c == SHORT_MAX],
         z.And(c_new == -(2**15), incorrect), 'sat'),
        ('candidate_capacity_guard_allows_an_invalid_flip_increment',
         valid + [z.Not(over_capacity)], incorrect, 'unsat'),
        ('counter_corruption_before_the_capacity_boundary', valid,
         z.Xor(over_capacity, incorrect), 'unsat'),
        ('decrement_can_free_an_original_diagonal_endpoint', a_b,
         z.Or(a - 1 < 2, b - 1 < 2), 'unsat'),
    ]


def winding_geometry():
    path = 'mesh_ops/ExactMeshArrangement.cs'
    pin(path, 'void cull(List<AxisAlignedBox3d>[]', 'internal int CullTests', '349c16fc1e938b6a926d050701596313f688e7c7a9c69897d2035c294b8cfc4c')
    pin(path, 'static bool overlaps(double[] b, int i', '[ThreadStatic] static int[] queryStack', 'fbc751949850c3bf7788105abb3a983b3e9ef138fb5d273b44ac67edade4d52f')
    pin(path, 'public bool Any(AxisAlignedBox3d query)', '/// <summary> call f for every box', '172a3efd48dbfcad1e36aa7e410c79dc7428ad939f11c57a3e0ce6c65e9255af')
    a, b, c, u, v, p = z.Reals('vertex_a vertex_b vertex_c bary_u bary_v point')
    low, high, q0, q1 = z.Reals('box_low box_high query_low query_high')
    # If all three triangle coordinates lie in their interval, so
    # does the coordinate of every point on the closed triangle.
    triangle = [z.And(low <= t, t <= high) for t in (a, b, c)]
    triangle += [u >= 0, v >= 0, u + v <= 1, p == (1 - u - v) * a + u * b + v * c]
    # The 1D statements apply independently on x, y and z. A single
    # disjoint coordinate suffices to reject an AABB overlap.
    disjoint = z.Or(high < q0, low > q1)
    parent_low, parent_high = z.Reals('parent_low parent_high')
    enclosing = [parent_low <= low, low <= high, high <= parent_high,
                 q0 <= q1, low <= q1, high >= q0]

    i, parent, grandparent = z.Ints('triangle_id parent grandparent')
    lower_root, higher_root = z.Ints('lower_root higher_root')
    root_before, root_at_parent, root_at_grandparent = z.Ints('root_before root_at_parent root_at_grandparent')
    rank = [i > parent, parent >= grandparent, grandparent >= 0,
            root_before == root_at_parent, root_at_parent == root_at_grandparent]
    return [
        ('finite_triangle_can_leave_its_closed_aabb', triangle, z.Or(p < low, p > high), 'unsat'),
        ('strictly_disjoint_slab_can_contain_triangle_point', triangle + [disjoint],
         z.And(q0 <= p, p <= q1), 'unsat'),
        ('overlapping_child_can_be_dropped_by_parent_box', enclosing,
         z.Or(parent_high < q0, parent_low > q1), 'unsat'),
        ('touching_slab_mutant_can_drop_contact', [low < high, high == q0, q0 < q1],
         z.And(z.Not(disjoint), high <= q0), 'sat'),
        ('path_halving_can_increase_rank_or_change_root', rank,
         z.Or(grandparent >= i, root_before != root_at_grandparent), 'unsat'),
        ('smaller_root_link_can_violate_decrease', [0 <= lower_root, lower_root < higher_root],
         lower_root >= higher_root, 'unsat'),
    ]


def cancellation():
    path = 'spatial/MeshBatchQueries.cs'
    pin(path, 'const int BlockSize =', 'public static BatchStatus IntersectBatch', '5837ed9d4679e7653d90d27a708d762cb68d3235e2dec3b03f78808e70773630')
    pin(path, 'static BatchStatus Execute(', 'internal static int[] RayOrder(', '84084e6c2e38acef84766ca0f75c1a7b86a2b8b060c5484d42013e656e79a504')
    pin(path, 'static int[] SortedOrder(', 'static double HitParameter(', 'ad799dbb5a99231ad036db2a1052ecd3d6f311f32b5ed98cbbe57e62c1ad3640')

    n, i, j = z.Ints('n loop_index other_loop_index')
    pre_cancelled = z.Bool('pre_cancelled')
    observed = z.Array('observed_cancellation_at_block_entry', z.IntSort(), z.BoolSort())
    order = z.Array('order', z.IntSort(), z.IntSort())
    inverse = z.Array('inverse_order', z.IntSort(), z.IntSort())
    ci, cj = z.Select(observed, i / BLOCK_SIZE), z.Select(observed, j / BLOCK_SIZE)
    a, b = z.Select(order, i), z.Select(order, j)
    domain = [0 <= i, i < n, 0 <= j, j < n,
              z.Select(inverse, a) == i, z.Select(inverse, b) == j]
    writes_i = z.And(z.Not(pre_cancelled), z.Not(ci))
    writes_j = z.And(z.Not(pre_cancelled), z.Not(cj))
    # A completed block runs its callbacks with no further token
    # reads. The status flag only records an observation before a block.
    late = z.Bool('token_cancelled_after_last_block_entry')
    first_observation = z.Select(observed, 0)
    next_observation = z.Select(observed, 1)
    one_block = [n == BLOCK_SIZE, z.Not(pre_cancelled), z.Not(first_observation), late]
    two_blocks = [n == BLOCK_SIZE + 1, z.Not(pre_cancelled),
                  z.Not(first_observation), next_observation]
    return [
        ('pre_cancelled_batch_can_write', domain + [pre_cancelled], writes_i, 'unsat'),
        ('skipped_slot_can_be_written_by_a_different_admitted_index', domain + [ci, writes_j], a == b, 'unsat'),
        ('two_admitted_indices_can_alias_one_slot', domain + [i != j, writes_i, writes_j], a == b, 'unsat'),
        ('late_token_can_leave_valid_index_unwritten_in_one_admitted_block',
         domain + one_block, z.Not(writes_i), 'unsat'),
        ('observed_second_block_cancellation_can_admit_that_block',
         domain + two_blocks + [i >= BLOCK_SIZE], writes_i, 'unsat'),
        ('late_token_with_all_writes_and_ok_is_reachable', domain + one_block, writes_i, 'sat'),
        ('second_block_skip_leaves_first_admitted', domain + two_blocks + [i == 0, j == BLOCK_SIZE],
         z.And(writes_i, z.Not(writes_j)), 'sat'),
    ]


PILOTS = dict(refcounts=refcounts, winding_geometry=winding_geometry, cancellation=cancellation)


def run(timeout_ms=120000):
    reports = {}
    for pilot, spec in PILOTS.items():
        rows = []
        for name, assumptions, bad, expected in spec():
            _, _, row = check(name, assumptions, bad, timeout_ms)
            row['expected'] = expected
            rows.append(row)
        reports[pilot] = dict(passed=all(row['result'] == row['expected'] for row in rows),
                              obligations=rows)
    return dict(z3=z.get_version_string(), passed=all(r['passed'] for r in reports.values()),
                pilots=reports)
