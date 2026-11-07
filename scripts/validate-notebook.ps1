param(
    [ValidateSet(5,30,60,240)][int[]]$Minutes = @(5,30,60,240),
    [string]$PublishedEngine
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repo
try {
    $dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
    if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_CLI_HOME = Join-Path $repo '.tools/home'
    $env:NUGET_PACKAGES = Join-Path $repo '.tools/nuget'
    $env:GCM_RUN_LONG_PASSIVE_VALIDATION = '1'
    $env:GCM_PASSIVE_VALIDATION_ENGINE = $null
    if ($PublishedEngine) {
        $engine = (Resolve-Path -LiteralPath $PublishedEngine).Path
        if ((Split-Path -Leaf $engine) -ne 'GcmMonitor.Engine.exe' -or
            !(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $engine) 'coreclr.dll') -PathType Leaf)) {
            throw 'Informe o motor self-contained gerado por scripts/package.ps1.'
        }
        $env:GCM_PASSIVE_VALIDATION_ENGINE = $engine
    }

    $run = Join-Path $repo ('.artifacts/validation/notebook-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($run) | Out-Null
    $env:GCM_PASSIVE_VALIDATION_OUTPUT = $run
    Write-Output "Evidências em $run"
    foreach ($duration in ($Minutes | Select-Object -Unique)) {
        $env:GCM_PASSIVE_VALIDATION_MINUTES = "$duration"
        Write-Output "Iniciando coleta passiva de $duration min."
        $trx = "passive-${duration}min.trx"
        & $dotnet test tests/GcmMonitor.IntegrationTests -c Release --no-build --no-restore `
            --filter 'FullyQualifiedName~FullTimedPassiveSessionCompletesAndKeepsBoundedUiSnapshots' `
            --logger "trx;LogFileName=$trx" --results-directory $run
        if ($LASTEXITCODE -ne 0) { throw "Ensaio de $duration min falhou. Confira as evidências da rodada." }
        $result = [xml](Get-Content -LiteralPath (Join-Path $run $trx) -Raw)
        if ($result.TestRun.ResultSummary.Counters.passed -ne '1' -or
            !(Test-Path -LiteralPath (Join-Path $run "passive-${duration}min.json"))) {
            throw "O ensaio de $duration min não produziu um resultado aprovado."
        }
    }
}
finally {
    Remove-Item Env:GCM_RUN_LONG_PASSIVE_VALIDATION, Env:GCM_PASSIVE_VALIDATION_MINUTES, Env:GCM_PASSIVE_VALIDATION_ENGINE, Env:GCM_PASSIVE_VALIDATION_OUTPUT -ErrorAction SilentlyContinue
    Pop-Location
}
