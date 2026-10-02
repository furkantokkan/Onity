<#
.SYNOPSIS
    Runs paired baseline/candidate Release Player rounds with the Unity-noise screen.
.DESCRIPTION
    Plan 16 PERF-0, promoted from the returnbuffer-20260930 packet helper. Players are expected at
      <EvidenceRoot>/Players/<revision>-<Backend>/OnityTaskBenchmark.exe (+ .build.json sidecar),
    revision in baseline|candidate. Per round and backend one process per revision runs, one at a
    time, with the order reversed between rounds (r1: Backends in order, baseline first for the
    first backend; r2: Backends reversed; revision order alternates per backend index).
    Before anything starts: all planned evidence names are checked (the run refuses to overwrite
    any existing evidence) and quiet-check.ps1 must report QUIET. Each Player starts at -Priority
    with the -AffinityMask (default: every logical core except 0 and 1). Per process the other
    Unity CPU is observed; <=0.5 s AND <=5% of wall is "accepted", otherwise "flagged", an
    unreadable/new process is "unknown". Writes <stem>.json/.startup.log/.player.log and
    <stem>.observation.json.
    -Replacement runs replacement rounds (round numbers Rounds+1 ...) only for backends that have
    a flagged/unknown pair, or that are named in -ReplaceBackend (denominator CV failure reported
    by validate-reports.py), never more than -MaxReplacement per backend.
    Works on Windows PowerShell 5.1 and PowerShell 7.
#>
param(
    [Parameter(Mandatory = $true)][string]$EvidenceRoot,
    [string]$Suite = 'builderlifecycle',
    [string[]]$Backends = @('Mono', 'IL2CPP'),
    [int]$Rounds = 3,
    [int]$MaxReplacement = 2,
    [ValidateSet('Idle', 'BelowNormal', 'Normal', 'AboveNormal', 'High', 'RealTime')][string]$Priority = 'High',
    [string]$AffinityMask = '',
    [switch]$Replacement,
    [string[]]$ReplaceBackend = @(),
    [string[]]$ExtraPlayerArgs = @(),
    [int]$QuietSeconds = 10,
    [int]$TimeoutSeconds = 900,
    [switch]$SkipQuietCheck
)
$ErrorActionPreference = 'Stop'
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if (!(Test-Path -LiteralPath $EvidenceRoot)) { throw 'Evidence root not found: ' + $EvidenceRoot }

function Get-UnityCpuSnapshot
{
    $observations = @()
    foreach ($process in @(Get-Process -Name Unity -ErrorAction SilentlyContinue))
    {
        try
        {
            $observations += [pscustomobject]@{
                pid = $process.Id
                startUtc = $process.StartTime.ToUniversalTime().ToString('O')
                cpuSeconds = $process.TotalProcessorTime.TotalSeconds
                observed = $true
            }
        }
        catch
        {
            $observations += [pscustomobject]@{ pid = $process.Id; observed = $false }
        }
    }
    return $observations
}

function Get-NoiseObservation($Before, $After, [double]$WallSeconds)
{
    $unknown = $false
    $cpu = 0.0
    foreach ($item in $Before)
    {
        $matched = @($After | Where-Object { $_.pid -eq $item.pid -and $_.startUtc -eq $item.startUtc })
        if (!$item.observed -or $matched.Count -ne 1 -or !$matched[0].observed) { $unknown = $true }
        else
        {
            $delta = $matched[0].cpuSeconds - $item.cpuSeconds
            if ($delta -lt 0) { $unknown = $true } else { $cpu += $delta }
        }
    }
    foreach ($item in $After)
    {
        if (@($Before | Where-Object { $_.pid -eq $item.pid -and $_.startUtc -eq $item.startUtc }).Count -ne 1) { $unknown = $true }
    }
    $fraction = [double]::PositiveInfinity
    if ($WallSeconds -gt 0) { $fraction = $cpu / $WallSeconds }
    $status = 'flagged'
    if ($unknown) { $status = 'unknown' } elseif ($cpu -le 0.5 -and $fraction -le 0.05) { $status = 'accepted' }
    return [pscustomobject]@{ unknown = $unknown; cpu = $cpu; fraction = $fraction; status = $status }
}

function Get-Stem([string]$Revision, [string]$Backend, [int]$Round)
{
    return $Revision + '-' + $Backend.ToLowerInvariant() + '-r' + $Round
}

function Get-BinaryPath([string]$Revision, [string]$Backend)
{
    return Join-Path $EvidenceRoot ('Players/' + $Revision + '-' + $Backend + '/OnityTaskBenchmark.exe')
}

