# GCM Monitor — Especificação Mestre de Desenvolvimento (v1.0)

**Nome completo:** Gimenes Coleta e Monitoramento  
**Produto:** aplicativo desktop de diagnóstico e monitoramento para Windows  
**Versão da especificação:** 1.0 — escopo funcional consolidado  
**Data de consolidação:** 06/11/2026  
**Destinatários:** proprietário do projeto, desenvolvedores e ChatGPT Work  
**Repositório planejado:** `Taconi2003/GCM-MONITOR`  
**Idioma:** português do Brasil (pt-BR)

> **Status do documento:** as decisões funcionais e arquiteturais aqui consolidadas foram aprovadas durante o planejamento. A implementação, a compatibilidade efetiva de APIs e sensores, os limites de segurança e a matriz de testes **ainda não foram validados em software executável**. “Aprovado” significa aprovado como requisito, **não** que o programa já passou em testes.

## 1. Objetivo e princípios do produto

O **GCM Monitor** auxilia técnicos de informática a investigar o estado de um computador, executar medições durante um período definido, identificar ocorrências relevantes e emitir **um relatório PDF** com evidências e recomendações cautelosas. O aplicativo **não é uma ferramenta de reparo automático**.

### 1.1 Objetivos

1. Identificar características do equipamento e condições de funcionamento do Windows.
2. Coletar, registrar e exibir métricas de CPU, RAM, armazenamento, GPU, temperaturas, ventoinhas e rede quando disponíveis.
3. Consultar eventos relevantes, drivers/dispositivos, energia, bateria e situação informativa do Windows Update.
4. Permitir diagnóstico modular e opcional, com testes de observação e, mediante consentimento específico, estresse de CPU.
5. Manter uma experiência simples para o técnico: **Início → Monitoramento → Resultados → Histórico**, com **Configurações** acessíveis pela navegação lateral.
6. Registrar sessões parciais em caso de cancelamento, falha do aplicativo ou interrupção do computador, sempre que houver dados recuperáveis.
7. Gerar PDF legível, com observações do técnico, limitações da coleta e evidências que fundamentem as conclusões.
8. Ser instalável e desinstalável de forma controlada, sem prejudicar arquivos do cliente.

### 1.2 Limites inegociáveis

- **Somente diagnóstico e orientação:** não reparar, otimizar, atualizar, desinstalar, desabilitar ou configurar o Windows e seus componentes automaticamente.
- Não atualizar drivers, executar Windows Update, alterar BIOS/UEFI, registro, políticas, plano de energia, serviços ou aplicativos de terceiros.
- Não executar testes destrutivos de disco, rotinas intensivas de gravação, reparos de partição ou procedimentos capazes de apagar dados.
- Não coletar conteúdo de documentos, e-mails, senhas, credenciais, histórico pessoal, pacotes de rede ou dados confidenciais não necessários.
- Nunca inventar leitura de sensor, atribuir causa definitiva a evento ambíguo ou confundir “recurso indisponível” com “falha física”.
- Teste de estresse **separado e desabilitado por padrão**, nunca incluído automaticamente por “Habilitar todos”.
- Segurança, cancelamento e preservação dos dados têm precedência sobre terminar um teste.

## 2. Público-alvo e plataformas

- **Público:** técnicos e profissionais de suporte e manutenção de computadores.
- **Plataforma principal:** Windows 11 x64, em versões/edições validadas.
- **Compatibilidade adicional:** Windows 10 x64 atualizado, quando o executável e suas dependências puderem operar, sem garantia de suporte integral para todas as edições.
- **Arquitetura inicial do pacote:** x64; ARM64 e x86 não incluídos no escopo inicial.
- Na abertura, identificar edição, compilação, versão e arquitetura do sistema operacional.
- Se o ambiente não tiver sido validado, mostrar na **tela Início**: **“Esta versão do Windows pode apresentar limitações em determinados diagnósticos. Alguns recursos podem não funcionar corretamente.”**
- Executar checagem de capacidades **por módulo**; módulos compatíveis continuam disponíveis. Recursos sem suporte são marcados como **Indisponíveis**, com motivo e eventual alternativa.
- Se o próprio aplicativo ou runtime não puder iniciar, informar incompatibilidade durante o instalador sempre que for tecnicamente possível; um aviso dentro da interface não substitui essa verificação.
- Em ambientes não validados, **não disponibilizar estresse** se a supervisão de segurança não puder ser garantida.

## 3. Tecnologias e dependências — decisões aprovadas

| Camada | Escolha |
|---|---|
| Linguagem | **C#** |
| Framework | **.NET 10 LTS** |
| Interface desktop | **WPF + XAML** |
| Organização da interface | **MVVM** |
| Biblioteca MVVM candidata | `CommunityToolkit.Mvvm` |
| Banco local | **SQLite**, acesso via `Microsoft.Data.Sqlite` |
| Gráficos WPF candidatos | `ScottPlot.WPF` |
| Sensores candidatos | `LibreHardwareMonitorLib` |
| Windows/WMI | `System.Management`, APIs nativas do Windows |
| Processos e desempenho | `System.Diagnostics`, Performance Counters/PDH conforme validação |
| Eventos | `System.Diagnostics.Eventing.Reader` / `System.Diagnostics.EventLog` |
| PDF | **PDFsharp + MigraDoc**, sujeito a prova de conceito de layout e licença |
| Instalador | **Inno Setup**, instalador `.exe` |
| Distribuição | **.NET self-contained**, com arquivos e dependências próprios incluídos |

**Importante:** escolher a tecnologia como candidata **não comprova** suporte a todos os sensores, licenças, sistemas ou versões de API. Congelar versões dos pacotes após provas de conceito; registrar licença, origem, atualização e riscos dos componentes externos. Priorizar bibliotecas gratuitas e adequadas à distribuição planejada.

## 4. Arquitetura de execução

### 4.1 Componentes

1. **Aplicativo WPF (processo de interface):** navegação, seleção de módulos, configuração dos testes, gráficos, resultados, observações, histórico, alertas, exportação e diálogo de confirmação. Não executa coletas pesadas na thread gráfica.
2. **Motor de diagnóstico (processo separado por sessão):** coordena coletores, cronômetros, eventos de coleta, gravação incremental, estados e consolidação. Deve sobreviver a falhas não críticas da apresentação, observados os requisitos de segurança.
3. **Supervisor de estresse (componente/processo isolado, sob demanda):** inicia, monitora e interrompe **apenas a carga criada pelo GCM Monitor**; exige autorização adicional; deve falhar para o estado seguro.
4. **Persistência:** SQLite local com registros duráveis, estados de sessão, falhas, resumos e amostras. Escrita centralizada ou serializada para evitar disputa entre processos.
5. **Auxiliar privilegiado (somente se necessário e autorizado):** executa um conjunto mínimo e validado de operações de leitura que precisem de elevação. Não é um serviço permanente.

### 4.2 Comunicação e isolamento

