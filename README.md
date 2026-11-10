# GCM Monitor — Gimenes Coleta e Monitoramento

**v1.0.0-preview.7 — prévia de desenvolvimento para Windows x64.** Coleta passiva, histórico
paginado, relatórios PDF e simulação térmica de laboratório. A prévia acrescenta a consulta
de cadastros de inicialização e melhora o tratamento de falhas de gravação.
Fonte obrigatória: [Especificação Mestre v1.0](docs/ESPECIFICACAO_MESTRE_GCM_MONITOR_v1.0.md), seção 14.
A ativação do estresse depende das provas térmicas e dos P0 em laboratório.
Resultados da rodada local em [Prévia 7](docs/testes/V1.0-PREVIEW.7.md).

## Funcionalidades

- Exatamente cinco telas: **Início, Monitoramento, Resultados, Histórico e Configurações**.
  WPF/XAML, MVVM, DI e tema exclusivamente escuro azul-marinho com azul/ciano.
- Os onze módulos podem ser selecionados no Início, com verificação de capacidade ao executar.
  Armazenamento requer disco físico detectado. Ausência/permissão/limitação não vira resultado saudável.
- CPU, RAM física/comprometimento/cache/paginação, discos/volumes e Top 10 de processos da v0.2.
  CPU acumulada por sessão e working set ponderado; processos encerrados preservados.
- Inventário de GPU e driver instalado. Uso por **engine/LUID físico** via PDH quando exposto,
  sem associar automaticamente esse identificador ao modelo de Win32_VideoController.
- Temperaturas/RPM/carga GPU pelo provedor **WMI LibreHardwareMonitor já existente**, quando
  compatível. GCM não o instala ou inicia. Zonas ACPI são identificadas separadamente, nunca como CPU.
- Indicadores de confiabilidade de disco (temperatura, desgaste, horas e erros) via associação
  Storage do Windows, quando expostos. Não é leitura SMART bruta completa nem atestado de integridade.
- Bateria: carga, capacidades/desgaste quando disponíveis, ciclos e autonomia estimada por consumo
  observado. Capacidades relativas não viram mWh; identidades/tags precisam coincidir na associação.
- Wi-Fi passivo: adaptador/estado/sinal/enlace/canal e reconexões observadas. Sem SSID/BSSID/perfil,
  varredura de redes, ping ou teste de velocidade. Banda permanece não determinada nesta etapa.
- Drivers/dispositivos PnP, eventos selecionados dos últimos sete dias, alimentação/plano e mudanças.
  Eventos têm log/origem/ID/horário/nível; mensagens integrais e conteúdo pessoal não são coletados.
- Windows Update: **histórico local** e reinicialização informada pelo WUA. Não executa busca,
  download/instalação nem afirma conhecer todas as pendências ou que o sistema está atualizado.
- Motor separado, relógio real, cadências automáticas, consultas pontuais uma vez, cancelamento,
  estados parciais e recuperação. Sessões somente pontuais terminam ao concluir as consultas.
- SQLite WAL esquema 5, com migração aditiva, amostras e evidências incrementais, notas e histórico.
  Resultados reabertos recuperam os registros persistidos, além do snapshot limitado enviado à UI.

**Estresse permanece desligado e bloqueado**, inclusive após Habilitar todos. Não há geração de
carga, reparo, atualização de drivers/Windows, alteração de energia ou instalação de driver de sensores.
Consulte a [compilação e validação do instalador de prévia](installer/InnoSetup/README.md).

## Segurança acrescentada na v0.4

- Criação atômica de filho próprio dentro de Job Object, ainda suspenso; fechamento encerra o auxiliar.
- Supervisor separado com identidade local, mensagens limitadas, heartbeat e confirmação de saída.
- Provas com **processos ociosos**, sem estresse, para queda do supervisor, perda de comunicação,
  cancelamento, sinais simulados de falha e proteção de processos independentes.
- Contratos de fases, orçamento e consentimento específico; avaliação de bloqueio gravada na sessão.

