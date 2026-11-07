# Relatório v0.4.0 — infraestrutura de supervisão, liberação bloqueada

Data local: 06/11/2026, America/Sao_Paulo. Ambiente anonimizado DEV-LOCAL-01, Windows 11 x64 build 26200,
SDK .NET 10.0.403. Branch `codex/v0.4-supervisao-protegida`, baseada na v0.3, sem merge na main.
Código/testes desta entrega: `dd06339`. Especificação Mestre v1.0 preservada. CI do código aprovado: https://github.com/Taconi2003/GCM-MONITOR/actions/runs/42326042225.

**Estado da entrega:** infraestrutura e provas ociosas implementadas; **execução real de estresse NÃO
liberada nem validada**. A condição de avanço da seção 14 — todos os P0 de estresse aprovados em laboratório —
ainda não foi satisfeita. Não existe gerador de carga nesta versão. Não considerar a v0.4 pronta para estresse
em clientes, nem atribuir aprovação integral dos CT a partir destes testes.

## Implementado e verificável

- Biblioteca interna Execution: Job anônimo com KILL_ON_JOB_CLOSE, limite de um filho, criação suspensa
  e associação atômica por PROC_THREAD_ATTRIBUTE_JOB_LIST antes da execução; sem fallback fora do Job.
- Supervisor em processo separado para prova ociosa. Filho só aguarda, sem laço de cálculo/carga.
  Encerramento confirmado pelo handle exato do processo antes de enviar sucesso.
- Canal local por usuário, identidade PID+início de ambos os pares, sessão/nonce/sequência, mensagens de
  até 16 KiB e rejeição de campos/comandos inválidos. Nenhum executável/PID arbitrário selecionável por IPC.
- Heartbeat com referência de 3 s; cabeçalho/payload incompletos e mensagens após o prazo não renovam a
  supervisão. Falhas de comunicação, cancelamento e sinais simulados encerram o auxiliar ocioso.
- Contratos de planejamento com as janelas aprovadas, cálculo de extensão e fases previstas; consentimento
  específico da sessão/plano, extensão separada, validade interna e consumo único. São contratos testados,
  sem fluxo de carga ativo na interface.
- Bloqueio em opções, UI, protocolo e persistência; avaliação e motivos gravados em cada sessão passiva nova.
  Histórico anterior sem esse campo continua legível. Protocolo passivo 4; schema SQLite 3 mantido.
- Cinco telas/tema escuro/seleção dos onze módulos preservados. Não há sexto menu, modo claro, edição
  portátil, serviço permanente, driver instalado ou atualização/reparo do Windows.

## Testes efetivamente executados

- Restore travado: aprovado. Build Release final: **zero avisos, zero erros**.
- Suíte rápida: **74 unitários + 46 integrados aprovados**, zero falhas. O ensaio passivo longo aparece
  explicitamente como um caso ignorado nessa execução e é executado separadamente.
- Planejamento: quatro durações, tempos antes/depois, extensão, consentimento de sessão/plano,
  expiração/reutilização, rejeição de fases alteradas e bloqueio mesmo com consentimento válido.
- Contenção nativa com auxiliares ociosos: filho suspenso já contido, fechamento do Job, cancelamento,
  parada, perda de heartbeat, heartbeat válido até prazo, EOF, morte inesperada do supervisor sem matar
  a árvore pelo teste, identidade incorreta, mensagem repetida/desconhecida/grande/incompleta e rejeição
  de início de carga. Um processo independente continua vivo após parar o Job.
- Sinais **injetados** de sensor ausente, suspensão, energia e falha de persistência: resposta de parada
  verificada. Isso não valida detecção física desses eventos nem política térmica.
- Coleta passiva real independente continua depois da parada da prova ociosa, depois é cancelada
  preservando dados. Avaliação de bloqueio é reaberta no SQLite; alegação de carga disponível/executada é rejeitada.
