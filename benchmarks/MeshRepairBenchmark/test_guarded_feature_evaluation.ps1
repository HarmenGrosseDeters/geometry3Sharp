param([string]$PythonExecutable = 'python')
$ErrorActionPreference = 'Stop'
$prior = Join-Path $PSScriptRoot 'results\alpha-quality-20261029'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('g3-guarded-evaluation-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item $temp -ItemType Directory
function Assert-That($condition, [string]$message) { if (!$condition) { throw $message } }
function Evaluate {
    & powershell -NoProfile -File (Join-Path $PSScriptRoot 'evaluate_guarded_feature_trial.ps1') -ResultDirectory $temp -PythonExecutable $PythonExecutable | Out-Null
    if ($LASTEXITCODE -notin @(0,1)) { throw 'Evaluation command failed.' }
    Get-Content (Join-Path $temp 'trial-summary.json') -Raw | ConvertFrom-Json
}
try {
    $original = @(Get-Content (Join-Path $prior 'before.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
    $selected = @($original | Where-Object { $_.robust_used_wrap -eq $true })
    Assert-That ($selected.Count -eq 17) 'Unexpected original wrap fixture.'
    @{ guarded_feature_trial=$true; timeout_seconds=300; files=@($selected.file) } |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $temp 'metadata.json') -Encoding UTF8
    $control = @()
    $candidate = @()
    foreach ($row in $selected) {
        $c = $row | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        $c.repair_ms *= 2
        $c | Add-Member -NotePropertyName process_timed_out -NotePropertyValue $false
        $c | Add-Member -NotePropertyName process_exit_code -NotePropertyValue 0
        $c | Add-Member -NotePropertyName stdout_parse_errors -NotePropertyValue 0
        $c | Add-Member -NotePropertyName wrap_certificate_hi_over_offset -NotePropertyValue 0.0 -Force
        $control += $c
        $p = $c | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        $p.repair_ms *= 1.05
        $candidate += $p
    }
    $control | ForEach-Object { $_ | ConvertTo-Json -Depth 30 -Compress } | Set-Content (Join-Path $temp 'control.jsonl') -Encoding UTF8
    $candidate | ForEach-Object { $_ | ConvertTo-Json -Depth 30 -Compress } | Set-Content (Join-Path $temp 'guarded-features.jsonl') -Encoding UTF8
    $summary = Evaluate
    Assert-That ($summary.control_baseline_gates_pass -eq $true) 'Unchanged synthetic control failed baseline gates.'
    Assert-That ($summary.subset_pass -eq $false) 'Original below-target angles must still fail.'
    Assert-That ([Math]::Abs($summary.aggregate.repair_time_ratio - 1.05) -lt 1e-12) 'Timing was not paired with fresh control.'
    $derived = @(Get-Content (Join-Path $temp 'baseline-original-geometry-fresh-times.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-That ($derived.Count -eq $original.Count) 'Derived baseline dropped original IDs.'
    for ($i=0; $i -lt $original.Count; ++$i) {
        $derived[$i].repair_ms = $original[$i].repair_ms
        Assert-That (($derived[$i] | ConvertTo-Json -Depth 30 -Compress) -ceq ($original[$i] | ConvertTo-Json -Depth 30 -Compress)) 'A baseline field other than timing changed.'
    }
    $control[0].process_timed_out = $true
    $control | ForEach-Object { $_ | ConvertTo-Json -Depth 30 -Compress } | Set-Content (Join-Path $temp 'control.jsonl') -Encoding UTF8
    $summary = Evaluate
    Assert-That ($summary.control_baseline_gates_pass -eq $false -and $summary.subset_pass -eq $false) 'Timed-out control was accepted.'
    $bad = Get-Content (Join-Path $temp 'baseline-original-geometry-fresh-times.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.file -eq $control[0].file }
    Assert-That ($null -eq $bad.repair_ms) 'Timed-out control retained usable baseline timing.'
    Assert-That (@($summary.process_failures).Count -eq 1) 'Control timeout was not reported.'
    Write-Output 'PASS: original geometry preserved, fresh-control timing used, angle failure retained, timeout rejected.'
} finally { Remove-Item $temp -Recurse -Force }
