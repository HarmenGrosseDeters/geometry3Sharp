# Runs the boolean and alpha wrap benchmarks on net10.0 and net48 on the same machine, the runtimes alternated, and writes one row per
# run: stratum, runtime, round, threads, time_ms, hash (benchmarks/net48-compare/README.md). Works in Windows PowerShell 5.1 and PowerShell 7.
#
#   ./run.ps1 -Smoke                                 tiny cases, one round, fails if a hash differs between the runtimes (CI)
#   ./run.ps1 -BData D:\bdata -WrapMeshes D:\wrap    the strata of strata.csv, 5 rounds
#   ./run.ps1 -Stratum boolean-thingi-tail -Rounds 3 -Runtimes net10.0,net48,net10.0-pgo0
#
# Runtimes: net10.0, net48, and net10.0-pgo0 (net10.0 with DOTNET_TieredPGO=0; any other net10.0-<suffix> is net10.0 with the
# environment variables of -Net10Env, a hashtable). Threads: "all" cores, or "one" (the process runs on core 0).
# Data is fetched at run time by the commands of the README (-BData, -WrapMeshes); nothing of it is part of the repository.
[CmdletBinding()]
param(
    [string[]]$Stratum = @(),
    [int]$Rounds = 5,
    [string[]]$Runtimes = @('net10.0', 'net48'),
    [string]$BData = $(if ($env:BDATA) { $env:BDATA } else { Join-Path $HOME 'bdata' }),
    [string]$WrapMeshes = '',
    [string]$OutDir = '',
    [hashtable]$Net10Env = @{},
    [switch]$Smoke,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$bench = Split-Path -Parent $here
$repo = Split-Path -Parent $bench
if ($Smoke) { $Rounds = 1 }
if (-not $OutDir) { $OutDir = Join-Path $here ('results\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# --- tools: the three benchmarks, built for both runtimes (-p:BenchNet48=true adds net48) ---------------------------------------------
$tools = @{
    'booleans' = 'BooleanComparison'
    'small' = 'SmallInputWrapBenchmark'
    'repair' = 'MeshRepairBenchmark'
}
if (-not $NoBuild) {
    foreach ($name in $tools.Values) {
        & dotnet build (Join-Path $bench "$name\$name.csproj") -c Release -p:BenchNet48=true -v q -nologo
        if ($LASTEXITCODE -ne 0) { throw "build of $name failed" }
    }
}
function Get-ToolCommand([string]$kind, [string]$runtime) {
    $name = $tools[$kind]
    if ($runtime -eq 'net48') {
        return @{ File = (Join-Path $bench "$name\bin\Release\net48\$name.exe"); Prefix = @() }
    }
    return @{ File = 'dotnet'; Prefix = @((Join-Path $bench "$name\bin\Release\net10.0\$name.dll")) }
}

# --- one process, stdout as lines; extraEnv = environment variables of this run; Threads one = the process runs on core 0 -------------------
function Invoke-Tool([string]$kind, [string]$runtime, [string[]]$toolArgs, [hashtable]$extraEnv, [string]$threads, [string]$cwd) {
    $cmd = Get-ToolCommand $kind $runtime
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $cmd.File
    $psi.Arguments = (($cmd.Prefix + $toolArgs) | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $cwd
    foreach ($k in $extraEnv.Keys) { $psi.EnvironmentVariables[$k] = [string]$extraEnv[$k] }
    if ($runtime -like 'net10.0-*') {
        if ($runtime -eq 'net10.0-pgo0') { $psi.EnvironmentVariables['DOTNET_TieredPGO'] = '0' }
        foreach ($k in $Net10Env.Keys) { $psi.EnvironmentVariables[$k] = [string]$Net10Env[$k] }
    }
    $p = [System.Diagnostics.Process]::Start($psi)
    if ($threads -eq 'one') { try { $p.ProcessorAffinity = [IntPtr]1 } catch { Write-Warning "affinity: $_" } }
    $errTask = $p.StandardError.ReadToEndAsync()
    $out = $p.StandardOutput.ReadToEnd()
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { Write-Warning ("exit code {0}: {1} {2}`n{3}" -f $p.ExitCode, $cmd.File, $psi.Arguments, $errTask.Result) }
    return @{ Lines = @($out -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 }); ExitCode = $p.ExitCode }
}

function Get-Hash16([string[]]$parts) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes(($parts -join '|')))
    return ([BitConverter]::ToString($bytes) -replace '-', '').Substring(0, 16)
}

# --- strata: name, kind, threads, cases (strata.csv; the smoke strata are built in) ------------------------------------------------------
function Get-Strata {
    if ($Smoke) {
        # a procedural octahedron as the input of the wrap smoke run (no mesh file in the repository)
        $oct = Join-Path $OutDir 'smoke-octahedron.obj'
        @('v 1 0 0', 'v -1 0 0', 'v 0 1 0', 'v 0 -1 0', 'v 0 0 1', 'v 0 0 -1',
            'f 1 3 5', 'f 3 2 5', 'f 2 4 5', 'f 4 1 5', 'f 3 1 6', 'f 2 3 6', 'f 4 2 6', 'f 1 4 6') | Set-Content -Encoding ASCII $oct
        return @(
            [pscustomobject]@{ name = 'smoke-boolean'; kind = 'booleans'; threads = 'all'; cases = 'grid:2' },
            [pscustomobject]@{ name = 'smoke-smallwrap'; kind = 'small'; threads = 'all'; cases = '--only|62 B|--mode|new' },
            [pscustomobject]@{ name = 'smoke-wrap'; kind = 'repair'; threads = 'all'; cases = $oct }
        )
    }
    $all = @(Import-Csv (Join-Path $here 'strata.csv'))
    if ($Stratum.Count -gt 0) { $all = @($all | Where-Object { $Stratum -contains $_.name }) }
    foreach ($s in $all) {
        if ($s.cases -eq '{WrapMeshes}') {
            if (-not $WrapMeshes) { Write-Warning "$($s.name): skipped, -WrapMeshes is not set"; $s.cases = $null } else { $s.cases = $WrapMeshes }
        }
    }
    return @($all | Where-Object { $_.cases -ne $null })
}

# one run of a stratum: the rows of its cases ({case; op; ms; hash}) and an error text if the run failed
function Invoke-Stratum($s, [string]$runtime) {
    $rows = @()
    $threads = $s.threads
    if ($s.kind -eq 'booleans') {
        # a case is a directory below $BData\cases (A.off, B.off), grid:<n>, or @<list file of the repository> with one case per line
        $caseArgs = @()
        foreach ($c in ($s.cases -split ' ')) {
            if ($c.StartsWith('@')) { $caseArgs += (Join-Path $here ($c.Substring(1))) } else { $caseArgs += $c }
        }
        $cwd = Join-Path $BData 'cases'
        if (-not (Test-Path $cwd)) { New-Item -ItemType Directory -Force -Path $cwd | Out-Null }
        $r = Invoke-Tool 'booleans' $runtime (@('profile', '--repeat', '1') + $caseArgs) @{} $threads $cwd
        foreach ($line in $r.Lines) {
            if (-not $line.StartsWith('{')) { continue }
            $j = $line | ConvertFrom-Json
            if ($j.error) { return @{ Rows = $rows; Error = "case $($j.case): $($j.error)" } }
            $rows += [pscustomobject]@{ case = $j.case; op = $j.op; ms = [double]($j.ms | Select-Object -First 1); hash = [string]$j.hash }
        }
    } elseif ($s.kind -eq 'small') {
        $r = Invoke-Tool 'small' $runtime (@('--hash') + ($s.cases -split '\|' | Where-Object { $_ })) @{} $threads $here
        foreach ($line in ($r.Lines | Select-Object -Skip 1)) {
            $f = $line -split ','
            # input,mode,seconds,ok,wrap,wrap_triangles,result_triangles,...,hash
            $rows += [pscustomobject]@{ case = $f[0]; op = $f[1]; ms = 1000 * [double]::Parse($f[2], [Globalization.CultureInfo]::InvariantCulture); hash = $f[-1] }
        }
    } elseif ($s.kind -eq 'repair') {
        # the wrap of MeshRepairBenchmark (BENCH_REPAIR=wrap), one process per mesh: a mesh file, or a directory of them (-WrapMeshes)
        $files = @()
        if (Test-Path $s.cases -PathType Container) { $files = @(Get-ChildItem $s.cases -File | Sort-Object Name | ForEach-Object { $_.FullName }) } else { $files = @($s.cases) }
        foreach ($f in $files) {
            $r = Invoke-Tool 'repair' $runtime @($f) @{ BENCH_REPAIR = 'wrap' } $threads $here
            $line = $r.Lines | Where-Object { $_.StartsWith('{') } | Select-Object -Last 1
            if (-not $line) { return @{ Rows = $rows; Error = "no result line for $f" } }
            $j = $line | ConvertFrom-Json
            if ($j.status -notin @('ok', 'repaired', 'clean', $null) -and -not $j.after_hash) { return @{ Rows = $rows; Error = "$f : status $($j.status)" } }
            $rows += [pscustomobject]@{ case = (Split-Path -Leaf $f); op = 'wrap'; ms = [double]$j.wrap_ms; hash = [string]$j.after_hash }
        }
    } else { throw "unknown kind $($s.kind)" }
    return @{ Rows = $rows; Error = $null }
}

# --- the runs ---------------------------------------------------------------------------------------------------------------------------
$strata = @(Get-Strata)
if ($strata.Count -eq 0) { throw 'no stratum selected' }
$runsCsv = Join-Path $OutDir 'runs.csv'
$casesCsv = Join-Path $OutDir 'cases.csv'
'stratum,runtime,round,threads,time_ms,hash,cases,status' | Set-Content -Encoding ASCII $runsCsv
'stratum,runtime,round,threads,case,op,ms,hash' | Set-Content -Encoding ASCII $casesCsv
$settings = [ordered]@{
    date = (Get-Date -Format 'o'); machine = $env:COMPUTERNAME; cores = [Environment]::ProcessorCount; os = [Environment]::OSVersion.VersionString
    dotnet = (& dotnet --version); rounds = $Rounds; runtimes = $Runtimes; net10Env = $Net10Env
    dotnetEnvironment = @(Get-ChildItem Env: | Where-Object { $_.Name -like 'DOTNET_*' -or $_.Name -like 'COMPlus_*' -or $_.Name -like 'BOOLCMP_*' } | ForEach-Object { "$($_.Name)=$($_.Value)" })
    note = 'net48 has no tiered PGO; net10.0 runs with the runtime defaults unless a net10.0-<suffix> runtime sets variables (net10.0-pgo0: DOTNET_TieredPGO=0)'
}
$settings | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $OutDir 'settings.json')

