<#
.SYNOPSIS
    Mirrors Packages/com.onity.framework from a source worktree into the benchmark host.
.DESCRIPTION
    Plan 16 PERF-0. Compares per-file SHA256 of the source package against the host package,
    lists missing / different / extra files, and (without -DryRun) copies differing files and
    deletes extras. Extras are listed first and the run is REFUSED when any extra does not match
    -AllowedExtraPattern. Never touches Packages/manifest.json, packages-lock.json or
    ProjectSettings. Refuses a real run while Temp/UnityLockfile exists in the host.
    Writes staging.json (source HEAD, dirty list, per-file SHA256 of source and host, count)
    to -ManifestPath; refuses to overwrite an existing manifest.
    Works on Windows PowerShell 5.1 and PowerShell 7.
.PARAMETER Source
    Source worktree root (contains Packages/com.onity.framework).
.PARAMETER HostProject
    Host Unity project root.
.PARAMETER DryRun
    Report only; the host is not modified (the manifest, if requested, goes to -ManifestPath).
.PARAMETER AllowedExtraPattern
    Wildcard patterns (relative paths, forward slashes) of host-only files that may be deleted.
.PARAMETER FrozenFile
    Leaf names reported separately as the frozen-file check.
.PARAMETER ManifestPath
    Where staging.json is written. Required for a real run.
#>
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$HostProject,
    [switch]$DryRun,
    [string[]]$AllowedExtraPattern = @(),
    [string[]]$FrozenFile = @('OnityAsync.cs', 'OnityAsyncStateMachineRunner.cs', 'OnityTaskThreadSwitch.cs',
        'OnityTaskBuilderLifecycleBenchmarkRunner.cs', 'OnityWhenAnyArrayTaskSource.cs'),
    [string]$ManifestPath = ''
)
$ErrorActionPreference = 'Stop'
$packageRelative = 'Packages\com.onity.framework'

