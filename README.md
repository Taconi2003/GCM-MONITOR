# GCM Monitor — Gimenes Coleta e Monitoramento

**v0.2.0 — coleta básica passiva para Windows x64.** Agora utiliza dados reais de CPU,
memória, processos, discos e inventário do computador. Versão de desenvolvimento;
ainda não liberada para diagnóstico de clientes.

Fonte de escopo: [Especificação Mestre v1.0](docs/ESPECIFICACAO_MESTRE_GCM_MONITOR_v1.0.md),
seção 14. A especificação original permanece preservada. A v0.2 dá continuidade à v0.1.

## O que está disponível

- Cinco telas: **Início, Monitoramento, Resultados, Histórico e Configurações**.
- WPF/XAML, MVVM, injeção de dependências e tema escuro azul-marinho/azul/ciano.
- Inventário por consultas de leitura do Windows: edição/build/arquitetura, boot/uptime,
  CPU/núcleos/processadores lógicos, RAM/módulos e discos físicos associados aos volumes.
- CPU por deltas de `GetSystemTimes`, RAM física disponível/ocupada, comprometimento,
  cache do sistema e contadores de paginação quando acessíveis.
- Tráfego de leitura/gravação por disco físico em **MB/s decimais**, sem teste de escrita.
  Capacidade e espaço livre são apresentados respeitando a diferença entre disco e volume.
- Top 10 de CPU acumulada e RAM média ponderada no tempo (working set), com pico,
  período observado e processos encerrados. Identidade por **PID + início do processo**.
- Motor separado por sessão, seleção de módulos/discos, relógio real, gráficos limitados,
  cancelamento passivo e consolidação parcial.
- SQLite WAL com transações, amostras normalizadas, módulos, estatísticas, transições,
  ocorrências, agregados de processos, notas e recuperação de sessões interrompidas.

O catálogo mantém os 11 módulos aprovados. Nesta etapa estão disponíveis **M01, M02, M03
(inventário/espaço/tráfego; SMART indisponível) e M08**. Os demais aparecem como indisponíveis,
previstos para v0.3. GPU, temperatura, RPM, SMART, bateria e Wi-Fi não recebem valores simulados.

**Estresse continua separado, desligado e bloqueado.** Não há gerador de carga,
alteração de energia, atualização, reparo, instalação de driver ou leitura de conteúdo de processos.
PDF, preferências definitivas e Inno Setup pertencem às próximas etapas aprovadas.

## Compilar e abrir

