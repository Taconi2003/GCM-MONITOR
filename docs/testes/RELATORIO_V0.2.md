# Relatório de entrega — GCM Monitor v0.2.0

Data: **06/11/2026**, America/Sao_Paulo. Código de produto/testes: **0bb2b4e**.
Fonte: Especificação Mestre v1.0, seção 14. A branch parte da v0.1 aprovada e mantém as
decisões de interface, módulos, durações e segurança. Acesso ao repositório confirmado antes das alterações.

## Implementado

- Coleta passiva real de **CPU, RAM, comprometimento/cache/paginação, processos e discos/espaço**.
- Inventário de Windows/hardware, núcleos/processadores lógicos, módulos de RAM, boot/uptime,
  discos físicos e suas associações a volumes. Metadados sem fonte inequívoca ficam indisponíveis.
- CPU por deltas de tempo do sistema; processos por PID + início, CPU acumulada observada,
  RAM working set ponderada no tempo, pico e cobertura. Processos encerrados persistidos.
- Motor em processo separado com duração real, referência de 2 s para CPU/RAM/discos e 5 s
  para processos, cancelamento, falhas isoladas e registro de lacunas.
- SQLite WAL com amostras, módulos, transições, ocorrências e agregados normalizados;
  notas independentes; recuperação de sessões não terminadas e proteção de registros ativos.
- IPC versionado, restrito ao usuário, com validação do PID do cliente contra o motor criado;
  proteção contra UIs/sessões concorrentes e separação entre origem real e sintética.
- Cinco telas preservadas: inventário e seleção no Início, gráficos reais limitados no
  Monitoramento, estatísticas/fontes/Top 10 nos Resultados e sessões persistidas no Histórico.
- Módulos avançados explicitamente indisponíveis na v0.2. Armazenamento é parcial porque
  SMART permanece fora desta etapa. Estresse continua separado, desligado e bloqueado.

## Testes efetivamente executados

Ambiente local: Windows x64, build **10.0.26200.0**, SDK **10.0.403**. Não foram publicados
hostname, modelos de hardware, nomes de processos, notas, bancos ou capturas reais do computador.

| Verificação | Resultado |
|---|---|
| Restore final com lockfiles | Aprovado, nove projetos |
| Build final Release | Aprovado, **zero avisos e zero erros** |
| Suíte unitária | **39 aprovados**, zero falhas |
| Integração rápida | **18 aprovados**, zero falhas; ensaio longo ignorado explicitamente nesta execução |
| Ensaio longo, executado separadamente | **1 aprovado**, zero falhas/ignorados |
| Casos distintos aprovados no conjunto | **58**; o caso ignorado da suíte rápida foi executado no ensaio separado |
| Auditoria NuGet, diretos/transitivos | Nenhuma vulnerabilidade conhecida apontada nas versões resolvidas, na consulta realizada |
| Interface aberta | Inventário real, início, gráficos/fontes/tempo efetivo, navegação durante coleta, bloqueio de segunda sessão e cancelamento parcial conferidos |
| Encerramento após revisão/testes | Nenhum processo GCM remanescente na verificação final |
| Estresse | **Não executado. Nenhum gerador de carga foi implementado** |

As execuções finais e o resultado individual de cada caso estão em
[RESULTADOS_V0.2.json](RESULTADOS_V0.2.json), extraídos dos TRX e sanitizados.
O ensaio de cinco minutos terminou às **17:18:51 UTC−03**. A suíte rápida final terminou
às **17:21:33 UTC−03**. Alterações posteriores à validação de código são documentação/metadados.

Cobertura exercitada:

- Regressão da v0.1, catálogo/durações, seleção e estresse bloqueado.
- Cálculo de CPU com idle/kernel/usuário, contadores regressivos e amostra inicial insuficiente.
- Médias ponderadas por tempo, pico/mínimo, ausência de interpolação em falhas e lacunas.
- Processos encerrados, reutilização de PID e ausência de preenchimento artificial para contadores restritos.
- Duração por relógio testável, cancelamento antecipado, módulos avançados indisponíveis sem espera artificial,
  falha isolada de CPU, falha de persistência antes/depois do início e lacuna simulada de amostragem.
- Leituras nativas reais de CPU/RAM/discos/processos, unidades e correspondência de volumes.
- SQLite real: transação de lote, amostras/agregados, notas, rankings encerrados, proteção de dados ativos,
  preservação de arquivo externo sentinela, mistura de origem rejeitada e identidade de motor vivo respeitada.
- Processo real de coleta cancelado e reaberto; encerramento forçado **somente do motor criado pelo teste**,
  seguido de recuperação de registros confirmados como Interrompidos.
- Renderização WPF de cinco telas, estados concluído/cancelado/interrompido/erro, gráficos e rankings,
  em **1440×960 e 1000×700 unidades lógicas**, com pixels visíveis e sem erros de binding nos cenários testados.
  As 24 renderizações dessa rotina usam fixtures identificadas; não são capturas de dados de clientes.

