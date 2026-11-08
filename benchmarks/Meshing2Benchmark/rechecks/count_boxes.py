"""Diagnostic bounding-box candidate counts; no geometric verdict is inferred."""
import bisect
import heapq
import json
import struct
import sys
import random
import numpy as np

class Fenwick:
    def __init__(self, n): self.a = [0]*(n+1)
    def add(self, i, value):
        i += 1
        while i < len(self.a):
            self.a[i] += value
            i += i & -i
    def prefix(self, n):
        total = 0
        while n:
            total += self.a[n]
            n -= n & -n
        return total

def point_box_count(points, boxes):
    ys = sorted(set(y for x,y in points)); tree = Fenwick(len(ys))
    ordered = sorted(points); queries = []
    for xmin,xmax,ymin,ymax in boxes:
        queries.extend([(xmin, -1, ymin, ymax), (xmax, 1, ymin, ymax)])
    cursor = total = 0
    for x,sign,ymin,ymax in sorted(queries):
        while cursor < len(ordered) and (ordered[cursor][0] < x or sign == 1 and ordered[cursor][0] == x):
            tree.add(bisect.bisect_left(ys, ordered[cursor][1]), 1); cursor += 1
        total += sign*(tree.prefix(bisect.bisect_right(ys,ymax))-tree.prefix(bisect.bisect_left(ys,ymin)))
    return total

def box_pair_count(boxes):
    ys = sorted({y for x0,x1,y0,y1 in boxes for y in (y0,y1)})
    lower = Fenwick(len(ys)); upper = Fenwick(len(ys)); ends = []
    total = 0
    for xmin,xmax,ymin,ymax in sorted(boxes):
        while ends and ends[0][0] < xmin:
            _, lo, hi = heapq.heappop(ends)
            lower.add(lo,-1); upper.add(hi,-1)
        total += lower.prefix(bisect.bisect_right(ys,ymax))-upper.prefix(bisect.bisect_left(ys,ymin))
        lo = bisect.bisect_left(ys,ymin); hi = bisect.bisect_left(ys,ymax)
        lower.add(lo,1); upper.add(hi,1); heapq.heappush(ends,(xmax,lo,hi))
    return total

def selftest():
    rng = random.Random(117)
    for _ in range(100):
        points = [(rng.randrange(6),rng.randrange(6)) for _ in range(15)]
        boxes = []
        for _ in range(15):
            x0,x1 = sorted([rng.randrange(6),rng.randrange(6)])
            y0,y1 = sorted([rng.randrange(6),rng.randrange(6)])
            boxes.append((x0,x1,y0,y1))
        assert point_box_count(points,boxes) == sum(x0<=x<=x1 and y0<=y<=y1 for x,y in points for x0,x1,y0,y1 in boxes)
        assert box_pair_count(boxes) == sum(max(a[0],b[0])<=min(a[1],b[1]) and max(a[2],b[2])<=min(a[3],b[3]) for i,a in enumerate(boxes) for b in boxes[i+1:])

def main(path):
    selftest()
    with open(path) as stream:
        while not (line:=stream.readline()).startswith('vertices '):
            if not line: raise ValueError('Missing vertices in mesh2-output')
        n = int(line.split()[1])
        points = np.array([[struct.unpack('>d',bytes.fromhex(t[2:]))[0] for t in stream.readline().split()] for _ in range(n)])
        line = stream.readline(); assert line.startswith('triangles ')
        nt = int(line.split()[1]); triangles = np.array([list(map(int,stream.readline().split())) for _ in range(nt)],dtype=np.int32)
    edges = np.unique(np.sort(np.concatenate([triangles[:,[0,1]],triangles[:,[1,2]],triangles[:,[2,0]]]),axis=1),axis=0)
    def boxes(ids):
        coords = points[ids]
        return list(zip(coords[:,:,0].min(axis=1).tolist(),coords[:,:,0].max(axis=1).tolist(),coords[:,:,1].min(axis=1).tolist(),coords[:,:,1].max(axis=1).tolist()))
    used = np.unique(triangles); pv = point_box_count([tuple(p) for p in points[used]],boxes(triangles))
    ee = box_pair_count(boxes(edges)); degree = np.bincount(edges.ravel()).astype(np.int64)
    shared = int(np.sum(degree*(degree-1)//2))
    print(json.dumps(dict(vertices=n,triangles=nt,edges=len(edges),point_triangle_boxes_including_corners=pv,point_triangle_boxes_excluding_corners=pv-3*nt,edge_boxes_including_shared_endpoints=ee,edge_boxes_excluding_shared_endpoints=ee-shared,selftest_random_batches=100)),flush=True)

if __name__ == '__main__': main(sys.argv[1])
