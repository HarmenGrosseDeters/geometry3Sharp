"""Local SMT checks for DMesh edge flips, winding decisions and batch queries."""
import hashlib
import re

import z3 as z

from pilot import F, ROOT, check


def pin(path, start, end, expected):
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    first = text.index(start)
    last = text.index(end, first + len(start))
    code = re.sub(r'\s+', '', re.sub(r'//[^\n]*', '', text[first:last]))
    if hashlib.sha256(code.encode()).hexdigest() != expected:
        raise ValueError(f'{path}: review the model at {start}')


def results(obligations, timeout_ms):
    rows = []
    for name, assumptions, bad, expected in obligations:
        _, _, row = check(name, assumptions, bad, timeout_ms)
        row['expected'] = expected
        rows.append(row)
    return dict(z3=z.get_version_string(), obligations=rows,
                passed=all(row['result'] == row['expected'] for row in rows))


def same_edge(pair, other):
    a, b = pair
    x, y = other
    return z.Or(z.And(a == x, b == y), z.And(a == y, b == x))


def triangle_edges(triangle):
    a, b, c = triangle
    return [(a, b), (b, c), (c, a)]


def replace_owner(pair, old, new):
    first, second = pair
    return z.If(first == old, new, first), z.If(first == old, second, new)


def mesh_flip(timeout_ms):
    """The successful flip of an already valid oriented two-face neighborhood."""
    path = 'mesh/DMesh3_edge_operators.cs'
    pin(path, 'public MeshResult FlipEdge(int eab', 'void debug_fail(', '7b86090d22beb41d51f2e7f438315114cb863d374f50dc8faf2c9c39a8d46b75')
    pin(path, 'void set_triangle(int tid', 'int add_edge(', '1f1be50a8ee2f4c0b578064c4e75bd0ce990c95a4d55115d919938b952d596d9')
    pin(path, 'int replace_edge_triangle(', 'int replace_triangle_edge(', '1b80f012439d0101b8554065436d2a1e4b9d77dd408d641b6131c76b13a175ab')

    pin(path, 'void set_edge_vertices(', 'int replace_edge_vertex(', '1b9e6282bd7212a4951435a1d69cf13dc3c879a6debf6d834cc36598b953ccd2')

    a, b, c, d, t0, t1 = z.Ints('a b c d t0 t1')
    external = z.Ints('other_bc other_ca other_ad other_db')
    local_owner_first = z.Bools('bc_first ca_first ad_first db_first')
    valid = [z.Distinct(a, b, c, d), z.Distinct(t0, t1), t0 >= 0, t1 >= 0]
    valid += [v >= 0 for v in (a, b, c, d)]
    valid += [z.And(owner >= -1, owner != t0, owner != t1, z.Implies(owner == -1, first))
              for owner, first in zip(external, local_owner_first)]

    # Use symbolic outer owners (boundary sentinel or another face).
    old_owners = [t0, t0, t1, t1]
    outer_slots = [(z.If(first, local, other), z.If(first, other, local))
                   for first, local, other in zip(local_owner_first, old_owners, external)]
    ebc, eca, ead, edb = outer_slots
    diagonal = z.If(c < d, c, d), z.If(c < d, d, c)
    endpoints = [diagonal, (b, c), (c, a), (a, d), (d, b)]
    new_owners = [(t0, t1), ebc, replace_owner(eca, t0, t1),
                  ead, replace_owner(edb, t1, t0)]
    old_faces = [(a, b, c), (b, a, d)]
    new_faces = [(c, d, b), (d, c, a)]
    # The actual set_triangle_edges slot order from the source.
    actual_order = [(0, 4, 1), (0, 2, 3)]
    slot_mismatches = []
    ownership_mismatches = []
    for face, tri_id, slots in zip(new_faces, (t0, t1), actual_order):
        expected_edges = triangle_edges(face)
        slot_mismatches += [z.Not(same_edge(endpoints[key], expected))
                            for key, expected in zip(slots, expected_edges)]
        for key, (front, back) in enumerate(new_owners):
            expected_owner = z.Or(*(same_edge(endpoints[key], edge) for edge in expected_edges))
            ownership_mismatches.append(expected_owner != z.Or(front == tri_id, back == tri_id))
    outer_owners_lost = [z.And(other != slots[0], other != slots[1])
                         for other, slots in zip(external, new_owners[1:])]
    refcount_mismatch = []
    for v, delta in ((a, -1), (b, -1), (c, 1), (d, 1)):
        old_incidence = z.Sum([z.If(z.Or(*(v == p for p in face)), 1, 0) for face in old_faces])
        new_incidence = z.Sum([z.If(z.Or(*(v == p for p in face)), 1, 0) for face in new_faces])
        refcount_mismatch.append(z.Or(old_incidence + delta != new_incidence, new_incidence < 1))

    # Additionally check that the orientation of the outer boundary
    # does not change. The new diagonal cancels in opposite directions.
    old_outer = [(b, c), (c, a), (a, d), (d, b)]
    new_outer = [edge for face in new_faces for edge in triangle_edges(face)[1:]]
    orientation_mismatch = [
        z.Not(z.Or(*(z.And(x == u, y == v) for u, v in new_outer))) for x, y in old_outer
    ]
    vertex = z.Int('vertex_query')
    diagonal_before = z.Or(vertex == a, vertex == b)
    diagonal_after = z.Or(z.And(diagonal_before, vertex != a, vertex != b), vertex == c, vertex == d)
    return results([
        ('valid_flip_neighborhood_exists', valid, z.And(*local_owner_first), 'sat'),
        ('wrong_cyclic_triangle_edge_slot', valid, z.Or(*slot_mismatches), 'unsat'),
        ('reciprocal_edge_triangle_incidence_differs', valid, z.Or(*ownership_mismatches), 'unsat'),
        ('outside_triangle_owner_lost', valid, z.Or(*outer_owners_lost), 'unsat'),
        ('vertex_edge_lists_disagree_with_diagonal', valid,
         diagonal_after != z.Or(vertex == diagonal[0], vertex == diagonal[1]), 'unsat'),
        ('stored_diagonal_endpoints_are_not_sorted', valid, diagonal[0] >= diagonal[1], 'unsat'),
        ('vertex_reference_count_update_differs', valid, z.Or(*refcount_mismatch), 'unsat'),
        ('outer_edge_direction_changes', valid, z.Or(*orientation_mismatch), 'unsat'),
        ('swapped_outer_slots_mutant', valid, z.Not(same_edge(endpoints[1], (d, b))), 'sat'),
        ('unreplaced_outer_owner_mutant', valid, z.And(edb[0] != t0, edb[1] != t0), 'sat'),
    ], timeout_ms)


