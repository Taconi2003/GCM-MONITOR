param(
    [Parameter(Mandatory)]
    [ValidateSet('Awake', 'BeforeDeadline', 'AfterDeadline')]
    [string]$Expected,
    [string]$PublishedEngine
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$relativeRun = '.artifacts/validation/passive-suspension-' + [Guid]::NewGuid().ToString('N')
$run = Join-Path $repo $relativeRun
[IO.Directory]::CreateDirectory($run) | Out-Null
$dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
$engine = $null
if ($PublishedEngine) {
    $engine = (Resolve-Path -LiteralPath $PublishedEngine).Path
    if ((Split-Path -Leaf $engine) -ne 'GcmMonitor.Engine.exe' -or
        !(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $engine) 'coreclr.dll') -PathType Leaf)) {
        throw 'Informe o motor self-contained gerado por scripts/package.ps1.'
    }
}

function Read-StandbyEvents([DateTimeOffset]$Start, [DateTimeOffset]$End) {
    $filter = @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-Kernel-Power'; Id = 506,507,566;
        StartTime = $Start.LocalDateTime; EndTime = $End.LocalDateTime }
    try { $events = @(Get-WinEvent -FilterHashtable $filter -ErrorAction Stop) }
    catch {
        if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound,*') { return }
        throw
    }
    foreach ($event in ($events | Sort-Object TimeCreated)) {
        $fields = @{}
        foreach ($field in ([xml]$event.ToXml()).Event.EventData.Data) { $fields[$field.Name] = $field.'#text' }
        [pscustomobject]@{ Id = $event.Id; Utc = [DateTimeOffset]$event.TimeCreated.ToUniversalTime();
            Scenario = $fields.ScenarioInstanceIdV2; PreviousSessionId = $fields.PreviousSessionId;
            PreviousSessionType = $fields.PreviousSessionType; NextSessionId = $fields.NextSessionId;
            NextSessionType = $fields.NextSessionType; OnAc = $fields.PowerStateAc }
    }
}

function Compare-StandbyCollection($Events, $Ready, $Collection) {
    $entries = @($events | Where-Object Id -eq 506)
    $exits = @($events | Where-Object Id -eq 507)
    $phaseStarts = @($events | Where-Object { $_.Id -eq 566 -and $_.NextSessionType -eq '2' })
    $phaseEnds = @($events | Where-Object { $_.Id -eq 566 -and $_.PreviousSessionType -eq '2' })
    $outcome = 'Inconclusive'
    $sleepSeconds = $null
    $matchingGap = $null
    if ($entries.Count -eq 0 -and $exits.Count -eq 0 -and $phaseStarts.Count -eq 0 -and $phaseEnds.Count -eq 0) {
        $outcome = 'Awake'
    }
    elseif ($entries.Count -eq 1 -and $exits.Count -eq 1 -and $phaseStarts.Count -eq 1 -and $phaseEnds.Count -eq 1) {
        $startSleep = $phaseStarts[0].Utc
        $endSleep = $phaseEnds[0].Utc
        $sleepSeconds = ($endSleep - $startSleep).TotalSeconds
        $deadline = [DateTimeOffset]$ready.DeadlineUtc
        $matchingIssue = @($collection.GapIssuesUtc | Where-Object { [DateTimeOffset]$_ -ge $endSleep.AddSeconds(-2) -and [DateTimeOffset]$_ -le $endSleep.AddSeconds(15) }).Count -gt 0
        if ($entries[0].Scenario -and $entries[0].Scenario -eq $exits[0].Scenario -and
            $phaseStarts[0].NextSessionId -and $phaseStarts[0].NextSessionId -eq $phaseEnds[0].PreviousSessionId -and
            $entries[0].Utc -ge [DateTimeOffset]$ready.ReadyUtc -and $startSleep -ge $entries[0].Utc -and
            $endSleep -le $exits[0].Utc.AddSeconds(2) -and $sleepSeconds -ge 20 -and $matchingIssue) {
            $matchingGap = $collection.RamGaps | Where-Object {
                [DateTimeOffset]$_.BeforeUtc -le $startSleep.AddSeconds(1) -and [DateTimeOffset]$_.AfterUtc -ge $endSleep.AddSeconds(-2)
            } | Select-Object -First 1
            if ($endSleep -lt $deadline -and $matchingGap) { $outcome = 'BeforeDeadline' }
            if ($startSleep -lt $deadline -and $endSleep -gt $deadline -and !$matchingGap -and
                [DateTimeOffset]$collection.LatestMeasurementUtc -le $startSleep.AddSeconds(1) -and
                [DateTimeOffset]$collection.EndedAt -ge $endSleep.AddSeconds(-2)) { $outcome = 'AfterDeadline' }
        }
    }

    [pscustomobject]@{ Observed = $outcome; SleepSeconds = $sleepSeconds; RamGap = $matchingGap }
}

