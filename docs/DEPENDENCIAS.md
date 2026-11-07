# Dependências da v0.4

SDK de validação: .NET 10.0.403 x64, isolado no workspace. Ferramentas locais não são versionadas.
Versões diretas em `Directory.Packages.props`; resolução exata e hashes nos `packages.lock.json`.
O restore de desenvolvimento e CI usa `--locked-mode`.

| Componente | Versão | Origem/licença declarada |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | [CommunityToolkit](https://github.com/CommunityToolkit/dotnet), MIT |
| Microsoft.Extensions.DependencyInjection / Abstractions | 10.0.0 | [dotnet/runtime](https://github.com/dotnet/runtime), MIT |
| Microsoft.Data.Sqlite / Core | 10.0.0 | [dotnet/efcore](https://github.com/dotnet/efcore), MIT |
| System.Management | 10.0.0 | [dotnet/runtime](https://github.com/dotnet/runtime), MIT |
| SQLitePCLRaw.bundle/config/core/provider_e_sqlite3 | 3.0.5 | [SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw), Apache-2.0 |
| SQLite (binário nativo `e_sqlite3`) | 3.53.4 | [Pacote SQLite](https://www.nuget.org/packages/SQLite/3.53.4), arquivo LICENSE: Public Domain |
| Microsoft.NET.Test.Sdk | 17.14.1 | [microsoft/vstest](https://github.com/microsoft/vstest), MIT |
| xunit | 2.9.3 | [xunit](https://github.com/xunit/xunit), Apache-2.0 |
| xunit.runner.visualstudio | 3.1.4 | [visualstudio.xunit](https://github.com/xunit/visualstudio.xunit), Apache-2.0 |

Metadados de licença conferidos nos pacotes restaurados; não substituem revisão final da distribuição.
Transitivos de testes (incluindo TestPlatform, CodeCoverage e Newtonsoft.Json) constam dos lockfiles.
Dependências de testes não integram o aplicativo distribuído.

## Correção aplicada na v0.1 e mantida na v0.2/v0.3

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

ScottPlot.WPF e PDFsharp/MigraDoc continuam candidatos aprovados, ainda sem versões congeladas ou
prova de conceito nesta etapa. LibreHardwareMonitorLib 0.9.6 foi fixada apenas para a prova passiva
de laboratório descrita abaixo; não foi adicionada aos pacotes do aplicativo.

System.Management 10.0.0 foi fixado e exercitado em consultas de inventário somente de leitura.
Sensores/drivers, compatibilidade ampla e qualidade de cada metadado continuam sujeitos às provas
de conceito previstas. A auditoria da v0.2 incluiu os transitivos dessa nova referência.

System.Diagnostics.EventLog **10.0.0**, Microsoft, licença **MIT** conferida no nuspec restaurado. Usada apenas para consultas EventLogReader com XPath limitado. Sem novos transitivos de produção no alvo net10.0. Auditoria NuGet direta/transitiva da v0.3 sem avisos conhecidos na data do ensaio.

LibreHardwareMonitor não é empacotado, instalado ou iniciado. A integração WMI opcional usa o contrato público do provedor v0.9.4 existente. A prova de contrato identificou conversão de null para zero; zero externo não é aceito como leitura comprovada. Ver docs/arquitetura/V0.3.md. Não há aprovação de sensor/driver para estresse.

A v0.4 adiciona somente a biblioteca interna GcmMonitor.Execution, sem novos pacotes externos. Job Objects e identidade de pipes usam APIs do Windows. Versões/licenças anteriores mantidas; nova auditoria direta/transitiva sem advisories conhecidos na consulta. Nenhum driver ou biblioteca de geração de carga incorporado.

Continuação térmica: ferramenta separada usa o arquivo oficial LHM .NET 10 **0.9.6**, verificado por
SHA-256, sem novos pacotes NuGet na solução. **PawnIO 2.2.0** foi instalado localmente com autorização
expressa; instalador/driver com assinatura válida. Não é instalado, empacotado ou distribuído pelo
GCM. LHM declara MPL-2.0; PawnIO GPL-2.0-or-later com exceção própria, além das licenças dos módulos
e dependências. Revisão de redistribuição pendente. Hashes, origem, limites e remoção em
[validação térmica](testes/VALIDACAO_TERMICA_V0.4.md).
