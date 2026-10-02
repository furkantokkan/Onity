<#
.SYNOPSIS
    Runs the messaging comparison Players (Onity.Messaging vs MessagePipe) one process at a time.
.DESCRIPTION
    Runs -Processes rounds. Each round runs the IL2CPP and the Mono Player once: IL2CPP first in odd
    rounds, Mono first in even rounds. Players are read from
    -PlayersFrom\<IL2CPP|Mono>\OnityMessagingBenchmark.exe with their <exe>.build.json sidecars (written by
    Onity.Editor.Benchmarks.OnityMessagingBenchmarkPlayerBuildRunner.BuildPlayerFromCommandLine).
    Every process runs batch mode at -Priority (default High) with an affinity mask that excludes cores
    0-1 (-AffinityMask overrides, 0 disables) under -TimeoutSeconds. Before each process the script refuses
    when a Unity Editor runs on -HostProject, the host's Temp/UnityLockfile is held, or any benchmark Player
    (this one or -OtherPlayerName) is running.
    Both sidecars must record the MessagePipe flavor (nuget-netstandard2.0 or unity-package) and the SHA-256
    of the MessagePipe package (the dll, or the .unitypackage), and the two Players must agree on both.
    Evidence under -EvidenceRoot:
      runs\<stem>.json              Player report
      runs\<stem>.player.log        Player log
      runs\<stem>.observation.json  exit code, wall time, priority/affinity, other Unity CPU, hashes
      run-manifest.json             Players (hashes, build GUIDs, MessagePipe flavor and package hash, UniTask
                                    version), plan, observations, status
    Existing evidence is never overwritten: a non-empty -EvidenceRoot is refused.
    -SelfTest passes -onityMessagingBenchmarkSelfTest (tiny counts; never performance evidence).
    Summarize with: python tools/benchmark-host/messaging-summary.py --evidence-root <EvidenceRoot>
    (add --allow-self-test for a self-test). Exit code 0 when every process succeeded, otherwise 1.
    Works on Windows PowerShell 5.1 and PowerShell 7.
.EXAMPLE
    run-messaging-comparison.ps1 -HostProject C:\host\Onity -PlayersFrom C:\host\Onity\BenchmarkResults\messaging-players\<stamp> -EvidenceRoot C:\host\Onity\BenchmarkResults\messaging-<label>
