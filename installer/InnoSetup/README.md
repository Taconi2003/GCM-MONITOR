# Instalador Windows

O script `GCMMonitor.iss` empacota a interface e o motor self-contained do .NET 10.0.14 x64.
O destino padrão por usuário é `%LOCALAPPDATA%\Programs\GCM Monitor`. O instalador cria um
atalho no menu Iniciar e um registro em Aplicativos instalados. A configuração exige Windows x64
com build a partir de 19045. A matriz de compatibilidade continua pendente.

Use o [Inno Setup 7.1.1 oficial](https://github.com/jrsoftware/issrc/releases/tag/is-7_1_1).
O compilador local pode ficar em `.tools/inno-7.1.1/ISCC.exe`. Confirme a assinatura do fornecedor
e o hash informado na release antes da instalação. Para reproduzir o pacote:

```powershell
.\scripts\package.ps1
# Para usar o compilador de outra pasta
.\scripts\package.ps1 -CompilerPath 'C:\caminho\ISCC.exe'
```

A saída fica em `.artifacts/installer/<versão>-<id>/GCMMonitor-Setup.exe`. O script publica com
restauração travada, valida os binários e inclui os avisos do runtime. `-PublishOnly` verifica a
publicação sem compilar o instalador. A prévia gera um executável local sem assinatura do produto.

Para atualizar manualmente, encerre o GCM Monitor e execute o novo instalador no mesmo perfil
Windows. Mantenha o diretório da instalação anterior. O instalador reaproveita a identidade do
produto e o diretório anterior. O banco e as preferências ficam em outro diretório do perfil.

O App e o motor mantêm um marcador de uso por perfil durante sua execução. O Inno Setup pede
encerramento antes de instalar ou remover os arquivos. Fechar o GCM durante uma sessão usa o fluxo
de cancelamento e preservação dos dados parciais. Na desinstalação interativa, o usuário escolhe
preservar ou remover dados locais. A limpeza reconhece o banco, seus arquivos auxiliares, as
preferências e os temporários nomeados pelo GCM. Ela valida os diretórios e preserva arquivos
desconhecidos, junções e PDFs externos. A desinstalação silenciosa do produto preserva dados.

Para o ensaio em perfil sem instalação ou atalho anterior:

```powershell
.\scripts\dev.ps1 Build -Configuration Release
.\scripts\test-installer.ps1 -InstallerPath '.artifacts/installer/<pasta>/GCMMonitor-Setup.exe'
```

O teste usa uma pasta isolada no workspace, registra a instalação por usuário, executa o motor e
remove a instalação de ensaio. Veja [resultados e pendências](../../docs/testes/V1.0-PREVIEW.3.md).

Para ensaiar uma atualização mesmo com o GCM já instalado, use duas publicações self-contained
geradas por `package.ps1`, uma da prévia de origem e outra da prévia de destino:

```powershell
.\scripts\dev.ps1 Build -Configuration Release
.\scripts\test-installer-upgrade.ps1 `
    -PreviousPublish '.artifacts/installer/<origem>/publish' `
    -UpdatedPublish '.artifacts/installer/<destino>/publish'
```

O ensaio compila pacotes com identidade e pasta de dados próprias. Ele abre somente o motor
instalado e grava as evidências em `.artifacts/installer/upgrade-<id>`. Os executáveis chamados
`GCMMonitor-LabSetup.exe` pertencem ao ensaio. Distribua somente `GCMMonitor-Setup.exe`.
Veja os [resultados da prévia 4](../../docs/testes/V1.0-PREVIEW.4.md).

Para conferir a primeira abertura e as confirmações do desinstalador, gere o pacote de
laboratório com `./scripts/package.ps1 -LabUi`. Use um perfil sem outra instalação
**GCM Monitor Laboratorio**. Instale `GCMMonitor-LabSetup.exe` no destino sugerido e abra o
atalho **GCM Monitor Laboratorio**. O executável da interface usa a pasta `profile` ao lado
do diretório instalado e um marcador de uso separado. Desinstale pelo registro ou executável
da instalação de laboratório. Use dados sintéticos para testar a opção de remover o histórico.