# Affinity: default excludes cores 0 and 1.
$affinity = [int64]0
if ($AffinityMask -eq '')
{
    $cores = [Environment]::ProcessorCount
    if ($cores -gt 63) { $cores = 63 }
    if ($cores -gt 2) { $affinity = (([int64]1 -shl $cores) - 1) -band (-bnot [int64]3) }
}
elseif ($AffinityMask -ne '0')
{
    if ($AffinityMask -match '^0x') { $affinity = [Convert]::ToInt64($AffinityMask.Substring(2), 16) }
    else { $affinity = [int64]$AffinityMask }
}

$runs = @()
$quietHash = $null
$pairsPerBackend = @{}
if (!$Replacement)
{
    for ($round = 1; $round -le $Rounds; $round++)
    {
        $order = @($Backends)
        if (($round % 2) -eq 0) { [array]::Reverse($order) }
        foreach ($backend in $order)
        {
            $index = [array]::IndexOf($Backends, $backend)
            $baselineFirst = ((($round - 1) + $index) % 2) -eq 0
            $revisions = @('candidate', 'baseline')
            if ($baselineFirst) { $revisions = @('baseline', 'candidate') }
            foreach ($revision in $revisions)
            {
                $runs += [pscustomobject]@{ backend = $backend; round = $round; revision = $revision; replacement = $false }
            }
        }
    }
}
else
{
    foreach ($backend in $Backends)
    {
        $lastRound = 0
        $flagged = 0
        $accepted = 0
        for ($round = 1; $round -le ($Rounds + $MaxReplacement); $round++)
        {
            $paths = @()
            foreach ($revision in @('baseline', 'candidate')) { $paths += Join-Path $EvidenceRoot ((Get-Stem $revision $backend $round) + '.observation.json') }
            if (!((Test-Path -LiteralPath $paths[0]) -and (Test-Path -LiteralPath $paths[1]))) { continue }
            $lastRound = $round
            $statuses = @($paths | ForEach-Object { (Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json).noiseStatus })
            if ($statuses -contains 'flagged' -or $statuses -contains 'unknown') { $flagged++ } else { $accepted++ }
        }
        if ($lastRound -lt $Rounds) { throw ('Backend ' + $backend + ' has not finished its initial ' + $Rounds + ' rounds.') }
        $used = $lastRound - $Rounds
        $needed = $flagged
        if ($ReplaceBackend -contains $backend -and $needed -lt 1) { $needed = 1 }
        if ($accepted -ge $Rounds -and !($ReplaceBackend -contains $backend)) { $needed = 0 }
        $allowed = $MaxReplacement - $used
        if ($needed -gt $allowed) { $needed = $allowed }
        for ($k = 1; $k -le $needed; $k++)
        {
            $round = $lastRound + $k
            $order = @('candidate', 'baseline')
            if (($round % 2) -eq 0) { $order = @('baseline', 'candidate') }
            foreach ($revision in $order) { $runs += [pscustomobject]@{ backend = $backend; round = $round; revision = $revision; replacement = $true } }
        }
    }
    if ($runs.Count -eq 0) { throw 'No backend is eligible for a replacement round (none flagged/named, or the replacement budget is spent).' }
}

# Refuse the whole dispatch before launching anything if evidence would be overwritten or inputs are missing.
foreach ($run in $runs)
{
    $stem = Get-Stem $run.revision $run.backend $run.round
    $binary = Get-BinaryPath $run.revision $run.backend
    if (!(Test-Path -LiteralPath $binary) -or !(Test-Path -LiteralPath ($binary + '.build.json')))
    {
        throw 'Missing exact executable or build sidecar: ' + $binary
    }
    foreach ($suffix in @('.json', '.startup.log', '.player.log', '.observation.json'))
    {
        if (Test-Path -LiteralPath (Join-Path $EvidenceRoot ($stem + $suffix))) { throw 'Refusing existing evidence: ' + $stem + $suffix }
    }
}
Write-Output ('planned ' + $runs.Count + ' process(es): ' + (($runs | ForEach-Object { Get-Stem $_.revision $_.backend $_.round }) -join ', '))

# Quiet pre-gate (plan 1.4).
if (!$SkipQuietCheck)
{
    $quietScript = Join-Path $PSScriptRoot 'quiet-check.ps1'
    $quietJson = Join-Path $EvidenceRoot ('quiet-pregate-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmss') + 'Z.json')
    $shell = (Get-Process -Id $PID).Path
    & $shell -NoProfile -ExecutionPolicy Bypass -File $quietScript -Seconds $QuietSeconds -OutputJson $quietJson
    if ($LASTEXITCODE -ne 0) { throw 'quiet-check refused (exit ' + $LASTEXITCODE + '); no Player was started.' }
    $quietHash = (Get-FileHash -LiteralPath $quietJson -Algorithm SHA256).Hash
}