- Usar canal local autenticado e com permissões restritas (por exemplo, named pipes do Windows), com protocolo versionado e validação de mensagens.
- A interface apresenta informações; o **motor determina o estado real** da sessão. Não assumir que fechar uma janela encerra automaticamente todos os processos.
- Estabelecer *heartbeat* entre controlador e supervisor. A perda prolongada de supervisão deve **encerrar o estresse**, com tempo-limite definido e validado nos testes.
- Estabelecer controle de processos filhos por **Job Objects**, incluindo a opção de encerramento ao fechar o último identificador quando aplicável; validar na prática o tratamento de processos órfãos, herança e fechamento.
- Durante falhas da UI, coleta **passiva** pode continuar se o motor estiver íntegro; **estresse ativo não pode permanecer sem supervisão válida**.
- Não criar serviço permanente, tarefa agendada ou processo residente após o uso normal do aplicativo.
- O aplicativo pode ser aberto sem diagnóstico em execução; sessões anteriores são consultadas no Histórico.

### 4.3 Estados de sessão e módulos

**Sessão:** `Não iniciada`, `Validando`, `Em execução`, `Finalizando`, `Concluída`, `Cancelada`, `Interrompida`, `Erro`.  
**Módulos:** `Pendente`, `Executando`, `Concluído`, `Parcial`, `Erro`, `Indisponível`, `Cancelado`.

Cada transição precisa ser persistida com horário, motivo quando aplicável e indicadores de integridade. Uma falha isolada deve marcar o módulo correspondente e não necessariamente cancelar a sessão inteira. Resultados recuperados não podem ser marcados como concluídos se houve interrupção real.

## 5. Módulos do diagnóstico — catálogo fechado da v1.0

Os **11 módulos a seguir** são selecionáveis individualmente na tela Início. Uma função específica de estresse de CPU é oferecida **fora dos 11 módulos**.

| ID | Módulo | Dados e verificações | Limitações importantes |
|---|---|---|---|
| M01 | **Memória RAM e paginação** | RAM total, uso atual/médio/máximo, disponibilidade, pressão de memória, comprometimento e paginação com histórico | Distinguir memória usada, em cache, comprometida e memória do processo; não inferir defeito físico sem evidência |
| M02 | **Processador (CPU)** | Nome, núcleos, threads, utilização, frequência quando disponível, média/pico e gráfico | Frequência e contadores podem variar com hardware e política do sistema; **não executa estresse automaticamente** |
| M03 | **Armazenamento e SMART** | Discos físicos e unidades, tipo NVMe/SATA/HDD, capacidade, espaço livre, atividade, integridade/SMART, temperatura e desgaste quando acessíveis | Mapear volumes para discos corretamente; indisponibilidade de SMART não equivale a saudável; nenhuma escrita de teste |
| M04 | **Eventos relevantes do Windows** | Eventos BugCheck 1001 quando pertinentes, Kernel-Power 41, EventLog 6008, Display/TDR 4101, WHEA-Logger, Disk/StorPort/NTFS e Application Error 1000; origem/ID/horário/severidade/contexto | Event ID isolado não prova causa; usar janela temporal e correlacionar evidências, sem percorrer indiscriminadamente todos os logs |
| M05 | **Bateria** | Presença, energia conectada, carga, capacidade de projeto, capacidade atual, desgaste %, ciclos se expostos, histórico, estimativa de autonomia baseada no consumo | Ausente em desktops; estimativa não equivale a previsão exata; não forçar descarga nem alterar energia |
| M06 | **Windows Update** | Estado informativo disponível, atualizações pendentes conhecidas e reinicializações pendentes quando consultáveis | Não iniciar busca ativa não autorizada, download nem instalação; informar limites da consulta |
| M07 | **Temperaturas e ventoinhas** | CPU, GPU, disco, demais sensores úteis; temperatura, tendência e RPM quando o hardware permitir | Sensor ausente ou bloqueado é **indisponível**, não 0°C/0 RPM nem defeito comprovado |
| M08 | **Processos Top 10** | Ranking de processos por consumo acumulado de CPU e uso de RAM (média/pico), com histórico do intervalo | Incluir processos terminados na sessão; chave por PID + início de processo para evitar reutilização de PID; não ler conteúdo dos processos |
| M09 | **Dispositivos e drivers** | Dispositivos PnP, problemas/códigos, drivers instalados e versões disponíveis | Somente consulta; não instalar, atualizar, reparar ou remover drivers |
| M10 | **Wi-Fi** | Adaptador, estado, sinal, banda, taxa de enlace, reconexões e instabilidade; latência/perda quando autorizado | Sem teste de velocidade volumoso; medições ativas opt-in; respeitar permissões de localização/API |
| M11 | **Estado de energia** | AC/bateria, alimentação, plano de energia atual e mudanças durante a sessão | Não modificar o plano nem impedir suspensão alterando políticas |

**Informações complementares, sem criar um 12º módulo:** identificação geral de hardware, último boot, uptime e programas de inicialização. Esses dados poderão aparecer no resumo ou no módulo Windows conforme disponibilidade e autorização; a consulta não deve executar modificações.

### 5.1 Consultas pontuais versus contínuas

- **Contínuas (quando habilitadas):** CPU, RAM, processos, temperaturas/RPM, atividade dos discos, energia e alguns indicadores de Wi-Fi.
- **Pontuais ou atualização eventual:** identificação de hardware, SMART, Windows Update, drivers, programas de inicialização e varredura histórica de eventos. Não repetir consultas custosas a cada amostra do gráfico.
- Se só houver módulos pontuais, finalizar assim que todos terminarem; o programa **não deve esperar artificialmente** o período de monitoramento escolhido.
- Todos os módulos precisam permitir falhas isoladas e fornecer fonte, timestamp, unidade e qualidade/disponibilidade da medição.

### 5.2 Regras para ranking de processos

- CPU: acumular deltas de tempo de CPU dos processos observados durante a sessão; exibir também indicadores de média/pico de utilização normalizados de forma consistente ao número de processadores lógicos.
- RAM: especificar no relatório o contador adotado (por exemplo, working set ou memória privada); calcular média **ponderada no tempo**, pico e período observado.
- Não descartar processos encerrados; diferenciar processos sem amostragem suficiente daqueles com consumo comprovadamente baixo.
- Proteger contra vazamento de memória: agregar e descartar amostras brutas excessivas quando houver retenção de estatísticas suficientes.

## 6. Durações e regras de diagnóstico

### 6.1 Monitoramento

As únicas opções da versão inicial são **5 minutos, 30 minutos, 1 hora e 4 horas**.

### 6.2 Estresse de CPU — separadamente selecionado

- Desligado por padrão, **inclusive após “Habilitar todos”**.
- Só iniciar depois de autorização específica do técnico e validação de capacidade do ambiente, privilégios e sensores essenciais.
- Durações possíveis, independentemente do monitoramento: **5 minutos, 30 minutos, 1 hora e 4 horas**.
- Requer mecanismos comprovados de observabilidade térmica, encerramento e watchdog. Sem esses mecanismos, **bloquear estresse**, mantendo os demais diagnósticos disponíveis.
- Não presumir limite térmico universal; parâmetros de proteção devem ser projetados com fonte verificável e validados em hardware real. Condições desconhecidas devem levar a comportamento conservador.
- Permitir **“Parar estresse”** sem encerrar a coleta, para observar resfriamento.
- Permitir **“Cancelar diagnóstico”** em qualquer etapa. A carga é interrompida antes dos demais procedimentos de fechamento.
- Uma janela de monitoramento insuficiente para estresse + fases de observação deve gerar proposta clara de extensão do tempo total, **com confirmação explícita** antes de iniciar.

