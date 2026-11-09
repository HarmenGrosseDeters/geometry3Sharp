#!/usr/bin/env python3
"""Create one tiny, procedurally defined binary STL seed without overwriting files."""
import argparse
from pathlib import Path
import struct


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    args.directory.mkdir(parents=True, exist_ok=True)
    target = args.directory / 'triangle.stl'
    # Header (80 bytes), face count, normal, three vertices, attribute byte count.
    data = bytes(80) + struct.pack('<I12fH', 1, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0)
    with target.open('xb') as output:
        output.write(data)
    print(target)


if __name__ == '__main__':
    main()