- Regressões anteriores de inventário/coleta, cálculos, processos, SQLite, recuperação e WPF mantidas.
  26 cenários WPF sintéticos identificados, em 1440×960 e 1000×700 lógicos, sem erros de binding.
- Auditoria NuGet direta/transitiva: nenhum advisory conhecido informado na consulta; nenhuma nova
  dependência externa adicionada. Isso não certifica ausência de vulnerabilidades.

**Regressão passiva final de cinco minutos:** 121 testes distintos aprovados no total. Ensaio final: 300,747 s reais, 3689 medições persistidas, 432 registros reabertos; máximo de 512 pontos e 288042 bytes por snapshot (limite IPC 1 MiB). Onze módulos selecionados, atividade contínua de disco validada; estado final Concluída com limitações por módulo. Nenhum estresse.

Evidências sanitizadas: RESULTADOS_V0.4.json, CONTENCAO_OCIOSA_V0.4.json, VALIDACAO_PASSIVA_V0.4.json,
CAPACIDADES_V0.4.json e COMPARACAO_CPU_V0.4.json. O limiar/latência de heartbeat publicado se refere ao
ensaio ocioso local, medido após receber a confirmação inicial; não é garantia sob carga ou suspensão.
Bancos de teste são temporários e isolados. Não foram versionados nonces, PIDs, nomes reais, registros
brutos de eventos, dados de rede ou capturas reais. Capturas do CI são exclusivamente sintéticas.

## Problemas e limitações encontradas

1. A v0.3 não comprovou sensor térmico CPU/GPU com frescor e limites qualificados. Não foi escolhido um
   limite universal nem usado ACPI genérico/zero WMI ambíguo como proteção. O bloqueio foi mantido.
2. A prova ociosa comprova partes do encerramento/isolamento, mas não a segurança de geração de carga,
   fases reais, temperatura, sensores que param sob carga ou resposta durante suspensão física.
3. Diálogo de consentimento e extensão na UI, motor de fases sob carga, carga real e conexão dos eventos
   físicos a esse fluxo permanecem pendentes. Não foram apresentados como funcionalidades liberadas.
4. A revisão do watchdog reforçou o prazo sobre a mensagem inteira e a recusa de renovação tardia.
   A solução gerada pelo SDK acrescentou configurações x86; foram removidas para preservar o escopo x64.
5. A matriz ampla Windows 10/11, sessões reais de 30 min/1 h/4 h, recursos sob carga e instalação/desinstalação
   continuam pendentes. Nenhum teste final falhou; isso não substitui os ensaios faltantes.
6. A publicação usa branch de revisão. Criação de PR depende da permissão da integração GitHub; a main
   não foi alterada.

## Próximos passos para concluir a liberação da v0.4

1. Selecionar hardware de laboratório e comprovar fonte térmica essencial identificada, atualizada e
   comparável à mesma leitura em ferramenta de referência, incluindo ausência, travamento e recuperação.
2. Projetar limites e respostas com referência específica do hardware e validar tolerâncias/tempos;
   revisar permissões/licença/assinatura de eventual componente de sensores antes de instalar qualquer driver.
3. Integrar autorização real, extensão explícita, fases, persistência e supervisor ao motor somente após
   as pré-condições qualificadas. Conservar estresse desligado por padrão e fora de Habilitar todos.
4. Executar os P0 em laboratório com carga controlada e instrumentada, além das falhas de UI/supervisor,
   canal, sensores, energia, suspensão e banco. Parar estresse deve manter a coleta quando seguro.
5. Registrar evidências e liberar apenas ambientes cobertos. Até lá, manter a política bloqueada.

PDF, histórico/configurações definitivos, Inno Setup e liberação de uso em clientes continuam na v1.0.
Detalhes e fontes primárias: ../arquitetura/V0.4.md. Rastreabilidade integral: RASTREABILIDADE_V0.4.md.
