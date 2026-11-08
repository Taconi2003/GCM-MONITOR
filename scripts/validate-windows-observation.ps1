param(
    [Parameter(Mandatory)]
    [ValidateSet('PowerChanged', 'Suspended', 'DurationCompleted')]
    [string]$Expected
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$relativeRun = '.artifacts/validation/windows-observation-' + [Guid]::NewGuid().ToString('N')
$run = Join-Path $repo $relativeRun
[IO.Directory]::CreateDirectory($run) | Out-Null
$dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
$environment = @{
    DOTNET_ROOT = Split-Path -Parent $dotnet
    DOTNET_CLI_HOME = Join-Path $repo '.tools/home'
    NUGET_PACKAGES = Join-Path $repo '.tools/nuget'
    GCM_WINDOWS_SIGNAL_EXPECTED = $Expected
    GCM_WINDOWS_SIGNAL_OUTPUT = $run
}
$arguments = @('test', 'tests/GcmMonitor.IntegrationTests', '-c', 'Release', '--no-build', '--no-restore',
    '--filter', 'FullyQualifiedName~RequestedWindowsEventStopsOnlyTheOwnedIdleWorker',
    '--logger', 'trx;LogFileName=windows-observation.trx', '--results-directory', $relativeRun)
$stdout = Join-Path $run 'output.log'
$stderr = Join-Path $run 'error.log'
$marker = Join-Path $run 'ready'
Write-Host 'Preparando a prova ociosa. Aguarde a confirmação antes de interagir com o notebook.'
$test = Start-Process -FilePath $dotnet -ArgumentList $arguments -Environment $environment -WorkingDirectory $repo `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
try {
    while (!$test.HasExited -and !(Test-Path -LiteralPath $marker)) { Start-Sleep -Milliseconds 150 }
    if (!$test.HasExited -and (Test-Path -LiteralPath $marker)) {
        switch ($Expected) {
            'PowerChanged' { Write-Host 'Prova pronta. Troque agora a alimentação entre tomada e bateria.' }
            'Suspended' { Write-Host 'Prova pronta. Use Suspender no menu do Windows e depois retome a sessão.' }
            'DurationCompleted' { Write-Host 'Prova pronta. Aguarde o término em estado estável.' }
        }
    }
    $test.WaitForExit()
    Get-Content -LiteralPath $stdout | Write-Host
    Write-Host "Evidências locais em $run"
    $result = Join-Path $run 'result.json'
    if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result -Raw }
    if ($test.ExitCode -ne 0 -or !(Test-Path -LiteralPath $result)) { throw 'A prova não confirmou o resultado esperado.' }
    $evidence = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if ($evidence.Passed -ne $true -or $evidence.LoadExecuted -ne $false) { throw 'A evidência não confirma a prova ociosa.' }
}
finally { $test.Dispose() }