### 6.3 Janela de observação recomendada para estresse

| Duração do estresse | Observação antes | Observação após | Tempo total mínimo de referência |
|---|---:|---:|---:|
| 5 min | 30 s | 1 min | 6 min 30 s |
| 30 min | 1 min | 2 min | 33 min |
| 1 h | 2 min | 3 min | 1 h 5 min |
| 4 h | 3 min | 5 min | 4 h 8 min |

Esses tempos de observação integram o planejamento aprovado e devem ser configuráveis internamente apenas se ensaios técnicos indicarem necessidade de revisão; **não criar novas opções visíveis ao usuário** sem revisão de escopo. Caso o estresse seja interrompido por segurança, registrar o período efetivamente executado e preservar os dados de recuperação disponíveis. A duração exibida da sessão inclui as fases de observação e demais operações necessárias.

### 6.4 Frequências iniciais de coleta (sujeitas a validação)

| Fonte | Cadência inicial |
|---|---|
| CPU e RAM | 2 s |
| Atividade dos discos | 2 s |
| GPU | 2–5 s, conforme fonte |
| Temperaturas e ventoinhas | 5 s |
| Processos | 5 s |
| Eventos, SMART, drivers, atualizações | Consulta pontual/por necessidade |

- A tela Configurações **não permite editar** a cadência na v1.0; o programa gerencia automaticamente o equilíbrio entre qualidade e impacto da coleta.
- Gráficos mantêm somente janela limitada na memória e usam agregação/decimação para exibir sessões longas. O histórico detalhado é persistido conforme política de retenção.
- Cadências não são promessa de amostra exata sob carga; cada amostra tem timestamp e falhas/atrasos devem ser registrados.

## 7. Interface e experiência visual — cinco telas

### 7.1 Identidade visual e navegação

- **Tema exclusivo da v1.0:** escuro (azul-marinho / azul-preto), com realces azul e ciano, aparência tecnológica, cartões arredondados, gráficos legíveis e estados codificados por cor **e por texto**.
- Barra de navegação **fixa à esquerda**, com exatamente cinco itens: **Início, Monitoramento, Resultados, Histórico, Configurações**.
- **Não criar menu “Diagnóstico”**: a tela **Início** inclui seleção, preparação e acionamento dos testes.
- Respeitar resolução, redimensionamento e escalonamento de DPI do Windows. Nenhuma informação essencial pode depender exclusivamente de cor ou animação.
- As imagens dos protótipos aprovados servem como referência visual; caso não estejam disponíveis no repositório, solicitar/registrar o material antes de interpretar detalhes estéticos. Não substituir por layout radicalmente diferente.
- PT-BR como único idioma da v1.0; idioma e tema aparecem como informações fixas na área de Aparência para futura expansão.

### 7.2 Tela 01 — Início

**Cabeçalho:** `Início` / `Visão geral do computador, seleção e execução do diagnóstico`.

**Primeira faixa (cinco cartões de resumo):**

1. **Computador:** ícone adequado (desktop/notebook), nome, edição do Windows, último boot e uptime.
2. **CPU:** modelo, quantidade de núcleos e threads.
3. **RAM:** capacidade total e, quando detectável, módulos físicos instalados.
4. **GPU:** modelo(s), integrada/dedicada e VRAM quando disponibilizada.
5. **Armazenamento:** até dois discos principais, capacidade e espaço livre, barra de ocupação; indicador `+N` para dispositivos adicionais.

**Parte inferior em duas colunas:**

- **Esquerda — Módulos de diagnóstico:** onze opções independentes, com ícone e chave de seleção; comandos `Habilitar todos`, `Desabilitar todos`, `Restaurar padrão`. O estresse não é módulo e nunca é habilitado por `Habilitar todos`.
- **Direita — Configurações do diagnóstico:** duração do monitoramento `[5 min, 30 min, 1 h, 4 h]`; ativação separada do **estresse da CPU**, com sua duração `[5 min, 30 min, 1 h, 4 h]`; seleção de discos físicos para o módulo de armazenamento (todos/recomendados ou seleção individual); caixas `Análise detalhada da bateria` e `Análise detalhada de Wi-Fi`, disponíveis somente com o respectivo módulo ativo.

**Rodapé:** aviso curto de funcionamento observacional: **“O GCM Monitor coleta e analisa informações do sistema. Não realiza alterações nas configurações, drivers ou no sistema operacional.”**; botão principal azul **`Iniciar diagnóstico`**.

- Validar seleções, recursos e condições de segurança antes do início.
- Ao iniciar, direcionar à tela **Monitoramento**.
- Se houver uma sessão ativa e o técnico voltar ao Início, exibir resumo, tempo/progresso e ações **`Abrir monitoramento`** e **`Cancelar diagnóstico`** em vez de criar outra sessão inadvertidamente.
- Exibir aviso de compatibilidade quando apropriado, sem impedir módulos comprovadamente disponíveis.

### 7.3 Tela 02 — Monitoramento

**Cabeçalho:** `Monitoramento` / `Diagnóstico em execução — coletando e analisando dados.`

**Painel principal:** `Diagnóstico em andamento`, descrição da etapa, duração total planejada, barra de progresso e percentual baseado em tempo efetivo versus duração planejada (incluindo fases antes/depois do estresse, quando presentes). Não confundir percentual de tempo com percentual de módulos concluídos. Mostrar estado do estresse e interrupções quando aplicável.

**Gráficos em cartões separados, com eixos, nomes e unidades próprias:** CPU (%), temperatura CPU (°C), RAM (% e/ou GB), atividade de disco (% ou MB/s, por disco), GPU (%), temperatura GPU (°C), ventoinhas (RPM). **Exibir somente métricas realmente disponíveis, selecionadas e com dados suficientes**. Se um sensor ficar indisponível, preservar dados anteriores, indicar a falha e **não produzir pontos fictícios**.

**Faixa `Etapa atual`:** descrever coletas em andamento, consultas pontuais, estresse, observação após estresse e consolidação; pode mostrar atividades simultâneas.

**Controles persistentes:** botão vermelho `Cancelar diagnóstico` e, quando houver carga ativa, `Parar estresse`. Navegar para outra tela não interrompe a coleta. A tela também deve representar ausência de sessão, consultas apenas pontuais, sessão cancelada, erro e finalização, com encaminhamento para **Resultados**.

### 7.4 Tela 03 — Resultados

**Cabeçalho:** `Resultados` / `Resumo e análise do diagnóstico realizado.`

