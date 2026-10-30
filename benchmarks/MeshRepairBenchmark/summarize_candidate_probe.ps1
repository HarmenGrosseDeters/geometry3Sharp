param(
    [string]$ResultDirectory = (Join-Path $PSScriptRoot 'results\alpha-quality-20261029\candidate-probe-20261029')
)
$ErrorActionPreference = 'Stop'
function Read-Rows([string]$path) { @(Get-Content $path | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json }) }
function Index-Rows($rows) {
    $index = @{}
    foreach ($row in $rows) {
        if (!$row.file -or $index.ContainsKey($row.file)) { throw 'Missing or duplicate file ID.' }
        $index[$row.file] = $row
    }
    return $index
}
function Median($values) {
    foreach ($value in $values) {
        if ($null -eq $value -or $value -is [string] -or $value -is [bool] -or [double]::IsNaN($value) -or [double]::IsInfinity($value)) { throw 'Missing or nonfinite metric.' }
    }
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return 0.5 * ($sorted[$middle - 1] + $sorted[$middle])
}
$metadata = Get-Content (Join-Path $ResultDirectory 'metadata.json') -Raw | ConvertFrom-Json
$actual = Index-Rows (Read-Rows (Join-Path $ResultDirectory 'actual.jsonl'))
$probes = Index-Rows (Read-Rows (Join-Path $ResultDirectory 'probes.jsonl'))
$prior = Join-Path $PSScriptRoot 'results\alpha-quality-20261029'
$before = Index-Rows (Read-Rows (Join-Path $prior 'before.jsonl'))
$unsimplified = Index-Rows (Read-Rows (Join-Path $prior 'unsimplified.jsonl'))
foreach ($index in @($actual, $probes)) {
    if (@(Compare-Object @($metadata.files) @($index.Keys)).Count -ne 0) { throw 'Recorded ID set differs from metadata.' }
}
$complete = @($probes.Values | Where-Object {
    $_.record_type -eq 'wrap_candidate_probe' -and $_.diagnostic_only -eq $true -and $_.probe_stage -eq 'complete' -and
    $_.status -eq 'closed' -and $_.process_timed_out -eq $false -and $_.stdout_parse_errors -eq 0 -and
    ($null -eq $_.process_exit_code -or $_.process_exit_code -eq 0)
} | Sort-Object file)
$details = @()
foreach ($probe in $complete) {
    $file = $probe.file
    $control = $actual[$file]
    if ($control.status -ne 'closed' -or !$before.ContainsKey($file) -or !$unsimplified.ContainsKey($file)) { throw "Missing control: $file" }
    $details += [pscustomobject][ordered]@{
        file = $file
        original_baseline_worst1_angle = $before[$file].fm_q_out.minangle[1]
        actual_worst1_angle = $control.fm_q_out.minangle[1]
        candidate_worst1_angle = $probe.fm_q_out.minangle[1]
        candidate_tris_over_unsimplified = $probe.after_tris / $unsimplified[$file].after_tris
        actual_tris = $control.after_tris; candidate_tris = $probe.after_tris
        actual_accepted_source = $control.robust_wrap_simplification.acceptedSource
        clean = ($probe.after_valid -eq $true -and $probe.candidate_clean_reason -ceq '')
        triangle_cert_all = $probe.candidate_triangle_cert_all
        triangle_cert_count = $probe.candidate_triangle_cert_passed
        fine_status = $probe.candidate_fine_status; fine_converged = $probe.candidate_fine_converged
        lower = $probe.candidate_fine_lower; upper = $probe.candidate_fine_upper
        budget = $probe.candidate_distance_budget; tolerance = $probe.candidate_fine_tolerance
        original_baseline_k_error = $before[$file].fm_k_err; candidate_k_error = $probe.fm_k_err
        k_error_over_five_percent = ($probe.fm_k_err -gt 1.05 * $before[$file].fm_k_err)
    }
}
$summary = [ordered]@{
    diagnostic_only = $true
    command = "powershell -NoProfile -File benchmarks\MeshRepairBenchmark\summarize_candidate_probe.ps1 -ResultDirectory `"$ResultDirectory`""
    expected_cases = @($metadata.files).Count
    actual_closed = @($actual.Values | Where-Object { $_.status -eq 'closed' }).Count
    actual_timeouts = @($actual.Values | Where-Object { $_.status -eq 'timeout' } | ForEach-Object { $_.file } | Sort-Object)
    complete_probes = $complete.Count
    unknown_process_exit_codes = @($probes.Values | Where-Object { $null -eq $_.process_exit_code } | ForEach-Object { $_.file } | Sort-Object)
    incomplete_or_missing_probes = @($metadata.files | Where-Object { $_ -notin @($complete.file) })
    clean_complete_candidates = @($details | Where-Object { $_.clean }).Count
    all_triangles_certified = @($details | Where-Object { $_.triangle_cert_all -eq $true }).Count
    finer_passed = @($details | Where-Object { $_.fine_status -eq 'passed' }).Count
    finer_exceeded = @($details | Where-Object { $_.fine_status -eq 'exceeded' }).Count
    finer_incomplete = @($details | Where-Object { $_.fine_status -eq 'incomplete' } | ForEach-Object { $_.file })
    completed_pair_medians = @{
        original_baseline_worst1_angle = (Median @($details.original_baseline_worst1_angle))
        actual_worst1_angle = (Median @($details.actual_worst1_angle))
        candidate_worst1_angle = (Median @($details.candidate_worst1_angle))
        candidate_tris_over_unsimplified = (Median @($details.candidate_tris_over_unsimplified))
    }
    completed_candidates_below_angle_gate = @($details | Where-Object { $_.candidate_worst1_angle -lt 10 }).Count
    k_error_regressions_against_original_baseline = @($details | Where-Object { $_.k_error_over_five_percent } | ForEach-Object { $_.file })
    limitations = @('Completed-pair metrics exclude missing probes; they are not full-set acceptance statistics.',
        'Different machine and SDK from original baseline: no paired performance conclusion.',
        'Complete probe payloads with unknown process exit codes remain diagnostic evidence, not successful-run attestations.',
        'Only angle, count, cleanliness and Gaussian-curvature observations are summarized; not a full fidelity acceptance audit.',
        'Diagnostic certificate outcomes do not alter the production decision or authorize tuning.')
    details = $details
}
$summary | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $ResultDirectory 'summary.json') -Encoding UTF8
[pscustomobject]$summary | Select-Object expected_cases,actual_closed,actual_timeouts,complete_probes,incomplete_or_missing_probes,
    clean_complete_candidates,all_triangles_certified,finer_passed,finer_exceeded,finer_incomplete,
    completed_pair_medians,completed_candidates_below_angle_gate,k_error_regressions_against_original_baseline | ConvertTo-Json -Depth 5