$hashes = @{}   # stratum|threads -> runtime -> set of run hashes
$failed = $false
foreach ($s in $strata) {
    foreach ($round in 1..$Rounds) {
        # alternate: the runtime order flips every round
        $order = if ($round % 2 -eq 1) { $Runtimes } else { @($Runtimes[($Runtimes.Count - 1)..0]) }
        foreach ($runtime in $order) {
            $res = Invoke-Stratum $s $runtime
            $status = if ($res.Error) { 'error: ' + ($res.Error -replace '[,\r\n]', ' ') } else { 'ok' }
            if ($res.Error) { $failed = $true }
            $time = 0.0 + ($res.Rows | Measure-Object -Property ms -Sum).Sum
            $hash = if ($res.Rows.Count -gt 0) { Get-Hash16 ($res.Rows | ForEach-Object { "$($_.case)/$($_.op)=$($_.hash)" }) } else { '-' }
            "{0},{1},{2},{3},{4},{5},{6},{7}" -f $s.name, $runtime, $round, $s.threads, [math]::Round($time), $hash, $res.Rows.Count, $status | Add-Content -Encoding ASCII $runsCsv
            foreach ($row in $res.Rows) {
                "{0},{1},{2},{3},{4},{5},{6},{7}" -f $s.name, $runtime, $round, $s.threads, $row.case, $row.op, [math]::Round($row.ms), $row.hash | Add-Content -Encoding ASCII $casesCsv
            }
            Write-Host ("{0} {1} round {2}: {3} ms, hash {4}, {5}" -f $s.name, $runtime, $round, [math]::Round($time), $hash, $status)
            $key = "$($s.name)|$($s.threads)"
            if (-not $hashes.ContainsKey($key)) { $hashes[$key] = @{} }
            if (-not $hashes[$key].ContainsKey($runtime)) { $hashes[$key][$runtime] = @{} }
            $hashes[$key][$runtime][$hash] = $true
        }
    }
}

# --- the hash comparison: one hash per stratum over all runs and runtimes, or a determinism finding (#789, #803, #804) --------------------
$mismatch = $false
foreach ($key in $hashes.Keys) {
    $distinct = @($hashes[$key].Values | ForEach-Object { $_.Keys } | Sort-Object -Unique)
    if ($distinct.Count -ne 1 -or $distinct[0] -eq '-') {
        $mismatch = $true
        Write-Host "HASH MISMATCH $key : $($distinct -join ' ')" -ForegroundColor Red
    }
}
Write-Host "rows: $runsCsv"
if ($failed -or $mismatch) { exit 1 }
Write-Host 'all runs ok, hashes equal across the runtimes'
