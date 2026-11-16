# after_reboot.ps1 -- the overnight run for #929 / #858, plain PowerShell, no Claude session needed.
# 0. tool check: the base 22370752 with the reduced tool (no EnvelopeBench) must give the hash D39147B5... on net10.0, 1 thread;
#    if not: "tool check failed", candidates 2-4 are skipped, the thread scaling still runs
# 5. pair of candidate 2 (ce7b57ec / 2c9c6340), net10.0, 1 thread and 12 threads, same reduced tool on both sides
#    candidates 3 and 4 (be8815d5/b4380c1c, e2c23d41/9f00591d): NOT BUILDABLE (missing API in measured path: Profile.cs uses
#    RoundingMaxPairTests, AntipodalMaxBoundarySegments, AntipodalMaxTriangles, which the older ExactMeshBoolean does not have)
# 6. thread scaling of the base, net10.0, 1 2 4 6 12 threads (run.ps1 -Scaling; per-phase times in phases.csv)
# Only BooleanComparison is built (it is the only tool measured). Reduced tool = tool 22370752 without EnvelopeBench.cs and without its case in
# Program.cs, working copies only, nothing committed; candidate 2 stands also have runs/ removed (probe code, not part of the library).
# Every step writes its output and its error text to the log; a failing step is logged and the script goes on.
# The standby timeout on AC is set to 0 for the run and restored at the end.
$ErrorActionPreference = 'Continue'
$root = 'D:\work'
$base = Join-Path $root 'geometry3Sharp-624'
# dry run (to test the script itself): AFTER_REBOOT_OUT redirects the output folder, AFTER_REBOOT_STUB replaces run.ps1 by a stub
$out = if ($env:AFTER_REBOOT_OUT) { $env:AFTER_REBOOT_OUT } else { Join-Path $root 'after-reboot' }
New-Item -ItemType Directory -Force -Path $out | Out-Null
$log = Join-Path $out 'after_reboot.log'
$tool = '22370752fa4b95d6bc7bf16f6c50e2ab8247a8ce'
$baseHash = 'D39147B535C3519E'
$toolNote = 'tool 22370752 without EnvelopeBench.cs (PolyhedralEnvelope missing in older stands)'

function Log($m) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $m" | Add-Content -Encoding UTF8 $log; Write-Output $m }

# --- energy plan: no sleep while the run goes on; the old value is kept and restored at the end
$q = powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 2>&1 | Out-String
# language independent: the last two hex values of the output are the current AC and DC index (seconds)
$hex = @([regex]::Matches($q, '0x([0-9a-fA-F]+)'))
$oldMinutes = if ($hex.Count -ge 2) { [int]([Convert]::ToInt32($hex[$hex.Count - 2].Groups[1].Value, 16) / 60) } else { 30 }
powercfg /change standby-timeout-ac 0 | Out-Null
Log "standby timeout on AC set to 0 (was $oldMinutes min)"

function Invoke-Bench($dir, $wt, $runtimes, $threads) {
    # one run.ps1 call in a worktree (the tool of the worktree is already built); output to $dir
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $script = if ($env:AFTER_REBOOT_STUB) { $env:AFTER_REBOOT_STUB } else { Join-Path $wt 'benchmarks\net48-compare\run.ps1' }
    # -Command, not -File: with -File the thread list "1,12" arrives as one string and is converted to the number 112
    $thr = ($threads | ForEach-Object { [string]$_ }) -join ','
    $rts = ($runtimes | ForEach-Object { "'" + $_ + "'" }) -join ','
    $cmd = "& '$script' -Scaling -ThreadCounts $thr -ScalingRounds 1 -Runtimes $rts -NoBuild -OutDir '$dir'; exit `$LASTEXITCODE"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -Command $cmd *> (Join-Path $dir 'run.log')
    return $LASTEXITCODE
}

function ReduceTool($wt) {
    # the reduced tool: EnvelopeBench.cs deleted and its case line in Program.cs removed (idempotent, working copy only)
    $dir = Join-Path $wt 'benchmarks\BooleanComparison'
    Remove-Item -Force (Join-Path $dir 'EnvelopeBench.cs') -ErrorAction SilentlyContinue
    $p = Join-Path $dir 'Program.cs'
    $lines = Get-Content $p
    $keep = @($lines | Where-Object { $_ -notmatch '\bEnvelopeBench\.' })
    if ($keep.Count -ne $lines.Count) { Set-Content -Encoding UTF8 $p $keep }
}