1. **Resumo da sessão:** computador, horário de início/fim, duração efetiva, módulos executados, estado final e informações sobre estresse, se utilizado.
2. **Contadores de achados:** `Normal`, `Atenção`, `Falhas`, `Indisponível`; não criar pontuação global arbitrária de “saúde do computador”.
3. **Principais ocorrências:** achados ordenados por relevância, com evidência, interpretação prudente, impacto potencial e recomendação **não automática**.
4. **Resultados por módulo:** cartões expansíveis, com tabelas, estatísticas, gráficos e referências a dados. Mostrar dados de sensores e SMART quando disponíveis; lista de eventos com fonte, ID e horário; Top 10 processos; drivers; bateria; Wi-Fi; energia; Windows Update; programas de inicialização quando contemplados.
5. **Ocorrências da coleta:** distinguir falhas do **próprio GCM Monitor** de possíveis problemas do **computador analisado**, mostrando indisponibilidades e lacunas de medição.
6. **Observações do técnico:** campo editável e persistente, associado à sessão.
7. **Ação única de saída:** botão **`Exportar relatório PDF`**. Não criar seletor de formatos.

Deve suportar ausência de diagnóstico, sessão ativa sem resultado consolidado, dados parciais/cancelados/interrompidos e erro. Resultados anteriores acessíveis pelo Histórico.

### 7.5 Tela 04 — Histórico

**Cabeçalho:** `Histórico` / `Diagnósticos realizados neste computador.`

- Busca/filtro por nome de computador, data e status.
- Tabela ordenada do mais recente: `Data e hora | Computador | Duração efetiva | Status | Ações`.
- Estados visíveis: concluído, cancelado, interrompido, erro ou parcialmente concluído, conforme modelagem de sessão.
- Ações por linha: abrir/ver resultado, exportar novamente em PDF se os dados estiverem preservados e excluir sessão com confirmação.
- Rodapé: **`Limpar histórico`** (vermelho, pede confirmação) e total de diagnósticos armazenados.
- Pesquisa, paginação/virtualização e retenção projetadas para uso prolongado sem consumir memória desnecessária.
- **Excluir histórico não remove PDFs exportados** para pastas externas e nunca apaga sessões em execução.

### 7.6 Tela 05 — Configurações

**Cabeçalho:** `Configurações` / `Preferências e gerenciamento do GCM Monitor`.

| Área | Conteúdo | Editável na v1.0? |
|---|---|---|
| Aparência | Tema escuro e idioma PT-BR | **Não** — informações fixas |
| Coleta e desempenho | Cadência automática, limitação de recursos, persistência periódica | **Não** — proteção automática |
| Notificações | Avisos informativos | **Sim** |
| Proteções de segurança | Alertas e interrupção obrigatória de estresse | **Não** — nunca podem ser desligadas |
| Relatórios | Pasta padrão de exportação do PDF; usuário pode escolher outra ao exportar | **Sim** |
| Histórico e armazenamento | Uso em disco, número de sessões, política de retenção e limpeza local | **Sim**, com confirmação para remoções |
| Sobre | Produto, versão, informações técnicas e localização da instalação | **Consulta** |

- Retenção de histórico deve ser configurável, com opções adequadas a definir na implementação; **não aplicar exclusão retroativa surpresa**. O usuário deve entender o efeito de cada opção.
- Limpeza de dados locais e limpeza de histórico devem proteger sessões ativas e preservar arquivos externos.
- O GCM Monitor deve administrar somente seus próprios recursos, nunca encerrar aplicativos de terceiros para liberar memória.

## 8. Fluxo completo de uma sessão

1. Inicializar aplicativo, detectar Windows/hardware e carregar configurações.
2. Recuperar e sinalizar sessões interrompidas de execuções anteriores; nunca retomar estresse automaticamente.
3. Usuário configura módulos, discos, duração e eventualmente estresse/diagnósticos detalhados.
4. Validar seleção não vazia, compatibilidade, permissões, sensores indispensáveis, pasta/banco gravável, orçamento de tempo e consentimento específico.
5. Criar `SessionId`, registrar opções, versões e estados iniciais.
6. Iniciar motor de coleta; executar consultas pontuais e amostragem periódica conforme módulos selecionados.
7. Se houver estresse autorizado, coletar linha de base → iniciar carga supervisionada → coletar durante a carga → encerrar carga → observar recuperação.
8. Persistir transições, dados e falhas incrementalmente; atualizar interface por canal local.
9. Em cancelamento: **interromper carga primeiro**, finalizar módulos, gravar estado parcial e consolidar o que foi coletado.
10. Em conclusão: consolidar estatísticas, ocorrências e achados com referências às evidências; abrir Resultados.
11. Técnico revisa, insere observações, exporta único PDF ou acessa depois pelo Histórico.
12. Ao fechar normalmente, encerrar componentes sob demanda e liberar recursos/identificadores/arquivos.

### 8.1 Princípios de interpretação

- Separar **observação**, **hipótese/possível causa** e **recomendação**.
- Registrar fonte da informação, horário, unidade, período e disponibilidade.
- Não afirmar que um problema de Windows foi causado por um evento se houver somente correlação temporal.
- Não declarar “saudável” um dispositivo cujo SMART ou sensor não foi acessível.
- Não considerar falhas do coletor como defeitos do computador.
- Conclusões e recomendações devem ser compreensíveis para o técnico e refletir os dados coletados.

## 9. Persistência, banco de dados e recuperação

### 9.1 Caminhos locais — instalação por usuário

Usar APIs de diretórios especiais do Windows; os exemplos abaixo são caminhos lógicos, não códigos fixos para unidade `C:`.

