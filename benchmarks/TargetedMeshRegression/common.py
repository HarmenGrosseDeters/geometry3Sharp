import json
import math
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent


def load(path):
    points, faces = [], []
    for line in path.read_text().splitlines():
        if not line.strip():
            continue
        kind, *values = line.split()
        if kind == 'v':
            points.append(tuple(map(float, values)))
        elif kind == 'f':
            indices = [int(value.split('/', 1)[0]) for value in values]
            faces.append(tuple(i - 1 if i > 0 else len(points) + i for i in indices))
    return points, faces


def save(path, mesh):
    points, faces = mesh
    with path.open('w') as stream:
        for point in points:
            stream.write('v ' + ' '.join(format(value, '.17g') for value in point) + '\n')
        for face in faces:
            stream.write('f ' + ' '.join(str(index + 1) for index in face) + '\n')


def operands(case, fixtures):
    return {key: load(fixtures / name) for key, name in case['inputs'].items() if name.endswith('.obj')}


def sub(a, b):
    return tuple(x-y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def dot(a, b):
    return math.fsum(x*y for x, y in zip(a, b))


def mesh_stats(mesh):
    points, faces = mesh
    neighbors = defaultdict(set)
    links = defaultdict(lambda: defaultdict(set))
    edges = Counter()
    degenerate = 0
    terms = []
    origin = points[0] if points else (0, 0, 0)
    for i, j, k in faces:
        a, b, c = [points[index] for index in (i, j, k)]
        degenerate += cross(sub(b, a), sub(c, a)) == (0, 0, 0)
        terms.append(dot(sub(a, origin), cross(sub(b, origin), sub(c, origin))))
        for x, y, z in ((i,j,k), (j,k,i), (k,i,j)):
            edges[x,y] += 1
            neighbors[x].add(y)
            neighbors[y].add(x)
            links[x][y].add(z)
            links[x][z].add(y)
    edge_keys = {tuple(sorted(pair)) for pair in edges}
    return {
        'faces': len(faces), 'vertices': len(points), 'volume': math.fsum(terms)/6,
        'components': component_count(neighbors),
        'boundary_edges': sum(edges[i,j]+edges[j,i]==1 for i,j in edge_keys),
        'nonmanifold_edges': sum(edges[i,j]+edges[j,i]>2 for i,j in edge_keys),
        'inconsistent_edges': sum(edges[i,j]+edges[j,i]==2 and edges[i,j]!=edges[j,i] for i,j in edge_keys),
        'disconnected_vertex_links': sum(component_count(link)>1 for link in links.values()),
        'degenerate_faces': degenerate,
    }


def component_count(neighbors):
    seen = set()
    count = 0
    for start in neighbors:
        if start in seen:
            continue
        count += 1
        seen.add(start)
        stack = [start]
        while stack:
            for other in neighbors[stack.pop()] - seen:
                seen.add(other)
                stack.append(other)
    return count


def json_write(path, value):
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    temporary.replace(path)
