# Set up and run the FPTaylor pilot in a WSL2 Linux distribution, without Podman or Docker.
#
# usage (Windows PowerShell, from the repository):
#   powershell -ExecutionPolicy Bypass -File tools\FPTaylorPilot\wsl_setup.ps1 [-Full] [-Distro Ubuntu-24.04]
#
# Steps: install the distribution if it is missing (this needs administrator rights and a download of a few GB;
# run from an elevated PowerShell in that case), install the build packages, copy tools/ into the distribution's
# Linux file system with LF line endings (the Windows checkout has CRLF, which the shell scripts cannot run with),
# then run wsl_run.sh, which installs the pinned FPTaylor toolchain once and runs the checks.
# The toolchain lives in the distribution (default /root/fptaylor-local); nothing is installed on Windows.
param(
    [string]$Distro = 'Ubuntu-24.04',
    [string]$InstallDir = '/root/fptaylor-local',
    [switch]$Full
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

function Invoke-InDistro([string]$Command) {
    & wsl.exe -d $Distro -u root -- bash -c $Command
    if ($LASTEXITCODE -ne 0) { throw "command failed in $Distro (exit $LASTEXITCODE): $Command" }
}

& wsl.exe -d $Distro -u root -- true
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installing $Distro (download; administrator rights may be required) ..."
    & wsl.exe --install -d $Distro --no-launch
    if ($LASTEXITCODE -ne 0) {
        throw "wsl --install -d $Distro failed; run this script from an elevated PowerShell, or install the distribution first"
    }
    & wsl.exe -d $Distro -u root -- true
    if ($LASTEXITCODE -ne 0) { throw "$Distro is installed but does not start; a restart of Windows may be needed" }
}

Invoke-InDistro 'export DEBIAN_FRONTEND=noninteractive; apt-get update -qq && apt-get install -y -qq build-essential curl ca-certificates python3 m4 coreutils'

$wslRepo = (& wsl.exe -d $Distro -- wslpath -a $repo).Trim()
if ($LASTEXITCODE -ne 0 -or -not $wslRepo) { throw "cannot map $repo into $Distro" }

Invoke-InDistro ('rm -rf /root/fptaylor-src && mkdir -p /root/fptaylor-src && cp -r ''' + $wslRepo + '/tools'' /root/fptaylor-src/ && find /root/fptaylor-src/tools -type f \( -name ''*.sh'' -o -name ''*.txt'' -o -name ''*.spec'' -o -name ''*.cs'' -o -name ''*.py'' \) -exec sed -i ''s/\r$//'' {} +')

$flags = ''
if ($Full) { $flags = ' --full' }
Invoke-InDistro ('bash /root/fptaylor-src/tools/FPTaylorPilot/wsl_run.sh /root/fptaylor-src ' + $InstallDir + $flags)

Write-Host "FPTaylor pilot checks passed in $Distro (installation: $InstallDir)."