#>
param(
    [Parameter(Mandatory = $true)][string]$HostProject,
    [Parameter(Mandatory = $true)][string]$PlayersFrom,
    [Parameter(Mandatory = $true)][string]$EvidenceRoot,
    [ValidateRange(1, 50)][int]$Processes = 3,
    [switch]$SelfTest,
    [ValidateSet('Idle', 'BelowNormal', 'Normal', 'AboveNormal', 'High', 'RealTime')][string]$Priority = 'High',
    [string]$AffinityMask = '',
    [ValidateRange(10, 86400)][int]$TimeoutSeconds = 900,
    [string]$ExeName = 'OnityMessagingBenchmark.exe',
    [string[]]$OtherPlayerName = @('OnityReactiveBenchmark', 'OnityTaskBenchmark', 'OnityDiBenchmarkPlayer')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$HostProject = [IO.Path]::GetFullPath($HostProject).TrimEnd('\')
$PlayersFrom = [IO.Path]::GetFullPath($PlayersFrom).TrimEnd('\')
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot).TrimEnd('\')
$runsDir = Join-Path $EvidenceRoot 'runs'
$backends = @('IL2CPP', 'Mono')
$playerProcessName = [IO.Path]::GetFileNameWithoutExtension($ExeName)

if (!(Test-Path -LiteralPath (Join-Path $HostProject 'ProjectSettings\ProjectVersion.txt'))) { throw 'Host is not a Unity project: ' + $HostProject }
if ((Test-Path -LiteralPath $EvidenceRoot) -and @(Get-ChildItem -LiteralPath $EvidenceRoot -Force).Count -gt 0)
{
    throw 'Refusing to reuse a non-empty evidence root: ' + $EvidenceRoot
}

function Get-Sha([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

function Read-Sidecar([string]$Exe, [string]$Backend)
{
    $sidecar = $Exe + '.build.json'
    if (!(Test-Path -LiteralPath $Exe)) { throw ('Missing Player: ' + $Exe) }
    if (!(Test-Path -LiteralPath $sidecar)) { throw ('Missing build sidecar: ' + $sidecar) }
    $data = Get-Content -LiteralPath $sidecar -Raw | ConvertFrom-Json
    if ($data.backend -ne $Backend) { throw ('Sidecar backend ' + $data.backend + ' does not match the ' + $Backend + ' folder: ' + $sidecar) }
    if ($data.development) { throw ('The ' + $Backend + ' Player is a Development build: ' + $sidecar) }
    foreach ($field in @('messagePipeFlavor', 'messagePipePackageSha256', 'uniTaskVersion'))
    {
        if (!($data.PSObject.Properties.Name -contains $field) -or !$data.$field) { throw ('The sidecar records no ' + $field + ': ' + $sidecar) }
    }
    return $data
}

function Assert-QuietHost
{
    foreach ($row in @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue))
    {
        $cmd = [string]$row.CommandLine
        if ($cmd -and $cmd.Replace('/', '\').ToLowerInvariant().Contains($HostProject.ToLowerInvariant()))
        {
            throw ('Unity PID ' + $row.ProcessId + ' runs with the host project; refusing to time Players.')
        }
    }
    $lockfile = Join-Path $HostProject 'Temp\UnityLockfile'
    if (Test-Path -LiteralPath $lockfile)
    {
        try { $stream = [IO.File]::Open($lockfile, 'Open', 'ReadWrite', 'None'); $stream.Close() }
        catch { throw 'Temp/UnityLockfile of the host is held; refusing to time Players.' }
    }
    foreach ($name in @($playerProcessName) + $OtherPlayerName)
    {
        $running = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
        if ($running.Count -gt 0) { throw ('A ' + $name + ' Player is already running (PID ' + $running[0].Id + ').') }
    }
}

function Get-AffinityValue
{
    if ($AffinityMask -eq '0') { return [int64]0 }
    if ($AffinityMask -ne '')
    {
        if ($AffinityMask -match '^0x') { return [Convert]::ToInt64($AffinityMask.Substring(2), 16) }
        return [int64]$AffinityMask
    }
    $cores = [Environment]::ProcessorCount
    if ($cores -gt 63) { $cores = 63 }
    if ($cores -le 2) { return [int64]0 }
    return (([int64]1 -shl $cores) - 1) -band (-bnot [int64]3)
}

# CPU time of every Unity Editor process; other Editors on the machine are only observed, never touched.
function Get-UnityCpuSnapshot
{
    $observations = @()
    foreach ($process in @(Get-Process -Name Unity -ErrorAction SilentlyContinue))
    {
        try
        {
            $observations += [pscustomobject]@{ pid = $process.Id; startUtc = $process.StartTime.ToUniversalTime().ToString('O')
                cpuSeconds = $process.TotalProcessorTime.TotalSeconds; observed = $true }
        }
        catch { $observations += [pscustomobject]@{ pid = $process.Id; startUtc = ''; cpuSeconds = 0.0; observed = $false } }
    }
    return $observations
}

function Get-NoiseObservation($Before, $After, [double]$WallSeconds)
{
    $unknown = $false
    $cpu = 0.0
    foreach ($item in $Before)
    {
        $matched = @($After | Where-Object { $_.pid -eq $item.pid -and $_.observed -and $item.observed -and $_.startUtc -eq $item.startUtc })
        if (!$item.observed -or $matched.Count -ne 1) { $unknown = $true }
        else
        {
            $delta = $matched[0].cpuSeconds - $item.cpuSeconds
            if ($delta -lt 0) { $unknown = $true } else { $cpu += $delta }
        }
    }
    foreach ($item in $After)
    {
        if (@($Before | Where-Object { $_.pid -eq $item.pid }).Count -ne 1) { $unknown = $true }
    }
    $fraction = -1.0
    if ($WallSeconds -gt 0) { $fraction = $cpu / $WallSeconds }
    $status = 'flagged'
    if ($unknown) { $status = 'unknown' } elseif ($cpu -le 0.5 -and $fraction -le 0.05) { $status = 'accepted' }
    return [pscustomobject]@{ cpu = $cpu; fraction = $fraction; status = $status }
}

function Invoke-PlayerProcess($Run, [int64]$Affinity)
{
    Assert-QuietHost
    $exe = $players[$Run.backend].executable
    $report = Join-Path $runsDir ($Run.stem + '.json')
    $log = Join-Path $runsDir ($Run.stem + '.player.log')
    $observationPath = Join-Path $runsDir ($Run.stem + '.observation.json')
    foreach ($path in @($report, $log, $observationPath)) { if (Test-Path -LiteralPath $path) { throw ('Refusing existing evidence: ' + $path) } }
    $arguments = @('-batchmode', '-nographics', '-onityRunMessagingBenchmark',
        '-onityMessagingBenchmarkOutput', ('"' + $report + '"'),
        '-onityMessagingBenchmarkBuildMetadata', ('"' + $exe + '.build.json"'),
        '-logFile', ('"' + $log + '"'))
    if ($SelfTest) { $arguments += '-onityMessagingBenchmarkSelfTest' }
    $binaryHash = Get-Sha $exe
    $before = @(Get-UnityCpuSnapshot)
    $started = [DateTime]::UtcNow
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $player = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    # Caching the handle keeps ExitCode readable after exit on Windows PowerShell 5.1.
    $null = $player.Handle
    $appliedPriority = $null
    $appliedAffinity = $null
    try
    {
        $player.PriorityClass = $Priority
        $appliedPriority = [string]$player.PriorityClass
        if ($Affinity -ne 0)
        {
            $player.ProcessorAffinity = [IntPtr]$Affinity
            $appliedAffinity = [int64]$player.ProcessorAffinity
        }
    }
    catch { Write-Host ('WARNING could not apply priority/affinity to PID ' + $player.Id + ': ' + $_.Exception.Message) }
    $timedOut = $false
    if (!$player.WaitForExit($TimeoutSeconds * 1000))
    {
        $timedOut = $true
        try { $player.Kill() } catch { }
    }
    $player.WaitForExit()
    $watch.Stop()
    $after = @(Get-UnityCpuSnapshot)
    $noise = Get-NoiseObservation $before $after $watch.Elapsed.TotalSeconds
    $exitCode = $null
    try { $exitCode = $player.ExitCode } catch { }
    $fresh = (Test-Path -LiteralPath $report) -and ((Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started.AddSeconds(-2))
    $unchanged = $binaryHash -eq (Get-Sha $exe)
    $ok = !$timedOut -and $exitCode -eq 0 -and $fresh -and $unchanged
    $observation = [ordered]@{
        schemaVersion = 1
        stem = $Run.stem
        backend = $Run.backend
        round = $Run.round
        sequence = $Run.sequence
        selfTest = [bool]$SelfTest
        executable = $exe
        binarySha256 = $binaryHash
        sidecarSha256 = $players[$Run.backend].sidecarSha256
        buildGuid = $players[$Run.backend].buildGuid
        arguments = ($arguments -join ' ')
        report = $report
        playerLog = $log
        playerPid = $player.Id
        startedUtc = $started.ToString('O')
        elapsedSeconds = $watch.Elapsed.TotalSeconds
        exitCode = $exitCode
        timedOut = $timedOut
        timeoutSeconds = $TimeoutSeconds
        reportFresh = $fresh
        binaryUnchanged = $unchanged
        succeeded = $ok
        reportSha256 = $(if (Test-Path -LiteralPath $report) { Get-Sha $report } else { $null })
        priorityRequested = $Priority
        priorityApplied = $appliedPriority
        affinityRequested = $Affinity
        affinityApplied = $appliedAffinity
        otherUnityCpuSeconds = $noise.cpu
        otherUnityWallFraction = $noise.fraction
        noiseStatus = $noise.status
        noiseRule = 'Other Unity Editor CPU <= 0.5 s and <= 5% of wall time; a missing, new or unreadable process is unknown. Informational.'
    }
    [pscustomobject]$observation | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $observationPath -Encoding UTF8
    $suffix = ''
    if (!$ok) { $suffix = ' FAILED (timeout=' + $timedOut + ' fresh=' + $fresh + ' binaryUnchanged=' + $unchanged + ')' }
    Write-Host ($Run.stem + ' exit=' + $exitCode + ' wall=' + $watch.Elapsed.TotalSeconds.ToString('F1') + 's noise=' + $noise.status + $suffix)
    return [pscustomobject]$observation
}

$players = @{}
foreach ($backend in $backends)
{
    $exe = Join-Path (Join-Path $PlayersFrom $backend) $ExeName
    $sidecar = Read-Sidecar $exe $backend
    $players[$backend] = [pscustomobject]@{
        executable = $exe
        exeSha256 = (Get-Sha $exe)
        sidecarSha256 = (Get-Sha ($exe + '.build.json'))
        buildGuid = $sidecar.buildGuid
        unityVersion = $sidecar.unityVersion
        onityVersion = $sidecar.onityVersion
        messagePipeFlavor = $sidecar.messagePipeFlavor
        messagePipeVersion = $sidecar.messagePipeVersion
        messagePipePackageSha256 = $sidecar.messagePipePackageSha256
        messagePipeDllSha256 = $sidecar.messagePipeDllSha256
        uniTaskVersion = $sidecar.uniTaskVersion
        sourceHead = $sidecar.sourceHead
    }
}
$sameFlavor = $players['IL2CPP'].messagePipeFlavor -eq $players['Mono'].messagePipeFlavor
$samePackage = $players['IL2CPP'].messagePipePackageSha256 -eq $players['Mono'].messagePipePackageSha256
if (!$sameFlavor -or !$samePackage) { throw 'The IL2CPP and Mono Players were built from different MessagePipe flavors or packages.' }

$plan = @()
$sequence = 0
for ($round = 1; $round -le $Processes; $round++)
{
    $order = @('IL2CPP', 'Mono')
    if (($round % 2) -eq 0) { $order = @('Mono', 'IL2CPP') }
    foreach ($backend in $order)
    {
        $sequence++
        $plan += [pscustomobject]@{ backend = $backend; round = $round; sequence = $sequence
            stem = ($backend.ToLowerInvariant() + '-messaging-p' + $round) }
    }
}

New-Item -ItemType Directory -Path $runsDir -Force | Out-Null
$state = [ordered]@{
    schemaVersion = 1
    selfTest = [bool]$SelfTest
    startedUtc = [DateTime]::UtcNow.ToString('O')
    endedUtc = $null
    status = 'running'
    failure = $null
    hostProject = $HostProject
    playersFrom = $PlayersFrom
    evidenceRoot = $EvidenceRoot
    processesPerBackend = $Processes
    priority = $Priority
    timeoutSeconds = $TimeoutSeconds
    players = $players
    plan = $plan
    processes = @()
}
$manifestPath = Join-Path $EvidenceRoot 'run-manifest.json'

function Save-State
{
    $state.endedUtc = [DateTime]::UtcNow.ToString('O')
    [pscustomobject]$state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

$exitCode = 1
try
{
    if ($SelfTest) { Write-Host 'HARNESS SELF-TEST: tiny counts, never performance evidence.' }
    $affinity = Get-AffinityValue
    Write-Host ('planned ' + $plan.Count + ' Player process(es): ' + (($plan | ForEach-Object { $_.stem }) -join ', '))
    $failed = @()
    foreach ($run in $plan)
    {
        $observation = Invoke-PlayerProcess $run $affinity
        $state.processes += $observation
        Save-State
        if (!$observation.succeeded) { $failed += $run.stem }
    }
    if ($failed.Count -gt 0)
    {
        $state.status = 'completed-with-failed-processes'
        $state.failure = 'Failed processes: ' + ($failed -join ', ')
        Write-Host $state.failure
    }
    else
    {
        $state.status = 'completed'
        $exitCode = 0
    }
}
catch
{
    $state.status = 'aborted'
    $state.failure = $_.Exception.Message
    Write-Host ('ABORTED: ' + $_.Exception.Message)
}
finally
{
    Save-State
    $selfTestFlag = ''
    if ($SelfTest) { $selfTestFlag = ' --allow-self-test' }
    Write-Host ('status: ' + $state.status + '; manifest: ' + $manifestPath)
    Write-Host ('summary: python "' + (Join-Path $PSScriptRoot 'messaging-summary.py') + '" --evidence-root "' + $EvidenceRoot + '" --processes ' + $Processes + $selfTestFlag)
}
exit $exitCode