Requisitos: Windows x64, SDK **.NET 10.0.403 x64** (`global.json`), Git e acesso ao NuGet no
primeiro restore. O SDK .NET 8 não compila esta solução. PowerShell 7 é recomendado.
Um SDK isolado em `.tools/dotnet/` é reconhecido pelos scripts; também pode ser usado o
[SDK instalado da Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

```powershell
.\scripts\dev.ps1 Restore
.\scripts\dev.ps1 Build -Configuration Release
.\scripts\dev.ps1 Test -Configuration Release
.\scripts\dev.ps1 Run -Configuration Release
```

Os scripts isolam cache em `.tools/`, fazem restore travado e propagam falhas.
O build copia o motor e suas dependências para `engine/` ao lado do aplicativo.
O script Run configura o runtime local; ao abrir o `.exe` diretamente, é necessário
runtime .NET Desktop 10 instalado ou `DOTNET_ROOT` apontando ao SDK isolado.
Não copie somente o `.exe`. Esta saída é de desenvolvimento, **não uma edição portátil**.

## Utilizar a v0.2

1. Aguarde o inventário no **Início**. Somente fontes disponíveis são selecionadas por padrão.
2. Escolha módulos, discos e uma das durações aprovadas: **5 min, 30 min, 1 h ou 4 h**.
   As durações são reais; nenhuma aceleração é aplicada na execução normal.
3. Inicie a coleta. CPU/RAM/discos têm cadência de referência de 2 s; processos, 5 s.
   O programa registra o timestamp efetivo e não promete amostras exatas sob atrasos.
4. Navegue sem perder a sessão. O Início mostra a execução ativa e impede iniciar outra.
   **Cancelar diagnóstico** interrompe a coleta e preserva resultados parciais.
5. Em **Resultados**, revise estatísticas, fontes, limitações e rankings; salve observações.
   Ausência de achados não equivale a integridade física comprovada.
6. Em **Histórico**, filtre por computador/data `dd/MM/aaaa`/estado, abra resultados e exclua
   apenas sessões finalizadas com confirmação. A consulta atual carrega as 100 mais recentes;
   paginação e retenção automática ainda não estão implementadas.

Cada gráfico apresenta no máximo 32 pontos por série; o snapshot da UI limita o total a 512.
As amostras completas e estatísticas do período observado permanecem no banco.
Lacunas não são preenchidas com zero ou valores artificiais. As médias não interpolam
intervalos acima de 15 s; atrasos/suspensão são registrados como ocorrência.

## Testes e evidências

A suíte combina fixtures determinísticas com **leituras passivas curtas do Windows**.
Também encerra um motor criado pelo próprio teste para validar recuperação; não encerra
aplicativos de terceiros. Não executa estresse.

O ensaio real completo de cinco minutos é separado da suíte rápida:

```powershell
$env:GCM_RUN_LONG_PASSIVE_VALIDATION = '1'
# Usar o dotnet do SDK 10 selecionado; os caches/runtime devem estar configurados como em scripts/dev.ps1.
dotnet test tests/GcmMonitor.IntegrationTests -c Release --no-build --no-restore `
  --filter 'FullyQualifiedName~FullFiveMinutePassiveSession' --logger trx
```

Sem a variável, esse caso aparece explicitamente como ignorado; não é contado como aprovado.
O relatório registra separadamente os ensaios executados. TRX/capturas ficam em `.artifacts/`
e não são versionados. Capturas automatizadas usam apenas fixtures, claramente identificadas.
O CI publica somente TRX, capturas sintéticas e resumos sanitizados, nunca bancos reais.

Veja [relatório v0.2](docs/testes/RELATORIO_V0.2.md),
[arquitetura v0.2](docs/arquitetura/V0.2.md) e [dependências/licenças](docs/DEPENDENCIAS.md).

## Dados e limites

Banco real por usuário: `%LOCALAPPDATA%\GCM Monitor\Data\gcm-monitor.db`.
O banco `demo-v0.1.db` da entrega anterior não é importado nem misturado automaticamente.
Fixtures permanecem reservadas aos testes. Dados reais e sintéticos são rejeitados quando
misturados na mesma sessão. Não inclua bancos, notas, nomes de máquinas/processos reais,
relatórios ou capturas reais no repositório público.

- CPU representa **tempo ocupado**, podendo diferir da utilidade com ajuste de frequência
  mostrada pelo Gerenciador de Tarefas. Acima de 64 processadores lógicos, a leitura agregada
  fica indisponível até validação de grupos de processadores.
- RAM física, cache e comprometimento são contadores diferentes; GiB usa base 1024.
  Paginação não comprova defeito de RAM.
- Discos/volumes complexos e metadados de tipo/barramento podem ficar indisponíveis.
  Tipo desconhecido não é inferido pelo nome do modelo. SMART não acessado não significa saudável.
- Processos protegidos podem não fornecer identidade/contadores; o ranking sinaliza cobertura parcial.
- Houve validação passiva local; **não há validação ampla em hardware real**, precisão certificada,
  ensaio real de quatro horas ou aprovação da matriz completa de Windows/sensores.
- Preferências de avisos/pasta são ilustrativas e valem apenas nesta execução. PDF desabilitado.
- Não há instalador nesta etapa. A futura distribuição é instalada, self-contained, por usuário,
  com Inno Setup; ver [contrato de distribuição](installer/InnoSetup/README.md).

Próxima etapa do plano: v0.3 (diagnósticos avançados). Estresse somente na v0.4 após os P0
de segurança; PDF/distribuição e matriz de liberação completa na v1.0.
