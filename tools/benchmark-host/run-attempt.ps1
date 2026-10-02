<#
.SYNOPSIS
    Runs one surpass-UniTask benchmark attempt (or a harness self-test) on the benchmark host, end to end.
.DESCRIPTION
    Steps, each logged to <attempt>/attempt.log:
      1. Lease and lock checks: a valid HOST-LEASE.json owned by -LeaseOwner (renewed before every step),
         no Temp/UnityLockfile, no Unity process on the host, no running OnityTaskBenchmark Player.
      2. Quiet pre-gate (quiet-check.ps1; recorded, refuses only with -RequireQuiet).
      3. Settings: -Record when no originals exist, then -Check (a mismatch is restored by copy, then re-checked).
      4. Stage the source package (stage-host.ps1, extras deleted) with <attempt>/staging.json.
      5. When an .asmdef/.asmref changed (or -ForceProjectSync): headless Unity SyncSolution so the generated
         projects match; settings/temp-scene/manifest checks follow every Editor run.
      6. Roslyn compile of every compiled host package assembly (host-compile-check.py, --dependents);
         anything but COMPILE_OK aborts before a build.
      7. One Mono and one IL2CPP non-development Release Player (BuildPlayerFromCommandLine) into
         <attempt>/Players/<Backend>/, with binary manifests. -PlayersFrom reuses the Players of an earlier
         attempt directory (normally the self-test) when its staged sources are byte-identical.
      8. Per backend, sequentially, High priority, affinity excluding cores 0-1: 3 processes each of primary
         (retention matched), builderlifecycle (retention matched) and throughput, rounds interleaved with
         alternating backend and rotating suite order, then 1 report-only process each of primary and
         builderlifecycle at default retention. Each process gets <stem>.json/.startup.log/.player.log/
         .observation.json under <attempt>/runs/. A process that died, hung or never reached benchmark entry
         (no benchmark-completed/benchmark-failed marker) is moved to <attempt>/failed-runs/ and run again
         (-InfrastructureRetries, default 1); a failure the Player reported itself is final.
      9. Settings check/restore, temp scene absent, Packages/manifest.json unchanged.
     10. attempt-manifest.json (sources, staging, build GUIDs, binaries, processes) and files.sha256.
     11. surpass-gate.py over <attempt>/runs; prints the verdict path.
    -SelfTest passes -onityTaskBenchmarkSelfTest, runs 1 process per suite and retention per backend and asks
    the gate for its self-test parse mode (no verdict). Self-test reports are never usable for ratios.
    Works on Windows PowerShell 5.1 and PowerShell 7.
.EXAMPLE
    run-attempt.ps1 -SelfTest -Label selftest -Source C:\path\to\worktree
.EXAMPLE
    run-attempt.ps1 -Label attempt-1 -Source C:\path\to\worktree -PlayersFrom <host>\BenchmarkResults\surpass-selftest-<stamp>
