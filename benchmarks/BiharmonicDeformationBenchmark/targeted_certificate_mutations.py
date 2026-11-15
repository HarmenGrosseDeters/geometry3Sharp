"""Run targeted hand mutations against a prebuilt focused test assembly.

Run alone in a disposable/dedicated worktree. This temporarily edits one source
file and replaces two library artifacts, restoring all of them in finally. Build
the original source and focused test project freshly before invoking it. A
hard process kill cannot run finally; do not interrupt the process forcibly.
This is targeted evidence, not the repository's incremental Stryker gate.
"""
import argparse
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

parser=argparse.ArgumentParser()
parser.add_argument('dotnet');parser.add_argument('artifacts');parser.add_argument('logs')
args=parser.parse_args()
root=Path(__file__).resolve().parents[2]
source=root/'mesh_ops/BiharmonicSparseCertificate.cs'
artifacts=Path(args.artifacts).resolve();logs=Path(args.logs).resolve();logs.mkdir(parents=True,exist_ok=True)
library=artifacts/'bin/geometry3Sharp_netstandard/release_net10.0/geometry3Sharp.dll'
testlibrary=artifacts/'bin/geometry3Sharp.Tests/release_net10.0/geometry3Sharp.dll'
tests=artifacts/'bin/geometry3Sharp.Tests/release_net10.0/geometry3Sharp.Tests.dll'
original=source.read_bytes();text=original.decode()
condition='''if(restLower.Sign<=0 || errorSquared*unit>position*position*restLower ||
                    errorSquared*(unit+relative)*(unit+relative)>relative*relative*responseLower*unit)return false;'''
axis='''for(int axis=0;axis<3;axis++) {
                    BigInteger sum=0;'''
verifier_test='SparseVerifier_LargerComponentRejectsEveryAxisAndHandleCorruption'
cache_key='if(prepared!=null && !prepared.Matches(mesh,ids,original,free))prepared=null;'
mutants=[('skip_accuracy',condition,'if(false)return false;',verifier_test),
         ('omit_z',axis,axis.replace('axis<3','axis<2'),verifier_test),
         ('skip_cache_key',cache_key,'if(false)prepared=null;','PreparedCertificate_RechecksOutputAndInvalidatesGeometryTopologyAndIdentity')]
artifact_paths=(library,testlibrary,library.with_suffix('.pdb'),testlibrary.with_suffix('.pdb'))
if any(not path.exists() for path in artifact_paths):raise RuntimeError('all baseline DLL/PDB artifacts must exist')
backups={path:path.read_bytes() for path in artifact_paths}
if backups[library]!=backups[testlibrary]:raise RuntimeError('baseline library/test copy differ; rebuild tests first')
for _,old,_,_ in mutants:
    if text.count(old)!=1:raise RuntimeError('mutation location changed')

def run_tests(name):
    resultdir=logs/name;resultdir.mkdir(exist_ok=True)
    if (resultdir/'results.trx').exists():raise RuntimeError('use a fresh log directory; refusing stale TRX')
    with (logs/(name+'-test.log')).open('w') as log:
        result=subprocess.run([args.dotnet,'vstest',str(tests),'--TestCaseFilter:FullyQualifiedName~BiharmonicMeshDeformerTests','--Logger:trx;LogFileName=results.trx','--ResultsDirectory:'+str(resultdir)],cwd=root,stdout=log,stderr=subprocess.STDOUT)
    tree=ET.parse(resultdir/'results.trx');ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counters=tree.find('.//t:Counters',ns)
    failed_names=[item.attrib['testName'] for item in tree.findall('.//t:UnitTestResult',ns) if item.attrib.get('outcome')=='Failed']
    return result.returncode,int(counters.attrib['failed']),int(counters.attrib['executed']),failed_names

code,failed,executed,_=run_tests('baseline')
if code or failed or executed<1:raise RuntimeError('baseline is not green; no mutants run')
print(f'baseline: green; executed={executed}',flush=True)
try:
    for name,old,new,expected in mutants:
        source.write_text(text.replace(old,new))
        with (logs/(name+'-build.log')).open('w') as log:
            build=subprocess.run([args.dotnet,'build','geometry3Sharp_netstandard.csproj','-c','Release','-f','net10.0','--artifacts-path',str(artifacts),'--no-restore','--no-dependencies','-m:1','-p:UseSharedCompilation=false','-p:RunAnalyzers=false'],cwd=root,stdout=log,stderr=subprocess.STDOUT)
        if build.returncode:raise RuntimeError(name+' build failed; not counted as killed')
        shutil.copyfile(library,testlibrary)
        code,failed,executed,names=run_tests(name)
        if code==0 or failed<1 or executed<1 or not any(expected in item for item in names):
            raise RuntimeError(name+' survived or failed outside the expected verifier test')
        print(f'{name}: killed; executed={executed} failed={failed}',flush=True)
finally:
    source.write_bytes(original)
    for path,data in backups.items():path.write_bytes(data)
    print('source and original library artifacts restored',flush=True)