Isso é infraestrutura de desenvolvimento, **não estresse liberado**. Não existe gerador de carga nesta
entrega. O diálogo e as fases de uma execução real, sensores térmicos confiáveis, limites por hardware,
eventos físicos e matriz P0 sob carga ainda precisam ser integrados/validados em laboratório. A UI não
oferece o modo de prova. Os ensaios ociosos não aprovam os CT integrais de estresse nem liberam uso em clientes.

A continuação de laboratório integra temperaturas diretas da CPU ao monitoramento, resultados e histórico
com LHM 0.9.6/PawnIO 2.2.0. A interface permanece sem elevação; somente o auxiliar temporário de leitura
usa a confirmação do Windows, se autorizado para a sessão. O build padrão continua funcionando sem ele.
Veja [como preparar o auxiliar](docs/arquitetura/CANAL_TERMICO_V0.4.md) e a
[prova de acesso, versões e remoção](docs/testes/VALIDACAO_TERMICA_V0.4.md). O
[relatório da integração](docs/testes/RELATORIO_CANAL_TERMICO_V0.4.md) contém o ensaio local de cinco minutos.

O supervisor também exercita perda de amostras sintéticas, observação de energia e suspensão e
parada com comunicação atrasada. A prova usa filhos ociosos. Consulte os
[resultados da proteção ambiental](docs/testes/RELATORIO_PROTECAO_AMBIENTAL_V0.4.md).

## Compilar, testar e abrir

