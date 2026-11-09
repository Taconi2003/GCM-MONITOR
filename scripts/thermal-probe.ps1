param([switch]$Elevated, [switch]$BuildOnly, [switch]$PrepareBridge, [switch]$PackageStatus)
$ErrorActionPreference = 'Stop'
if (([int]$Elevated.IsPresent + [int]$BuildOnly.IsPresent + [int]$PrepareBridge.IsPresent) -gt 1) {
    throw 'Escolha somente um modo: Elevated, BuildOnly ou PrepareBridge.'
}
if ($PackageStatus -and ($BuildOnly -or $PrepareBridge)) {
    throw 'PackageStatus pertence à captura avulsa de laboratório.'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
$archive = Join-Path $projectRoot '.tools/sensors/lhm-0.9.6/LibreHardwareMonitor.NET.10.zip'
$expectedArchive = '29739C4959B01B348FDDAD87664066634BCFD4F46E9BF41E4E916C318BCFDB99'
$expectedLibrary = '984B539048880D37257A5EDEAB36378152C54943069F400D68A34565292D35DA'
if ($Elevated -or $PrepareBridge) {
    $installed = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO'
    $service = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\PawnIO'
    $driverPath = $service.ImagePath.Replace('\SystemRoot', $env:SystemRoot)
    if ($installed.DisplayVersion -ne '2.2.0.0' -or
        (Get-FileHash -LiteralPath $driverPath -Algorithm SHA256).Hash -ne 'FCA6E7D58B0CF38DBB913A2B9E532F48629145D395F454B16A9F58E97B8D3940' -or
        (Get-AuthenticodeSignature -LiteralPath $driverPath).Status -ne 'Valid') {
        throw 'Versão/assinatura do PawnIO diferente da prova revisada. Não será iniciado um processo elevado.'
    }
}
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedArchive) {
    throw 'Arquivo LHM 0.9.6 .NET 10 ausente ou divergente. Consulte docs/testes/VALIDACAO_TERMICA_V0.4.md. Nada instalado.'
}
# Extração nova a cada build: não reutiliza DLLs potencialmente modificadas de outra execução.
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
$review = Join-Path $projectRoot ('.tools/sensors/verified-' + $runId)
[System.IO.Directory]::CreateDirectory($review) | Out-Null
$reviewPrefix = [System.IO.Path]::GetFullPath($review) + [System.IO.Path]::DirectorySeparatorChar
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($entry in $zip.Entries) {
        $target = [System.IO.Path]::GetFullPath((Join-Path $review $entry.FullName))
        if (!$target.StartsWith($reviewPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Caminho inválido no arquivo.' }
    }
}
finally { $zip.Dispose() }
Expand-Archive -LiteralPath $archive -DestinationPath $review
if ((Get-FileHash -LiteralPath (Join-Path $review 'LibreHardwareMonitorLib.dll') -Algorithm SHA256).Hash -ne $expectedLibrary) {
    throw 'Biblioteca extraída divergente; prova cancelada.'
}
$dotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $projectRoot 'tools/GcmMonitor.SensorProbe/GcmMonitor.SensorProbe.csproj'
& $dotnet build $project -c Release "-p:SensorLibraryDirectory=$review" --nologo
if ($LASTEXITCODE -ne 0) { throw 'A compilação da prova passiva falhou.' }
$probe = Join-Path $projectRoot 'tools/GcmMonitor.SensorProbe/bin/Release/net10.0-windows/GcmMonitor.SensorProbe.dll'
if ((Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $probe) 'LibreHardwareMonitorLib.dll') -Algorithm SHA256).Hash -ne $expectedLibrary) {
    throw 'Biblioteca de saída divergente; execução cancelada.'
}
if ($BuildOnly) { return }
if ($PrepareBridge) {
    $destination = Join-Path $projectRoot 'src/GcmMonitor.App/bin/Release/net10.0-windows/engine/thermal'
    if (!(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $destination) 'GcmMonitor.Engine.exe'))) {
        throw 'Compile a solução Release antes de preparar o auxiliar.'
    }
    [System.IO.Directory]::CreateDirectory($destination) | Out-Null
    $files = @((Get-ChildItem -LiteralPath $review -File -Filter '*.dll').Name) + @(
        'GcmMonitor.SensorProbe.dll','GcmMonitor.SensorProbe.deps.json','GcmMonitor.SensorProbe.runtimeconfig.json',
        'GcmMonitor.Core.dll','GcmMonitor.Execution.dll')
    foreach ($file in ($files | Select-Object -Unique)) {
        Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $probe) $file) -Destination (Join-Path $destination $file) -Force
    }
    Write-Output 'Auxiliar passivo preparado. Ao iniciar uma sessão com Sensores, o GCM oferecerá a leitura privilegiada opcional. Nenhuma carga será executada.'
    return
}
$output = Join-Path $projectRoot ('.artifacts/thermal-validation/' + $runId)
[System.IO.Directory]::CreateDirectory($output) | Out-Null
$extraArguments = @(if ($PackageStatus) { '--package-status' })
if ($Elevated) {
    # O usuário responde ao UAC normal. Não altera ACL do driver nem eleva o aplicativo principal.
    $probeArguments = @(('"' + $probe + '"'), '--output', ('"' + $output + '"')) + $extraArguments
    $process = Start-Process -FilePath $dotnet -ArgumentList $probeArguments -Verb RunAs -WindowStyle Hidden -PassThru
    Write-Output "Prova passiva iniciada (processo $($process.Id)). Resultado local em $output"
}
else {
    & $dotnet $probe --output $output @extraArguments
    if ($LASTEXITCODE -ne 0) { throw 'Prova falhou. Consulte o registro privado no diretório de saída.' }
    Get-Content -LiteralPath (Join-Path $output 'summary.json') -Raw
}
}
finally { Pop-Location }
