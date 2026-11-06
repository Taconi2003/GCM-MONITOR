# Dependências da v0.1

SDK de validação: .NET 10.0.403 x64, isolado no workspace. Ferramentas locais não são versionadas.
Versões diretas em `Directory.Packages.props`; resolução exata e hashes nos `packages.lock.json`.
O restore de desenvolvimento e CI usa `--locked-mode`.

| Componente | Versão | Origem/licença declarada |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | [CommunityToolkit](https://github.com/CommunityToolkit/dotnet), MIT |
| Microsoft.Extensions.DependencyInjection / Abstractions | 10.0.0 | [dotnet/runtime](https://github.com/dotnet/runtime), MIT |
| Microsoft.Data.Sqlite / Core | 10.0.0 | [dotnet/efcore](https://github.com/dotnet/efcore), MIT |
| SQLitePCLRaw.bundle/config/core/provider_e_sqlite3 | 3.0.5 | [SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw), Apache-2.0 |
| SQLite (binário nativo `e_sqlite3`) | 3.53.4 | [Pacote SQLite](https://www.nuget.org/packages/SQLite/3.53.4), arquivo LICENSE: Public Domain |
| Microsoft.NET.Test.Sdk | 17.14.1 | [microsoft/vstest](https://github.com/microsoft/vstest), MIT |
| xunit | 2.9.3 | [xunit](https://github.com/xunit/xunit), Apache-2.0 |
| xunit.runner.visualstudio | 3.1.4 | [visualstudio.xunit](https://github.com/xunit/visualstudio.xunit), Apache-2.0 |

Metadados de licença conferidos nos pacotes restaurados; não substituem revisão final da distribuição.
Transitivos de testes (incluindo TestPlatform, CodeCoverage e Newtonsoft.Json) constam dos lockfiles.
Dependências de testes não integram o aplicativo distribuído.

## Correção aplicada durante esta entrega

O restore inicial de Microsoft.Data.Sqlite 10.0.0 resolveu o bundle nativo antigo 2.1.11 e
falhou com NU1903: [GHSA-2m69-gcr7-jv3q / CVE-2025-6965](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).
Foi incluída referência explícita a SQLitePCLRaw.bundle_e_sqlite3 **3.0.5**, cuja resolução
traz SQLite **3.53.4**. Não foi desativada a auditoria nem suprimido o aviso.
Um teste de integração consulta `sqlite_version()` na biblioteca efetivamente carregada e exige pelo menos 3.50.2.

## Atualização e distribuição futura

Atualizar versões de forma deliberada, restaurar lockfiles, revisar advisories/licenças e repetir build/testes.
Antes de distribuir, incluir os avisos/licenças necessários, revisar dependências nativas e arquitetura,
executar a matriz Windows e assinar os artefatos conforme planejamento. NuGet audit é uma verificação
pontual de advisories conhecidos, não uma garantia de ausência de vulnerabilidades.

ScottPlot.WPF, LibreHardwareMonitorLib, System.Management, APIs de eventos e PDFsharp/MigraDoc
continuam candidatos aprovados, ainda sem versões congeladas ou prova de conceito nesta etapa.
