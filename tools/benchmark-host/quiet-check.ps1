<#
.SYNOPSIS
    Pre-gate for benchmark timing: samples other Unity-related processes and the power plan.
.DESCRIPTION
    Plan 16 section 1.4. Samples TotalProcessorTime of every Unity / Unity Hub / compiler helper
    process (except the benchmark Players and, optionally, processes of the host project) for
    -Seconds, prints PID, project name and average CPU, and refuses (exit 2) when any process
    averages above -MaxCpuPercent (percent of ONE core) or when the power plan is not High
    performance / Ultimate performance. Reads only; never touches any process.
    Works on Windows PowerShell 5.1 and PowerShell 7.
.PARAMETER Seconds
    Sampling window. Default 10.
.PARAMETER MaxCpuPercent
    Refusal threshold per process, percent of one core. Default 2.
.PARAMETER HostProject
    Optional host project path; Unity processes whose -projectPath is the host are reported as
    "host" and excluded from the refusal (the host Editor must not run during timing anyway:
    they are listed with -FailOnHostEditor).
.PARAMETER FailOnHostEditor
    Refuse when any Unity process owns the host project.
.PARAMETER SkipPowerPlan
    Do not check the power plan.
.PARAMETER OutputJson
    Optional path; writes the result object as JSON (refuses to overwrite).
#>
param(
    [int]$Seconds = 10,
    [double]$MaxCpuPercent = 2.0,
    [string]$HostProject = '',
    [switch]$FailOnHostEditor,
    [switch]$SkipPowerPlan,
    [string]$OutputJson = ''
)
$ErrorActionPreference = 'Stop'

$watchedNames = @('Unity', 'Unity Hub', 'UnityHelper', 'UnityPackageManager', 'UnityShaderCompiler',
    'UnityCrashHandler64', 'Bee.BeeDriver', 'bee_backend', 'il2cpp', 'VBCSCompiler', 'csc')

function Get-ProjectName([string]$CommandLine)
{
    if ([string]::IsNullOrEmpty($CommandLine)) { return '' }
    $match = [regex]::Match($CommandLine, '(?i)"?-project-?path"?\s+"?([^"\s][^"]*?)"?(\s+-|\s+"-|$)')
    if (!$match.Success) { return '' }
    return $match.Groups[1].Value.Trim().Replace('/', '\')
}

function Get-Snapshot
{
    $table = @{}
    $commandLines = @{}
    foreach ($row in @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue))
    {
        $commandLines[[int]$row.ProcessId] = $row.CommandLine
    }
    foreach ($name in $watchedNames)
    {
        foreach ($process in @(Get-Process -Name $name -ErrorAction SilentlyContinue))
        {
            try
            {
                $cmd = $commandLines[[int]$process.Id]
                $table[[int]$process.Id] = [pscustomobject]@{
                    pid = $process.Id
                    name = $process.ProcessName
                    startUtc = $process.StartTime.ToUniversalTime().ToString('O')
                    cpuSeconds = $process.TotalProcessorTime.TotalSeconds
                    project = (Get-ProjectName $cmd)
                    isWorker = ($cmd -match 'AssetImportWorker|-adb2')
                }
            }
            catch { }
        }
    }
    return $table
}

$hostFull = ''
if ($HostProject) { $hostFull = [IO.Path]::GetFullPath($HostProject).TrimEnd('\') }

$before = Get-Snapshot
$watch = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds $Seconds
$after = Get-Snapshot
$watch.Stop()
$wall = $watch.Elapsed.TotalSeconds

$rows = @()
foreach ($id in $after.Keys)
{
    $now = $after[$id]
    $cpu = $null
    if ($before.ContainsKey($id) -and $before[$id].startUtc -eq $now.startUtc)
    {
        $cpu = $now.cpuSeconds - $before[$id].cpuSeconds
    }
    else
    {
        $cpu = $now.cpuSeconds   # started during the window: all of its CPU counts
    }
    $percent = 100.0 * $cpu / $wall
    $isHost = $false
    if ($hostFull -and $now.project)
    {
        $isHost = [IO.Path]::GetFullPath($now.project).TrimEnd('\') -ieq $hostFull
    }
    $rows += [pscustomobject]@{
        pid = $now.pid; name = $now.name; project = $now.project; isWorker = $now.isWorker
        isHost = $isHost; cpuSeconds = [math]::Round($cpu, 3); cpuPercentOfOneCore = [math]::Round($percent, 2)
    }
}

# Processes that existed before but vanished count as activity we cannot attribute.
$vanished = @()
foreach ($id in $before.Keys)
{
    if (!$after.ContainsKey($id)) { $vanished += $id }
}

$powerName = ''
$powerOk = $true
if (!$SkipPowerPlan)
{
    $scheme = (& powercfg /getactivescheme) -join ' '
    $powerName = $scheme.Trim()
    $powerOk = ($scheme -match '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c') -or
        ($scheme -match 'e9a42b02-d5df-448d-aa00-03f14749eb61') -or ($scheme -match '(?i)high performance|ultimate performance')
}

$offenders = @($rows | Where-Object { !$_.isHost -and $_.cpuPercentOfOneCore -gt $MaxCpuPercent })
$hostEditors = @($rows | Where-Object { $_.isHost })
$reasons = @()
if ($offenders.Count -gt 0) { $reasons += ('CPU above ' + $MaxCpuPercent + '%: PIDs ' + (($offenders | ForEach-Object { $_.pid }) -join ',')) }
if ($vanished.Count -gt 0) { $reasons += ('processes exited during sampling: ' + ($vanished -join ',')) }
if (!$powerOk) { $reasons += 'power plan is not High performance' }
if ($FailOnHostEditor -and $hostEditors.Count -gt 0) { $reasons += 'a Unity process owns the host project' }

$result = [pscustomobject]@{
    schemaVersion = 1
    startedUtc = ([DateTime]::UtcNow.AddSeconds(-$wall)).ToString('O')
    elapsedSeconds = [math]::Round($wall, 3)
    maxCpuPercentOfOneCore = $MaxCpuPercent
    powerPlan = $powerName
    powerPlanOk = $powerOk
    processes = $rows
    vanishedPids = $vanished
    quiet = ($reasons.Count -eq 0)
    refusalReasons = $reasons
}

Write-Output ('quiet-check: sampled ' + $rows.Count + ' watched process(es) for ' + [math]::Round($wall, 1) + ' s')
foreach ($row in ($rows | Sort-Object cpuPercentOfOneCore -Descending))
{
    $label = $row.project
    if (!$label) { $label = '(no project)' }
    $tag = ''
    if ($row.isHost) { $tag = ' [host]' }
    if ($row.isWorker) { $tag += ' [worker]' }
    Write-Output ('  PID ' + $row.pid + '  ' + $row.name + '  cpu=' + $row.cpuPercentOfOneCore + '%  project=' + $label + $tag)
}
if (!$SkipPowerPlan) { Write-Output ('  power plan: ' + $powerName + ' -> ' + $(if ($powerOk) { 'ok' } else { 'NOT high performance' })) }

if ($OutputJson)
{
    if (Test-Path -LiteralPath $OutputJson) { throw 'Refusing to overwrite ' + $OutputJson }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputJson -Encoding UTF8
}

if ($result.quiet)
{
    Write-Output 'quiet-check: QUIET'
    exit 0
}
Write-Output ('quiet-check: REFUSED - ' + ($reasons -join '; '))
exit 2
