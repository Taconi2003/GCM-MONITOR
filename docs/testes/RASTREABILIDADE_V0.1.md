# Rastreabilidade da Especificação Mestre — v0.1

Todos os 77 critérios de liberação da v1.0 permanecem pendentes de validação integral. A coluna de evidência registra verificações parciais da estrutura sintética; não aprova medições, segurança de estresse ou distribuição.

| ID | Cenário | Prioridade | Critério final v1.0 | Evidência parcial da v0.1 |
|---|---|---|---|---|
| CT-001 | Instalar via `GCMMonitor-Setup.exe` | P1 | Pendente | Não exercitado nesta etapa |
| CT-002 | Executar Windows 11 sem .NET pré-instalado | P1 | Pendente | Não exercitado nesta etapa |
| CT-003 | Conferir pastas e permissões | P0 | Pendente | Não exercitado nesta etapa |
| CT-004 | Desinstalar removendo dados | P0 | Pendente | Não exercitado nesta etapa |
| CT-005 | Desinstalar preservando dados | P1 | Pendente | Não exercitado nesta etapa |
| CT-006 | Desinstalar após exportar PDF | P0 | Pendente | Não exercitado nesta etapa |
| CT-007 | Desinstalar com teste ativo | P0 | Pendente | Não exercitado nesta etapa |
| CT-008 | Abrir Início | P1 | Pendente | Não exercitado nesta etapa |
| CT-009 | Navegar entre cinco telas | P1 | Pendente | PresentationTests + WpfViewTests + navegação no aplicativo |
| CT-010 | `Habilitar todos` | P1 | Pendente | EnableDisableAndRestoreNeverEnableStress |
| CT-011 | `Desabilitar todos` | P1 | Pendente | EnableDisableAndRestoreNeverEnableStress |
| CT-012 | `Restaurar padrão` | P1 | Pendente | Padrão demonstrativo testado; recomendação por capacidade ainda pendente |
| CT-013 | Executar módulo isolado | P1 | Pendente | CancellingDemoPreservesPartialStateAndSelectedModulesOnly |
| CT-014 | Iniciar sem módulos | P1 | Pendente | EmptySelectionDisplaysValidationAndNeverStartsEngine |
| CT-015 | Redimensionar janela / mudar DPI | P2 | Pendente | Renderização em dois tamanhos; DPI real ainda pendente |
| CT-016 | Identificação do computador | P1 | Pendente | Não exercitado nesta etapa |
| CT-017 | Uso da CPU | P1 | Pendente | Não exercitado nesta etapa |
| CT-018 | RAM e paginação | P1 | Pendente | Não exercitado nesta etapa |
| CT-019 | Coleta dos processos | P1 | Pendente | Não exercitado nesta etapa |
| CT-020 | Top 10 CPU | P1 | Pendente | Não exercitado nesta etapa |
| CT-021 | Top 10 RAM | P1 | Pendente | Não exercitado nesta etapa |
| CT-022 | Temperatura CPU | P1 | Pendente | Não exercitado nesta etapa |
| CT-023 | GPU e temperatura | P1 | Pendente | Não exercitado nesta etapa |
| CT-024 | Ventoinhas | P1 | Pendente | Não exercitado nesta etapa |
| CT-025 | Discos físicos e volumes | P1 | Pendente | Não exercitado nesta etapa |
| CT-026 | SMART | P1 | Pendente | Não exercitado nesta etapa |
| CT-027 | Atividade de armazenamento | P1 | Pendente | Não exercitado nesta etapa |
| CT-028 | Eventos relevantes | P1 | Pendente | Não exercitado nesta etapa |
| CT-029 | Interpretação de eventos | P1 | Pendente | Não exercitado nesta etapa |
| CT-030 | Dispositivos e drivers | P1 | Pendente | Não exercitado nesta etapa |
| CT-031 | Windows Update | P1 | Pendente | Não exercitado nesta etapa |
| CT-032 | Inicialização | P1 | Pendente | Não exercitado nesta etapa |
| CT-033 | Estado de energia | P1 | Pendente | Não exercitado nesta etapa |
| CT-034 | Wi-Fi | P1 | Pendente | Não exercitado nesta etapa |
| CT-035 | Estabilidade Wi-Fi | P1 | Pendente | Não exercitado nesta etapa |
| CT-036 | Bateria | P1 | Pendente | Não exercitado nesta etapa |
| CT-037 | Sem Wi-Fi ou bateria | P1 | Pendente | Fixtures de bateria/sensores indisponíveis; fontes reais pendentes |
| CT-038 | Monitoramento 5 min | P1 | Pendente | Não exercitado nesta etapa |
| CT-039 | Monitoramento 30 min | P1 | Pendente | Não exercitado nesta etapa |
| CT-040 | Monitoramento 1 h | P1 | Pendente | Não exercitado nesta etapa |
| CT-041 | Monitoramento 4 h | P0 | Pendente | Não exercitado nesta etapa |
| CT-042 | Somente módulos pontuais | P1 | Pendente | SeparateEngineCompletesPointQueriesWithoutWaitingMonitoringDuration |
| CT-043 | Fases do estresse | P0 | Pendente | Não exercitado nesta etapa |
| CT-044 | Tempo insuficiente para estresse | P1 | Pendente | Não exercitado nesta etapa |
| CT-045 | Encerrar estresse no tempo definido | P0 | Pendente | Não exercitado nesta etapa |
| CT-046 | Cancelar sessão | P0 | Pendente | Cancelamento do processo demonstrativo; estresse não implementado |
| CT-047 | `Parar estresse` | P0 | Pendente | Não exercitado nesta etapa |
| CT-048 | Falha de sensor | P1 | Pendente | Não exercitado nesta etapa |
| CT-049 | Perda térmica essencial em estresse | P0 | Pendente | Não exercitado nesta etapa |
| CT-050 | Encerramento inesperado da UI | P0 | Pendente | Não exercitado nesta etapa |
| CT-051 | Falha de comunicação/supervisão | P0 | Pendente | Não exercitado nesta etapa |
| CT-052 | Suspensão/retomada do Windows | P0 | Pendente | Não exercitado nesta etapa |
| CT-053 | Mudança de alimentação | P1 | Pendente | Não exercitado nesta etapa |
| CT-054 | Sem privilégio requerido | P1 | Pendente | Não exercitado nesta etapa |
| CT-055 | Verificar efeitos colaterais | P0 | Pendente | Não exercitado nesta etapa |
| CT-056 | Privacidade da coleta | P0 | Pendente | Não exercitado nesta etapa |
| CT-057 | Persistência incremental | P0 | Pendente | Snapshots e transições sintéticas em SQLite; telemetria real pendente |
| CT-058 | Encerramento inesperado | P0 | Pendente | Reabertura do SQLite; queda real não ensaiada |
| CT-059 | Reabrir depois de interrupção | P0 | Pendente | RecoverInterruptedAsync testado apenas com snapshots sintéticos |
| CT-060 | Disco cheio/banco inacessível | P0 | Pendente | Não exercitado nesta etapa |
| CT-061 | Escrita + consulta simultâneas | P1 | Pendente | Não exercitado nesta etapa |
| CT-062 | Retenção/limpeza | P1 | Pendente | CleanupCannotDeleteRunningSessionAndDoesNotTouchExternalPdf |
| CT-063 | Resultados por módulo | P1 | Pendente | Cartões e estados sintéticos renderizados |
| CT-064 | Observações do técnico | P1 | Pendente | ReopeningDatabasePreservesSessionTransitionsAndTechnicianNotes |
| CT-065 | Exportar sessão concluída | P1 | Pendente | Não exercitado nesta etapa |
| CT-066 | Exportar sessão parcial | P1 | Pendente | Não exercitado nesta etapa |
| CT-067 | Histórico | P1 | Pendente | Histórico de fixtures consultado e apresentado em ordem decrescente |
| CT-068 | Pesquisa do histórico | P1 | Pendente | HistoryFiltersStatusComputerAndDate |
| CT-069 | Excluir uma sessão | P1 | Pendente | Confirmação de exclusão e DELETE protegido testados |
| CT-070 | Limpar histórico | P0 | Pendente | Limpeza protege sessão ativa e arquivo externo sentinela |
| CT-071 | Reexportar sessão antiga | P1 | Pendente | Não exercitado nesta etapa |
| CT-072 | Windows 11 validado | P1 | Pendente | Não exercitado nesta etapa |
| CT-073 | Windows 10 de teste | P1 | Pendente | Não exercitado nesta etapa |
| CT-074 | Sensor incompatível | P1 | Pendente | Contrato sintético de sensor indisponível; sensor real pendente |
| CT-075 | Máquina com recursos limitados | P1 | Pendente | Não exercitado nesta etapa |
| CT-076 | Execução prolongada | P0 | Pendente | Não exercitado nesta etapa |
| CT-077 | Gráficos ao vivo | P1 | Pendente | Gráficos sintéticos renderizados; desempenho real pendente |
