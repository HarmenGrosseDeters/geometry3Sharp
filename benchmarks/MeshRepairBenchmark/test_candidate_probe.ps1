$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$dll = Join-Path $PSScriptRoot 'bin\Release\net10.0\MeshRepairBenchmark.dll'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('g3-candidate-probe-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item $temp -ItemType Directory
function Assert-That($condition, [string]$message) { if (!$condition) { throw $message } }
$oldEnvironment = @{}
Get-ChildItem Env: | Where-Object { $_.Name.StartsWith('BENCH_') } | ForEach-Object { $oldEnvironment[$_.Name] = $_.Value; Remove-Item ("Env:" + $_.Name) }
try {
    $mesh = Join-Path $temp 'tetra.stl'
    $vertices = @('0 0 0', '1 0 0', '0 1 0', '0 0 1')
    $facets = @('solid tetra')
    foreach ($triangle in @(@(0,2,1), @(0,1,3), @(0,3,2), @(1,2,3))) {
        $facets += 'facet normal 0 0 0', 'outer loop'
        foreach ($id in $triangle) { $facets += 'vertex ' + $vertices[$id] }
        $facets += 'endloop', 'endfacet'
    }
    $facets += 'endsolid tetra'
    $facets | Set-Content $mesh -Encoding ASCII
    $env:BENCH_REPAIR = 'robust'; $env:BENCH_FIDELITY = '0'
    $env:BENCH_WRAP_POLICY = 'tangential'; $env:BENCH_WRAP_DIAGNOSTICS = '1'
    $off = @(& dotnet $dll $mesh std none rel | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-That ($LASTEXITCODE -eq 0 -and $off.Count -eq 1 -and $off[0].status -eq 'closed') 'Probe-off output changed.'
    $env:BENCH_WRAP_CANDIDATE_PROBE = '1'
    $on = @(& dotnet $dll $mesh std none rel | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-That ($LASTEXITCODE -eq 0 -and $on.Count -eq 2) 'Probe-on did not emit actual plus diagnostic.'
    Assert-That ($on[0].status -eq $off[0].status -and $on[0].after_tris -eq $off[0].after_tris -and $on[0].after_closed) 'Probe changed actual result.'
    Assert-That ($on[1].record_type -eq 'wrap_candidate_probe' -and $on[1].diagnostic_only -eq $true -and $on[1].status -eq 'candidate_unavailable') 'Clean procedural input should have no wrap candidate.'
    $env:BENCH_WRAP_PROGRESS = '1'
    $stdout = Join-Path $temp 'progress.stdout.jsonl'
    $stderr = Join-Path $temp 'progress.stderr.jsonl'
    $process = Start-Process (Get-Command dotnet).Source -ArgumentList ('"' + $dll + '" "' + $mesh + '" std none rel') -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $null = $process.Handle
    try {
        if (!$process.WaitForExit(30000)) { Stop-Process -Id $process.Id -Force; throw 'Procedural progress test timed out.' }
        $process.WaitForExit()
        Assert-That ($process.ExitCode -eq 0) 'Progress subprocess failed.'
    } finally { $process.Dispose() }
    $traced = @(Get-Content $stdout | ForEach-Object { $_ | ConvertFrom-Json })
    $events = @(Get-Content $stderr | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-That ($traced.Count -eq 2 -and $traced[0].after_tris -eq $on[0].after_tris -and $traced[0].status -eq $on[0].status) 'Progress altered actual output.'
    $lastMs = -1
    foreach ($event in $events) {
        Assert-That ($event.record_type -eq 'wrap_probe_progress' -and $event.file -eq 'tetra.stl' -and $event.elapsed_ms -ge $lastMs) 'Invalid progress record.'
        $lastMs = $event.elapsed_ms
    }
    foreach ($stage in @('load.begin','load.end','repair.begin','repair.repair.complete','repair.end','actual.stats.begin','actual.stats.end','actual.fidelity.volume_iou.begin','actual.fidelity.volume_iou.end')) {
        Assert-That ($stage -in @($events.stage)) ("Missing progress stage: " + $stage)
    }
    Remove-Item Env:BENCH_WRAP_PROGRESS
    $env:BENCH_WRAP_CANDIDATE_PROBE = '0'; $env:BENCH_WRAP_POLICY = 'guarded-features'; $env:BENCH_WRAP_DIAGNOSTICS = '0'
    $guarded = @(& dotnet $dll $mesh std none rel | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-That ($LASTEXITCODE -eq 0 -and $guarded.Count -eq 1 -and $guarded[0].status -eq 'closed' -and $guarded[0].robust_wrap_policy -eq 'guarded-features') 'Fixed guarded-feature CLI policy unavailable.'
    $rejectedModes = $false
    try { & (Join-Path $PSScriptRoot 'run_candidate_probe.ps1') -TimeoutFollowUp -GuardedFeatureTrial } catch { $rejectedModes = $_.Exception.Message -like '*separate modes*' }
    Assert-That $rejectedModes 'Mutually exclusive measurement modes accepted.'
    $env:BENCH_WRAP_CANDIDATE_PROBE = '1'; $env:BENCH_WRAP_POLICY = 'tangential'; $env:BENCH_WRAP_DIAGNOSTICS = '1'
    foreach ($mode in @('default', 'analysis')) {
        $env:BENCH_REPAIR = $mode
        $invalid = @(& dotnet $dll $mesh std none rel | ForEach-Object { $_ | ConvertFrom-Json })
        Assert-That ($invalid.Count -eq 1 -and $invalid[0].status -eq 'exception' -and $invalid[0].error -like '*Candidate probe requires*') 'Invalid repair mode accepted.'
    }
    $env:BENCH_REPAIR = 'robust'; $env:BENCH_WRAP_DIAGNOSTICS = '0'
    $invalid = @(& dotnet $dll $mesh std none rel | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-That ($invalid.Count -eq 1 -and $invalid[0].status -eq 'exception') 'Missing diagnostics accepted.'
    $env:BENCH_WRAP_DIAGNOSTICS = '1'; $env:BENCH_WRAP_POLICY = 'default'
    $invalid = @(& dotnet $dll $mesh std none rel | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-That ($invalid.Count -eq 1 -and $invalid[0].status -eq 'exception') 'Non-tangential policy accepted.'

    $source = Join-Path $PSScriptRoot 'results\alpha-quality-20261029\candidate-probe-20261029'
    if (!(Test-Path $source)) { throw 'Run the fixed candidate probe before testing its summary.' }
    foreach ($file in @('metadata.json', 'actual.jsonl', 'probes.jsonl')) { Copy-Item (Join-Path $source $file) $temp }
    $summarizer = Join-Path $PSScriptRoot 'summarize_candidate_probe.ps1'
    & $summarizer -ResultDirectory $temp | Out-Null
    $summary = Get-Content (Join-Path $temp 'summary.json') -Raw | ConvertFrom-Json
    $rows = @(Get-Content (Join-Path $temp 'probes.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
    $selected = $rows | Where-Object { $_.probe_stage -eq 'complete' -and !$_.process_timed_out } | Select-Object -First 1
    Assert-That ($null -ne $selected) 'No completed probe fixture.'
    $selected.process_timed_out = $true
    $rows | ForEach-Object { $_ | ConvertTo-Json -Depth 30 -Compress } | Set-Content (Join-Path $temp 'probes.jsonl') -Encoding UTF8
    & $summarizer -ResultDirectory $temp | Out-Null
    $timed = Get-Content (Join-Path $temp 'summary.json') -Raw | ConvertFrom-Json
    Assert-That ($timed.complete_probes -eq $summary.complete_probes - 1 -and $selected.file -in $timed.incomplete_or_missing_probes) 'Timeout counted as a complete probe.'
    Copy-Item (Join-Path $source 'probes.jsonl') (Join-Path $temp 'probes.jsonl') -Force
    Get-Content (Join-Path $source 'probes.jsonl') -TotalCount 1 | Add-Content (Join-Path $temp 'probes.jsonl') -Encoding UTF8
    $rejected = $false
    try { & $summarizer -ResultDirectory $temp | Out-Null } catch { $rejected = $_.Exception.Message -like '*duplicate*' }
    Assert-That $rejected 'Duplicate probe IDs accepted.'
    Write-Output 'PASS: procedural CLI smoke, stderr progress isolation, invalid configurations, probe isolation, timeout exclusion and duplicate rejection.'
} finally {
    Get-ChildItem Env: | Where-Object { $_.Name.StartsWith('BENCH_') } | ForEach-Object { Remove-Item ("Env:" + $_.Name) }
    foreach ($key in $oldEnvironment.Keys) { Set-Item ("Env:" + $key) $oldEnvironment[$key] }
    Remove-Item $temp -Recurse -Force
}
