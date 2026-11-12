cd $S
export PROBE_BASE=$S/out/base.jsonl
dotnet bin2/SharpFeatureProbe.dll out/full.jsonl thingi/79961.stl thingi/94664.stl thingi/96675.stl thingi/96773.stl -- BS > out/bs4.log 2>&1
dotnet bin3/SharpFeatureProbe.dll out/local.jsonl proc:all thingi/104640.stl thingi/1582399.stl thingi/43551.stl thingi/57084.stl thingi/64983.stl thingi/73088.stl thingi/94664.stl thingi/96675.stl thingi/96773.stl  -- A,KR,L,KL > out/local.log 2>&1
dotnet bin3/SharpFeatureProbe.dll out/wdc.jsonl proc:all thingi/1582399.stl thingi/43551.stl thingi/64983.stl -- A,WDC > out/wdc.log 2>&1