Windows x64 e SDK **.NET 10.0.403 x64** (`global.json`); acesso ao NuGet no primeiro restore.
O SDK 8 não compila esta solução. PowerShell 7 recomendado. Um SDK local em `.tools/dotnet/`
é reconhecido pelos scripts; também pode usar o [SDK oficial](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

```powershell
.\scripts\dev.ps1 Restore
.\scripts\dev.ps1 Build -Configuration Release
.\scripts\dev.ps1 Test -Configuration Release
.\scripts\dev.ps1 Run -Configuration Release
```

Restore usa versões travadas. Caches ficam em `.tools/`; motor/dependências são copiados para
`engine/` ao lado da UI. Abrir o executável diretamente exige .NET Desktop 10 ou `DOTNET_ROOT`
configurado. Não copie somente o `.exe`: o build de desenvolvimento **não é uma edição portátil**.
O pacote de prévia usa instalação self-contained por usuário com Inno Setup.

O ensaio térmico demonstrativo é reproduzido no pacote separado de laboratório. O roteiro,
a comparação passiva deste notebook e a revisão das telas estão no
[relatório da prévia 6](docs/testes/V1.0-PREVIEW.6.md).

## Uso e estrutura

1. Aguarde o inventário no Início; selecione módulos/discos e 5 min, 30 min, 1 h ou 4 h.
2. Análise detalhada da bateria acrescenta capacidades/consumo; a de Wi-Fi acrescenta reconexões
   amostradas. Não representam consentimento para testes ativos.
3. Inicie. CPU/RAM/discos têm referência de 2 s; processos, sensores, bateria/Wi-Fi/energia, 5 s.
   Consultas pontuais e tempos de API podem atrasar amostras; fontes e timestamps ficam registrados.
4. Navegue entre as cinco telas sem iniciar outra sessão. Cancelar preserva os dados confirmados.
5. Em Resultados, expanda os módulos para ver registros, fontes, horários e limitações; salve notas.
6. Histórico filtra computador/data `dd/MM/aaaa`/estado e abre resultados completos preservados.
   Exclusão só de sessões finalizadas, com confirmação, sem remover PDFs externos.

Projetos em `src/`: **Execution** (contenção de processos Windows), **Core** (contratos/modelos), **Collector** (fontes), **Persistence** (SQLite),
**Engine** (coordenação/processo), **Presentation** (MVVM testável), **Reporting** (PDF), **App** (WPF/DI/IPC),
**StressSupervisor** (supervisor com prova ociosa, recusa carga real). `tests/` contém testes unitários e integrados Windows.
Detalhes e fontes: [arquitetura v0.4](docs/arquitetura/V0.4.md),
[dependências/licenças](docs/DEPENDENCIAS.md), [distribuição](installer/InnoSetup/README.md).

## Verificação e limites

A suíte combina fixtures e leituras passivas reais locais. Também encerra somente um motor
criado pelo próprio teste para exercitar recuperação; não encerra programas de terceiros.
Os ensaios passivos com onze módulos são separados da suíte rápida. Execute no notebook de
laboratório, preferencialmente ligado à tomada. Compile em Release antes do ensaio:

```powershell
.\scripts\validate-notebook.ps1 -Minutes 5
.\scripts\validate-notebook.ps1 -Minutes 30,60,240 `
  -PublishedEngine '.artifacts/installer/<pasta>/publish/engine/GcmMonitor.Engine.exe'
```

Cada duração é executada de forma sequencial, em tempo real, com um banco temporário.
Os checkpoints e resultados ficam em `.artifacts/validation/notebook-<id>`. O caso aparece como
ignorado na suíte rápida.
Veja a [validação passiva de laboratório neste notebook](docs/testes/VALIDACAO_LABORATORIO_NOTEBOOK.md).
Consulte a [estratégia de validação em duas frentes](docs/arquitetura/ESTRATEGIA_VALIDACAO_DUAS_FRENTES.md)
para o escopo de uso passivo em laboratório e os critérios de proteção que mantêm a carga bloqueada.
Evidências: [relatório v0.4](docs/testes/RELATORIO_V0.4.md) e
[rastreabilidade dos 77 CT](docs/testes/RASTREABILIDADE_V0.4.md).
TRX/capturas ficam em `.artifacts/`. O CI publica apenas resultados de testes, imagens sintéticas
identificadas e resumos sanitizados, nunca bancos locais ou registros brutos do equipamento.

- A leitura direta de temperatura CPU foi exercitada em laboratório. GPU, RPM e fontes WMI
  dependem das capacidades expostas. Zero do contrato WMI externo é ambíguo e fica indisponível.
  A qualificação para proteção térmica está pendente.
- GPU PDH é por engine; não se somam engines diferentes como porcentagem global. Não há
  correlação validada entre LUID e modelo, VRAM ou classificação integrada/dedicada.
- SMART indisponível não significa saudável. Controladoras/RAID/USB e associações podem limitar dados.
- Wi-Fi depende do serviço/API/permissões de localização. Sem banda validada ou testes ativos.
- Eventos: até 128 por log, sete dias, fontes selecionadas. Limite excedido é sinalizado como parcial.
  PnP: até 512 dispositivos, problemas primeiro. Nenhum Event ID isolado determina causa.
- Gráficos: até 32 pontos/série e 512 amostras no snapshot; registros IPC: até 64 por módulo/192
  no total. Os registros completos coletados ficam no banco e são carregados ao abrir o resultado.
- Histórico paginado em 25 sessões. As preferências ficam no perfil do Windows, e a limpeza por
  prazo exige confirmação com a data de corte. [Prévia de histórico e preferências](docs/testes/V1.0-PREVIEW.1.md).
- CPU representa tempo ocupado, não utilidade ajustada por frequência. Mais de 64 processadores
  lógicos permanece indisponível até validar grupos. Processos protegidos podem ficar parciais.
- A exportação PDF está disponível na segunda prévia. [Relatório de validação do PDF](docs/testes/V1.0-PREVIEW.2.md).
- A prévia 7 passou em 5 min, 30 min, 1 h e 4 h no notebook de laboratório. Os demais ambientes
  da matriz Windows 10/11 seguem pendentes.

Dados por usuário: `%LOCALAPPDATA%\GCM Monitor\Data\gcm-monitor.db`; banco demonstrativo antigo
não é importado. Não versione bancos, notas, identificadores, relatórios ou capturas reais.
Próximo marco: concluir a liberação da **v0.4 protegida**, condicionada às provas térmicas e aos P0 de
segurança em laboratório. A v0.3 não autoriza liberar estresse antes dessas provas.
