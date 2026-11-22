"""Run after a Release build. Requires dotnet on PATH, SciPy and NumPy."""
import json
from pathlib import Path
import subprocess

import numpy as np
from scipy.spatial import ConvexHull, KDTree
from scipy.stats import qmc


def disc_points(u, turns):
    return np.sqrt(u)[:, None] * np.column_stack((np.cos(2*np.pi*turns), np.sin(2*np.pi*turns)))


def quadrature(seed):
    samples = qmc.Sobol(2, scramble=True, seed=seed).random_base2(17)
    return disc_points(*samples.T)


training = quadrature(11)
validation = quadrature(23)
size = 253
binary = Path(__file__).parent / 'bin/Release/net10.0/PhyllotaxisConsumers.dll'
for seed in (None, 1424, 1425, 1426):
    if seed is None:
        i = np.arange(size)
        u, turns = (i+.5)/size, i*(3-np.sqrt(5))/2
    else:
        u, turns = np.random.default_rng(seed).random((2, size))
    points = disc_points(u, turns)
    z = 1-2*u
    radial = np.sqrt(1-z*z)
    sphere = np.column_stack((radial*np.cos(2*np.pi*turns), radial*np.sin(2*np.pi*turns), z))
    faces = ConvexHull(sphere).simplices.copy()
    a, b, c = sphere[faces].transpose(1, 0, 2)
    inward = (np.cross(b-a, c-a)*a).sum(axis=1) < 0
    faces[inward] = faces[inward, ::-1]
    print('Fibonacci' if seed is None else f'Random seed {seed}', flush=True)
    payload = dict(Disc=points.tolist(), Sphere=sphere.tolist(), Faces=faces.tolist())
    subprocess.run(['dotnet', str(binary)], input=json.dumps(payload), text=True, check=True)
    for step in range(6):
        tree = KDTree(points)
        if step in (0, 5):
            print('Lloyd', step, 'mean squared quantization error',
                  np.mean(tree.query(validation)[0]**2), flush=True)
        if step < 5:
            labels = tree.query(training)[1]
            counts = np.bincount(labels, minlength=size)
            assert counts.min() > 0
            points = np.column_stack([np.bincount(labels, coord)/counts for coord in training.T])