#>
param(
    [Parameter(Mandatory = $true)][string]$Label,
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$HostProject = 'C:\Users\e-fur\.codex\worktrees\onity-builder-benchmark-4be50dc\Onity',
    [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe',
    [switch]$SelfTest,
    [string]$LeaseOwner = 'claude bench agent',
    [string]$LeasePacket = '',
    [switch]$AcquireLease,
    [switch]$ReleaseLease,
    [string]$PlayersFrom = '',
    [switch]$ForceProjectSync,
    [switch]$RequireQuiet,
    [ValidateSet('Idle', 'BelowNormal', 'Normal', 'AboveNormal', 'High', 'RealTime')][string]$Priority = 'High',
    [string]$AffinityMask = '',
    [int]$PlayerTimeoutSeconds = 1800,
    [int]$PlayerStartupSeconds = 120,
    [ValidateRange(0, 3)][int]$InfrastructureRetries = 1,
    [int]$EditorTimeoutSeconds = 3600,
    [string]$Python = 'python',
    [string]$CompileChecker = 'C:/Users/e-fur/.claude/skills/unity-cli/scripts/unity_compile_check.py'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

if ($Label -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') { throw 'Label must be a short file-name-safe word, for example attempt-1.' }
if (!$LeasePacket) { $LeasePacket = 'surpass-' + $Label }
$Source = [IO.Path]::GetFullPath($Source).TrimEnd('\')
$HostProject = [IO.Path]::GetFullPath($HostProject).TrimEnd('\')
$tools = $PSScriptRoot
$shell = (Get-Process -Id $PID).Path
$stampUtc = [DateTime]::UtcNow
$attempt = Join-Path $HostProject ('BenchmarkResults\surpass-' + $Label + '-' + $stampUtc.ToString('yyyyMMdd-HHmm'))
$runsDir = Join-Path $attempt 'runs'
$playersDir = Join-Path $attempt 'Players'
$logPath = Join-Path $attempt 'attempt.log'
$leasePath = Join-Path $HostProject 'BenchmarkResults\HOST-LEASE.json'
$manifestJson = Join-Path $HostProject 'Packages\manifest.json'
$lockfile = Join-Path $HostProject 'Temp\UnityLockfile'
$tempScene = Join-Path $HostProject 'Assets\OnityBenchmarkTemp'
$backends = @('Mono', 'IL2CPP')
$buildMethod = 'Onity.Editor.Benchmarks.OnityTaskBenchmarkPlayerBuildRunner.BuildPlayerFromCommandLine'

if (!(Test-Path -LiteralPath (Join-Path $Source 'Packages\com.onity.framework') -PathType Container)) { throw 'Source package not found under ' + $Source }
# The harness is staged from the source too: refuse before any host work when it lacks the attempt harness.
$harnessRoot = Join-Path $Source 'Packages\com.onity.framework\Benchmarks\Tasks'
foreach ($required in @('Runtime\OnityTaskThroughputBenchmarkRunner.cs', 'Runtime\OnityTaskBenchmarkInternals.cs', 'Runtime\OnityTaskBenchmarkOptions.cs'))
{
    if (!(Test-Path -LiteralPath (Join-Path $harnessRoot $required))) { throw ('The source lacks the surpass harness (' + $required + '); merge perf/surpass-bench-harness first.') }
}
if (!(Select-String -LiteralPath (Join-Path $harnessRoot 'Editor\OnityTaskBenchmarkPlayerBuildRunner.cs') -Pattern 'BuildPlayerFromCommandLine' -Quiet))
{
    throw 'The source build runner lacks BuildPlayerFromCommandLine; merge perf/surpass-bench-harness first.'
}
if (!(Test-Path -LiteralPath (Join-Path $HostProject 'ProjectSettings\ProjectVersion.txt'))) { throw 'Host is not a Unity project: ' + $HostProject }
if (!(Test-Path -LiteralPath $UnityExe)) { throw 'Unity Editor not found: ' + $UnityExe }
if (Test-Path -LiteralPath $attempt) { throw 'Refusing to reuse an existing attempt directory: ' + $attempt }
New-Item -ItemType Directory -Path $attempt, $runsDir -Force | Out-Null

$state = [ordered]@{
    schemaVersion = 1
    label = $Label
    selfTest = [bool]$SelfTest
    startedUtc = $stampUtc.ToString('O')
    endedUtc = $null
    status = 'running'
    failure = $null
    source = $Source
    sourceHead = $null
    sourceDirty = @()
    hostProject = $HostProject
    unityExe = $UnityExe
    lease = [ordered]@{ packet = $LeasePacket; owner = $LeaseOwner }
    quietCheck = $null
    settingsChecks = @()
    stagingManifest = $null
    stagingSha256 = $null
    projectSync = $null
    roslyn = $null
    players = [ordered]@{}
    playersFrom = $PlayersFrom
    processes = @()
    gate = $null
}

# Logs to the console host and attempt.log. Write-Host (not the output stream) keeps function return values clean.
function Write-Log([string]$Message)
{
    $line = [DateTime]::UtcNow.ToString('HH:mm:ss') + ' ' + $Message
    Write-Host $line
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

# Runs a native command with stderr merged; Windows PowerShell 5.1 would otherwise turn stderr into a
# terminating error under ErrorActionPreference Stop. Returns the output lines; the exit code is in $LASTEXITCODE.
function Invoke-Native([string]$Command, [string[]]$Arguments)
{
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try
    {
        $output = & $Command @Arguments 2>&1 | ForEach-Object { [string]$_ }
    }
    finally
    {
        $ErrorActionPreference = $previous
    }
    return $output
}

function Write-LogLines($Lines, [string]$Prefix = '    ')
{
    foreach ($line in @($Lines)) { if ($null -ne $line) { Write-Log ($Prefix + [string]$line) } }
}

function Get-Sha([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

function Read-UtcStamp([string]$Raw, [string]$Field)
{
    $match = [regex]::Match($Raw, '"' + $Field + '"\s*:\s*"([^"]+)"')
    if (!$match.Success) { return $null }
    $styles = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
    return [DateTime]::Parse($match.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture, $styles)
}

function Write-Lease([string]$Started)
{
    $now = [DateTime]::UtcNow
    if (!$Started) { $Started = $now.ToString('yyyy-MM-ddTHH:mm:ssZ') }
    $lease = [ordered]@{
        packet = $LeasePacket
        owner = $LeaseOwner
        started = $Started
        renewed = $now.ToString('yyyy-MM-ddTHH:mm:ssZ')
        expires = $now.AddMinutes(60).ToString('yyyy-MM-ddTHH:mm:ssZ')
    }
    $lease | ConvertTo-Json | Set-Content -LiteralPath $leasePath -Encoding UTF8
}

# Verifies the lease belongs to -LeaseOwner and has not expired, then renews it for another 60 minutes.
function Update-Lease
{
    $now = [DateTime]::UtcNow
    if (!(Test-Path -LiteralPath $leasePath))
    {
        if (!$AcquireLease) { throw 'No host lease (BenchmarkResults/HOST-LEASE.json); pass -AcquireLease to take a free host.' }
        Write-Lease ''
        Write-Log ('lease acquired: ' + $LeasePacket + ' / ' + $LeaseOwner)
        return
    }
    $raw = Get-Content -LiteralPath $leasePath -Raw
    $owner = [regex]::Match($raw, '"owner"\s*:\s*"([^"]*)"').Groups[1].Value
    $expires = Read-UtcStamp $raw 'expires'
    $started = [regex]::Match($raw, '"started"\s*:\s*"([^"]*)"').Groups[1].Value
    $expired = ($null -eq $expires) -or ($expires -lt $now)
    if ($owner -ne $LeaseOwner)
    {
        if (!$expired) { throw ('The host lease is held by "' + $owner + '" until ' + $expires.ToString('O') + '; refusing.') }
        if (!$AcquireLease) { throw ('The host lease of "' + $owner + '" expired; pass -AcquireLease to replace it.') }
        Write-Lease ''
        Write-Log ('lease replaced (expired lease of ' + $owner + '): ' + $LeasePacket + ' / ' + $LeaseOwner)
        return
    }
    if ($expired -and !$AcquireLease) { throw 'Our host lease expired; pass -AcquireLease to renew it.' }
    Write-Lease $started
}

function Assert-HostIdle
{
    if (Test-Path -LiteralPath $lockfile) { throw 'Temp/UnityLockfile exists: a Unity process owns the host.' }
    $hostKey = $HostProject.ToLowerInvariant()
    foreach ($row in @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue))
    {
        $cmd = [string]$row.CommandLine
        if ($cmd -and $cmd.Replace('/', '\').ToLowerInvariant().Contains($hostKey))
        {
            throw ('Unity PID ' + $row.ProcessId + ' runs with the host project path; refusing.')
        }
    }
    $players = @(Get-Process -Name 'OnityTaskBenchmark' -ErrorAction SilentlyContinue)
    if ($players.Count -gt 0) { throw ('An OnityTaskBenchmark Player is already running (PID ' + $players[0].Id + ').') }
}

function Invoke-Script([string]$Name, [string[]]$Arguments)
{
    $script = Join-Path $tools $Name
    $output = Invoke-Native $shell (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script) + $Arguments)
    $code = $LASTEXITCODE
    Write-LogLines $output
    return $code
}

function Invoke-SettingsCheck([string]$When)
{
    $code = Invoke-Script 'verify-host-settings.ps1' @('-HostProject', $HostProject, '-Check')
    $restored = $false
    if ($code -ne 0)
    {
        Write-Log ('settings mismatch ' + $When + '; restoring the recorded originals by copy')
        if (Test-Path -LiteralPath $lockfile) { throw 'Cannot restore settings while Temp/UnityLockfile exists.' }
        $restoreCode = Invoke-Script 'verify-host-settings.ps1' @('-HostProject', $HostProject, '-Restore')
        if ($restoreCode -ne 0) { throw 'verify-host-settings.ps1 -Restore failed.' }
        $code = Invoke-Script 'verify-host-settings.ps1' @('-HostProject', $HostProject, '-Check')
        $restored = $true
        if ($code -ne 0) { throw 'Settings still differ after -Restore.' }
    }
    $state.settingsChecks += [pscustomobject]@{ when = $When; passed = $true; restored = $restored; utc = [DateTime]::UtcNow.ToString('O') }
}

function Wait-LockfileGone
{
    for ($i = 0; $i -lt 60 -and (Test-Path -LiteralPath $lockfile); $i++) { Start-Sleep -Milliseconds 500 }
    if (Test-Path -LiteralPath $lockfile) { throw 'Temp/UnityLockfile still exists 30 s after the Editor exited.' }
}

function Confirm-HostAfterEditor([string]$When)
{
    Wait-LockfileGone
    Invoke-SettingsCheck $When
    if (Test-Path -LiteralPath $tempScene) { throw ('Assets/OnityBenchmarkTemp exists after ' + $When + '; the build runner did not clean up.') }
    $now = Get-Sha $manifestJson
    if ($now -ne $script:manifestHash) { throw ('Packages/manifest.json changed after ' + $When + '.') }
}

function Invoke-UnityEditor([string]$When, [string[]]$Arguments, [string]$EditorLog)
{
    Update-Lease
    Assert-HostIdle
    $all = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $HostProject + '"')) + $Arguments + @('-logFile', ('"' + $EditorLog + '"'))
    Write-Log ('editor: ' + $When + ' (log ' + $EditorLog + ')')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $editor = Start-Process -FilePath $UnityExe -ArgumentList $all -PassThru -WindowStyle Hidden
    $null = $editor.Handle
    if (!$editor.WaitForExit($EditorTimeoutSeconds * 1000))
    {
        try { $editor.Kill() } catch { }
        $editor.WaitForExit()
        throw ('Unity did not finish ' + $When + ' within ' + $EditorTimeoutSeconds + ' s.')
    }
    $editor.WaitForExit()
    $code = $editor.ExitCode
    Write-Log ('editor: ' + $When + ' exit=' + $code + ' in ' + [math]::Round($watch.Elapsed.TotalSeconds, 1) + ' s')
    $errors = @(Select-String -LiteralPath $EditorLog -Pattern 'error CS\d+|Scripts have compiler errors|Aborting batchmode' -ErrorAction SilentlyContinue | Select-Object -First 20)
    foreach ($hit in $errors) { Write-Log ('    ' + $hit.Line.Trim()) }
    Confirm-HostAfterEditor $When
    return $code
}

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
        catch { $observations += [pscustomobject]@{ pid = $process.Id; observed = $false } }
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
    $fraction = [double]::PositiveInfinity
    if ($WallSeconds -gt 0) { $fraction = $cpu / $WallSeconds }
    $status = 'flagged'
    if ($unknown) { $status = 'unknown' } elseif ($cpu -le 0.5 -and $fraction -le 0.05) { $status = 'accepted' }
    return [pscustomobject]@{ unknown = $unknown; cpu = $cpu; fraction = $fraction; status = $status }
}

function Get-AffinityMask
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

# Folders a Player never loads: IL2CPP's generated-source backup and Burst debug information.
function Test-NonShippingPath([string]$RelativePath)
{
    return $RelativePath -match '(^|[\\/])[^\\/]*(_BackUpThisFolder_ButDontShipItWithYourGame|_BurstDebugInformation_DoNotShip)([\\/]|$)'
}

function Write-BinaryManifest([string]$Directory, [string]$Path)
{
    $prefix = $Directory.TrimEnd('\') + '\'
    $files = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $Directory -Recurse -File | Sort-Object FullName))
    {
        $relative = $file.FullName.Substring($prefix.Length)
        if (Test-NonShippingPath $relative) { continue }
        $files += [pscustomobject]@{ path = $relative.Replace('\', '/'); bytes = $file.Length; sha256 = (Get-Sha $file.FullName) }
    }
    [pscustomobject]@{ schemaVersion = 1; directory = $Directory; excluded = 'non-shipping *_BackUpThisFolder_ButDontShipItWithYourGame and *_BurstDebugInformation_DoNotShip folders'
        fileCount = $files.Count; files = $files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Path -Encoding UTF8
    return $files.Count
}

function Copy-ShippingPlayer([string]$From, [string]$To)
{
    $prefix = $From.TrimEnd('\') + '\'
    foreach ($file in @(Get-ChildItem -LiteralPath $From -Recurse -File))
    {
        $relative = $file.FullName.Substring($prefix.Length)
        if (Test-NonShippingPath $relative) { continue }
        $target = Join-Path $To $relative
        $directory = Split-Path -Parent $target
        if (!(Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}

function Read-BuildReport([string]$Exe)
{
    $path = $Exe + '.buildreport.json'
    if (!(Test-Path -LiteralPath $path)) { throw ('Missing build report ' + $path) }
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($report.result -ne 'Succeeded') { throw ('Build report says ' + $report.result + ': ' + $path) }
    if (!(Test-Path -LiteralPath $Exe) -or !(Test-Path -LiteralPath ($Exe + '.build.json'))) { throw ('Missing Player or sidecar for ' + $Exe) }
    return $report
}

function Invoke-PlayerProcess($Run, [int64]$Affinity)
{
    Update-Lease
    Assert-HostIdle
    $exe = Join-Path $playersDir ($Run.backend + '\OnityTaskBenchmark.exe')
    $report = Join-Path $runsDir ($Run.stem + '.json')
    $startup = Join-Path $runsDir ($Run.stem + '.startup.log')
    $log = Join-Path $runsDir ($Run.stem + '.player.log')
    foreach ($path in @($report, $startup, $log)) { if (Test-Path -LiteralPath $path) { throw 'Refusing existing evidence: ' + $path } }
    $arguments = @('-batchmode', '-nographics', '-onityRunTaskBenchmark',
        '-onityTaskBenchmarkSuite', $Run.suite,
        '-onityTaskBenchmarkOutput', ('"' + $report + '"'),
        '-onityTaskBenchmarkStartupTrace', ('"' + $startup + '"'),
        '-onityTaskBenchmarkBuildMetadata', ('"' + $exe + '.build.json"'),
        '-logFile', ('"' + $log + '"'))
    if ($Run.suite -ne 'throughput') { $arguments += @('-onityTaskBenchmarkRetention', $Run.retention) }
    if ($SelfTest) { $arguments += '-onityTaskBenchmarkSelfTest' }
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
    catch { Write-Log ('WARNING could not apply priority/affinity to PID ' + $player.Id + ': ' + $_.Exception.Message) }
    $timedOut = $false
    $startupFailed = $false
    while (!$player.WaitForExit(1000))
    {
        $seconds = $watch.Elapsed.TotalSeconds
        $entered = (Test-Path -LiteralPath $startup) -and ((Get-Content -LiteralPath $startup -Raw -ErrorAction SilentlyContinue) -match "`tbenchmark-entry`t")
        if (!$entered -and $seconds -ge $PlayerStartupSeconds) { $startupFailed = $true }
        if ($seconds -ge $PlayerTimeoutSeconds) { $timedOut = $true }
        if ($startupFailed -or $timedOut)
        {
            try { $player.Kill() } catch { }
            $player.WaitForExit()
            break
        }
    }
    $player.WaitForExit()
    $watch.Stop()
    $after = @(Get-UnityCpuSnapshot)
    $noise = Get-NoiseObservation $before $after $watch.Elapsed.TotalSeconds
    $fresh = (Test-Path -LiteralPath $report) -and ((Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started.AddSeconds(-2))
    $exitCode = $null
    try { $exitCode = $player.ExitCode } catch { }
    $trace = ''
    if (Test-Path -LiteralPath $startup) { $trace = Get-Content -LiteralPath $startup -Raw }
    $completed = $trace -match "`tbenchmark-completed`t"
    $reportedFailure = $trace -match "`tbenchmark-failed`t"
    $ok = !$timedOut -and !$startupFailed -and $exitCode -eq 0 -and $fresh -and $completed -and ($binaryHash -eq (Get-Sha $exe))
    # A Player that died, hung or never started gave no benchmark verdict; one it reported itself is final.
    $infrastructureFailure = !$ok -and !$reportedFailure
    $observation = [ordered]@{
        schemaVersion = 1
        stem = $Run.stem
        suite = $Run.suite
        retention = $Run.retention
        backend = $Run.backend
        round = $Run.round
        sequence = $Run.sequence
        selfTest = [bool]$SelfTest
        executable = $exe
        binarySha256 = $binaryHash
        arguments = ($arguments -join ' ')
        report = $report
        startupTrace = $startup
        playerLog = $log
        playerPid = $player.Id
        startedUtc = $started.ToString('O')
        elapsedSeconds = $watch.Elapsed.TotalSeconds
        exitCode = $exitCode
        timedOut = $timedOut
        startupFailed = $startupFailed
        reportFresh = $fresh
        benchmarkCompletedMarker = $completed
        benchmarkFailedMarker = $reportedFailure
        infrastructureFailure = $infrastructureFailure
        succeeded = $ok
        reportSha256 = $(if (Test-Path -LiteralPath $report) { Get-Sha $report } else { $null })
        priorityApplied = $appliedPriority
        affinityApplied = $appliedAffinity
        otherUnityCpuSeconds = $noise.cpu
        otherUnityWallFraction = $noise.fraction
        noiseStatus = $noise.status
        noiseRule = 'Other Unity CPU <=0.5s AND <=5% wall; a missing/new/unreadable process is unknown. Informational.'
    }
    [pscustomobject]$observation | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runsDir ($Run.stem + '.observation.json')) -Encoding UTF8
    Write-Log ($Run.stem + ' exit=' + $exitCode + ' wall=' + $watch.Elapsed.TotalSeconds.ToString('F1') + 's noise=' + $noise.status + $(if ($ok) { '' } else { ' FAILED (timeout=' + $timedOut + ' startup=' + $startupFailed + ' fresh=' + $fresh + ' completed=' + $completed + ')' }))
    return [pscustomobject]$observation
}

function Get-RunPlan
{
    $plan = @()
    $sequence = 0
    $rounds = 3
    if ($SelfTest) { $rounds = 1 }
    $suiteOrders = @(@('primary', 'builderlifecycle', 'throughput'), @('builderlifecycle', 'throughput', 'primary'), @('throughput', 'primary', 'builderlifecycle'))
    for ($round = 1; $round -le $rounds; $round++)
    {
        $order = @('IL2CPP', 'Mono')
        if (($round % 2) -eq 0) { $order = @('Mono', 'IL2CPP') }
        foreach ($backend in $order)
        {
            foreach ($suite in $suiteOrders[($round - 1) % 3])
            {
                $sequence++
                $plan += [pscustomobject]@{ backend = $backend; suite = $suite; retention = 'matched'; round = $round; sequence = $sequence
                    stem = ($backend.ToLowerInvariant() + '-' + $suite + '-matched-p' + $round) }
            }
        }
    }
    foreach ($backend in @('IL2CPP', 'Mono'))
    {
        foreach ($suite in @('primary', 'builderlifecycle'))
        {
            $sequence++
            $plan += [pscustomobject]@{ backend = $backend; suite = $suite; retention = 'default'; round = 1; sequence = $sequence
                stem = ($backend.ToLowerInvariant() + '-' + $suite + '-default-p1') }
        }
    }
    return $plan
}

function Save-State
{
    $state.endedUtc = [DateTime]::UtcNow.ToString('O')
    [pscustomobject]$state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $attempt 'attempt-manifest.json') -Encoding UTF8
}

$exitCode = 1
$script:manifestHash = $null
try
{
    Write-Log ('surpass attempt ' + $Label + $(if ($SelfTest) { ' (HARNESS SELF-TEST: never performance evidence)' } else { '' }))
    Write-Log ('attempt directory: ' + $attempt)
    $state.sourceHead = ((Invoke-Native 'git' @('-C', $Source, 'rev-parse', 'HEAD')) | Out-String).Trim()
    $state.sourceDirty = @(Invoke-Native 'git' @('-C', $Source, 'status', '--porcelain'))
    Write-Log ('source: ' + $Source + ' @ ' + $state.sourceHead + ' (' + $state.sourceDirty.Count + ' dirty path(s))')

    # 1. Lease and locks.
    Update-Lease
    Assert-HostIdle
    $script:manifestHash = Get-Sha $manifestJson
    $state.manifestSha256 = $script:manifestHash
    Write-Log ('lease ok; host idle; Packages/manifest.json ' + $script:manifestHash)

    # 2. Quiet pre-gate (informational unless -RequireQuiet).
    $quietJson = Join-Path $attempt 'quiet-pregate.json'
    $quietCode = Invoke-Script 'quiet-check.ps1' @('-Seconds', '10', '-HostProject', $HostProject, '-FailOnHostEditor', '-OutputJson', $quietJson)
    $state.quietCheck = [ordered]@{ exitCode = $quietCode; quiet = ($quietCode -eq 0); report = $quietJson }
    if ($quietCode -ne 0)
    {
        if ($RequireQuiet) { throw 'quiet-check refused and -RequireQuiet was given.' }
        Write-Log 'WARNING quiet-check did not report QUIET; continuing (noise is recorded per process).'
    }

    # 3. Settings: record once, then check (restore by copy on mismatch).
    if (!(Test-Path -LiteralPath (Join-Path $HostProject 'BenchmarkResults\host-settings-original\settings.json')))
    {
        if ((Invoke-Script 'verify-host-settings.ps1' @('-HostProject', $HostProject, '-Record')) -ne 0) { throw 'verify-host-settings.ps1 -Record failed.' }
    }
    Invoke-SettingsCheck 'before staging'

    # 4. Stage.
    Update-Lease
    Assert-HostIdle
    $staging = Join-Path $attempt 'staging.json'
    $stageCode = Invoke-Script 'stage-host.ps1' @('-Source', $Source, '-HostProject', $HostProject, '-ManifestPath', $staging, '-AllowedExtraPattern', '*')
    if ($stageCode -ne 0) { throw 'stage-host.ps1 failed.' }
    $stagingData = Get-Content -LiteralPath $staging -Raw | ConvertFrom-Json
    $state.stagingManifest = $staging
    $state.stagingSha256 = Get-Sha $staging
    $changed = @($stagingData.different) + @($stagingData.missingInHost) + @($stagingData.extraInHost) | Where-Object { $_ }
    Write-Log ('staged ' + $stagingData.fileCount + ' file(s); changed ' + @($changed).Count)

    # 5. Project sync when assembly definitions changed.
    $asmChanged = @($changed | Where-Object { $_ -match '\.(asmdef|asmref)$' })
    $synced = $false
    if ($ForceProjectSync -or $asmChanged.Count -gt 0)
    {
        Write-Log ('project sync needed: ' + $(if ($asmChanged.Count) { ($asmChanged -join ', ') } else { '-ForceProjectSync' }))
        $syncCode = Invoke-UnityEditor 'project sync' @('-executeMethod', 'UnityEditor.SyncVS.SyncSolution') (Join-Path $attempt 'editor-project-sync.log')
        $state.projectSync = [ordered]@{ exitCode = $syncCode; reason = ($asmChanged -join ', ') }
        if ($syncCode -ne 0) { throw 'Unity project sync failed (see editor-project-sync.log).' }
        $synced = $true
    }

    # 6. Roslyn compile of every compiled host package assembly.
    $roslynLog = Join-Path $attempt 'roslyn-host.log'
    $roslynArgs = @((Join-Path $tools 'host-compile-check.py'), '--project', $HostProject, '--staging', $staging,
        '--checker', $CompileChecker, '--list', (Join-Path $attempt 'roslyn-files.txt'))
    $env:PYTHONDONTWRITEBYTECODE = '1'
    $roslynOut = Invoke-Native $Python $roslynArgs
    $roslynCode = $LASTEXITCODE
    $roslynOut | Set-Content -LiteralPath $roslynLog -Encoding UTF8
    Write-LogLines $roslynOut
    if ($roslynCode -eq 2 -and !$synced)
    {
        Write-Log 'COMPILE_UNVERIFIED: regenerating projects once and retrying.'
        $syncCode = Invoke-UnityEditor 'project sync (retry)' @('-executeMethod', 'UnityEditor.SyncVS.SyncSolution') (Join-Path $attempt 'editor-project-sync-retry.log')
        if ($syncCode -ne 0) { throw 'Unity project sync failed (see editor-project-sync-retry.log).' }
        $roslynOut = Invoke-Native $Python $roslynArgs
        $roslynCode = $LASTEXITCODE
        $roslynOut | Add-Content -LiteralPath $roslynLog -Encoding UTF8
        Write-LogLines $roslynOut
    }
    $state.roslyn = [ordered]@{ exitCode = $roslynCode; log = $roslynLog }
    if ($roslynCode -ne 0) { throw ('Roslyn compile check did not report COMPILE_OK (exit ' + $roslynCode + '); no build was started.') }

    # 7. Players: build one per backend, or reuse byte-identical ones.
    New-Item -ItemType Directory -Path $playersDir -Force | Out-Null
    if ($PlayersFrom)
    {
        $from = [IO.Path]::GetFullPath($PlayersFrom).TrimEnd('\')
        $fromStaging = Get-Content -LiteralPath (Join-Path $from 'staging.json') -Raw | ConvertFrom-Json
        $mine = @($stagingData.files | ForEach-Object { $_.path + '=' + $_.sourceSha256 }) -join "`n"
        $theirs = @($fromStaging.files | ForEach-Object { $_.path + '=' + $_.sourceSha256 }) -join "`n"
        if ($mine -ne $theirs) { throw ('The staged sources differ from those the Players in ' + $from + ' were built from; rebuild instead.') }
        foreach ($backend in $backends)
        {
            $fromPlayer = Join-Path $from ('Players\' + $backend)
            Copy-ShippingPlayer $fromPlayer (Join-Path $playersDir $backend)
            $fromManifest = Join-Path $from ('binary-manifest-' + $backend.ToLowerInvariant() + '.json')
            if (Test-Path -LiteralPath $fromManifest)
            {
                # The copy must be byte-identical to the binaries the earlier run measured.
                foreach ($entry in @((Get-Content -LiteralPath $fromManifest -Raw | ConvertFrom-Json).files))
                {
                    $copy = Join-Path (Join-Path $playersDir $backend) $entry.path.Replace('/', '\')
                    if (!(Test-Path -LiteralPath $copy) -or (Get-Sha $copy) -ne $entry.sha256) { throw ('Reused Player file differs from its manifest: ' + $entry.path) }
                }
            }
            Write-Log ('reused ' + $backend + ' Player from ' + $from)
        }
    }
    else
    {
        foreach ($backend in $backends)
        {
            $exe = Join-Path $playersDir ($backend + '\OnityTaskBenchmark.exe')
            $buildCode = Invoke-UnityEditor ('build ' + $backend) @('-executeMethod', $buildMethod, '-onityTaskBenchmarkBackend', $backend,
                '-onityTaskBenchmarkBuildPath', ('"' + $exe + '"')) (Join-Path $attempt ('editor-build-' + $backend.ToLowerInvariant() + '.log'))
            if ($buildCode -ne 0) { throw ($backend + ' Player build failed (exit ' + $buildCode + ').') }
        }
    }
    foreach ($backend in $backends)
    {
        $exe = Join-Path $playersDir ($backend + '\OnityTaskBenchmark.exe')
        $build = Read-BuildReport $exe
        $binaryManifest = Join-Path $attempt ('binary-manifest-' + $backend.ToLowerInvariant() + '.json')
        $count = Write-BinaryManifest (Join-Path $playersDir $backend) $binaryManifest
        $state.players[$backend] = [ordered]@{ executable = $exe; buildGuid = $build.buildGuid; result = $build.result
            exeSha256 = (Get-Sha $exe); sidecarSha256 = (Get-Sha ($exe + '.build.json')); binaryManifest = $binaryManifest; files = $count }
        Write-Log ($backend + ' Player GUID ' + $build.buildGuid + ' (' + $count + ' files)')
    }
    Save-State

    # 8. Player processes.
    $affinity = Get-AffinityMask
    $plan = @(Get-RunPlan)
    Write-Log ('planned ' + $plan.Count + ' Player process(es): ' + (($plan | ForEach-Object { $_.stem }) -join ', '))
    $failed = @()
    $failedRunsDir = Join-Path $attempt 'failed-runs'
    foreach ($run in $plan)
    {
        $observation = Invoke-PlayerProcess $run $affinity
        $state.processes += $observation
        Save-State
        for ($retry = 1; !$observation.succeeded -and $observation.infrastructureFailure -and $retry -le $InfrastructureRetries; $retry++)
        {
            # Keep the failed evidence outside runs/ (the gate reads runs/ only) and run the same process again.
            $keep = Join-Path $failedRunsDir ($run.stem + '-try' + $retry)
            New-Item -ItemType Directory -Path $keep -Force | Out-Null
            $failedReport = Join-Path $runsDir ($run.stem + '.json')
            $failedHash = $null
            if (Test-Path -LiteralPath $failedReport) { $failedHash = Get-Sha $failedReport }
            foreach ($file in @(Get-ChildItem -LiteralPath $runsDir -File))
            {
                # The primary suite also writes a byte-identical onity-task-benchmark-<stamp>.json copy.
                $copyOfFailed = $failedHash -and $file.Name -like 'onity-task-benchmark-*.json' -and (Get-Sha $file.FullName) -eq $failedHash
                if ($file.Name.StartsWith($run.stem + '.') -or $copyOfFailed)
                {
                    Move-Item -LiteralPath $file.FullName -Destination (Join-Path $keep $file.Name)
                }
            }
            Write-Log ($run.stem + ': infrastructure failure (no benchmark verdict); evidence kept in ' + $keep + '; retry ' + $retry)
            $observation = Invoke-PlayerProcess $run $affinity
            $state.processes += $observation
            Save-State
        }
        if (!$observation.succeeded) { $failed += $run.stem }
    }

    # 9. Host state after the runs.
    Invoke-SettingsCheck 'after the Player processes'
    if (Test-Path -LiteralPath $tempScene) { throw 'Assets/OnityBenchmarkTemp exists after the attempt.' }
    if ((Get-Sha $manifestJson) -ne $script:manifestHash) { throw 'Packages/manifest.json changed during the attempt.' }
    if ($failed.Count -gt 0) { Write-Log ('FAILED processes (evidence retained): ' + ($failed -join ', ')) }

    # 11. Gate.
    $gateArgs = @((Join-Path $tools 'surpass-gate.py'), $runsDir, '--output-dir', $attempt)
    if ($SelfTest) { $gateArgs += '--allow-self-test' }
    $gateOut = Invoke-Native $Python $gateArgs
    $gateCode = $LASTEXITCODE
    $gateOut | Set-Content -LiteralPath (Join-Path $attempt 'gate.log') -Encoding UTF8
    $verdictLine = @($gateOut | Where-Object { [string]$_ -match '^verdict: ' } | Select-Object -Last 1)
    $state.gate = [ordered]@{ exitCode = $gateCode; verdict = $(if ($verdictLine.Count) { ([string]$verdictLine[0]).Substring(9) } else { 'n/a' })
        report = (Join-Path $attempt 'surpass-gate.md'); summary = (Join-Path $attempt 'gate-summary.json') }
    Write-Log ('gate exit=' + $gateCode + ' verdict=' + $state.gate.verdict)
    if ($failed.Count -gt 0)
    {
        $state.status = 'completed-with-failed-processes'
        $exitCode = 1
    }
    else
    {
        $state.status = 'completed'
        $exitCode = $gateCode
    }
}
catch
{
    $state.status = 'aborted'
    $state.failure = $_.Exception.Message
    Write-Log ('ABORTED: ' + $_.Exception.Message)
    $exitCode = 1
}
finally
{
    try
    {
        if (!(Test-Path -LiteralPath $lockfile))
        {
            $finalCode = Invoke-Script 'verify-host-settings.ps1' @('-HostProject', $HostProject, '-Check')
            if ($finalCode -ne 0)
            {
                Write-Log 'final settings mismatch; restoring by copy'
                [void](Invoke-Script 'verify-host-settings.ps1' @('-HostProject', $HostProject, '-Restore'))
                $finalCode = Invoke-Script 'verify-host-settings.ps1' @('-HostProject', $HostProject, '-Check')
            }
            $state.finalSettingsCheck = ($finalCode -eq 0)
        }
        $state.tempSceneAbsent = !(Test-Path -LiteralPath $tempScene)
        if ($script:manifestHash) { $state.manifestUnchanged = ((Get-Sha $manifestJson) -eq $script:manifestHash) }
    }
    catch { Write-Log ('final host check failed: ' + $_.Exception.Message) }
    Save-State
    # 10. Archive index: every file except the Players (they have their own binary manifests).
    try
    {
        $prefix = $attempt.TrimEnd('\') + '\'
        $lines = @()
        foreach ($file in @(Get-ChildItem -LiteralPath $attempt -Recurse -File | Where-Object { !$_.FullName.StartsWith($playersDir + '\') -and $_.Name -ne 'files.sha256' } | Sort-Object FullName))
        {
            $lines += (Get-Sha $file.FullName) + '  ' + $file.FullName.Substring($prefix.Length).Replace('\', '/')
        }
        $lines | Set-Content -LiteralPath (Join-Path $attempt 'files.sha256') -Encoding UTF8
    }
    catch { Write-Log ('archive index failed: ' + $_.Exception.Message) }
    if ($ReleaseLease -and (Test-Path -LiteralPath $leasePath))
    {
        $raw = Get-Content -LiteralPath $leasePath -Raw
        if ([regex]::Match($raw, '"owner"\s*:\s*"([^"]*)"').Groups[1].Value -eq $LeaseOwner)
        {
            Remove-Item -LiteralPath $leasePath -Force
            Write-Log 'lease released'
        }
    }
    Write-Log ('status: ' + $state.status + '; attempt manifest: ' + (Join-Path $attempt 'attempt-manifest.json'))
    if ($state.gate) { Write-Log ('verdict: ' + $state.gate.verdict + ' -> ' + $state.gate.report) }
}
exit $exitCode