$failedRuns = @()
$sequence = 0
$existingSequence = @(Get-ChildItem -LiteralPath $EvidenceRoot -Filter '*.observation.json' -ErrorAction SilentlyContinue).Count
foreach ($run in $runs)
{
    $sequence++
    $stem = Get-Stem $run.revision $run.backend $run.round
    $binary = Get-BinaryPath $run.revision $run.backend
    $report = Join-Path $EvidenceRoot ($stem + '.json')
    $startup = Join-Path $EvidenceRoot ($stem + '.startup.log')
    $log = Join-Path $EvidenceRoot ($stem + '.player.log')
    $binaryHash = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
    $sidecarHash = (Get-FileHash -LiteralPath ($binary + '.build.json') -Algorithm SHA256).Hash
    $before = @(Get-UnityCpuSnapshot)
    $started = [DateTime]::UtcNow
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $arguments = @(
        '-batchmode', '-nographics', '-onityRunTaskBenchmark',
        '-onityTaskBenchmarkSuite', $Suite,
        '-onityTaskBenchmarkOutput', ('"' + $report + '"'),
        '-onityTaskBenchmarkStartupTrace', ('"' + $startup + '"'),
        '-onityTaskBenchmarkBuildMetadata', ('"' + $binary + '.build.json"'),
        '-logFile', ('"' + $log + '"')
    ) + $ExtraPlayerArgs
    $player = Start-Process -FilePath $binary -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $appliedPriority = $null
    $appliedAffinity = $null
    try
    {
        $player.PriorityClass = $Priority
        $appliedPriority = [string]$player.PriorityClass
        if ($affinity -ne 0)
        {
            $player.ProcessorAffinity = [IntPtr]$affinity
            $appliedAffinity = [int64]$player.ProcessorAffinity
        }
    }
    catch { Write-Warning ('Could not apply priority/affinity to PID ' + $player.Id + ': ' + $_.Exception.Message) }
    $timedOut = !$player.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut)
    {
        $player.Kill()
        $player.WaitForExit()
    }
    $watch.Stop()
    $ended = [DateTime]::UtcNow
    $after = @(Get-UnityCpuSnapshot)
    $noise = Get-NoiseObservation $before $after $watch.Elapsed.TotalSeconds
    $binaryStable = $binaryHash -eq (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
    $sidecarStable = $sidecarHash -eq (Get-FileHash -LiteralPath ($binary + '.build.json') -Algorithm SHA256).Hash
    $reportHash = $null; if (Test-Path -LiteralPath $report) { $reportHash = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash }
    $startupHash = $null; if (Test-Path -LiteralPath $startup) { $startupHash = (Get-FileHash -LiteralPath $startup -Algorithm SHA256).Hash }
    $logHash = $null; if (Test-Path -LiteralPath $log) { $logHash = (Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash }
    $identity = [pscustomobject]@{
        schemaVersion = 1
        suite = $Suite
        revision = $run.revision
        backend = $run.backend
        round = $run.round
        sequence = $existingSequence + $sequence
        replacement = [bool]$run.replacement
        quietCheckSha256 = $quietHash
        priorityApplied = $appliedPriority
        affinityApplied = $appliedAffinity
        executable = $binary
        buildMetadata = $binary + '.build.json'
        report = $report
        startupTrace = $startup
        playerLog = $log
        playerPid = $player.Id
        startedUtc = $started.ToString('O')
        endedUtc = $ended.ToString('O')
        elapsedSeconds = $watch.Elapsed.TotalSeconds
        exitCode = $player.ExitCode
        timedOut = $timedOut
        binarySha256 = $binaryHash
        buildMetadataSha256 = $sidecarHash
        binaryStable = $binaryStable
        buildMetadataStable = $sidecarStable
        reportSha256 = $reportHash
        startupSha256 = $startupHash
        playerLogSha256 = $logHash
        otherUnityBefore = $before
        otherUnityAfter = $after
        unknown = $noise.unknown
        otherUnityCpuSeconds = $noise.cpu
        otherUnityWallFraction = $noise.fraction
        noiseStatus = $noise.status
        noiseRule = 'Other Unity CPU <=0.5s AND <=5% wall; no core normalization. Missing/new/unreadable process is unknown.'
        taskOwnedOverlap = 'Run only after all task-owned builds, tests and Roslyn finish; helper launches Players sequentially.'
    }
    $identity | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $EvidenceRoot ($stem + '.observation.json')) -Encoding UTF8
    Write-Output ($stem + ' exit=' + $player.ExitCode + ' wall=' + $watch.Elapsed.TotalSeconds.ToString('F3') +
        's UnityCPU=' + $noise.cpu.ToString('F3') + 's noise=' + $noise.status)
    if ($timedOut -or $player.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report) -or !$binaryStable -or !$sidecarStable)
    {
        $failedRuns += $stem
    }
}
if ($failedRuns.Count -ne 0)
{
    throw 'Collection completed with failed processes retained: ' + ($failedRuns -join ', ')
}
