"""Test support: run the bash scripts of this directory (run_stryker.sh) on Windows with Git Bash.

The scripts call `python3`; on Windows that name is often the Microsoft Store stub (exit code 49), so the tests that put a
fake `dotnet` on the PATH put a `python3` next to it which runs the interpreter of the test. On Linux nothing is added."""
import os
import sys


def add_python3(bindir):
    """write bindir/python3 (a bash script that runs this interpreter) on Windows; no-op elsewhere"""
    if os.name != "nt":
        return
    os.makedirs(bindir, exist_ok=True)
    path = os.path.join(bindir, "python3")
    with open(path, "w", newline="\n") as f:
        f.write("#!/usr/bin/env bash\nexec '%s' \"$@\"\n" % sys.executable.replace("'", "'\\''"))
    os.chmod(path, 0o755)
