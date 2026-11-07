param(
    [Parameter(Mandatory)][string]$PreviousPublish,
    [Parameter(Mandatory)][string]$UpdatedPublish,
    [string]$CompilerPath
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repo
try {
    $output = (Resolve-Path -LiteralPath '.artifacts/installer').Path
    $registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{204540CC-B3ED-4A10-8C50-B3FCB628CE57}_is1'
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'GCM Monitor Laboratorio.lnk'
    if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $shortcut)) {
        throw 'Já existe uma instalação de laboratório. Confira o registro e o atalho antes de repetir o ensaio.'
    }
    if (!$CompilerPath) { $CompilerPath = Join-Path $repo '.tools/inno-7.1.1/ISCC.exe' }
    $CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
    if ((& $CompilerPath --version) -ne '7.1.1') { throw 'Use o compilador Inno Setup 7.1.1 revisado.' }
    $lab = Join-Path $output ('upgrade-' + [Guid]::NewGuid().ToString('N'))
    $target = Join-Path $lab 'installed-custom'
    $uninstaller = Join-Path $target 'unins000.exe'
    $database = Join-Path $lab 'profile/Data/gcm-monitor.db'
    $preferences = Join-Path $lab 'profile/Settings/preferences.json'
    $report = Join-Path $lab 'relatorio-demonstrativo.pdf'
    $dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
    if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet).Source }
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_CLI_HOME = Join-Path $repo '.tools/home'
    $env:NUGET_PACKAGES = Join-Path $repo '.tools/nuget'
    $env:GCM_INSTALLER_LAB = $lab
    $common = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    function Invoke-LabExecutable([string]$Executable, [string]$Log, [string[]]$Arguments) {
        $arguments = $common + ('/LOG="' + (Join-Path $lab "$Log.log") + '"') + $Arguments
        (Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru).ExitCode
    }
    function Get-ProductVersion([string]$Assembly) {
        (Get-Item -LiteralPath $Assembly).VersionInfo.ProductVersion.Split('+')[0]
    }
    function New-LabPackage([string]$Publish, [string]$Label) {
        $source = (Resolve-Path -LiteralPath $Publish).Path
        if (!$source.StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase) -or
            (Split-Path -Leaf $source) -ne 'publish') { throw 'Use uma publicação self-contained gerada em .artifacts/installer.' }
        $version = Get-ProductVersion (Join-Path $source 'GcmMonitor.App.dll')
        if ($version -notmatch '^1\.0\.0-preview\.[0-9]+$' -or
            $version -ne (Get-ProductVersion (Join-Path $source 'engine/GcmMonitor.Engine.dll'))) {
            throw 'As versões publicadas da interface e do motor precisam ser da mesma prévia.'
        }
        $destination = Join-Path $lab $Label
        [IO.Directory]::CreateDirectory($destination) | Out-Null
        & $CompilerPath '--quiet-progress' "--output-dir=$destination" "--define=PublishRoot=$source" `
            "--define=ProductVersion=$version" "--define=LabRoot=$lab" 'installer/InnoSetup/GCMMonitor.iss' | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Compilação do pacote de laboratório falhou ($Label)." }
        [pscustomobject]@{ Path = Join-Path $destination 'GCMMonitor-LabSetup.exe'; Version = $version }
    }
    function Assert-Installed([string]$Version) {
        $info = Get-ItemProperty -LiteralPath $registry
        if ($info.DisplayVersion -ne $Version -or $info.InstallLocation.TrimEnd('\') -ne $target -or
            !(Test-Path -LiteralPath $shortcut) -or !(Test-Path -LiteralPath $uninstaller) -or
            (Get-ProductVersion (Join-Path $target 'GcmMonitor.App.dll')) -ne $Version -or
            (Get-ProductVersion (Join-Path $target 'engine/GcmMonitor.Engine.dll')) -ne $Version) {
            throw "Identidade, diretório, atalho ou binários divergentes para $Version."
        }
    }
    function Assert-Uninstalled {
        if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $shortcut) -or
            (Test-Path -LiteralPath (Join-Path $target 'GcmMonitor.App.exe')) -or
            (Test-Path -LiteralPath (Join-Path $target 'engine/GcmMonitor.Engine.exe'))) {
            throw 'A instalação de laboratório deixou arquivos ou registros ativos.'
        }
    }
    function Invoke-InstalledTest([string]$Name, [string]$Version) {
        $env:GCM_VALIDATE_INSTALLED_ENGINE = Join-Path $target 'engine/GcmMonitor.Engine.exe'
        $env:GCM_INSTALLER_ENGINE_VERSION = $Version
        & $dotnet test tests/GcmMonitor.IntegrationTests -c Release --no-build --no-restore `
            --filter "FullyQualifiedName~$Name" --logger "trx;LogFileName=$Name.trx" --results-directory $lab
        if ($LASTEXITCODE -ne 0) { throw "O ensaio com o motor instalado falhou ($Name)." }
        $result = [xml](Get-Content -LiteralPath (Join-Path $lab "$Name.trx") -Raw)
        if ($result.TestRun.ResultSummary.Counters.passed -ne '1') { throw "O teste esperado não foi aprovado ($Name)." }
    }
    function Get-DataHashes {
        (Get-FileHash -LiteralPath @($database, $preferences, $report)).Hash -join '|'
    }

    $previous = New-LabPackage $PreviousPublish 'previous'
    $updated = New-LabPackage $UpdatedPublish 'updated'
    if ([int]($previous.Version.Split('.')[-1]) -ge [int]($updated.Version.Split('.')[-1])) {
        throw 'A versão de destino deve ser mais recente que a versão de origem.'
    }
    $installArgs = @('/SP-', ('/DIR="' + $target + '"'))
    $junction = Join-Path $lab 'profile/Logs'
    $external = Join-Path $lab 'external'
    try {
        if ((Invoke-LabExecutable $previous.Path 'setup-previous' $installArgs) -ne 0) { throw 'Instalação de origem falhou.' }
        Assert-Installed $previous.Version
        Invoke-InstalledTest 'SeedHistoryUsingPreviousInstalledEngine' $previous.Version
        $before = Get-DataHashes

        $marker = [Threading.Mutex]::new($false, 'Local\GcmMonitor.InUse.Laboratorio')
        try {
            if ((Invoke-LabExecutable $updated.Path 'update-busy' @('/SP-')) -eq 0 -or
                (Invoke-LabExecutable $uninstaller 'uninstall-busy' @()) -eq 0) {
                throw 'O marcador de uso não bloqueou a atualização ou a desinstalação.'
            }
            Assert-Installed $previous.Version
        }
        finally { $marker.Dispose() }

        # Sem /DIR, o Inno deve reutilizar o diretório personalizado da instalação anterior.
        if ((Invoke-LabExecutable $updated.Path 'setup-update' @('/SP-')) -ne 0) { throw 'Atualização de laboratório falhou.' }
        Assert-Installed $updated.Version
        if ((Get-DataHashes) -ne $before) { throw 'A atualização alterou o banco, as preferências ou o PDF.' }
        Invoke-InstalledTest 'ReopenHistoryAndRunUpdatedInstalledEngine' $updated.Version
        Invoke-InstalledTest 'InstalledEngineUsesBundledRuntime' $updated.Version

        $before = Get-DataHashes
        if ((Invoke-LabExecutable $uninstaller 'uninstall-keep' @()) -ne 0) { throw 'Desinstalação com preservação falhou.' }
        Assert-Uninstalled
        if ((Get-DataHashes) -ne $before) { throw 'A desinstalação alterou dados que deveriam ser preservados.' }

        if ((Invoke-LabExecutable $updated.Path 'setup-for-cleanup' $installArgs) -ne 0) { throw 'Reinstalação de laboratório falhou.' }
        Assert-Installed $updated.Version
        if ((Get-DataHashes) -ne $before) { throw 'A reinstalação alterou os dados preservados.' }
        $managedFiles = @($database, "$database-wal", "$database-shm", "$database-journal", $preferences,
            ($preferences + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'))
        foreach ($file in $managedFiles) {
            if (!(Test-Path -LiteralPath $file)) { Set-Content -LiteralPath $file -Value 'Arquivo sintético de laboratório' }
        }
        $foreignFile = $preferences + '.' + ('z' * 32) + '.tmp'
        Set-Content -LiteralPath $foreignFile -Value 'Arquivo de terceiros sintético'
        $foreignHash = (Get-FileHash -LiteralPath $foreignFile).Hash
        $pdfHash = (Get-FileHash -LiteralPath $report).Hash
        [IO.Directory]::CreateDirectory($external) | Out-Null
        $externalFile = Join-Path $external 'preservar.txt'
        Set-Content -LiteralPath $externalFile -Value 'Destino sintético fora dos dados gerenciados'
        $externalHash = (Get-FileHash -LiteralPath $externalFile).Hash
        New-Item -ItemType Junction -Path $junction -Target $external | Out-Null
        if ((Invoke-LabExecutable $uninstaller 'uninstall-cleanup' @('/LabRemoveData')) -ne 0) { throw 'Desinstalação com limpeza falhou.' }
        Assert-Uninstalled
        foreach ($file in $managedFiles) {
            if (Test-Path -LiteralPath $file) { throw "Arquivo gerenciado permaneceu após a limpeza: $(Split-Path -Leaf $file)" }
        }
        if ((Get-FileHash -LiteralPath $report).Hash -ne $pdfHash -or
            (Get-FileHash -LiteralPath $foreignFile).Hash -ne $foreignHash -or
            (Get-FileHash -LiteralPath $externalFile).Hash -ne $externalHash -or
            (Get-Item -LiteralPath $junction -Force).LinkType -ne 'Junction') {
            throw 'A limpeza afetou o PDF externo, um arquivo desconhecido ou a junção.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $junction) {
            $link = Get-Item -LiteralPath $junction -Force
            if ($link.LinkType -eq 'Junction' -and $link.FullName.StartsWith($lab + '\', [StringComparison]::OrdinalIgnoreCase)) {
                [IO.Directory]::Delete($link.FullName, $false)
            }
        }
        $info = Get-ItemProperty -LiteralPath $registry -ErrorAction SilentlyContinue
        if ($null -ne $info -and $info.InstallLocation.TrimEnd('\') -eq $target -and (Test-Path -LiteralPath $uninstaller)) {
            $exitCode = Invoke-LabExecutable $uninstaller 'uninstall-recovery' @()
            if ($exitCode -ne 0) { Write-Warning "A desinstalação de recuperação falhou ($exitCode). Confira $lab." }
        }
    }
    $summary = [pscustomobject]@{
        PreviousVersion=$previous.Version; UpdatedVersion=$updated.Version; BusyOperationsRefused=$true
        PreviousInstallDirectoryReused=$true; HistoryAndPreferencesReopened=$true; BundledEnginePassed=$true
        PreserveModePassed=$true; ManagedDataRemoved=$true; ExternalPdfAndUnknownFilePreserved=$true
        JunctionPreserved=$true; StressExecuted=$false
    }
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $lab 'summary.json')
    $summary | ConvertTo-Json
    Write-Output "Evidências: $lab"
}
finally {
    Remove-Item Env:GCM_INSTALLER_LAB, Env:GCM_VALIDATE_INSTALLED_ENGINE, Env:GCM_INSTALLER_ENGINE_VERSION -ErrorAction SilentlyContinue
    Pop-Location
}
