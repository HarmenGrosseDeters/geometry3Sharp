# Full local run of one or more mutation shards with the CI configuration (Windows PowerShell 5.1 or PowerShell 7), and
# the result file for the 3.0 completion gate (tests/mutation/README.md, "Local runs"; local_result.py checks it in CI).
#
#   pwsh tests/mutation/run_local.ps1 [-Touched <base ref>] [shard ...]
#
# Same as run_local.sh: shards are named as in the workflow (arithmetic, rounding-1, boolean-1, ...), -Touched <ref> takes
# the shards that select_shards.py would run for the files changed between <ref> and HEAD. Needs dotnet, git and Python 3
# (python or py -3) on the PATH, and a CLEAN working tree at the commit to gate. Result:
# tests/mutation/local-results/<head sha>/<shard>.json. Exit code 1: a shard below its break threshold (the file is written
# anyway); 2: an error.
param([string]$Touched = "", [Parameter(ValueFromRemainingArguments = $true)][string[]]$Shard = @())
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $root

function Find-Python {
    foreach ($c in @("python3", "python")) { if (Get-Command $c -ErrorAction SilentlyContinue) { return @($c) } }
    if (Get-Command py -ErrorAction SilentlyContinue) { return @("py", "-3") }
    throw "Python 3 not found"
}
$py = Find-Python
function Invoke-Py { param([Parameter(ValueFromRemainingArguments = $true)]$Args) & $py[0] @($py | Select-Object -Skip 1) @Args }

$shards = @($Shard | Where-Object { $_ })
if ($Touched) {
    $out = (git diff --name-only "$Touched...HEAD") | Invoke-Py tests/mutation/select_shards.py
    foreach ($line in $out) {
        if ($line -match '^arithmetic=(.*)$' -and $Matches[1] -ne "none") { $shards += "arithmetic" }
        if ($line -match '^mesh=(.*)$') { foreach ($m in ($Matches[1] | ConvertFrom-Json)) { $shards += $m.shard } }
    }
}
if ($shards.Count -eq 0) { Write-Error "no shard to run (name shards, or -Touched <base ref>)"; exit 2 }
if (git status --porcelain --untracked-files=no) { Write-Error "the working tree is not clean: commit or stash first (a result is valid for a commit only)"; exit 2 }
$sha = (git rev-parse HEAD).Trim()
dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit 2 }
$status = 0
foreach ($s in $shards) {
    $config = if ($s -eq "arithmetic") { "stryker-config.json" } else { "stryker-mesh-$s.json" }
    if (-not (Test-Path "tests/mutation/$config")) { Write-Error "unknown shard $s"; exit 2 }
    Write-Host "=== $s ($config) at $sha ==="
    Remove-Item -Recurse -Force "tests/mutation/StrykerOutput" -ErrorAction SilentlyContinue
    Push-Location "tests/mutation"
    try {
        if ($s -ne "arithmetic") { $env:DOTNET_GCHeapHardLimit = "0x180000000" } else { Remove-Item Env:DOTNET_GCHeapHardLimit -ErrorAction SilentlyContinue }   # 6 GB, as in the workflow
        dotnet stryker --config-file $config --log-to-file
        $rc = $LASTEXITCODE
    } finally { Pop-Location }
    $report = Get-ChildItem "tests/mutation/StrykerOutput/*/reports/mutation-report.json" -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -Last 1
    if (-not $report) { Write-Warning "no report for $s (Stryker exit code $rc)"; $status = 2; continue }
    if (((git rev-parse HEAD).Trim() -ne $sha) -or (git status --porcelain --untracked-files=no)) {
        Write-Warning "the working tree changed during the run of $s: the result would not be valid"; $status = 2; continue
    }
    Invoke-Py tests/mutation/local_result.py record --root . --shard $s --report $report.FullName --out "tests/mutation/local-results/$sha/$s.json" --platform "windows"
    if ($LASTEXITCODE -gt $status) { $status = $LASTEXITCODE }
}
Write-Host "results: tests/mutation/local-results/$sha/"
exit $status