def winding_decisions(timeout_ms):
    """Sparse tracking invariant, built-in classifiers and logical early-outs."""
    path = 'mesh_ops/ExactMeshBoolean.cs'
    pin(path, 'bool inside(int[] winding,', 'internal bool InsideOf(', 'b2ea2cdb82d8334efd27643e7d8ae4c846e608ea4138cecdc6e6c6a623185da3')
    pin(path, 'Action<int, int> add = (m, d)', 'var openInputs =', '9c439ee817a73b14da5e6b02c23c0f5b8520013794dcc7d91a2871f553f657ec')
    pin(path, 'foreach (int m in touched) {\n                    winding[m] = 0;', 'classifiedWithoutEffect =', '125c428ba108574d4036281c08223b33bea564f11a27e39a21aef62c2107ef55')
    pin(path, 'if (CullOutsideA && WindingPredicate is null', 'arr.Compute();', 'fa4d2ec09d93e763f3a9df1aa74c21874b8abe3c12c3e4821a272fc5d4370d6b')

    # An arbitrary index before and after a production add. The whole
    # loop invariant is the pointwise statement for every index.
    i, changed, value, delta, entries = z.Ints('mesh_index updated_mesh value delta entries')
    flag = z.Bool('touched_flag')
    pre = [z.Implies(z.Not(flag), value == 0), entries == z.If(flag, 1, 0)]
    hits = i == changed
    value_after = z.If(hits, value + delta, value)
    flag_after = z.Or(hits, flag)
    entries_after = z.If(z.And(hits, z.Not(flag)), entries + 1, entries)
    reset = z.If(flag_after, 0, value_after)

    # Instantiate independent dense Boolean specifications for several
    # operands. The size-independent count argument is checked below.
    w = z.Ints('w0 w1 w2 w3 w4')
    touched = z.Bools('touch0 touch1 touch2 touch3 touch4')
    sparse = [z.Implies(z.Not(marked), value == 0) for marked, value in zip(touched, w)]
    positives = [value > 0 for value in w]
    count = z.Sum([z.If(z.And(marked, pos), 1, 0) for marked, pos in zip(touched, positives)])
    actual = (count > 0, count == len(w), z.And(positives[0], count == 1))
    reference = (z.Or(*positives), z.And(*positives),
                 z.And(positives[0], z.Not(z.Or(*positives[1:]))))
    n, positive_count = z.Ints('operand_count positive_count')
    first_positive = z.Bool('first_positive')
    processed = z.Int('processed_touches')
    increment = z.Int('positive_increment')
    counts = [n > 1, 0 <= positive_count, positive_count <= n,
              z.Implies(first_positive, positive_count >= 1)]
    return results([
        ('add_can_lose_touch_invariant', pre,
         z.Or(z.And(z.Not(flag_after), value_after != 0),
              entries_after != z.If(flag_after, 1, 0)), 'unsat'),
        ('reset_can_leave_nonzero_value', pre, reset != 0, 'unsat'),
        ('sparse_index_contribution_differs_from_dense', pre,
         z.If(z.And(flag, value > 0), 1, 0) != z.If(value > 0, 1, 0), 'unsat'),
        ('unique_touch_scan_can_overflow_count',
         [n <= 2**31 - 1, 0 <= positive_count, positive_count <= processed,
          processed < n, 0 <= increment, increment <= 1],
         positive_count + increment > 2**31 - 1, 'unsat'),
        ('sparse_and_dense_decisions_differ', sparse,
         z.Or(*(a != b for a, b in zip(actual, reference))), 'unsat'),
        ('duplicate_positive_touch_changes_difference_mutant', sparse,
         z.And(positives[0], count == 1, count + 1 != 1), 'sat'),
        ('omitting_positive_touch_changes_union_mutant', [w[1] > 0, count == 0], z.Or(*positives), 'sat'),
        ('known_outside_operand_can_keep_intersection', counts + [positive_count < n],
         positive_count == n, 'unsat'),
        ('known_outside_first_operand_can_keep_difference', counts + [z.Not(first_positive)],
         z.And(first_positive, positive_count == 1), 'unsat'),
        ('outside_subtractor_does_not_justify_difference_cull', counts + [positive_count < n],
         z.And(first_positive, positive_count == 1), 'sat'),
        ('outside_operand_does_not_justify_union_cull', counts + [positive_count < n],
         positive_count > 0, 'sat'),
    ], timeout_ms)