## Ensaio passivo completo de cinco minutos

Resultado sanitizado: [VALIDACAO_PASSIVA_V0.2.json](VALIDACAO_PASSIVA_V0.2.json).

- Duração observada no controlador: **300,56 s**.
- **1.898 amostras confirmadas** no banco; snapshot máximo da UI: **416 pontos** (limite: 512).
- **206 mensagens** do motor e **16 identidades** na união dos rankings finais de CPU/RAM.
- Estado final **Concluída** e reabertura do registro no SQLite verificada.
- Sem estresse, teste de escrita, alteração de energia, reparo, elevação ou atualização.

Esse ensaio valida o fluxo passivo no computador de desenvolvimento. Não prova comportamento
em todas as máquinas nem substitui ensaio real de quatro horas, medição de sobrecarga ou testes elétricos.

## Comparação com o Windows

`GetSystemTimes` foi comparado com **PDH Processor(_Total) % Processor Time**, usando quatro
intervalos passivos próximos de 2,1 s e o mesmo conceito de tempo ocupado. No último ensaio,
a diferença absoluta média foi **0,77 ponto percentual**, com máximo **1,34 ponto percentual**.
Evidência: [COMPARACAO_CPU_V0.2.json](COMPARACAO_CPU_V0.2.json).

**Não existe tolerância de precisão aprovada ainda.** O teste verifica leituras válidas e registra
diferenças observadas; não certifica precisão nem equipara a métrica à utilidade ajustada por frequência
do Gerenciador de Tarefas. RAM/comprometimento/cache são tratados como contadores distintos;
discos usam throughput em MB/s, não percentual de tempo ativo. Os cálculos são verificados com fixtures exatas.

## Problemas encontrados e tratamentos

- A v0.1 pressupunha dados demonstrativos no contrato, banco, cliente e textos. A v0.2 separa
  explicitamente modos/origens; o aplicativo normal usa coleta passiva e banco real próprio.
- Processos protegidos não expõem todos os contadores sem privilégios. Eles são omitidos ou têm
  intervalos sem cobertura; a limitação aparece como ocorrência/resultado parcial, sem solicitar elevação.
- Tipo físico e barramento não foram identificados inequivocamente para todos os discos locais.
  Os campos permanecem indisponíveis ou refletem a interface informada pelo Windows; nenhum SSD/HDD/NVMe é inferido pelo nome.
- A amostra inicial de contadores por deltas não é uma medição de utilização. É marcada insuficiente,
  aguardando um intervalo válido. Falhas posteriores não geram zero nem linha fictícia.
- Recuperação baseada apenas em estado poderia interromper indevidamente um motor ainda vivo.
  A identidade PID + início agora é verificada, e outra sessão é bloqueada quando há estado ativo remanescente.
- Erros iniciais de chamadas/tipos de WMI e verificações do analisador de testes foram corrigidos antes do build final.

Não há teste reprovado aberto nas execuções registradas. Cenários não exercitados continuam pendentes.

## Limitações e próximos passos

- **Não validado em hardware real:** sessões de 30 min/1 h/4 h, suspensão/retomada real, disco cheio real,
  desligamento elétrico/BSOD, RAID/Storage Spaces complexos, máquinas com mais de 64 processadores lógicos
  e matriz completa de versões/edições do Windows. Houve validação passiva local de cinco minutos.
- Sensores, GPU, temperatura/RPM, SMART, bateria, Wi-Fi, drivers, eventos, energia e Windows Update
  avançados aguardam a **v0.3**. Nenhum resultado de saúde é fabricado para esses recursos.
- A precisão comparativa e as tolerâncias exigem ensaios adicionais com mesmas fontes/métricas/intervalos.
  CPU por tempo ocupado não representa necessariamente a utilidade do Gerenciador de Tarefas.
- A UI consulta as 100 sessões mais recentes; paginação, retenção automática e configurações definitivas
  permanecem pendentes. As amostras completas estão persistidas; os gráficos exibem uma janela limitada.
- O banco novo não importa automaticamente os exemplos da v0.1. Dados de demonstração não são misturados com coletas reais.
- PDF, Inno Setup, pacote self-contained final e desinstalação segura aguardam a etapa de distribuição.
- Supervisão de estresse, limites térmicos, watchdog e Job Objects aguardam **v0.4** e aprovação dos P0;
  o bloqueio permanece obrigatório.
- A matriz CT-001 a CT-077 não está aprovada integralmente. A
  [rastreabilidade v0.2](RASTREABILIDADE_V0.2.md) distingue evidências locais/parciais dos critérios finais da v1.0.

Próxima entrega do plano: **v0.3**, implementando e validando fontes avançadas uma a uma.
A documentação de build e os comandos para repetir a suíte rápida e o ensaio passivo estão no README.
