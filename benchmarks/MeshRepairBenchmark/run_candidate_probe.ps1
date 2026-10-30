param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'results\alpha-quality-20261029\candidate-probe-20261029'),
    [switch]$TimeoutFollowUp,
    [switch]$GuardedFeatureTrial
)
$ErrorActionPreference = 'Stop'
if ($TimeoutFollowUp -and $GuardedFeatureTrial) { throw 'Trial and timeout follow-up are separate modes.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$prior = Join-Path $PSScriptRoot 'results\alpha-quality-20261029'
$baseline = @(Get-Content (Join-Path $prior 'before.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
$trial = @(Get-Content (Join-Path $prior 'tangential.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
$files = @(@($baseline + $trial) | Where-Object { $_.robust_used_wrap -eq $true } | ForEach-Object { $_.file } | Sort-Object -Unique)
$trialFiles = @($trial | ForEach-Object { $_.file } | Sort-Object -Unique)
if ($files.Count -ne 17 -or @(Compare-Object $files $trialFiles).Count -ne 0) { throw 'Fixed wrap union does not match the prior 17-case trial.' }
$fixedUnion = $files
$timeoutSeconds = 300
$modeArgument = ''
if ($TimeoutFollowUp) {
    $files = @('229959.stl', '55928.stl', '96373.stl')
    if (@($files | Where-Object { $_ -notin $fixedUnion }).Count -ne 0) { throw 'Timeout case outside fixed union.' }
    $timeoutSeconds = 900
    $modeArgument = '-TimeoutFollowUp '
    if (!$PSBoundParameters.ContainsKey('OutputDirectory')) {
        $OutputDirectory = Join-Path $prior 'candidate-timeout-followup-20261029'
    }
}
if ($GuardedFeatureTrial) {
    $modeArgument = '-GuardedFeatureTrial '
    if (!$PSBoundParameters.ContainsKey('OutputDirectory')) {
        $OutputDirectory = Join-Path $prior 'guarded-feature-trial-20261029'
    }
}
$queue = @()
foreach ($file in $files) {
    if ($GuardedFeatureTrial) {
        $queue += [pscustomobject]@{ File = $file; Run = 'control' }, [pscustomobject]@{ File = $file; Run = 'guarded-features' }
    } else { $queue += [pscustomobject]@{ File = $file; Run = 'probe' } }
}
$meshes = Join-Path $PSScriptRoot 'data\meshes'
foreach ($file in $files) { if (!(Test-Path (Join-Path $meshes $file))) { throw "Missing input: $file" } }
$build = Join-Path $PSScriptRoot 'bin\Release\net10.0'
if (!(Test-Path (Join-Path $build 'MeshRepairBenchmark.dll'))) { throw 'Build the benchmark in Release first.' }
if (Test-Path $OutputDirectory) { throw "Output already exists: $OutputDirectory" }
$null = New-Item $OutputDirectory -ItemType Directory
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$bin = Join-Path $OutputDirectory 'bin'
$null = New-Item $bin -ItemType Directory
Copy-Item (Join-Path $build '*') $bin -Recurse
$hashes = @{}
Get-ChildItem $bin -File | ForEach-Object { $hashes[$_.Name] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
$sourceHashes = @{}
foreach ($source in @('mesh_ops\MeshRobustRepair.cs', 'benchmarks\MeshRepairBenchmark\Program.cs',
    'benchmarks\MeshRepairBenchmark\WrapCandidateProbe.cs', 'benchmarks\MeshRepairBenchmark\run_candidate_probe.ps1')) {
    $sourceHashes[$source] = (Get-FileHash (Join-Path $root $source) -Algorithm SHA256).Hash
}
$inputHashes = @{}
foreach ($file in $files) { $inputHashes[$file] = (Get-FileHash (Join-Path $meshes $file) -Algorithm SHA256).Hash }
$environment = @{
    BENCH_REPAIR = 'robust'; BENCH_FIDELITY = '1'; BENCH_WRAP_SIMPLIFY = '1'
    BENCH_WRAP_POLICY = 'tangential'; BENCH_WRAP_DIAGNOSTICS = '1'; BENCH_WRAP_CANDIDATE_PROBE = '1'
}
if ($TimeoutFollowUp) { $environment['BENCH_WRAP_PROGRESS'] = '1' }
if ($GuardedFeatureTrial) {
    $environment = @{ BENCH_REPAIR = 'robust'; BENCH_FIDELITY = '1'; BENCH_WRAP_SIMPLIFY = '1' }
}
$metadata = [ordered]@{
    recorded_at = (Get-Date).ToString('o'); sdk = (& dotnet --version)
    cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)
    concurrency = 2; timeout_seconds = $timeoutSeconds; timeout_scope = 'whole process including repair, fidelity and probe'
    timeout_followup = [bool]$TimeoutFollowUp; fixed_wrap_union = $fixedUnion
    guarded_feature_trial = [bool]$GuardedFeatureTrial; jobs = $queue
    trial_policy_by_run = @{ control = 'default'; 'guarded-features' = 'guarded-features' }
    command = "powershell -NoProfile -File benchmarks\MeshRepairBenchmark\run_candidate_probe.ps1 $($modeArgument)-OutputDirectory `"$OutputDirectory`""
    environment = $environment; files = $files; binary_sha256 = $hashes; source_sha256 = $sourceHashes
    input_sha256 = $inputHashes; selection_sha256 = (Get-FileHash (Join-Path $PSScriptRoot 'data\selection.csv')).Hash
    git_head = (& git -C $root rev-parse HEAD); diagnostic_only = $true
    timing = 'Different machine from original acceptance baseline; do not use for paired performance acceptance.'
}
if ($GuardedFeatureTrial) {
    $metadata.timing = 'Compare candidate repair_ms only with fresh control from this interleaved run; original geometric/fidelity baseline is unchanged.'
}
$metadata | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'metadata.json') -Encoding UTF8
Get-ChildItem Env: | Where-Object { $_.Name.StartsWith('BENCH_') } | ForEach-Object { Remove-Item ("Env:" + $_.Name) }
foreach ($key in $environment.Keys) { Set-Item ("Env:" + $key) $environment[$key] }
$dotnet = (Get-Command dotnet).Source
$active = @()
$next = 0
try {
    while ($next -lt $queue.Count -or $active.Count -gt 0) {
        while ($next -lt $queue.Count -and $active.Count -lt 2) {
            $spec = $queue[$next++]
            $file = $spec.File
            $logName = $file
            if ($GuardedFeatureTrial) {
                $logName = $spec.Run + '-' + $file
                $env:BENCH_WRAP_POLICY = $metadata.trial_policy_by_run[$spec.Run]
            }
            $stdout = Join-Path $OutputDirectory ($logName + '.stdout.jsonl')
            $stderr = Join-Path $OutputDirectory ($logName + '.stderr.txt')
            $arguments = '"' + (Join-Path $bin 'MeshRepairBenchmark.dll') + '" "' + (Join-Path $meshes $file) + '" std none rel'
            $process = Start-Process $dotnet -ArgumentList $arguments -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            $null = $process.Handle  # Retain the handle so Windows PowerShell can read ExitCode after exit.
            $active += [pscustomobject]@{ File = $file; Run = $spec.Run; Process = $process; Watch = [Diagnostics.Stopwatch]::StartNew(); Stdout = $stdout }
            Write-Output "START $($spec.Run) $file PID=$($process.Id)"
        }
        Start-Sleep -Seconds 1
        foreach ($job in @($active)) {
            $job.Process.Refresh()
            $timedOut = !$job.Process.HasExited -and $job.Watch.Elapsed.TotalSeconds -ge $timeoutSeconds
            if (!$job.Process.HasExited -and !$timedOut) { continue }
            if ($timedOut) { Stop-Process -Id $job.Process.Id -Force -ErrorAction SilentlyContinue }
            $job.Process.WaitForExit()
            $rows = @()
            $parseErrors = 0
            foreach ($line in @(Get-Content $job.Stdout)) {
                if (!$line.Trim()) { continue }
                try { $rows += $line | ConvertFrom-Json } catch { $parseErrors++ }
            }
            $actual = @($rows | Where-Object { $_.record_type -ne 'wrap_candidate_probe' })
            $probes = @($rows | Where-Object { $_.record_type -eq 'wrap_candidate_probe' })
            if ($actual.Count -gt 1) { throw "Multiple actual records for $($job.File)" }
            if ($actual.Count -eq 0) {
                $status = 'process_failed'
                if ($timedOut) { $status = 'timeout' }
                $actual = @([pscustomobject]@{ file = $job.File; status = $status })
            }
            $actualFile = 'actual.jsonl'
            if ($GuardedFeatureTrial) {
                $actualFile = $job.Run + '.jsonl'
                $actual[0] | Add-Member -NotePropertyName process_timed_out -NotePropertyValue $timedOut
                $actual[0] | Add-Member -NotePropertyName process_exit_code -NotePropertyValue $job.Process.ExitCode
                $actual[0] | Add-Member -NotePropertyName stdout_parse_errors -NotePropertyValue $parseErrors
                if ($timedOut -or $job.Process.ExitCode -ne 0 -or $parseErrors -ne 0) {
                    $actual[0].status = if ($timedOut) { 'timeout' } else { 'process_failed' }
                }
            }
            $actual[0] | ConvertTo-Json -Depth 30 -Compress | Add-Content (Join-Path $OutputDirectory $actualFile) -Encoding UTF8
            $probe = [ordered]@{ file = $job.File; record_type = 'wrap_candidate_probe'; diagnostic_only = $true; status = 'probe_unavailable' }
            if ($probes.Count -gt 0) {
                $probe = [ordered]@{}
                foreach ($property in $probes[-1].PSObject.Properties) { $probe[$property.Name] = $property.Value }
            }
            $probe['process_timed_out'] = $timedOut
            $probe['process_exit_code'] = $job.Process.ExitCode
            $probe['process_ms'] = $job.Watch.ElapsedMilliseconds
            $probe['stdout_parse_errors'] = $parseErrors
            if (!$GuardedFeatureTrial) {
                $probe | ConvertTo-Json -Depth 30 -Compress | Add-Content (Join-Path $OutputDirectory 'probes.jsonl') -Encoding UTF8
            }
            Write-Output "END $($job.Run) $($job.File) actual=$($actual[0].status) probe=$($probe['probe_stage']) timeout=$timedOut"
            $job.Process.Dispose()
            $active = @($active | Where-Object { $_ -ne $job })
        }
    }
} finally {
    foreach ($job in $active) {
        if (!$job.Process.HasExited) { Stop-Process -Id $job.Process.Id -Force -ErrorAction SilentlyContinue }
        $job.Process.Dispose()
    }
}
Write-Output "Results: $OutputDirectory"
