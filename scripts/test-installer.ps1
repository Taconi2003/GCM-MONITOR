param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repo
try {
    $output = (Resolve-Path -LiteralPath '.artifacts/installer').Path
    $installer = (Resolve-Path -LiteralPath $InstallerPath).Path
    if (!$installer.StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $installer) -ne 'GCMMonitor-Setup.exe') { throw 'Use o instalador gerado no workspace.' }
    $registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{EF081865-9263-4D99-AF40-279AE0F9B55F}_is1'
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'GCM Monitor.lnk'
    if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $shortcut) -or
        (Get-Process -Name GcmMonitor.App,GcmMonitor.Engine -ErrorAction SilentlyContinue)) {
        throw 'Já há instalação, atalho ou processo do GCM neste perfil. Use um perfil de laboratório vazio.'
    }
    $lab = Join-Path $output ('smoke-' + [Guid]::NewGuid().ToString('N'))
    $target = Join-Path $lab 'installed'
    [System.IO.Directory]::CreateDirectory($lab) | Out-Null
    $sentinel = Join-Path $lab 'relatorio-externo.pdf'
    Set-Content -LiteralPath $sentinel -Value 'Arquivo externo sintético'
    $sentinelHash = (Get-FileHash -LiteralPath $sentinel).Hash
    $common = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    $setupArgs = $common + @('/SP-', ('/DIR="' + $target + '"'))
    function Invoke-OwnExecutable([string]$Executable, [string[]]$Arguments) {
        (Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru).ExitCode
    }
    $profileRoot = [Environment]::GetFolderPath('LocalApplicationData').TrimEnd('\')
    $identity = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::Unicode.GetBytes($profileRoot)))
    $marker = [Threading.Mutex]::new($false, 'Global\GcmMonitor.InUse.' + $identity)
    try {
        $blocked = Invoke-OwnExecutable $installer ($setupArgs + ('/LOG="' + (Join-Path $lab 'busy.log') + '"'))
        if ($blocked -eq 0 -or (Test-Path -LiteralPath $registry)) { throw 'Instalação em uso não foi recusada.' }
    }
    finally { $marker.Dispose() }
    $installed = Invoke-OwnExecutable $installer ($setupArgs + ('/LOG="' + (Join-Path $lab 'setup.log') + '"'))
    if ($installed -ne 0) { throw "Instalação isolada falhou ($installed)." }
    $info = Get-ItemProperty -LiteralPath $registry
    if ($info.InstallLocation.TrimEnd('\') -ne $target -or !(Test-Path -LiteralPath $shortcut)) { throw 'Registro ou atalho divergente.' }
    $uninstaller = Join-Path $target 'unins000.exe'
    if (!(Test-Path -LiteralPath $uninstaller -PathType Leaf)) { throw 'Desinstalador do ensaio ausente.' }
    try {
        foreach ($file in @('GcmMonitor.App.exe','coreclr.dll','engine/GcmMonitor.Engine.exe','engine/coreclr.dll')) {
            if (!(Test-Path -LiteralPath (Join-Path $target $file))) { throw "Arquivo instalado ausente ($file)." }
        }
        $dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
        if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
        $env:DOTNET_ROOT = Split-Path -Parent $dotnet
        $env:DOTNET_CLI_HOME = Join-Path $repo '.tools/home'
        $env:NUGET_PACKAGES = Join-Path $repo '.tools/nuget'
        $env:GCM_VALIDATE_INSTALLED_ENGINE = Join-Path $target 'engine/GcmMonitor.Engine.exe'
        & $dotnet test tests/GcmMonitor.IntegrationTests -c Release --no-build --no-restore `
            --filter 'FullyQualifiedName~InstalledEngineUsesBundledRuntime' --logger trx --results-directory '.artifacts/TestResults'
        if ($LASTEXITCODE -ne 0) { throw 'O motor instalado falhou no ensaio passivo.' }
    }
    finally {
        Remove-Item Env:GCM_VALIDATE_INSTALLED_ENGINE -ErrorAction SilentlyContinue
        $removed = Invoke-OwnExecutable $uninstaller ($common + ('/LOG="' + (Join-Path $lab 'uninstall.log') + '"'))
        if ($removed -ne 0) { throw "Desinstalação isolada falhou ($removed)." }
    }
    if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $shortcut) -or
        (Test-Path -LiteralPath (Join-Path $target 'GcmMonitor.App.exe')) -or (Get-FileHash -LiteralPath $sentinel).Hash -ne $sentinelHash) {
        throw 'O ensaio encontrou resíduos ou alteração no arquivo externo.'
    }
    $summary = [pscustomobject]@{ BusySetupRefused=$true; PerUserRegistry=$true; BundledEnginePassed=$true;
        PassiveSessionReopened=$true; UninstallPreserveMode=$true; ExternalFilePreserved=$true; StressExecuted=$false }
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $lab 'summary.json')
    $summary | ConvertTo-Json
}
finally { Pop-Location }