SPREAD3 = ((16, 0x30000FF), (8, 0x300F00F), (4, 0x30C30C3), (2, 0x9249249))
SPREAD2 = ((16, 0x0000FFFF0000FFFF), (8, 0x00FF00FF00FF00FF),
           (4, 0x0F0F0F0F0F0F0F0F), (2, 0x3333333333333333), (1, 0x5555555555555555))


def spread(value, width, stages):
    value &= (1 << width) - 1
    for shift, mask in stages:
        value = (value | value << shift) & mask
    return value


def place_bits(value, source_width, spacing, offset=0):
    """Independent slow reference: move one input bit at a time."""
    result = z.BitVecVal(0, 64)
    for bit in range(source_width):
        result |= z.ZeroExt(63, z.Extract(bit, bit, value)) << (spacing * bit + offset)
    return result


def batch_morton(timeout_ms):
    """3D/6D quantized packing and the completed-execution slot mapping."""
    path = 'spatial/MeshBatchQueries.cs'
    pin(path, 'const int BlockSize =', 'public static BatchStatus IntersectBatch(', '5837ed9d4679e7653d90d27a708d762cb68d3235e2dec3b03f78808e70773630')
    pin(path, 'public static BatchStatus IntersectBatch(', 'delegate void RayQuery(', '2d119aa858e91fa08d504680ce8c240025f09d37750ab04e929fa3732eb846dd')
    pin(path, 'static BatchStatus RunRays(', 'static BatchStatus Check(', '8cba315c59d0e90b56ef1b12294319b05e310e017fe58640cc156f4ad954e9b8')
    pin(path, 'static BatchStatus Execute(', 'static double HitParameter(', '549f0fc2a322d703ac4a0bfc504fddcd8a348a6f10deed2dbd813bdaffbcae06')
    pin(path, 'static ulong[] RayKeys(', 'static ulong[] PointKeys(', 'de7f0a2ad321dc90e4253b2549b0766c5f75f39d9f1ff048008fddf9da2f3206')
    pin(path, 'static double Unit(', 'static ulong Morton3(', '88acbc3d4be1cd7318be9995d9d351630a5b7bc3b1803e30f6282e164f2bc680')
    pin(path, 'static ulong Morton3(', '\n    }', '59a67cbc51d60b199545b6593c45fe75a4053b2d63da32d99c9f3255eff7bbad')

    value = z.BitVec('spread_input', 64)
    ox, oy, oz, dx, dy, dz = z.BitVecs('ox oy oz dx dy dz', 64)
    origin, direction = (ox, oy, oz), (dx, dy, dz)

    def morton3(coords):
        a, b, c = [spread(v, 10, SPREAD3) for v in coords]
        return a | b << 1 | c << 2

    combined = spread(morton3(origin), 32, SPREAD2) | spread(morton3(direction), 32, SPREAD2) << 1
    reference = z.BitVecVal(0, 64)
    for offset, component in enumerate((ox, dx, oy, dy, oz, dz)):
        reference |= place_bits(component, 10, 6, offset)
    # Drop bit 0 in the final 3D spread mask. One occupied input
    # is enough to replay the resulting key collision.
    mutated_stages = (*SPREAD3[:-1], (SPREAD3[-1][0], SPREAD3[-1][1] & ~1))

    # Unit maps all call sites to [0,1], including its constant
    # branches for NaN and degenerate extents. Q10 clips the upper
    # end, and conversion of this bounded nonnegative product is
    # truncation to an integer.
    raw = z.FP('raw_unit_ratio', F)
    zero, one, half = (z.FPVal(v, F) for v in (0, 1, 0.5))
    clamped = z.If(z.fpIsNaN(raw), half, z.If(z.fpLT(raw, zero), zero,
                                             z.If(z.fpGT(raw, one), one, raw)))
    unit = z.Real('unit_coordinate')
    quantized = z.ToInt(1024 * unit)
    clipped = z.If(quantized < 1023, quantized, 1023)

    max_int = 2**31 - 1
    n, i, b1, b2 = z.Ints('query_count loop_index first_block second_block')
    block_size = 1024
    count = (n + block_size - 1) / block_size
    num = [n >= 0, n <= max_int]
    domain = num + [0 <= i, i < n]

    def visited(block):
        return z.And(0 <= block, block < count,
                     block * block_size <= i, i < (block + 1) * block_size)

    # Array.Sort preserves a permutation of the original indices,
    # even on ties. One arbitrary pair plus the inverse property
    # proves distinct slots after sorting.
    order = z.Array('query_order', z.IntSort(), z.IntSort())
    inverse = z.Array('inverse_order', z.IntSort(), z.IntSort())
    j = z.Int('other_loop_index')
    a, b = z.Select(order, i), z.Select(order, j)
    permutation = [z.Select(inverse, a) == i, z.Select(inverse, b) == j]
    return results([
        ('spread3_differs_from_bit_placement', [], spread(value, 10, SPREAD3) != place_bits(value, 10, 3), 'unsat'),
        ('spread2_differs_from_bit_placement', [], spread(value, 32, SPREAD2) != place_bits(value, 32, 2), 'unsat'),
        ('morton6_differs_from_direct_bit_placement', [], combined != reference, 'unsat'),
        ('morton6_can_set_a_bit_above_bit_59', [], z.Extract(63, 60, combined) != 0, 'unsat'),
        ('dropped_morton_low_bit_mutant', [value == 1], spread(value, 10, mutated_stages) != 1, 'sat'),
        ('unit_clamp_can_leave_unit_interval', [],
         z.Not(z.And(z.fpGEQ(clamped, zero), z.fpLEQ(clamped, one))), 'unsat'),
        ('q10_can_leave_ten_bit_domain', [0 <= unit, unit <= 1], z.Or(clipped < 0, clipped > 1023), 'unsat'),
        ('last_quantized_bucket_reachable', [unit == 1], clipped == 1023, 'sat'),
        ('valid_loop_index_is_not_in_its_block', domain, z.Not(visited(i / block_size)), 'unsat'),
        ('two_blocks_can_visit_same_index', domain + [visited(b1), visited(b2)], b1 != b2, 'unsat'),
        ('valid_block_start_can_overflow_int32', num + [0 <= b1, b1 < count],
         z.Or(b1 * block_size < 0, b1 * block_size >= n, b1 * block_size > max_int), 'unsat'),
        ('completed_permutation_can_write_same_slot_twice', permutation + [i != j], a == b, 'unsat'),
        ('using_iteration_index_as_result_slot_mutant',
         [i == 0, n == 2, a == 1, z.Select(order, 1) == 0, *permutation], a != i, 'sat'),
    ], timeout_ms)


PILOTS = dict(mesh_flip=mesh_flip, winding_decisions=winding_decisions, batch_morton=batch_morton)