$Source = [IO.Path]::GetFullPath($Source).TrimEnd('\')
$HostProject = [IO.Path]::GetFullPath($HostProject).TrimEnd('\')
$sourcePackage = Join-Path $Source $packageRelative
$hostPackage = Join-Path $HostProject $packageRelative

if (!(Test-Path -LiteralPath $sourcePackage -PathType Container)) { throw 'Source package not found: ' + $sourcePackage }
if (!(Test-Path -LiteralPath $hostPackage -PathType Container)) { throw 'Host package not found: ' + $hostPackage }
if ($sourcePackage -ieq $hostPackage) { throw 'Source and host are the same directory.' }
$hostItem = Get-Item -LiteralPath $hostPackage
if (($hostItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Host package is a link; refusing.' }
if (!$DryRun)
{
    if (!$ManifestPath) { throw 'A real run requires -ManifestPath.' }
    if (Test-Path -LiteralPath (Join-Path $HostProject 'Temp\UnityLockfile')) { throw 'Temp/UnityLockfile exists: a Unity process owns the host.' }
}
if ($ManifestPath -and (Test-Path -LiteralPath $ManifestPath)) { throw 'Refusing to overwrite ' + $ManifestPath }

function Get-RelativeFiles([string]$Root)
{
    $result = @{}
    $prefix = $Root.TrimEnd('\') + '\'
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force))
    {
        $relative = $file.FullName.Substring($prefix.Length).Replace('\', '/')
        $result[$relative] = $file.FullName
    }
    return $result
}

function Get-Sha([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

$sourceFiles = Get-RelativeFiles $sourcePackage
$hostFiles = Get-RelativeFiles $hostPackage

$missing = @(); $different = @(); $identical = 0; $extras = @()
$sourceHashes = @{}; $hostHashes = @{}
foreach ($key in ($sourceFiles.Keys | Sort-Object))
{
    $sourceHash = Get-Sha $sourceFiles[$key]
    $sourceHashes[$key] = $sourceHash
    if (!$hostFiles.ContainsKey($key)) { $missing += $key; continue }
    $hostHash = Get-Sha $hostFiles[$key]
    $hostHashes[$key] = $hostHash
    if ($hostHash -eq $sourceHash) { $identical++ } else { $different += $key }
}
foreach ($key in ($hostFiles.Keys | Sort-Object))
{
    if (!$sourceFiles.ContainsKey($key)) { $extras += $key }
}

$unexpected = @()
foreach ($extra in $extras)
{
    $allowed = $false
    foreach ($pattern in $AllowedExtraPattern) { if ($extra -like $pattern) { $allowed = $true } }
    if (!$allowed) { $unexpected += $extra }
}

Write-Output ('source : ' + $sourcePackage)
Write-Output ('host   : ' + $hostPackage)
Write-Output ('files  : source=' + $sourceFiles.Count + ' host=' + $hostFiles.Count + ' identical=' + $identical +
    ' different=' + $different.Count + ' missingInHost=' + $missing.Count + ' extraInHost=' + $extras.Count)
foreach ($item in $different) { Write-Output ('  DIFF    ' + $item) }
foreach ($item in $missing) { Write-Output ('  MISSING ' + $item) }
foreach ($item in $extras)
{
    $tag = 'EXTRA   '
    if ($unexpected -contains $item) { $tag = 'EXTRA!  ' }
    Write-Output ('  ' + $tag + $item)
}

$frozenReport = @()
foreach ($leaf in $FrozenFile)
{
    $hits = @($sourceHashes.Keys | Where-Object { $_.Split('/')[-1] -eq $leaf })
    if ($hits.Count -eq 0) { $frozenReport += [pscustomobject]@{ file = $leaf; status = 'not-in-source'; diffs = 0 }; continue }
    foreach ($hit in $hits)
    {
        $status = 'identical'
        if ($missing -contains $hit) { $status = 'missing-in-host' }
        elseif ($different -contains $hit) { $status = 'different' }
        $count = 0; if ($status -ne 'identical') { $count = 1 }
        $frozenReport += [pscustomobject]@{ file = $hit; status = $status; diffs = $count }
    }
}
$frozenDiffs = ($frozenReport | Measure-Object -Property diffs -Sum).Sum
Write-Output ('frozen runtime files: ' + $frozenReport.Count + ' checked, ' + $frozenDiffs + ' diff(s)')
foreach ($row in $frozenReport) { Write-Output ('  ' + $row.status.PadRight(15) + ' ' + $row.file) }

if (!$DryRun)
{
    if ($unexpected.Count -gt 0) { throw ('Refusing: ' + $unexpected.Count + ' unexpected extra file(s) in host package (see EXTRA! lines). Pass -AllowedExtraPattern to allow.') }
    foreach ($key in ($different + $missing))
    {
        $target = Join-Path $hostPackage $key.Replace('/', '\')
        $directory = Split-Path -Parent $target
        if (!(Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
        Copy-Item -LiteralPath $sourceFiles[$key] -Destination $target -Force
    }
    foreach ($key in $extras) { Remove-Item -LiteralPath $hostFiles[$key] -Force }
    # Remove directories left empty by deleted extras.
    foreach ($directory in @(Get-ChildItem -LiteralPath $hostPackage -Recurse -Directory -Force | Sort-Object { $_.FullName.Length } -Descending))
    {
        if (@(Get-ChildItem -LiteralPath $directory.FullName -Force).Count -eq 0) { Remove-Item -LiteralPath $directory.FullName -Force }
    }
    $verifyFiles = Get-RelativeFiles $hostPackage
    if ($verifyFiles.Count -ne $sourceFiles.Count) { throw 'Post-stage file count mismatch.' }
    foreach ($key in $sourceHashes.Keys)
    {
        $hostHashes[$key] = Get-Sha $verifyFiles[$key]
        if ($hostHashes[$key] -ne $sourceHashes[$key]) { throw 'Post-stage hash mismatch: ' + $key }
    }
    Write-Output ('staged: copied ' + ($different.Count + $missing.Count) + ', deleted ' + $extras.Count + ', verified ' + $sourceHashes.Count + ' file(s)')
}
else
{
    foreach ($key in $missing) { $hostHashes[$key] = $null }
}

if ($ManifestPath)
{
    $head = (& git -C $Source rev-parse HEAD).Trim()
    $dirty = @(& git -C $Source status --porcelain)
    $files = @()
    foreach ($key in ($sourceHashes.Keys | Sort-Object))
    {
        $files += [pscustomobject]@{ path = $key; sourceSha256 = $sourceHashes[$key]; hostSha256 = $hostHashes[$key] }
    }
    $manifest = [pscustomobject]@{
        schemaVersion = 1
        dryRun = [bool]$DryRun
        generatedUtc = [DateTime]::UtcNow.ToString('O')
        source = $Source
        hostProject = $HostProject
        sourceHead = $head
        sourceDirty = $dirty
        sourcePackageVersion = (Get-Content -LiteralPath (Join-Path $sourcePackage 'package.json') -Raw | ConvertFrom-Json).version
        fileCount = $sourceHashes.Count
        different = $different
        missingInHost = $missing
        extraInHost = $extras
        frozenRuntimeFiles = $frozenReport
        files = $files
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8
    Write-Output ('manifest: ' + $ManifestPath)
}
if ($DryRun)
{
    Write-Output 'dry run: host not modified'
    if ($unexpected.Count -gt 0) { Write-Output ('NOTE: a real run would be REFUSED (' + $unexpected.Count + ' unexpected extra(s)).') }
}
