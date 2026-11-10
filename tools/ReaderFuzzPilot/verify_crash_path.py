#!/usr/bin/env python3
"""Prove a synthetic fuzz crash is saved and replayable; uses temporary sources and leaves tracked checkout files unchanged."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--sharpfuzz', type=Path, required=True)
    parser.add_argument('--libfuzzer', type=Path, required=True)
    args = parser.parse_args()
    dotnet, sharpfuzz, fuzzer = (p.resolve(strict=True) for p in (args.dotnet, args.sharpfuzz, args.libfuzzer))
    env = dict(os.environ, DOTNET_ROOT=str(dotnet.parent), DOTNET_ROLL_FORWARD='Major',
               PATH=str(dotnet.parent) + os.pathsep + os.environ['PATH'])
    with tempfile.TemporaryDirectory(prefix='reader-fuzz-crash-check-') as directory:
        work = Path(directory)
        # a copy of the harness sources with a synthetic crash on 134-byte inputs (the generated binary STL seed)
        for name in ('Program.cs', 'FuzzTarget.cs', 'ReaderTargets.cs', 'ReaderTargetsMcp.cs', 'ReplayMutations.cs'):
            shutil.copy(HERE / name, work / name)
        source = (work / 'FuzzTarget.cs').read_text()
        needle = '            string problem = Check(data);'
        if source.count(needle) != 1:
            raise SystemExit('source drift: cannot inject the synthetic crash')
        (work / 'FuzzTarget.cs').write_text(source.replace(needle,
            '            if (data.Length == 134) throw new InvalidOperationException("SyntheticCrashForHarnessTest");\n' + needle))
        project = (HERE / 'ReaderFuzzPilot.csproj').read_text().replace(
            '../../geometry3Sharp_netstandard.csproj', str(ROOT / 'geometry3Sharp_netstandard.csproj')).replace(
            '../g3cli/g3cli.csproj', str(ROOT / 'tools/g3cli/g3cli.csproj'))
        (work / 'ReaderFuzzPilot.csproj').write_text(project)
        subprocess.run([str(dotnet), 'publish', str(work / 'ReaderFuzzPilot.csproj'), '-c', 'Release',
                        '-o', str(work / 'replay'), '-m:1', '-nr:false'], check=True, env=env,
                       stdout=subprocess.DEVNULL)
        shutil.copytree(work / 'replay', work / 'instrumented')
        subprocess.run([str(sharpfuzz), str(work / 'instrumented/geometry3Sharp.dll')], check=True, env=env)
        subprocess.run(['python3', str(HERE / 'generate_seed.py'), str(work / 'corpus')],
                       check=True, stdout=subprocess.DEVNULL)
        (work / 'artifacts').mkdir()
        result = subprocess.run([str(fuzzer), '-runs=1', '-timeout=5', '-rss_limit_mb=2048',
                                 '-artifact_prefix=' + str(work / 'artifacts') + '/',
                                 '--target_path=' + str(dotnet),
                                 '--target_arg=' + str(work / 'instrumented/ReaderFuzzPilot.dll'),
                                 str(work / 'corpus')], cwd=work, capture_output=True, text=True,
                                env=env, timeout=30)
        artifacts = list((work / 'artifacts').glob('crash-*'))
        if result.returncode == 0 or len(artifacts) != 1:
            raise SystemExit('synthetic crash was not saved: ' + result.stdout + result.stderr)
        replay = subprocess.run([str(dotnet), str(work / 'replay/ReaderFuzzPilot.dll'),
                                 'replay', str(artifacts[0])], cwd=work, capture_output=True,
                                text=True, env=env, timeout=30)
        if replay.returncode == 0 or 'SyntheticCrashForHarnessTest' not in replay.stderr:
            raise SystemExit('saved crash did not reproduce: ' + replay.stderr)
        print('synthetic crash saved and reproduced')


if __name__ == '__main__':
    main()