| Tipo | Local padrão |
|---|---|
| Programa e dependências | `%LOCALAPPDATA%\Programs\GCM Monitor\` |
| Dados locais do programa | `%LOCALAPPDATA%\GCM Monitor\` |
| Banco SQLite | `%LOCALAPPDATA%\GCM Monitor\Data\gcm-monitor.db` |
| Arquivos transitórios da sessão | `%LOCALAPPDATA%\GCM Monitor\Sessions\` |
| Logs da aplicação | `%LOCALAPPDATA%\GCM Monitor\Logs\` |
| Configurações | `%LOCALAPPDATA%\GCM Monitor\Settings\` |
| PDFs exportados | Pasta selecionada pelo técnico; padrão sugerido: `Documentos\GCM Monitor\Relatórios` |

O banco é por **usuário Windows**; não há requisito de compartilhamento de histórico entre contas do mesmo computador. Os PDFs exportados externamente pertencem ao fluxo do técnico e **não são apagados automaticamente**.

### 9.2 Estrutura lógica mínima dos dados

- **Sessions:** id, computador/ambiente, opções, início/fim, duração planejada/efetiva, estado e versão do aplicativo.
- **ModuleRuns:** módulo, estado, período efetivo, fonte, limitações, erro e contagem de leituras.
- **Measurements:** sessão, métrica, componente/sensor, valor, unidade, timestamp, validade e metadados mínimos.
- **Events/Findings:** ocorrências relevantes, evidências vinculadas, severidade, interpretação e recomendação.
- **ProcessAggregates:** chave estável de processo, estatísticas de CPU e RAM acumuladas durante a sessão.
- **TechnicianNotes:** observações inseridas manualmente, data de atualização.
- **ExportHistory/Settings:** metadados de exportação e preferências locais; não depender do PDF externo para reabrir sessões que ainda estejam no banco.

A implementação pode ajustar nomes e normalização; a semântica e a recuperação não podem ser perdidas.

### 9.3 Integridade e desempenho

- Usar **SQLite WAL**, com transações curtas, política de checkpoint e fechamento correto de conexões. Arquivos `-wal` e `-shm` fazem parte do ciclo normal de funcionamento.
- Configuração de sincronização deve priorizar durabilidade das transações confirmadas nos cenários de falha, com impacto de I/O medido; não prometer que o último ponto ainda em memória sobreviverá a desligamento abrupto.
- Gravar dados e estados periodicamente, em lotes pequenos, sem bloquear os gráficos.
- Evitar carregar quatro horas de medições brutas na memória de uma vez; usar janelas e agregação para gráficos, índices para consultas e limpeza controlada.
- Se escrita ficar impossível (disco cheio, banco indisponível), registrar quando possível, informar ao usuário e interromper qualquer teste ativo cuja segurança dependa da persistência; proteger contra loops de escrita infinita.
- Ao iniciar, localizar sessões sem estado terminal, marcá-las **Interrompidas/Parciais** após verificação; consolidar somente evidências recuperadas. Não simular continuidade.
- Limpeza de histórico individual e total requer confirmação e não pode apagar sessão ativa.

## 10. Instalação, atualização e desinstalação

### 10.1 Instalação

- **Inno Setup** cria `GCMMonitor-Setup.exe` para Windows x64.
- Publicar .NET 10 **self-contained** com dependências necessárias para dispensar instalação manual do runtime.
- **Instalação por usuário** em `%LOCALAPPDATA%\Programs\GCM Monitor\`, com atalhos e registro em **Aplicativos instalados**.
- Instalação normal não necessita elevação apenas para copiar arquivos no próprio perfil; solicitar privilégios apenas para recursos específicos, quando necessários.
- Verificar requisitos mínimos e arquitetura; não prometer plena compatibilidade com Windows 10 fora da matriz de ensaios.
- Sem serviços permanentes, tarefas agendadas, atualizadores silenciosos, drivers de baixo nível instalados sem autorização ou alteração automática de configurações do Windows.
- Atualizações futuras do GCM Monitor, se implementadas, devem preservar ou migrar dados do usuário por procedimento testado; atualizador automático **não integra o escopo v1.0**.

### 10.2 Desinstalação

1. Solicitar encerramento de sessão ativa; caso haja estresse, garantir seu desligamento antes de prosseguir.
2. Avisar sobre relatórios/históricos ainda não exportados e oferecer confirmação.
3. Permitir escolher **remover dados locais gerenciados pelo programa** ou **preservá-los intencionalmente**.
4. Encerrar motor, supervisor, auxiliar privilegiado, canais e conexões ao SQLite; tratar falhas com mensagem clara e sem fingir sucesso.
5. Remover executáveis, bibliotecas, atalhos, registros e componentes criados pelo instalador.
6. Se selecionada limpeza: remover somente banco, WAL/SHM, logs, configurações e arquivos temporários sob caminhos **previamente validados e de propriedade do GCM Monitor**.
7. **Preservar PDFs exportados em caminhos externos** e todos os dados de terceiros.
8. Validar o resultado e informar qualquer resíduo que não pôde ser removido.

**Não prometer erradicação forense:** o Windows e soluções de backup/antivírus podem reter históricos, caches, logs ou cópias fora do controle do aplicativo. Nunca fazer exclusão recursiva em caminho variável não validado.

### 10.3 Privilégios e proteção

- Interface WPF com privilégios normais, sempre que possível.
- Consultas que exijam administrador feitas por auxiliar restrito, autenticado e temporário.
- Não aceitar comandos arbitrários de processos locais; lista de operações privilegiadas deve ser explícita e **somente leitura**, salvo operações técnicas imprescindíveis sobre componentes internos do próprio GCM Monitor.
- Biblioteca de sensores com eventual driver de baixo nível exige revisão de origem, licenciamento, assinatura, escopo, autorização e remoção; **não instalar silenciosamente**.
- Recomendar assinatura digital do instalador/executáveis antes da distribuição ampla.

## 11. Relatório PDF — contrato funcional

- Na tela Resultados, fornecer **um único botão `Exportar relatório PDF`**. Não oferecer formatos alternativos na v1.0.
- Usuário pode escolher onde salvar; diretório padrão configurável.
- Conteúdo mínimo: identificação do equipamento e Windows, versão do GCM Monitor, intervalo da sessão, módulos selecionados/executados, estado e tempos efetivos, métodos/limitações, estatísticas com unidade, gráficos relevantes, Top 10 processos, informações de disco/SMART, eventos e drivers relevantes, bateria/rede/energia quando aplicáveis, principais ocorrências e recomendações, problemas do coletor, observações do técnico.
- Indicadores **não coletados** devem constar como `Não disponível`/`Não executado` com motivo — nunca preenchimento artificial.
- Se sessão foi cancelada/interrompida, destacar **“Relatório parcial”** e os trechos ausentes; não apresentar conclusões como se todos os testes tivessem terminado.
- Revisitar resultado salvo deve permitir nova exportação enquanto dados de origem existirem; exclusão definitiva da sessão pode inviabilizar regeneração.
- Informações pessoais ou identificadores do cliente devem ser mínimas e apenas as necessárias para uso técnico; não embutir dados sensíveis desnecessários.

## 12. Resiliência, segurança operacional e privacidade

| Cenário | Resposta exigida |
|---|---|
| Falha isolada de coletor | Marcar módulo `Erro/Parcial`, registrar motivo, manter demais módulos quando seguros |
| Sensor não oferecido pelo equipamento | `Indisponível`, motivo e sem valor fictício |
| Perda de sensor térmico essencial sob estresse | Encerrar carga e registrar risco |
| Perda de heartbeat/supervisor | Encerrar carga de forma automática e verificável |
| Travamento da interface | Não interromper desnecessariamente coleta passiva; **não deixar carga sem supervisão** |
| Cancelamento solicitado | Encerrar primeiro o estresse, depois coletas, consolidar dados parciais |
| Seleção `Parar estresse` | Encerrar somente carga, manter coletas até o tempo definido/observação pós-estresse |
| Suspensão do Windows | Registrar lacuna ao retomar; **não retomar estresse automaticamente** |
| BSOD/queda de energia | Recuperar o que estava persistido; registrar sessão interrompida |
| Falha de gravação | Avisar e agir conservadoramente; sem estresse desprovido de proteção |
| Módulo sem privilégios | Informar limitação, solicitar autorização quando justificada, ou continuar sem o módulo |
| Fechamento/desinstalação | Nenhum processo de carga ou coleta órfão deve permanecer |

### 12.1 Política de testes ativos

- Testes de estresse devem ser validados inicialmente em **ambiente controlado**, não em máquinas de clientes.
- Não forçar BSOD, superaquecimento, queda de energia ou falha física como método de validação. Simular encerramentos e erros de processo em laboratório/VM quando apropriado.
- Usar temperatura como sinal de proteção **somente com leitura confiável e hardware coberto pela política de segurança**. Não construir “proteção” fictícia baseada em um único sensor incapaz de identificar situações perigosas.
- Garantias absolutas de integridade do hardware são impossíveis; o produto deve minimizar riscos com supervisão, consentimento, limite de duração e interrupção fail-safe.

### 12.2 Logs e privacidade

- Registrar operações técnicas, falhas, versões e timestamps, com retenção limitada.
- Não gravar tokens, senhas, conteúdo de arquivos, mensagens ou pacotes de rede em logs.
- Ao compartilhar relatórios e evidências em GitHub/Work, usar dados fictícios ou anonimizados; **não versionar dados reais de cliente**.
- O `.gitignore` deve excluir bases SQLite reais e seus auxiliares, relatórios reais, logs, dumps de memória, builds, arquivos temporários, segredos e caches.

## 13. Critérios de aceitação e estratégia de testes

### 13.1 Prioridades

- **P0 — Crítico:** segurança de estresse, controle, integridade dos registros, preservação de arquivos de terceiros e impossibilidade de operações indesejadas. **Zero P0 reprovado na liberação.**
- **P1 — Obrigatório:** funcionalidades essenciais do escopo em ambientes declarados suportados. **Todos os P1 aplicáveis precisam passar.**
- **P2 — Complementar:** refinamentos não críticos que podem ser registrados para correção posterior se não prejudicarem segurança nem integridade.

**Resultados possíveis:** `Pendente`, `Aprovado`, `Reprovado`, `Bloqueado`, `Não aplicável`. Um recurso fisicamente indisponível pode gerar `Não aplicável` para teste de leitura daquele sensor, **mas o tratamento de indisponibilidade deve ser testado e aprovado**.

Cada teste precisa registrar versão/build, identificador, máquina e SO (de forma compatível com privacidade), data, passos, resultado, evidência e eventual defeito vinculado.

### 13.2 Critérios de precisão

- Comparar **mesma métrica, unidade, período e fonte comparável**. Não exigir igualdade entre contadores que representam conceitos diferentes.
- CPU: referência do Windows, alinhamento da janela de amostragem e normalização por processadores lógicos.
- RAM: distinguir memória disponível, ocupada, cache, comprometimento e memória de processos.
- Processos: validar CPU acumulada e RAM média/pico, inclusive processos terminados.
- Sensores: comparar leitura do **mesmo sensor** com ferramenta de referência; indisponibilidade não é zero.
- Discos: mapear unidade/volume/disco físico corretamente; SMART comparado com fonte equivalente, sem inferir integridade a partir da ausência de dados.
- Bateria: verificar fórmula da degradação e caracterizar autonomia como estimativa.
- **Tolerâncias numéricas ainda precisam ser definidas após as provas de conceito**, com referência reproduzível; não inventar margens de erro antes dos ensaios.

### 13.3 Ambientes mínimos de ensaio

Desktop Windows 11 (GPU integrada); notebook Windows 11 (bateria/Wi-Fi); Windows 11 com SSD + HD; equipamento com GPU dedicada; Windows 10 atualizado para a política de compatibilidade; computador sem certos sensores; máquina de laboratório/VM para injeção segura de falhas. Testar as durações mais longas em laboratório supervisionado.

### 13.4 Matriz completa — 77 casos de teste

Os casos abaixo são **planejados e não executados**. Os critérios detalhados e as evidências devem ser desdobrados em testes automatizados e roteiros manuais durante a implementação. Cada linha expressa o **resultado mínimo esperado**.

#### Grupo A — Instalação e desinstalação

| ID | Cenário | Critério mínimo de aprovação | Prioridade |
|---|---|---|---|
| CT-001 | Instalar via `GCMMonitor-Setup.exe` | Instalação bem-sucedida e registrada no Windows | P1 |
| CT-002 | Executar Windows 11 sem .NET pré-instalado | Aplicativo inicia com runtime fornecido pelo pacote | P1 |
| CT-003 | Conferir pastas e permissões | Binários e dados nos locais definidos, sem permissões indevidas | P0 |
| CT-004 | Desinstalar removendo dados | Componentes e dados próprios são excluídos, sem resíduos ativos | P0 |
| CT-005 | Desinstalar preservando dados | Dados locais são preservados intencionalmente, sem serviços/processos remanescentes | P1 |
| CT-006 | Desinstalar após exportar PDF | PDFs externos permanecem; dados próprios selecionados são removidos | P0 |
| CT-007 | Desinstalar com teste ativo | Estresse é encerrado e processos auxiliares terminam antes da remoção | P0 |

#### Grupo B — Interface e seleção de módulos

| ID | Cenário | Critério mínimo de aprovação | Prioridade |
|---|---|---|---|
| CT-008 | Abrir Início | Informações gerais do computador corretas ou marcadas indisponíveis | P1 |
| CT-009 | Navegar entre cinco telas | Navegação sem perda da sessão nem bloqueio dos gráficos | P1 |
| CT-010 | `Habilitar todos` | Seleciona os 11 módulos; **estresse permanece desligado** | P1 |
| CT-011 | `Desabilitar todos` | Desmarca os módulos opcionais e não inicia diagnóstico indevido | P1 |
| CT-012 | `Restaurar padrão` | Seleção recomendada restaurada | P1 |
| CT-013 | Executar módulo isolado | Somente módulo selecionado/dependências técnicas necessárias executam | P1 |
| CT-014 | Iniciar sem módulos | Aviso e impedimento de sessão vazia | P1 |
| CT-015 | Redimensionar janela / mudar DPI | Sem cortes críticos, sobreposição ou controles inacessíveis | P2 |

#### Grupo C — Coleta de hardware e desempenho

| ID | Cenário | Critério mínimo de aprovação | Prioridade |
|---|---|---|---|
| CT-016 | Identificação do computador | Windows/CPU/RAM/dispositivos consistentes com o equipamento | P1 |
| CT-017 | Uso da CPU | Valores e tendências coerentes com referência comparável do Windows | P1 |
| CT-018 | RAM e paginação | Cálculos e unidades corretos; diferentes conceitos não confundidos | P1 |
| CT-019 | Coleta dos processos | PID, início, identidade e métricas válidas | P1 |
| CT-020 | Top 10 CPU | Consumo cumulativo por sessão, inclusive processos encerrados | P1 |
| CT-021 | Top 10 RAM | Média/pico pela métrica definida e intervalo correto | P1 |
| CT-022 | Temperatura CPU | Fonte/sensor, horário, unidade e valor válidos | P1 |
| CT-023 | GPU e temperatura | Métricas vinculadas à GPU correta e devidamente disponíveis | P1 |
| CT-024 | Ventoinhas | RPM real quando disponível; ausência não significa ventoinha parada | P1 |
| CT-025 | Discos físicos e volumes | Mapeamento físico/lógico, capacidade e tipos coerentes | P1 |
| CT-026 | SMART | Coleta dos indicadores acessíveis sem operações destrutivas | P1 |
| CT-027 | Atividade de armazenamento | Unidade e disco corretos, com medidas coerentes | P1 |

#### Grupo D — Diagnósticos do Windows

| ID | Cenário | Critério mínimo de aprovação | Prioridade |
|---|---|---|---|
| CT-028 | Eventos relevantes | Identificar corretamente eventos presentes e suas fontes | P1 |
| CT-029 | Interpretação de eventos | Contextualizar IDs/origem/horário; não afirmar causalidade indevida | P1 |
| CT-030 | Dispositivos e drivers | Problemas PnP e versões disponíveis identificados | P1 |
| CT-031 | Windows Update | Estado informativo sem atualização/instalação/busca ativa não autorizada | P1 |
| CT-032 | Inicialização | Programas/estados disponíveis identificados sem alteração | P1 |
| CT-033 | Estado de energia | AC, bateria e plano informados quando disponíveis | P1 |
| CT-034 | Wi-Fi | Adaptador, conexão, sinal e taxas corretos | P1 |
| CT-035 | Estabilidade Wi-Fi | Ping/perda medidos **somente após consentimento** | P1 |
| CT-036 | Bateria | Saúde, capacidade, ciclos e autonomia tratados com estimativas adequadas | P1 |
| CT-037 | Sem Wi-Fi ou bateria | Módulos indisponíveis sem travamento da sessão | P1 |

#### Grupo E — Execução e duração

| ID | Cenário | Critério mínimo de aprovação | Prioridade |
|---|---|---|---|
| CT-038 | Monitoramento 5 min | Execução coerente com o período configurado | P1 |
| CT-039 | Monitoramento 30 min | Dados consistentes durante toda a coleta | P1 |
| CT-040 | Monitoramento 1 h | Estabilidade e recursos sob controle | P1 |
| CT-041 | Monitoramento 4 h | Sem vazamento/crescimento descontrolado; sessão e relatório válidos | P0 |
| CT-042 | Somente módulos pontuais | Encerrar ao completar consultas, sem espera artificial | P1 |
| CT-043 | Fases do estresse | Antes, durante e depois distinguidos e registrados | P0 |
| CT-044 | Tempo insuficiente para estresse | Solicitar extensão de sessão com confirmação | P1 |
| CT-045 | Encerrar estresse no tempo definido | Todos os processos de carga encerrados | P0 |

#### Grupo F — Segurança, falhas e cancelamento

| ID | Cenário | Critério mínimo de aprovação | Prioridade |
|---|---|---|---|
| CT-046 | Cancelar sessão | Interromper estresse e coletas; preservar relatório parcial | P0 |
| CT-047 | `Parar estresse` | Encerrar só a carga e manter monitoramento | P0 |
| CT-048 | Falha de sensor | Registrar erro e continuar módulos independentes | P1 |
| CT-049 | Perda térmica essencial em estresse | Carga encerrada automaticamente com ocorrência registrada | P0 |
| CT-050 | Encerramento inesperado da UI | Carga encerrada; dados persistidos preservados | P0 |
| CT-051 | Falha de comunicação/supervisão | Encerramento seguro e verificável da carga | P0 |
| CT-052 | Suspensão/retomada do Windows | Lacuna registrada; estresse não reinicia sozinho | P0 |
| CT-053 | Mudança de alimentação | Estado capturado e política de segurança aplicada | P1 |
| CT-054 | Sem privilégio requerido | Informação clara, solicitação pontual ou indisponibilidade do módulo | P1 |
| CT-055 | Verificar efeitos colaterais | Nenhuma alteração corretiva ou configuração do sistema sem autorização | P0 |
| CT-056 | Privacidade da coleta | Sem senhas, documentos, conteúdo de rede ou dados desnecessários | P0 |

#### Grupo G — Banco de dados e recuperação

| ID | Cenário | Critério mínimo de aprovação | Prioridade |
|---|---|---|---|
| CT-057 | Persistência incremental | Medições com sessão, horário e origem são confirmadas no banco | P0 |
| CT-058 | Encerramento inesperado | Transações já confirmadas permanecem recuperáveis | P0 |
| CT-059 | Reabrir depois de interrupção | Sessão aparece como interrompida; estresse não retoma | P0 |
| CT-060 | Disco cheio/banco inacessível | Falha detectada e resposta conservadora, especialmente no estresse | P0 |
| CT-061 | Escrita + consulta simultâneas | UI consulta sem corromper/interromper gravação | P1 |
| CT-062 | Retenção/limpeza | Remove somente registros elegíveis, nunca a sessão ativa | P1 |

#### Grupo H — Resultados, PDF e histórico

| ID | Cenário | Critério mínimo de aprovação | Prioridade |
|---|---|---|---|
| CT-063 | Resultados por módulo | Executados, parciais e indisponíveis diferenciados | P1 |
| CT-064 | Observações do técnico | Texto gravado e recuperável pela sessão | P1 |
| CT-065 | Exportar sessão concluída | PDF completo e legível com achados e gráficos corretos | P1 |
| CT-066 | Exportar sessão parcial | PDF sinaliza cancelamento/erro/lacunas claramente | P1 |
| CT-067 | Histórico | Sessões em ordem decrescente com informações corretas | P1 |
| CT-068 | Pesquisa do histórico | Filtragem por computador, data e status funciona | P1 |
| CT-069 | Excluir uma sessão | Somente registro selecionado é removido após confirmação | P1 |
| CT-070 | Limpar histórico | Confirmação; não afeta PDF externo nem sessão ativa | P0 |
| CT-071 | Reexportar sessão antiga | Gera/recupera PDF quando os dados necessários ainda existem | P1 |

#### Grupo I — Compatibilidade e desempenho

| ID | Cenário | Critério mínimo de aprovação | Prioridade |
|---|---|---|---|
| CT-072 | Windows 11 validado | Funcionalidades suportadas operam corretamente | P1 |
| CT-073 | Windows 10 de teste | Exibe aviso correto e só utiliza recursos disponíveis | P1 |
| CT-074 | Sensor incompatível | Indisponível sem comprometer outros módulos | P1 |
| CT-075 | Máquina com recursos limitados | Interface responsiva e impacto de coleta aceitável | P1 |
| CT-076 | Execução prolongada | RAM, logs e banco sem crescimento ilimitado injustificado | P0 |
| CT-077 | Gráficos ao vivo | Renderização não bloqueia coleta nem controles | P1 |

### 13.5 Regras de liberação

A **v1.0** só poderá ser considerada pronta para uso em clientes quando:

1. Todos os testes **P0 aplicáveis** estiverem aprovados, com evidências.
2. Todos os testes **P1 aplicáveis** às funcionalidades anunciadas e aos ambientes declarados como suportados estiverem aprovados.
3. Nenhum processo de carga puder ficar órfão nas falhas testadas; políticas de parada segura e supervisão estiverem comprovadas.
4. Cancelamento, recuperação, persistência, PDF e remoção dos dados tiverem passado nos respectivos testes.
5. Sessões longas tiverem sido testadas em hardware real e os recursos consumidos tiverem sido medidos.
6. Limitações de sensores e Windows estiverem documentadas; ausências não forem apresentadas como resultados saudáveis.
7. Eventuais P2 pendentes estiverem registrados e classificados como incapazes de afetar segurança, confiabilidade ou interpretação técnica.
8. A versão de cada pacote, licenças e artefatos de instalação estiverem documentados.
9. Relatórios e dados de clientes não forem inseridos no repositório público/compartilhado.

**Não declarar teste aprovado antes de executá-lo.** A existência de código, de compilação bem-sucedida ou de interface visual não substitui testes integrados e validação em hardware.

## 14. Plano de implementação incremental para o Work

| Entrega | Escopo | Condição para avançar |
|---|---|---|
| **v0.1 — Estrutura** | Solução C#/.NET 10, WPF/MVVM, DI, arquitetura de processos, contratos, tema escuro, navegação e cinco telas com estados demonstrativos | Compila, navegação responsiva, testes unitários básicos e estrutura modular |
| **v0.2 — Coleta básica** | CPU, RAM, processos, identificação de hardware, discos/espaço, motor de sessão, SQLite, gráficos reais e cancelamento passivo | Dados comparáveis ao Windows, histórico íntegro e falhas simuladas tratadas |
| **v0.3 — Diagnósticos avançados** | Temperaturas, ventoinhas, GPU, SMART, bateria, Wi-Fi, drivers, eventos, energia e Windows Update informativo | Cobertura por capacidade, dados indisponíveis corretos, referências rastreáveis |
| **v0.4 — Estresse protegido** | Supervisor isolado, consentimento, cronômetro, fases, Job Objects, heartbeat, interrupção e prova de conceito de segurança | **Todos os P0 associados ao estresse aprovados em laboratório**; caso contrário, estresse não liberado |
| **v1.0 — Distribuição** | PDF final, histórico completo, configurações, Inno Setup, desinstalação, privacidade, matriz completa, documentação e testes longos | Todos os critérios de liberação cumpridos |

### 14.1 Instruções obrigatórias ao Work

1. **Usar esta especificação como fonte central**. Não incluir funcionalidades novas por conta própria; registrar dúvidas reais como decisões pendentes, mantendo premissas seguras.
2. Criar projetos separados por responsabilidade — UI, domínio/contratos, coletores, persistência, supervisor e testes — evitando referências circulares.
3. Implementar antes um **esqueleto executável** e testes automatizados que validem contratos, persistência e estados; não tentar entregar os 11 módulos e o estresse em uma única alteração.
4. Construir coletor por coletor com interface/contrato testável; permitir substituir fontes reais por *mocks* para testes determinísticos.
5. Validar fontes nativas e sensores **por prova de conceito** antes de afirmar que determinado equipamento expõe temperatura, RPM, SMART etc.
6. Separar a telemetria real do rendering; nenhuma consulta custosa deve ser disparada diretamente pelo ciclo de pintura de gráficos.
7. Tratar cada CT da matriz como requisito de verificação e registrar evidências dos testes executados.
8. Não registrar dados sensíveis ou reais de cliente no repositório; fornecer dados sintéticos para demonstração e testes.
9. **Não desenvolver estresse como componente de uso livre antes de existir supervisão comprovada.** Se a implementação de segurança falhar, entregar o recurso bloqueado e documentar a limitação.
10. Produzir README de desenvolvimento, guia de build local, instruções do instalador, versões de pacotes e procedimentos para replicar testes.
11. Executar testes e build disponíveis a cada entrega. Se algo não puder ser testado em ambiente automatizado, declarar explicitamente `Não validado em hardware real`, sem simular aprovação.
12. Realizar alterações no repositório por commits pequenos, nomeados, preferencialmente com branches/PRs revisáveis.

### 14.2 Estrutura sugerida de repositório

```text
GCM-MONITOR/
├── README.md
├── .gitignore
├── docs/
│   ├── ESPECIFICACAO_MESTRE_GCM_MONITOR_v1.0.md
│   ├── requisitos/
│   ├── arquitetura/
│   ├── interface/
│   │   └── prototipos/
│   └── testes/
├── src/
│   ├── GcmMonitor.App/              # WPF
│   ├── GcmMonitor.Core/             # entidades e contratos
│   ├── GcmMonitor.Collector/        # fontes/coletores
│   ├── GcmMonitor.Persistence/      # SQLite
│   ├── GcmMonitor.Engine/           # coordenação por sessão
│   ├── GcmMonitor.StressSupervisor/ # segurança, sob demanda
│   └── GcmMonitor.PrivilegedHelper/ # opcional, somente leitura
├── tests/
│   ├── GcmMonitor.UnitTests/
│   └── GcmMonitor.IntegrationTests/
└── installer/
    └── InnoSetup/
