"""Procedural spacing, cap quadrature and similarity probe. Run with Python and SciPy."""
import numpy as np
from scipy.spatial import KDTree, Delaunay
from scipy.spatial.transform import Rotation

angle = np.pi * (3 - np.sqrt(5))
utm = np.array([632117.25, 5712043.5, 113.75])
identity = np.eye(3)
tilt = Rotation.from_euler("xz", [17, 31], degrees=True).as_matrix()
yaw = Rotation.from_euler("y", 23, degrees=True).as_matrix()
motions = [(s, identity, 0) for s in (1, 1/1024, 1024, .001, 1000)]
motions += [(1, identity, utm), (1, tilt, 0), (.001, yaw, utm)]


def nearest(points):
    return KDTree(points).query(points, k=2)[0][:, 1]


def cap_rmse(points, axes, weights=None):
    fractions = np.average(points @ axes.T >= .5, axis=0, weights=weights)
    return np.sqrt(np.mean((fractions - .25)**2))


for rows in (11, 23, 45):
    cols = 2 * rows + 1
    n = rows * cols
    i = np.arange(n)
    u = (i + .5) / n
    radial = np.column_stack((np.cos(i * angle), np.sin(i * angle)))
    disc = np.column_stack((np.sqrt(u)[:, None] * radial, np.zeros(n)))
    z = 1 - 2*u
    sphere = np.column_stack((np.sqrt(1-z*z)[:, None] * radial, z))
    for name, points in [('disc', disc), ('sphere', sphere)]:
        distances = nearest(points)
        errors = [np.max(abs(nearest(s * (points @ r.T) + t)/s - distances))
                  for s, r, t in motions]
        print(n, name, 'scaled min/max', distances.min()*np.sqrt(n),
              distances.max()*np.sqrt(n), 'max length drift', max(errors))
    lat, lon = np.meshgrid(np.pi*((np.arange(rows)+.5)/rows-.5),
                           2*np.pi*np.arange(cols)/cols)
    grid = np.column_stack([(np.cos(lat)*f(lon)).ravel() for f in (np.cos, np.sin)]
                           + [np.sin(lat).ravel()])
    axes = np.random.default_rng(1424).normal(size=(128, 3))
    axes /= np.linalg.norm(axes, axis=1)[:, None]
    print('cap', len(grid), cap_rmse(grid, axes, np.cos(lat).ravel()), cap_rmse(sphere, axes))
    mesh = Delaunay(disc[:, :2])
    best = (float('inf'), ())
    for face, adjacent in enumerate(mesh.neighbors):
        for other in adjacent[adjacent > face]:
            ids = np.union1d(mesh.simplices[face], mesh.simplices[other])
            p = disc[ids, :2] - disc[ids[-1], :2]
            diameter = np.linalg.norm(p[:, None]-p, axis=2).max()
            q = p[:3] / diameter
            residual = abs(np.linalg.det(np.column_stack((q, (q*q).sum(axis=1)))))
            best = min(best, (residual, tuple(ids.tolist())))
    print('minimum normalized incircle determinant', best)
