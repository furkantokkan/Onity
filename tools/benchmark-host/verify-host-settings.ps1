<#
.SYNOPSIS
    Records, checks or restores the four host settings files around Editor runs.
.DESCRIPTION
    Plan 16 PERF-0. Files: ProjectSettings/ProjectSettings.asset, EditorBuildSettings.asset,
    EditorSettings.asset, ProjectVersion.txt. Originals live under
    <HostProject>/BenchmarkResults/host-settings-original/ with a settings.json hash list.
      -Record  copies the current files there (refuses to overwrite; with -Baseline the current
               hashes must equal that before-settings.json, else it stops).
      -Check   read-only: current hashes vs the recorded originals (and vs -Baseline when given).
               Exit 0 = all match, 1 = mismatch.
      -Restore copies the recorded originals back over the host files (file copy, never git).
    -Record and -Restore refuse while Temp/UnityLockfile exists. Windows PowerShell 5.1 and 7.
.PARAMETER Baseline
    Optional before-settings.json ([{file, sha256}]) to compare against.
#>
param(
    [Parameter(Mandatory = $true)][string]$HostProject,
    [switch]$Record,
    [switch]$Check,
    [switch]$Restore,
    [string]$Baseline = ''
)
$ErrorActionPreference = 'Stop'
$modes = @($Record, $Check, $Restore) | Where-Object { $_ }
if (@($modes).Count -ne 1) { throw 'Specify exactly one of -Record, -Check, -Restore.' }

$HostProject = [IO.Path]::GetFullPath($HostProject).TrimEnd('\')
$settingsDir = Join-Path $HostProject 'ProjectSettings'
$originalDir = Join-Path $HostProject 'BenchmarkResults\host-settings-original'
$originalIndex = Join-Path $originalDir 'settings.json'
$files = @('ProjectSettings.asset', 'EditorBuildSettings.asset', 'EditorSettings.asset', 'ProjectVersion.txt')
$lockfile = Join-Path $HostProject 'Temp\UnityLockfile'

function Get-Sha([string]$Path)
{
    if (!(Test-Path -LiteralPath $Path)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Read-Index([string]$Path)
{
    $map = @{}
    $parsed = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $Path -Raw)
    $parsed | ForEach-Object { $map[$_.file] = $_.sha256.ToUpperInvariant() }
    return $map
}

$current = @{}
foreach ($file in $files) { $current[$file] = Get-Sha (Join-Path $settingsDir $file) }
$baselineMap = $null
if ($Baseline) { $baselineMap = Read-Index $Baseline }

if ($Record)
{
    if (Test-Path -LiteralPath $lockfile) { throw 'Temp/UnityLockfile exists: refusing to record.' }
    if (Test-Path -LiteralPath $originalIndex) { throw 'Originals already recorded at ' + $originalDir + '; refusing to overwrite.' }
    foreach ($file in $files) { if (!$current[$file]) { throw 'Missing settings file: ' + $file } }
    if ($baselineMap)
    {
        foreach ($file in $files)
        {
            if ($baselineMap[$file] -ne $current[$file]) { throw ('Current ' + $file + ' does not match the baseline hash; not recording.') }
        }
    }
    New-Item -ItemType Directory -Path $originalDir -Force | Out-Null
    $rows = @()
    foreach ($file in $files)
    {
        Copy-Item -LiteralPath (Join-Path $settingsDir $file) -Destination (Join-Path $originalDir $file)
        $rows += [pscustomobject]@{ file = $file; sha256 = $current[$file] }
    }
    ConvertTo-Json -InputObject $rows | Set-Content -LiteralPath $originalIndex -Encoding UTF8
    Write-Output ('recorded ' + $files.Count + ' original settings file(s) under ' + $originalDir)
    exit 0
}

if (!(Test-Path -LiteralPath $originalIndex))
{
    if ($Restore) { throw 'No recorded originals; run -Record first.' }
    Write-Output 'No recorded originals (host-settings-original absent); comparing to -Baseline only.'
    $recorded = $null
}
else
{
    $recorded = Read-Index $originalIndex
    foreach ($file in $files)
    {
        $copy = Get-Sha (Join-Path $originalDir $file)
        if ($copy -ne $recorded[$file]) { throw 'Recorded original copy was altered: ' + $file }
    }
}

if ($Restore)
{
    if (Test-Path -LiteralPath $lockfile) { throw 'Temp/UnityLockfile exists: refusing to restore.' }
    $restored = 0
    foreach ($file in $files)
    {
        if ($current[$file] -ne $recorded[$file])
        {
            Copy-Item -LiteralPath (Join-Path $originalDir $file) -Destination (Join-Path $settingsDir $file) -Force
            Write-Output ('restored ' + $file)
            $restored++
        }
    }
    foreach ($file in $files)
    {
        if ((Get-Sha (Join-Path $settingsDir $file)) -ne $recorded[$file]) { throw 'Restore verification failed: ' + $file }
    }
    Write-Output ('restore complete: ' + $restored + ' file(s) copied, all ' + $files.Count + ' match the recorded originals')
    exit 0
}

# -Check
if (Test-Path -LiteralPath $lockfile) { Write-Output 'WARNING: Temp/UnityLockfile exists; an Editor may still modify settings.' }
$bad = 0
foreach ($file in $files)
{
    $status = @()
    if ($recorded) { if ($recorded[$file] -eq $current[$file]) { $status += 'recorded=match' } else { $status += 'recorded=MISMATCH'; $bad++ } }
    if ($baselineMap) { if ($baselineMap[$file] -eq $current[$file]) { $status += 'baseline=match' } else { $status += 'baseline=MISMATCH'; $bad++ } }
    $hash = $current[$file]; if (!$hash) { $hash = '(missing)' }
    Write-Output ($file.PadRight(26) + ' ' + $hash + '  ' + ($status -join ' '))
}
if (!$recorded -and !$baselineMap) { throw 'Nothing to compare against.' }
if ($bad -gt 0) { Write-Output ('settings check: ' + $bad + ' mismatch(es)'); exit 1 }
Write-Output 'settings check: all match'
exit 0
