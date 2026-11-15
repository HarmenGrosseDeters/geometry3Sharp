# Thread scaling of ExactMeshBoolean on the T6 case set (docs/perf/BOOLEAN_THREAD_SCALING.md): the same cases at 1, 2, 4, 8, 12 threads, on net10.0 (and net48),
# the combinations alternated round by round, phase rows and result hashes to CSV, a drift check at the end. Works in Windows PowerShell 5.1 and PowerShell 7
# (also on Linux, where the affinity is set with the same API).
#
#   ./thread_scaling.ps1 -DryRun                                the plan and the commands, nothing is built, fetched or run
#   ./thread_scaling.ps1 -Set three -Rounds 1 -Limit 3          a short check of the setup (3 cases)
#   ./thread_scaling.ps1                                        the T6 set (98 pairs, 294 booleans), threads 1,2,4,8,12, 3 rounds, net10.0
#   ./thread_scaling.ps1 -Set t6,three -Runtimes net10.0,net48  both sets and both runtimes (net48 runs on Windows only)
#
# Sets: t6 = the 98 pairs of perf/t6.py (1k-100k triangles in total, the case set of the T6 comparison with CGAL); three = the three slowest roundings
# among them: p0466_203344+80414, p0861_80557+285607, p0974_97674+472023 (union / intersection / difference, each; the large rounding times are
# the union of 0861 and 0974 and the difference of 0466). Thingi10K is fetched at run time from huggingface into -Data (about 300 MB), no mesh
# is part of the repository.
#
# Threads: the process runs on n logical processors, set as an affinity mask right after the start (a 6-core machine with SMT shows 12 logical
# processors: -Stride 1 takes processors 0..n-1, which are SMT pairs, so 2 threads are one physical core; -Stride 2 takes every second processor,
# one per physical core, for n <= 6; 8 and 12 threads need Stride 1). net10.0 also gets DOTNET_PROCESSOR_COUNT=n (the pool size).
# Output (in -OutDir): runs.csv (one row per process: time of all booleans, hash over all), cases.csv (case, op, best time, hash), phases.csv
# (the phase times of the fastest run of each boolean), raw\*.jsonl (the profile output, input of perf/scaling_report.py), summary.csv, settings.json.
# Exit code 1: a failed run or a hash that differs between the runs (the results must not depend on the threads, the runtime or the round).
[CmdletBinding()]
param(
    [string[]]$Set = @('t6'),
    [string[]]$ThreadCounts = @('1', '2', '4', '8', '12'),
    [int]$Rounds = 3,
    [int]$Repeat = 2,
    [string[]]$Runtimes = @('net10.0'),
    [int]$Stride = 1,
    [int]$Limit = 0,
    [string]$Data = $(if ($env:BDATA) { Join-Path $env:BDATA 't6' } else { Join-Path $HOME 'bdata\t6' }),
    [string]$OutDir = '',
    [string]$Python = '',
    [switch]$NoBuild,
    [switch]$NoFetch,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$bc = Split-Path -Parent $here
$repo = Split-Path -Parent (Split-Path -Parent $bc)
$threeNames = @('p0466_203344+80414', 'p0861_80557+285607', 'p0974_97674+472023')
if (-not $OutDir) { $OutDir = Join-Path $here ('results-scaling\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
if ($Python -eq '') {
    foreach ($c in @('python3', 'python')) { if (Get-Command $c -ErrorAction SilentlyContinue) { $Python = $c; break } }
    if ($Python -eq '') { $Python = 'python3' }
}
# (powershell -File passes 'a,b' as one string: lists are split here)
$Set = @($Set | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Runtimes = @($Runtimes | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$ThreadCounts = @($ThreadCounts | ForEach-Object { $_ -split ',' } | Where-Object { $_ } | ForEach-Object { [int]$_ })
foreach ($s in $Set) { if (@('t6', 'three') -notcontains $s) { throw "unknown set $s (t6, three)" } }
foreach ($r in $Runtimes) { if (@('net10.0', 'net48') -notcontains $r) { throw "unknown runtime $r (net10.0, net48)" } }
$logical = [Environment]::ProcessorCount
$threads = @()
foreach ($n in $ThreadCounts) {
    if ($n * $Stride -gt $logical) { Write-Warning "$n threads with stride ${Stride}: skipped, the machine has $logical logical processors"; continue }
    $threads += $n
}
if ($threads.Count -eq 0) { throw 'no thread count fits this machine' }

# the affinity mask of n processors at the given stride: bits 0, s, 2s, ...
function Get-Mask([int]$n) {
    [int64]$m = 0
    for ($i = 0; $i -lt $n; $i++) { $m = $m -bor ([int64]1 -shl ($i * $Stride)) }
    return $m
}

# --- the tool, per runtime (frozen into OutDir so that a rebuild during the run cannot mix versions) --------------------------------------
$proj = Join-Path $bc 'BooleanComparison.csproj'
function Get-Tool([string]$runtime) {
    if ($runtime -eq 'net48') {
        $dir = Join-Path $OutDir 'tool\net48'
        return @{ File = (Join-Path $dir 'BooleanComparison.exe'); Prefix = @() }
    }
    return @{ File = 'dotnet'; Prefix = @((Join-Path $OutDir 'tool\net10.0\BooleanComparison.dll')) }
}
function Quote([string]$a) { if ($a -match '[\s"]') { return '"' + ($a -replace '"', '\"') + '"' } return $a }

$cases = New-Object System.Collections.ArrayList
$exportDir = Join-Path $Data 'cases'

if ($DryRun) {
    Write-Host "DRY RUN: nothing is built, fetched or run. Plan:"
    Write-Host "  out dir      $OutDir"
    Write-Host "  data dir     $Data   (fetch: $(-not $NoFetch))"
    Write-Host "  sets         $($Set -join ', ')   runtimes $($Runtimes -join ', ')   threads $($threads -join ', ')   stride $Stride   rounds $Rounds   repeat $Repeat   limit $Limit"
    if (-not $NoBuild) {
        $b = "dotnet build $(Quote $proj) -c Release"
        if ($Runtimes -contains 'net48') { $b += ' -p:BenchNet48=true' }
        Write-Host "  build        $b"
    }
    Write-Host "  case set     $Python $(Join-Path $here 't6.py') pairs <pairs_t6.csv>; $Python ... t6.py fetch $Data <pairs_t6.csv>; $Python ...\data\thingi_pairs.py manifest <pairs_t6.csv> $Data <cases.jsonl>"
    Write-Host "               one 'BooleanComparison thingi-pair' export per case into $exportDir"
}

# --- build, case set, data --------------------------------------------------------------------------------------------------------------
if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'raw') | Out-Null
    $OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
    if (-not $NoBuild) {
        $extra = @()
        if ($Runtimes -contains 'net48') { $extra += '-p:BenchNet48=true' }
        & dotnet build $proj -c Release -v q -nologo @extra
        if ($LASTEXITCODE -ne 0) { throw 'build failed' }
    }
    $bin = Join-Path $bc 'bin\Release'
    foreach ($r in $Runtimes) {
        $src = Join-Path $bin $r
        if (-not (Test-Path $src)) { throw "no build output $src (run without -NoBuild)" }
        $dst = Join-Path $OutDir "tool\$r"
        New-Item -ItemType Directory -Force -Path $dst | Out-Null
        Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $Data | Out-Null
    $pairs = Join-Path $Data 'pairs_t6.csv'
    $manifest = Join-Path $Data 'cases.jsonl'
    if (-not (Test-Path $pairs)) { & $Python (Join-Path $here 't6.py') pairs $pairs; if ($LASTEXITCODE -ne 0) { throw 'pairs failed' } }
    if (-not $NoFetch) { & $Python (Join-Path $here 't6.py') fetch $Data $pairs; if ($LASTEXITCODE -ne 0) { throw 'fetch failed' } }
    if (-not (Test-Path $manifest)) { & $Python (Join-Path $bc 'data\thingi_pairs.py') manifest $pairs $Data $manifest; if ($LASTEXITCODE -ne 0) { throw 'manifest failed' } }
    # export every case once (A.off, B.off), with net10.0 (the same files for all runtimes)
    $exportTool = @((Join-Path $OutDir 'tool\net10.0\BooleanComparison.dll'))
    if (-not (Test-Path $exportTool[0])) { $exportTool = @((Join-Path $bc 'bin\Release\net10.0\BooleanComparison.dll')) }
    foreach ($line in (Get-Content $manifest)) {
        if ($line.Trim().Length -eq 0) { continue }
        $c = $line | ConvertFrom-Json
        $d = Join-Path $exportDir $c.name
        if (-not (Test-Path (Join-Path $d 'B.off'))) {
            New-Item -ItemType Directory -Force -Path $d | Out-Null
            $tp = @($c.thingi_pair)
            $a = @($exportTool) + @('thingi-pair', $tp[0], $tp[1], $d) + @($tp[2..($tp.Count - 1)])
            & dotnet @a | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "export of $($c.name) failed" }
        }
        [void]$cases.Add($c.name)
    }
    if ($Limit -gt 0) { $cases = New-Object System.Collections.ArrayList(, @($cases | Select-Object -First $Limit)) }
}
else {
    Write-Host "  cases        the 98 pairs of the T6 set (names from $Data\cases.jsonl); set 'three': $($threeNames -join ', ')"
}

# the case list files (one case directory per line)
function Write-List([string]$sn) {
    $names = if ($sn -eq 'three') { @($cases | Where-Object { $threeNames -contains $_ }) } else { @($cases) }
    $list = Join-Path $OutDir "cases-$sn.txt"
    $names | ForEach-Object { Join-Path $exportDir $_ } | Set-Content -Encoding ASCII $list
    return @{ List = $list; Count = $names.Count }
}
$lists = @{}
if (-not $DryRun) {
    foreach ($s in $Set) { $lists[$s] = Write-List $s; Write-Host ("set {0}: {1} cases" -f $s, $lists[$s].Count); if ($lists[$s].Count -eq 0) { throw "set $s has no case" } }
}

# --- the matrix: round x set x (runtime, threads), the order alternating -----------------------------------------------------------------
$combos = @()
foreach ($r in $Runtimes) { foreach ($n in $threads) { $combos += [pscustomobject]@{ Runtime = $r; Threads = $n } } }

function Invoke-Profile($sn, $combo, [int]$round) {
    $tool = Get-Tool $combo.Runtime
    $list = if ($DryRun) { "<$OutDir\cases-$sn.txt>" } else { $lists[$sn].List }
    $toolArgs = @($tool.Prefix) + @('profile', '--repeat', [string]$Repeat, '--safe-rounding', $list)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $tool.File
    $psi.Arguments = ($toolArgs | ForEach-Object { Quote ([string]$_) }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if ($combo.Runtime -eq 'net10.0') { $psi.EnvironmentVariables['DOTNET_PROCESSOR_COUNT'] = [string]$combo.Threads }
    $mask = Get-Mask $combo.Threads
    if ($DryRun) {
        Write-Host ("  round {0} {1} {2} t={3} mask 0x{4:X}: {5} {6}" -f $round, $sn, $combo.Runtime, $combo.Threads, $mask, $psi.FileName, $psi.Arguments)
        return $null
    }
    $p = [System.Diagnostics.Process]::Start($psi)
    $got = ''
    try { $p.ProcessorAffinity = [IntPtr]$mask } catch { Write-Warning "affinity: $_" }
    try { $got = '0x{0:X}' -f [int64]$p.ProcessorAffinity } catch { }
    $errTask = $p.StandardError.ReadToEndAsync()
    $out = $p.StandardOutput.ReadToEnd()
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { Write-Warning ("exit code {0}: {1}`n{2}" -f $p.ExitCode, $psi.Arguments, $errTask.Result) }
    $raw = Join-Path $OutDir ("raw\{0}_{1}_t{2}_r{3}.jsonl" -f $sn, $combo.Runtime, $combo.Threads, $round)
    Set-Content -Encoding UTF8 -Path $raw -Value $out
    return @{ Lines = @($out -split "`r?`n" | Where-Object { $_.StartsWith('{') }); ExitCode = $p.ExitCode; Mask = $got }
}

if (-not $DryRun) {
    'set,runtime,round,threads,mask,time_ms,cases,status' | Set-Content -Encoding ASCII (Join-Path $OutDir 'runs.csv')
    'set,runtime,round,threads,case,op,ms,hash,rounding_ms' | Set-Content -Encoding ASCII (Join-Path $OutDir 'cases.csv')
    'set,runtime,round,threads,case,op,phase,ms' | Set-Content -Encoding ASCII (Join-Path $OutDir 'phases.csv')
    [ordered]@{
        date = (Get-Date -Format 'o'); machine = [Environment]::MachineName; logical_processors = $logical; os = [Environment]::OSVersion.VersionString
        dotnet = (& dotnet --version); sets = $Set; runtimes = $Runtimes; threads = $threads; stride = $Stride; rounds = $Rounds; repeat = $Repeat
        commit = (& git -C $repo rev-parse HEAD 2>$null)
        environment = @(Get-ChildItem Env: | Where-Object { $_.Name -like 'DOTNET_*' -or $_.Name -like 'BOOLCMP_*' } | ForEach-Object { "$($_.Name)=$($_.Value)" })
    } | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $OutDir 'settings.json')
}

$all = New-Object System.Collections.ArrayList     # every boolean of every run: set, runtime, round, threads, case, op, ms, hash
$failed = $false
for ($round = 1; $round -le $Rounds; $round++) {
    foreach ($sn in $Set) {
        $order = if ($round % 2 -eq 1) { $combos } else { @($combos[($combos.Count - 1)..0]) }
        foreach ($combo in $order) {
            $res = Invoke-Profile $sn $combo $round
            if ($DryRun) { continue }
            $rows = @()
            $err = $null
            foreach ($line in $res.Lines) {
                $j = $line | ConvertFrom-Json
                if ($j.error) { $err = "case $($j.case): $($j.error)"; break }
                $ms = [double](@($j.ms) | Measure-Object -Minimum).Minimum
                $rows += [pscustomobject]@{ case = (Split-Path -Leaf $j.case); op = $j.op; ms = $ms; hash = [string]$j.hash; phases = $j.phases_ms }
            }
            if ($res.ExitCode -ne 0 -and -not $err) { $err = "exit code $($res.ExitCode)" }
            if ($rows.Count -ne ($lists[$sn].Count * 3) -and -not $err) { $err = "$($rows.Count) rows, expected $($lists[$sn].Count * 3)" }
            if ($err) { $failed = $true }
            $status = if ($err) { 'error: ' + ($err -replace '[,\r\n]', ' ') } else { 'ok' }
            $time = [math]::Round(0.0 + ($rows | Measure-Object -Property ms -Sum).Sum)
            "{0},{1},{2},{3},{4},{5},{6},{7}" -f $sn, $combo.Runtime, $round, $combo.Threads, $res.Mask, $time, $rows.Count, $status | Add-Content -Encoding ASCII (Join-Path $OutDir 'runs.csv')
            foreach ($row in $rows) {
                $rm = if ($row.phases -and $row.phases.PSObject.Properties['rounding']) { $row.phases.rounding } else { '' }
                "{0},{1},{2},{3},{4},{5},{6},{7},{8}" -f $sn, $combo.Runtime, $round, $combo.Threads, $row.case, $row.op, [math]::Round($row.ms, 1), $row.hash, $rm | Add-Content -Encoding ASCII (Join-Path $OutDir 'cases.csv')
                if ($row.phases) {
                    foreach ($ph in $row.phases.PSObject.Properties) {
                        "{0},{1},{2},{3},{4},{5},{6},{7}" -f $sn, $combo.Runtime, $round, $combo.Threads, $row.case, $row.op, $ph.Name, $ph.Value | Add-Content -Encoding ASCII (Join-Path $OutDir 'phases.csv')
                    }
                }
                [void]$all.Add([pscustomobject]@{ set = $sn; runtime = $combo.Runtime; round = $round; threads = $combo.Threads; key = "$($row.case)/$($row.op)"; ms = $row.ms; hash = $row.hash })
            }
            Write-Host ("round {0} {1} {2} t={3}: {4} ms, {5} booleans, {6}" -f $round, $sn, $combo.Runtime, $combo.Threads, $time, $rows.Count, $status)
        }
    }
}
if ($DryRun) { Write-Host 'dry run done'; exit 0 }

# --- drift check: one hash per boolean over all runs (threads, runtimes, rounds, sets) --------------------------------------------------
$drift = 0
foreach ($g in ($all | Group-Object key)) {
    $h = @($g.Group | ForEach-Object { $_.hash } | Sort-Object -Unique)
    if ($h.Count -ne 1) { $drift++; Write-Host "HASH DRIFT $($g.Name): $($h -join ' ')" -ForegroundColor Red }
}
Write-Host ("drift check: {0} booleans, {1} with differing hashes" -f @($all | Group-Object key).Count, $drift)

# --- summary: per set, runtime and thread count the sum of the best time of each boolean over the rounds ---------------------------------
'set,runtime,threads,booleans,best_sum_ms,speedup_vs_first,efficiency' | Set-Content -Encoding ASCII (Join-Path $OutDir 'summary.csv')
foreach ($sn in $Set) {
    foreach ($r in $Runtimes) {
        $base = $null
        foreach ($n in $threads) {
            $g = @($all | Where-Object { $_.set -eq $sn -and $_.runtime -eq $r -and $_.threads -eq $n })
            if ($g.Count -eq 0) { continue }
            $sum = 0.0; $cnt = 0
            foreach ($k in ($g | Group-Object key)) { $sum += ($k.Group | Measure-Object -Property ms -Minimum).Minimum; $cnt++ }
            if ($null -eq $base) { $base = $sum; $n0 = $n }
            $sp = $base / $sum
            $line = "{0},{1},{2},{3},{4},{5},{6}" -f $sn, $r, $n, $cnt, [math]::Round($sum), [math]::Round($sp, 2), [math]::Round($sp / ($n / $n0), 2)
            $line | Add-Content -Encoding ASCII (Join-Path $OutDir 'summary.csv')
            Write-Host ("{0} {1} {2,2} threads: {3,8} ms, x{4:N2} (efficiency {5:N2})" -f $sn, $r, $n, [math]::Round($sum), $sp, ($sp / ($n / $n0)))
        }
    }
}
Write-Host "rows: $OutDir"
Write-Host "phase tables: $Python perf/scaling_report.py --bound 1=raw/<set>_net10.0_t1_r1.jsonl,... (see the README section)"
if ($failed -or $drift -gt 0) { exit 1 }
Write-Host 'all runs ok, no drift'
