# Rastreabilidade — v0.3

Evidências abaixo são locais ou parciais. Os 77 critérios integrais de liberação da v1.0 permanecem pendentes de validação na matriz completa; não se atribui aprovação geral por testes desta etapa.

| ID | Cenário | Prioridade | Critério integral v1.0 | Evidência da v0.3 |
|---|---|---|---|---|
| CT-001 | Instalar via `GCMMonitor-Setup.exe` | P1 | Pendente | Não exercitado nesta etapa |
| CT-002 | Executar Windows 11 sem .NET pré-instalado | P1 | Pendente | Não exercitado nesta etapa |
| CT-003 | Conferir pastas e permissões | P0 | Pendente | Não exercitado nesta etapa |
| CT-004 | Desinstalar removendo dados | P0 | Pendente | Não exercitado nesta etapa |
| CT-005 | Desinstalar preservando dados | P1 | Pendente | Não exercitado nesta etapa |
| CT-006 | Desinstalar após exportar PDF | P0 | Pendente | Não exercitado nesta etapa |
| CT-007 | Desinstalar com teste ativo | P0 | Pendente | Não exercitado nesta etapa |
| CT-008 | Abrir Início | P1 | Pendente | Inventário nativo local e UI real; cobertura completa da plataforma pendente |
| CT-009 | Navegar entre cinco telas | P1 | Pendente | Navegação testada durante sessão e revisão WPF |
| CT-010 | `Habilitar todos` | P1 | Pendente | Onze módulos selecionados para verificação por capacidade; estresse permanece bloqueado, testado |
| CT-011 | `Desabilitar todos` | P1 | Pendente | Desabilitar seleção e validar sessão vazia |
| CT-012 | `Restaurar padrão` | P1 | Pendente | Padrão dos onze módulos/discos detectados restaurado; duração aprovada e estresse desligado |
| CT-013 | Executar módulo isolado | P1 | Pendente | Seleção isolada verificada por contratos e processo demonstrativo |
| CT-014 | Iniciar sem módulos | P1 | Pendente | Seleção vazia bloqueada no contrato e na UI |
| CT-015 | Redimensionar janela / mudar DPI | P2 | Pendente | Renderização em dois tamanhos; DPI/múltiplos monitores reais pendentes |
| CT-016 | Identificação do computador | P1 | Pendente | Inventário real Windows/CPU/RAM/discos/GPU; tipo/VRAM não determinados e matriz ampla pendente |
| CT-017 | Uso da CPU | P1 | Pendente | GetSystemTimes comparado com PDH; tolerâncias de precisão ainda não aprovadas |
| CT-018 | RAM e paginação | P1 | Pendente | APIs nativas e cálculos com fixtures; conceitos físicos/comprometimento/cache distintos |
| CT-019 | Coleta dos processos | P1 | Pendente | PID + início, contadores nativos e cobertura parcial por permissão |
| CT-020 | Top 10 CPU | P1 | Pendente | Acumulador, reutilização de PID e ranking encerrado persistido testados |
| CT-021 | Top 10 RAM | P1 | Pendente | Working set, média ponderada, pico e cobertura testados |
| CT-022 | Temperatura CPU | P1 | Pendente | Contrato WMI/sensor simulado e ausência local tratados; leitura térmica CPU não validada em hardware real |
| CT-023 | GPU e temperatura | P1 | Pendente | Identificação GPU real e PDH por engine local; agrupamento testado; temperatura e correlação LUID-modelo não validadas em hardware |
| CT-024 | Ventoinhas | P1 | Pendente | Fixture RPM, perda de sensor e zero ambíguo cobertos; RPM não validado em hardware real |
| CT-025 | Discos físicos e volumes | P1 | Pendente | Mapeamento WMI físico/partição/volume conferido localmente; cenários complexos pendentes |
| CT-026 | SMART | P1 | Pendente | Associação Storage/qualidade testadas com fixture; fonte SMART local indisponível, sem aprovação de leitura real |
| CT-027 | Atividade de armazenamento | P1 | Pendente | Throughput PDH real, unidade MB/s e seleção física verificados |
| CT-028 | Eventos relevantes | P1 | Pendente | Consulta nativa System/Application com origem/ID/horário/nível; sete dias e limite por log sinalizado |
| CT-029 | Interpretação de eventos | P1 | Pendente | Pares provider/ID testados; evidência sem conclusão causal automática |
| CT-030 | Dispositivos e drivers | P1 | Pendente | PnP/driver nativos e associação por ID completo/único testada, inclusive IDs longos; sem mudanças |
| CT-031 | Windows Update | P1 | Pendente | WUA histórico/reboot local testados; não chama Search/download/install; pendências completas não determinadas |
| CT-032 | Inicialização | P1 | Pendente | Não exercitado nesta etapa |
| CT-033 | Estado de energia | P1 | Pendente | AC/plano consultados nativamente; mudança simulada coberta, sem alteração de política |
| CT-034 | Wi-Fi | P1 | Pendente | WLAN nativo passivo, sinal/enlace/canal; ausência/permissão tratadas; banda e matriz ampla pendentes |
| CT-035 | Estabilidade Wi-Fi | P1 | Pendente | Medições ativas não implementadas nesta etapa; análise detalhada não autoriza ping |
| CT-036 | Bateria | P1 | Pendente | Carga/capacidades e ciclos consultados; fórmulas/associação Tag/unidades relativas cobertas; descarga real não forçada |
| CT-037 | Sem Wi-Fi ou bateria | P1 | Pendente | Ausência de bateria/WLAN com fixtures e estado indisponível; sem zero fictício |
| CT-038 | Monitoramento 5 min | P1 | Pendente | Ensaio passivo real de cinco minutos com onze módulos; ver resumo sanitizado e escopo local |
| CT-039 | Monitoramento 30 min | P1 | Pendente | Não exercitado nesta etapa |
| CT-040 | Monitoramento 1 h | P1 | Pendente | Não exercitado nesta etapa |
| CT-041 | Monitoramento 4 h | P0 | Pendente | Não exercitado nesta etapa |
| CT-042 | Somente módulos pontuais | P1 | Pendente | Sessão pontual real e fixture de quatro horas finalizam sem espera; consultas não repetidas |
| CT-043 | Fases do estresse | P0 | Pendente | Não exercitado nesta etapa |
| CT-044 | Tempo insuficiente para estresse | P1 | Pendente | Não exercitado nesta etapa |
| CT-045 | Encerrar estresse no tempo definido | P0 | Pendente | Não exercitado nesta etapa |
| CT-046 | Cancelar sessão | P0 | Pendente | Cancelamento passivo preserva dados; consulta pontual já concluída mantém seu estado; estresse não executado |
| CT-047 | `Parar estresse` | P0 | Pendente | Não exercitado nesta etapa |
| CT-048 | Falha de sensor | P1 | Pendente | Falhas isoladas, perda de sensor/adaptador e invalidação de séries testadas; segurança térmica não validada |
| CT-049 | Perda térmica essencial em estresse | P0 | Pendente | Não exercitado nesta etapa |
| CT-050 | Encerramento inesperado da UI | P0 | Pendente | Fechamento normal e pipe controlados; perda abrupta de UI completa ainda pendente |
| CT-051 | Falha de comunicação/supervisão | P0 | Pendente | Versão/identidade/tamanho/PID verificados; supervisão de carga não implementada |
| CT-052 | Suspensão/retomada do Windows | P0 | Pendente | Lacuna simulada sem interpolação; suspensão real pendente |
| CT-053 | Mudança de alimentação | P1 | Pendente | Alimentação/plano reais consultados; mudança simulada, sem política de estresse nesta etapa |
| CT-054 | Sem privilégio requerido | P1 | Pendente | Fontes recusadas/ausentes geram indisponibilidade; não solicita elevação nem altera permissões |
| CT-055 | Verificar efeitos colaterais | P0 | Pendente | Fontes somente de leitura; auditoria ampla de efeitos colaterais pendente |
| CT-056 | Privacidade da coleta | P0 | Pendente | Projeções mínimas, sem SSID/BSSID/perfil, mensagens integrais de eventos, IDs PnP persistidos ou conteúdo de processos |
| CT-057 | Persistência incremental | P0 | Pendente | Amostras/estados/registros avançados na mesma transação; origem mista rejeitada e ensaio completo |
| CT-058 | Encerramento inesperado | P0 | Pendente | Motor próprio encerrado em teste; transações recuperadas. Queda elétrica real pendente |
| CT-059 | Reabrir depois de interrupção | P0 | Pendente | Recuperação anterior preservada; migração aditiva schema 2 para 3 e notas testadas |
| CT-060 | Disco cheio/banco inacessível | P0 | Pendente | Falha de escrita simulada antes/depois do início; disco cheio real pendente |
| CT-061 | Escrita + consulta simultâneas | P1 | Pendente | Motor escreve enquanto UI consulta; concorrência prolongada pendente |
| CT-062 | Retenção/limpeza | P1 | Pendente | Limpeza mantém sessão ativa e arquivo externo sentinela |
| CT-063 | Resultados por módulo | P1 | Pendente | Resultados com registros, estatísticas e limites por módulo; reabertura hidrata registros persistidos |
| CT-064 | Observações do técnico | P1 | Pendente | Notas salvas/reabertas sem sobrescrita pelo motor |
| CT-065 | Exportar sessão concluída | P1 | Pendente | Não exercitado nesta etapa |
| CT-066 | Exportar sessão parcial | P1 | Pendente | Não exercitado nesta etapa |
| CT-067 | Histórico | P1 | Pendente | Sessões duráveis/reabertas; paginação além das 100 recentes pendente |
| CT-068 | Pesquisa do histórico | P1 | Pendente | Filtros de computador/data/status testados |
| CT-069 | Excluir uma sessão | P1 | Pendente | Exclusão por chave e confirmação testadas |
| CT-070 | Limpar histórico | P0 | Pendente | Limpeza protege registros ativos e arquivo externo |
| CT-071 | Reexportar sessão antiga | P1 | Pendente | Não exercitado nesta etapa |
| CT-072 | Windows 11 validado | P1 | Pendente | Windows x64 build 26200 local; demais ambientes pendentes |
| CT-073 | Windows 10 de teste | P1 | Pendente | Não exercitado nesta etapa |
| CT-074 | Sensor incompatível | P1 | Pendente | Ausência de provedor, sensor e zero ambíguo tratados; dados prévios preservados com lacuna |
| CT-075 | Máquina com recursos limitados | P1 | Pendente | Não exercitado nesta etapa |
| CT-076 | Execução prolongada | P0 | Pendente | Snapshot limitado a 512 pontos; ensaio real de quatro horas e sobrecarga pendentes |
| CT-077 | Gráficos ao vivo | P1 | Pendente | 26 renderizações WPF em dois tamanhos sem erros de binding; navegação ao vivo nas cinco telas, sessões limitadas no IPC |