$readySeconds = switch ($Expected) { 'BeforeDeadline' { '150' }; 'AfterDeadline' { '180' }; 'Awake' { '0' } }
$environment = @{
    DOTNET_ROOT = Split-Path -Parent $dotnet
    DOTNET_CLI_HOME = Join-Path $repo '.tools/home'
    NUGET_PACKAGES = Join-Path $repo '.tools/nuget'
    GCM_PASSIVE_VALIDATION_ENGINE = $engine
    GCM_PASSIVE_SUSPENSION_OUTPUT = $run
    GCM_PASSIVE_SUSPENSION_READY_SECONDS = $readySeconds
}
$arguments = @('test', 'tests/GcmMonitor.IntegrationTests', '-c', 'Release', '--no-build', '--no-restore',
    '--filter', 'FullyQualifiedName~CapturePassiveSessionForWindowsStandbyCorrelation',
    '--logger', 'trx;LogFileName=passive-suspension.trx', '--results-directory', $relativeRun)
$readyFile = Join-Path $run 'ready.json'
$stdout = Join-Path $run 'output.log'
$stderr = Join-Path $run 'error.log'
$launchUtc = [DateTimeOffset]::UtcNow
# Valida o acesso ao log antes de aguardar uma intervenção física.
$null = Read-StandbyEvents $launchUtc.AddSeconds(-1) $launchUtc
Write-Host "Evidências locais em $run"
Write-Host 'Preparando sessão passiva de cinco minutos. Aguarde a confirmação.'
$test = Start-Process -FilePath $dotnet -ArgumentList $arguments -Environment $environment -WorkingDirectory $repo `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
try {
    while (!$test.HasExited -and !(Test-Path -LiteralPath $readyFile)) { Start-Sleep -Milliseconds 200 }
    $ready = if (Test-Path -LiteralPath $readyFile) { Get-Content -LiteralPath $readyFile -Raw | ConvertFrom-Json }
    if (!$test.HasExited -and $ready) {
        $deadline = ([DateTimeOffset]$ready.DeadlineUtc).ToLocalTime()
        switch ($Expected) {
            'Awake' { Write-Host 'Sessão pronta. Mantenha o computador acordado até a conclusão.' }
            'BeforeDeadline' {
                $suspend = $deadline.AddSeconds(-75).ToString('HH:mm:ss')
                $resume = $deadline.AddSeconds(-35).ToString('HH:mm:ss')
                Write-Host "Sessão pronta. Use Suspender às $suspend e retome às $resume (horário local)."
            }
            'AfterDeadline' {
                $suspend = $deadline.AddSeconds(-30).ToString('HH:mm:ss')
                $resume = $deadline.AddSeconds(12).ToString('HH:mm:ss')
                Write-Host "Sessão pronta. Use Suspender às $suspend e retome às $resume (horário local)."
            }
        }
    }
    $test.WaitForExit()
    Get-Content -LiteralPath $stdout | Write-Host
    $collectionFile = Join-Path $run 'collection.json'
    $collection = if (Test-Path -LiteralPath $collectionFile) { Get-Content -LiteralPath $collectionFile -Raw | ConvertFrom-Json }
    $from = if ($ready) { [DateTimeOffset]$ready.StartedAt } else { $launchUtc }
    $until = if ($collection) { ([DateTimeOffset]$collection.EndedAt).AddSeconds(2) } else { [DateTimeOffset]::UtcNow }
    $events = @(Read-StandbyEvents $from $until)
    ConvertTo-Json -InputObject $events -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'windows-events.json')
    $trx = Join-Path $run 'passive-suspension.trx'
    $passedTests = if (Test-Path -LiteralPath $trx) { ([xml](Get-Content -LiteralPath $trx -Raw)).TestRun.ResultSummary.Counters.passed }
    if ($test.ExitCode -ne 0 -or $passedTests -ne '1' -or !$ready -or !$collection -or $collection.StressExecuted -ne $false) {
        throw 'A coleta não produziu evidência aprovada. Confira o TRX, o banco local e os eventos.'
    }
    if ($collection.SessionSeconds -lt (([DateTimeOffset]$ready.DeadlineUtc) - ([DateTimeOffset]$ready.StartedAt)).TotalSeconds) {
        throw 'A sessão terminou antes da duração planejada.'
    }

    $observed = Compare-StandbyCollection $events $ready $collection
    $result = [pscustomobject]@{ Expected = $Expected; Observed = $observed.Observed; Passed = $Expected -eq $observed.Observed;
        SleepSeconds = $observed.SleepSeconds; RamGap = $observed.RamGap; Collection = $collection; StressExecuted = $false }
    $result | ConvertTo-Json -Depth 8 | Tee-Object -FilePath (Join-Path $run 'result.json')
    if (!$result.Passed) { throw 'Os eventos e a coleta não confirmaram o cenário esperado.' }
}
finally { $test.Dispose() }