```

A estrutura é uma **proposta de organização**, não uma imposição de nomes de assemblies. `src/` e `tests/` não precisam existir fisicamente antes do desenvolvimento; o Git não versiona pastas vazias sem arquivos.

## 15. Pontos técnicos ainda sujeitos a prova de conceito (sem mudar o escopo aprovado)

1. **Leitura de sensores e permissões:** cobertura real de CPU, GPU, ventoinhas, SSD/HDD por fabricante, possível necessidade de drivers e impacto no ambiente do cliente.
2. **Política de limites de segurança do estresse:** definir e validar limites com fontes adequadas ao hardware; testar falhas do próprio supervisor e medição térmica.
3. **Compatibilidade de Windows 10 com .NET 10:** distinguir “executa em laboratório” de “suporte oficial”; fixar matriz de versões/edições testadas.
4. **Bibliotecas e licenças:** verificar versões exatas, distribuição de componentes nativos, obrigações das licenças, atualizações e assinatura de executáveis.
5. **Cadências e precisão:** medir sobrecarga e estabelecer tolerâncias por API e métrica após a prova de conceito.
6. **Retenção e tamanho do banco:** definir limites operacionais a partir de dados de ensaio sem sacrificar utilidade do histórico.
7. **Consulta de atualizações:** garantir que seja estritamente informativa, sem iniciar busca ativa do Windows Update sem autorização.
8. **Comunicação e elevação:** especificar ACLs de canais, autenticação, tratamento de queda de processo e permissões do helper.
9. **Geração de relatório:** renderização de gráficos/tabelas, tratamento de grandes sessões e custo de PDF em máquinas modestas.
10. **Compatibilidade visual:** recuperar e versionar os protótipos aprovados para implementação fiel do tema escuro.

Esses itens são **tarefas de validação de engenharia**, não autorização para alterar unilateralmente os requisitos. Mudanças de comportamento devem ser documentadas e aprovadas.

## 16. Controle de mudanças e rastreabilidade

- Esta especificação é a referência de escopo da **v1.0**.
- Alterações futuras devem registrar: **ID, data, descrição, motivo, impacto, aprovação e casos de teste afetados**.
- Nenhuma alteração pode reduzir silenciosamente as proteções aprovadas para estresse, cancelamento, privacidade e desinstalação.
- Pacotes e APIs podem ser substituídos se houver justificativa técnica documentada e o comportamento final continuar atendendo aos critérios.
- Manter changelog e versão de documento distintos da versão de aplicação.

## 17. Próxima ação para o Work

**Objetivo do primeiro ciclo de desenvolvimento:** preparar a solução .NET 10/WPF e o repositório, integrar MVVM, tema escuro, cinco telas navegáveis, modelos de sessão e testes básicos, **sem executar estresse nem afirmar que os sensores já estão validados**. Entregar código compilável, instruções de build, testes executados, limitações e próximos passos, respeitando a entrega **v0.1** deste documento.

---

**Fim da Especificação Mestre — GCM Monitor v1.0**  
**Estado de desenvolvimento nesta data:** planejamento consolidado; implementação ainda não iniciada neste documento; casos CT-001 a CT-077 pendentes de execução.