function Build($wt) {
    # BooleanComparison only, both targets; the error text is returned, not discarded
    $o = & dotnet build (Join-Path $wt 'benchmarks\BooleanComparison\BooleanComparison.csproj') -c Release -p:BenchNet48=true -v q -nologo 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) { return '' }
    return (($o -split "`r?`n" | Where-Object { $_ -match ' error ' } | Select-Object -Unique -First 8) -join "`n")
}

# --- step 0: tool check on the base
Log 'step 0: tool check, base 22370752 with the reduced tool, net10.0, 1 thread'
ReduceTool $base
$err = Build $base
$toolOk = $false
if ($err -ne '') {
    Log 'tool check failed: the base does not build with the reduced tool'
    $err | Set-Content -Encoding UTF8 (Join-Path $out '00-toolcheck-build-error.txt')
} else {
    $dir0 = Join-Path $out '00-toolcheck-base'
    $csv = Join-Path $dir0 'runs.csv'
    $prevHash = if (Test-Path $csv) { (Import-Csv $csv | Select-Object -First 1).hash } else { '' }
    if ($prevHash -eq $baseHash) {
        $rc = 'reused'; Log 'step 0: the row of an earlier run (same reduced tool, hash matches) is reused'
    } else {
        $rc = Invoke-Bench $dir0 $base @('net10.0') @('1')
    }
    $hash = if (Test-Path $csv) { (Import-Csv $csv | Select-Object -First 1).hash } else { '' }
    Log "step 0: run.ps1 exit $rc, hash $hash (expected $baseHash)"
    if (-not (Test-Path $csv)) { Log "tool check failed: run.ps1 produced no runs.csv, see $dir0\run.log (this is not a hash difference)" }
    elseif ($hash -eq $baseHash) { $toolOk = $true; Log 'tool check passed' }
    else { Log 'tool check failed: hash differs from the base' }
}

# --- step 5: pairs of candidates 2 to 4
$pairs = @(
    @{ name = 'c2'; without = 'ce7b57ec'; with = '2c9c6340'; buildable = $true },
    @{ name = 'c3'; without = 'be8815d5'; with = 'b4380c1c'; buildable = $false },
    @{ name = 'c4'; without = 'e2c23d41'; with = '9f00591d'; buildable = $false }
)
foreach ($pr in $pairs) {
    if (-not $pr.buildable) {
        $why = 'NOT BUILDABLE (missing API in measured path: Profile.cs uses RoundingMaxPairTests, AntipodalMaxBoundarySegments, AntipodalMaxTriangles)'
        $why | Set-Content -Encoding UTF8 (Join-Path $out "$($pr.name)-NOT-BUILDABLE.txt")
        Log "$($pr.name): $why"
        continue
    }
    if (-not $toolOk) { Log "$($pr.name): skipped, tool check failed"; continue }
    $sides = @(@{ tag = 'without'; sha = $pr.without }, @{ tag = 'with'; sha = $pr.with })
    $buildOk = $true
    foreach ($s in $sides) {
        $w = Join-Path $root ('w-' + $s.sha)
        ReduceTool $w                 # the same reduced tool on both sides
        $e = Build $w
        if ($e -ne '') { $buildOk = $false; Log "$($pr.name) $($s.tag) ($($s.sha)): build failed"; $e | Set-Content -Encoding UTF8 (Join-Path $out "$($pr.name)-$($s.tag)-build-error.txt") }
    }
    if (-not $buildOk) { Log "$($pr.name): NOT BUILDABLE"; continue }
    foreach ($s in $sides) {
        $w = Join-Path $root ('w-' + $s.sha)
        $dir = Join-Path $out ("05-{0}-{1}-{2}" -f $pr.name, $s.tag, $s.sha)
        $rc = Invoke-Bench $dir $w @('net10.0') @('1', '12')
        "library=$($s.sha); $toolNote; runs/ removed from the working copy (probe code, not part of the library); runtime=net10.0 threads=1,12; run.ps1 exit=$rc" | Set-Content -Encoding UTF8 (Join-Path $dir 'state.txt')
        Log "$($pr.name) $($s.tag) done, exit $rc"
    }
}

# --- step 6: thread scaling of the base, net10.0 (the reduced tool, built in step 0)
if ($err -eq '') {
    Log 'step 6: thread scaling of the base, net10.0, 1 2 4 6 12 threads'
    $rc = Invoke-Bench (Join-Path $out '06-thread-scaling-base') $base @('net10.0') @('1', '2', '4', '6', '12')
    Log "step 6 done, run.ps1 exit $rc"
} else { Log 'step 6 skipped: the base tool did not build' }

# --- restore the energy plan
powercfg /change standby-timeout-ac $oldMinutes | Out-Null
Log "standby timeout on AC restored to $oldMinutes min"
Log 'AFTER-REBOOT DONE'
