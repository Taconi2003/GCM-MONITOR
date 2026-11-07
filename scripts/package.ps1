param([string]$CompilerPath, [switch]$PublishOnly, [switch]$LabUi)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repo
try {
    $dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
    if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_CLI_HOME = Join-Path $repo '.tools/home'
    $env:NUGET_PACKAGES = Join-Path $repo '.tools/nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $version = ([xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)).Project.PropertyGroup.Version
    $runtime = '10.0.14'
    $prefix = if ($LabUi) { 'lab-ui-' } else { '' }
    $run = Join-Path $repo ('.artifacts/installer/' + $prefix + $version + '-' + [Guid]::NewGuid().ToString('N'))
    $stage = Join-Path $run 'publish'
    $engine = Join-Path $stage 'engine'
    $appExecutable = if ($LabUi) { 'GcmMonitor.Lab.App.exe' } else { 'GcmMonitor.App.exe' }
    foreach ($item in @(@('src/GcmMonitor.App/GcmMonitor.App.csproj', $stage), @('src/GcmMonitor.Engine/GcmMonitor.Engine.csproj', $engine))) {
        $properties = @(if ($LabUi -and $item[1] -eq $stage) { '-p:GcmLabUi=true' })
        & $dotnet publish $item[0] -c Release -r win-x64 --self-contained true '-p:RestoreLockedMode=true' "-p:RuntimeFrameworkVersion=$runtime" @properties -o $item[1]
        if ($LASTEXITCODE -ne 0) { throw 'Publicação self-contained falhou.' }
    }
    foreach ($required in @($appExecutable,'coreclr.dll','engine/GcmMonitor.Engine.exe','engine/coreclr.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $stage $required) -PathType Leaf)) { throw "Publicação incompleta ($required)." }
    }
    $otherApp = if ($LabUi) { 'GcmMonitor.App.exe' } else { 'GcmMonitor.Lab.App.exe' }
    if (Test-Path -LiteralPath (Join-Path $stage $otherApp)) { throw 'Publicação contém uma interface de outra variante.' }
    if (Test-Path -LiteralPath (Join-Path $engine 'thermal')) { throw 'Auxiliar de laboratório presente na publicação.' }
    $licenses = Join-Path $stage 'Licencas'
    [System.IO.Directory]::CreateDirectory($licenses) | Out-Null
    Copy-Item -LiteralPath 'docs/DEPENDENCIAS.md' -Destination $licenses
    foreach ($notice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
        $source = Join-Path $env:DOTNET_ROOT $notice
        if (!(Test-Path -LiteralPath $source)) { throw 'Avisos do runtime .NET não encontrados.' }
        Copy-Item -LiteralPath $source -Destination (Join-Path $licenses ('.NET-' + $notice))
    }
    if (!$PublishOnly) {
        if (!$CompilerPath) { $CompilerPath = Join-Path $repo '.tools/inno-7.1.1/ISCC.exe' }
        $CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
        if ((& $CompilerPath --version) -ne '7.1.1') { throw 'Use o compilador Inno Setup 7.1.1 revisado.' }
        $definitions = @(if ($LabUi) { "--define=LabRoot=$run"; '--define=LabUi=1' })
        & $CompilerPath '--quiet-progress' "--output-dir=$run" "--define=PublishRoot=$stage" "--define=ProductVersion=$version" @definitions 'installer/InnoSetup/GCMMonitor.iss'
        if ($LASTEXITCODE -ne 0) { throw 'Compilação Inno Setup falhou.' }
        $setup = if ($LabUi) { 'GCMMonitor-LabSetup.exe' } else { 'GCMMonitor-Setup.exe' }
        Get-FileHash -LiteralPath (Join-Path $run $setup) -Algorithm SHA256
    }
    Write-Output "Saída de prévia: $run"
}
finally { Pop-Location }
