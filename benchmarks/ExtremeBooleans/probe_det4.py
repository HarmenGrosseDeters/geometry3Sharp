# Scratch A/B of docs/perf/EXTREME_RUNTIME_BOOLEANS.md (lever A1): ExactMeshBoolean.orient3d on four ExactPoint3
# without the BigInteger[4,4] matrix and the point array (the same cofactor expansion along the first row as
# ExactMeshArrangement.Det4, on locals; bit-identical by construction). Throw-away worktree only, never committed.
import sys
path = sys.argv[1]; s = open(path).read()
old = """            BigInteger[,] m = new BigInteger[4, 4];
            ExactPoint3[] pts = { a, b, c, d };
            for (int k = 0; k < 4; ++k) {
                m[k, 0] = pts[k].X; m[k, 1] = pts[k].Y; m[k, 2] = pts[k].Z; m[k, 3] = pts[k].W;
            }
            return ExactMeshArrangement.Det4(m).Sign;"""
if s.count(old) != 1: raise SystemExit("anchor")
s = s.replace(old, """            // rows a, b, c, d; columns X, Y, Z, W; expansion along row a (as Det4)
            BigInteger det = a.X * ExactPoint3.Det3(b.Y, b.Z, b.W, c.Y, c.Z, c.W, d.Y, d.Z, d.W)
                - a.Y * ExactPoint3.Det3(b.X, b.Z, b.W, c.X, c.Z, c.W, d.X, d.Z, d.W)
                + a.Z * ExactPoint3.Det3(b.X, b.Y, b.W, c.X, c.Y, c.W, d.X, d.Y, d.W)
                - a.W * ExactPoint3.Det3(b.X, b.Y, b.Z, c.X, c.Y, c.Z, d.X, d.Y, d.Z);
            return det.Sign;""")
open(path, "w").write(s)
