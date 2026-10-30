param(
    [string]$ResultDirectory = (Join-Path $PSScriptRoot 'results\alpha-quality-20261029\guarded-feature-trial-20261029'),
    [string]$PythonExecutable = 'python'
)
$ErrorActionPreference = 'Stop'
$prior = Join-Path $PSScriptRoot 'results\alpha-quality-20261029'
function Read-Rows([string]$path) { @(Get-Content $path | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json }) }
function Index-Rows($rows) {
    $index = @{}
    foreach ($row in $rows) {
        if (!$row.file -or $index.ContainsKey($row.file)) { throw 'Missing or duplicate file ID.' }
        $index[$row.file] = $row
    }
    return $index
}
$metadata = Get-Content (Join-Path $ResultDirectory 'metadata.json') -Raw | ConvertFrom-Json
if ($metadata.guarded_feature_trial -ne $true -or $metadata.timeout_seconds -ne 300 -or @($metadata.files).Count -ne 17) {
    throw 'Not the authorized fixed trial.'
}
$control = Index-Rows (Read-Rows (Join-Path $ResultDirectory 'control.jsonl'))
$candidate = Index-Rows (Read-Rows (Join-Path $ResultDirectory 'guarded-features.jsonl'))
foreach ($index in @($control, $candidate)) {
    if (@(Compare-Object @($metadata.files) @($index.Keys)).Count -ne 0) { throw 'Trial ID set differs from metadata.' }
}
$baseline = Read-Rows (Join-Path $prior 'before.jsonl')
foreach ($row in $baseline) {
    if (!$control.ContainsKey($row.file)) { continue }
    $measured = $control[$row.file]
    $valid = $measured.status -eq 'closed' -and $measured.process_timed_out -eq $false -and
        $null -ne $measured.process_exit_code -and $measured.process_exit_code -eq 0 -and $measured.stdout_parse_errors -eq 0
    $row.repair_ms = if ($valid) { $measured.repair_ms } else { $null }
}
$baselinePath = Join-Path $ResultDirectory 'baseline-original-geometry-fresh-times.jsonl'
$baseline | ForEach-Object { $_ | ConvertTo-Json -Depth 30 -Compress } | Set-Content $baselinePath -Encoding UTF8
$reports = @{}
foreach ($run in @('control', 'guarded-features')) {
    $report = Join-Path $ResultDirectory ($run + '-paired.json')
    & $PythonExecutable (Join-Path $PSScriptRoot 'analyze_fidelity.py') --paired $baselinePath (Join-Path $ResultDirectory ($run + '.jsonl')) (Join-Path $prior 'unsimplified.jsonl') --subset --require-certificate | Set-Content $report -Encoding UTF8
    if ($LASTEXITCODE -notin @(0, 1)) { throw "Paired analyzer failed for $run (exit $LASTEXITCODE)." }
    $reports[$run] = Get-Content $report -Raw | ConvertFrom-Json
}
# The unchanged control is expected to miss the target angle; any other failure invalidates the comparison.
$controlFailures = @($reports.control.failures | Where-Object { $_.metric -ne 'angle_median' })
$processFailures = @()
foreach ($run in @('control', 'guarded-features')) {
    $index = if ($run -eq 'control') { $control } else { $candidate }
    foreach ($row in $index.Values) {
        if ($row.process_timed_out -ne $false -or $null -eq $row.process_exit_code -or
            $row.process_exit_code -ne 0 -or $row.stdout_parse_errors -ne 0) {
            $processFailures += [pscustomobject]@{ run = $run; file = $row.file; status = $row.status }
        }
    }
}
$summary = [ordered]@{
    subset_only = $true; fixed_cases = @($metadata.files).Count
    original_geometry_baseline = (Join-Path $prior 'before.jsonl')
    timing_baseline = 'Only repair_ms for selected IDs is replaced with the fresh unchanged control; other baseline fields and unsimplified denominators are unchanged.'
    control_baseline_gates_pass = ($controlFailures.Count -eq 0)
    subset_pass = ($controlFailures.Count -eq 0 -and $processFailures.Count -eq 0 -and $reports['guarded-features'].pass)
    control_failures_other_than_target_angle = $controlFailures
    process_failures = $processFailures
    aggregate = $reports['guarded-features'].aggregate
    failed_candidate_meshes = @($reports['guarded-features'].failures | Where-Object { $_.file -ne '*' } | ForEach-Object { $_.file } | Sort-Object -Unique)
    failed_candidate_metrics = @($reports['guarded-features'].failures).Count
    candidate_failures = $reports['guarded-features'].failures
    decision = 'No tuning or default switch. A failed subset stops the trial; a passing subset is not full acceptance.'
}
$summary | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $ResultDirectory 'trial-summary.json') -Encoding UTF8
[pscustomobject]$summary | Select-Object subset_pass,control_baseline_gates_pass,process_failures,aggregate,failed_candidate_meshes,failed_candidate_metrics | ConvertTo-Json -Depth 6
if (!$summary.subset_pass) { exit 1 }
