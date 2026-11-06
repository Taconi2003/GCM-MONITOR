param([ValidateSet('Build','Test','Run','Restore')][string]$Action = 'Build', [string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\home'
    $env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
    $dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
    $env:DOTNET_ROOT = if (Test-Path -LiteralPath $localDotnet) { Split-Path -Parent $localDotnet } else { Split-Path -Parent (Get-Command dotnet).Source }
    switch ($Action) {
        'Restore' { & $dotnetCommand restore GcmMonitor.sln --locked-mode }
        'Build' { & $dotnetCommand build GcmMonitor.sln -c $Configuration --no-restore }
        'Test' { & $dotnetCommand test GcmMonitor.sln -c $Configuration --no-build --no-restore --logger 'trx' --results-directory '.artifacts\TestResults' }
        'Run' { & $dotnetCommand run --project src/GcmMonitor.App -c $Configuration --no-build --no-restore }
    }
    if ($LASTEXITCODE -ne 0) { throw "A operação $Action falhou (código $LASTEXITCODE)." }
}
finally { Pop-Location }
