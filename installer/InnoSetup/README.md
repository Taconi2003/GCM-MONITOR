# Instalador Windows da prévia 3

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

O App e o motor mantêm um marcador de uso por perfil durante sua execução. O Inno Setup pede
encerramento antes de instalar ou remover os arquivos. Fechar o GCM durante uma sessão usa o fluxo
de cancelamento e preservação dos dados parciais. Na desinstalação interativa, o usuário escolhe
preservar ou remover dados locais. A limpeza trabalha com nomes fixos de banco e preferências,
valida os diretórios e preserva arquivos desconhecidos. A desinstalação silenciosa preserva dados.

Para o ensaio em perfil sem instalação ou atalho anterior:

```powershell
.\scripts\dev.ps1 Build -Configuration Release
.\scripts\test-installer.ps1 -InstallerPath '.artifacts/installer/<pasta>/GCMMonitor-Setup.exe'
```

O teste usa uma pasta isolada no workspace, registra a instalação por usuário, executa o motor e
remove a instalação de ensaio. Veja [resultados e pendências](../../docs/testes/V1.0-PREVIEW.3.md).
