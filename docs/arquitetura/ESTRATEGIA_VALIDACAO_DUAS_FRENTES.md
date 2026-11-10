# Estratégia de validação em duas frentes

**Escopo.** Evolução da prévia 7 e preparação para uso em laboratório. A
[Especificação Mestre v1.0](../ESPECIFICACAO_MESTRE_GCM_MONITOR_v1.0.md) é a autoridade funcional.
As duas frentes podem avançar em paralelo. A aprovação de uma evidência passiva vale para a
versão, a configuração e o cenário ensaiados. A liberação final para clientes segue a seção 13.5.

## A. Diagnóstico e monitoramento passivo

- Manter os onze módulos disponíveis conforme as capacidades do equipamento. Avaliar o suporte por
  fonte e métrica. A falta de um sensor de CPU não impede as demais fontes de temperatura, GPU,
  ventoinha ou outros módulos. O diagnóstico mantém as durações aprovadas de 5 min, 30 min, 1 h e 4 h.
- Para temperatura informativa, identificar o sensor e a fonte, validar unidade, valor, horário e
  qualidade, e preservar esses dados em Resultados, Histórico e PDF. Distinguir pacote, núcleo,
  zona ACPI e GPU. Se não houver fonte compatível, permissão ou leitura válida, mostrar o motivo
  disponível e manter a lacuna na série. A cobertura pode crescer por fonte, API ou família de
  sensores, sem cadastro manual de cada modelo de CPU. A disponibilidade depende da capacidade
  detectada e da qualidade do sinal, com avaliação separada da qualificação para proteção.
  Na matriz de compatibilidade, incluir ventoinha sem RPM, armazenamento sem SMART acessível,
  desktop sem bateria e versão de Windows ainda sem evidência. Preservar respectivamente a ausência
  de valor, a limitação de acesso, o motivo de não aplicação e o escopo de suporte declarado.
- Usar o CT-022 para verificar identidade, unidade, horário e valor, além da consistência da
  apresentação. Comparar o mesmo sensor em janelas alinhadas e registrar idade da consulta,
  lacunas, dispersão e valores discrepantes. Ampliar a referência em repouso e uso normal, com
  e sem tomada, antes de publicar uma tolerância numérica. Nas leituras diretas atuais, o horário
  é o de recebimento no motor. A idade medida cobre a consulta por software.
- Ensaiar o motor da publicação self-contained nas quatro durações, com os onze módulos e uma
  base isolada. Conferir escrita incremental e consultas simultâneas ao Histórico, séries e
  registros reabertos, lacunas, limite do snapshot, consumo do motor, `PRAGMA quick_check` e
  exportação e reabertura do PDF. Exercitar cancelamento, interrupção do motor e falha de escrita
  em casos separados. As sessões longas correm em tempo real sob supervisão, com energia e eventos
  anotados, usando as configurações de energia vigentes no Windows.
- Repetir instalação limpa, atualização, reabertura do histórico e desinstalação com preservação
  ou remoção seletiva em perfil de laboratório. Conferir executáveis, registros, atalhos, integridade
  de banco e preferências, PDF externo, arquivo desconhecido e junção fora da área gerenciada.
  Usar o mesmo conteúdo publicado para o ensaio, registrando a variante do instalador e a revisão.
- O notebook disponível serve como primeira configuração de referência. Completar a matriz
  passiva com Windows 11 limpo sem .NET, Windows 10 conforme a política, desktop com GPU integrada,
  máquina com GPU dedicada, SSD mais HD, equipamento sem sensores e VM para injetar falhas. A
  validação progride por combinação relevante de capacidades e ambiente. As plataformas anunciadas
  como suportadas precisam de evidência própria.

### Evidência passiva de partida

| Ensaio | Resultado e alcance |
| --- | --- |
| Prévia 7, 5 e 30 min | 3.681 e 22.349 medições. SQLite reaberto e PDF de 12 páginas aberto nos dois ensaios. [Relatório](../testes/V1.0-PREVIEW.7.md). |
| Prévia 7, 1 h | 3.600,59 s de sessão, 44.702 medições, 430 registros reabertos, 8.293 mensagens e 512 pontos no maior snapshot. `quick_check` aprovado e PDF de 12 páginas reaberto. [Relatório](../testes/VALIDACAO_FRESCOR_TERMICO_NOTEBOOK.md). |
| Prévia 5, 4 h | 14.400,66 s, 178.629 medições, 239 consultas ao Histórico durante a execução, `quick_check` aprovado e PDF de 13 páginas reaberto. A evidência vale para a prévia 5. [Relatório](../testes/VALIDACAO_LABORATORIO_NOTEBOOK.md). |
| Canal direto de laboratório, 5 min | 572 leituras diretas, 52 de CPU Package e 11 séries reabertas. Auxiliar elevado mediante consentimento, controlador sem elevação. Canal rejeita consulta com mais de 6 s contados desde seu início. O frescor físico do sensor permanece pendente. [Relatório](../testes/VALIDACAO_FRESCOR_TERMICO_NOTEBOOK.md). |
| CPU Package passivo, referência DTS HWiNFO | 30 pares na janela de até 1.200 ms, com maior separação conservadora de 1.085,1 ms, diferença absoluta média de 3,73 °C e máxima de 11 °C. Na janela de 250 ms, seis pares, média de 2,50 °C e máxima de 11 °C. O CSV informa o horário da varredura por software. As diferenças podem reunir variação da temperatura entre consultas, latências e diferenças entre as cadeias de leitura. A rodada é exploratória e não define tolerância nem erro sistemático. [Relatório](../testes/COMPARACAO_CPU_PACKAGE_NOTEBOOK.md). |

### Rodada local de continuidade

- Pacote padrão da prévia 7 compilado a partir de `13fd8eb` com runtime .NET 10.0.14 e
  Inno Setup 7.1.1. SHA-256 do `GCMMonitor-Setup.exe`
  `A7F304935B5262337D546FD4E2A4D3B286AF5B2CBB7F1DE8EE0726BF02AF46C8`.
  A publicação foi conferida sem supervisor, auxiliar, biblioteca térmica e driver de laboratório.
- A atualização da prévia 6 para a prévia 7 passou com os binários publicados e o instalador
  Inno na variante de laboratório. Três testes opt-in verificaram criação do histórico na versão
  anterior, reabertura e nova sessão na versão atualizada e uso do runtime incluído. O ensaio
  também confirmou preservação ou remoção seletiva, PDF externo preservado, arquivo desconhecido
  e destino da junção intactos. O instalador e o desinstalador recusaram operação com o marcador de
  uso sintético. O CT-007 com carga segue pendente. Evidências locais em
  `.artifacts/installer/upgrade-602aceb58139442792e0c96626f3c8dd/`.
- A suíte Release do ajuste `c5b41df` compilou com zero avisos e erros. Passaram 109 testes
  unitários e 64 de integração, com seis casos opt-in separados. Entre as verificações estão RAM
  real com a fonte térmica direta ausente no mesmo lote, registro nativo de energia com processo
  ocioso, decisões de atenção, parada crítica, recuperação simulada e tratamento de amostras
  atrasadas na troca de fases. TRX em `.artifacts/TestResults/duas-frentes-release/`.
  A coleta longa mantinha arquivos de teste em uso. A recompilação e a suíte rápida passaram em
  saída Release separada, preservando o executável e o processo da prova em andamento. Após o
  ensaio, `scripts/dev.ps1 Build` e `scripts/dev.ps1 Test` em Release confirmaram os mesmos
  resultados na saída padrão. TRX da repetição em `.artifacts/TestResults/`.

### Quatro horas na prévia 7

Ensaio aprovado no notebook com Windows 11 build 26200, na noite de 9 a 10 de novembro de 2026.
O motor foi o da publicação `13fd8eb` indicada acima. A sessão começou na tomada e usou os
onze módulos em uma base separada, com as configurações de energia vigentes no Windows. Os
testes rápidos e do instalador isolado também foram executados no notebook no início da coleta.

| Medida | Resultado |
| --- | ---: |
| Tempo observado | 14.400,52 s |
| Duração efetiva da sessão | 14.400,03 s |
| Medições persistidas | 181.514 |
| Registros reabertos | 429 |
| Consultas ao Histórico durante a gravação | 237 |
| Atualizações do motor | 33.625 |
| Maior snapshot enviado à interface | 512 amostras |
| Maior intervalo entre leituras válidas de RAM | 9,70 s |
| Pico amostrado de memória privada do motor | 41,99 MiB |
| Tamanho final da base | 65,80 MiB |
| Verificação de integridade do SQLite | `PRAGMA quick_check` retornou `ok` |
| PDF exportado e reaberto | 13 páginas |

CPU, RAM, dispositivos e energia ficaram completos. Armazenamento, eventos, bateria, Windows
Update, sensores, processos e Wi-Fi ficaram parciais na configuração ensaiada. Os estados por
módulo foram preservados. O estresse permaneceu bloqueado durante o ensaio. A base e o PDF de
teste foram temporários. O TRX, os checkpoints e o resumo sem identificadores ficaram em
`.artifacts/validation/notebook-34b3045c118b4b4584c6086afe196e9f/`.

### Lacunas na retomada da coleta passiva

A revisão `046b2c1` registra uma ocorrência também quando uma lacuna de amostragem superior a
15 s alcança o fim da sessão. Ao voltar a amostrar, o módulo recalcula deltas a partir de uma nova
referência e preserva os períodos já contabilizados. Os gráficos em Monitoramento, Resultados e
PDF interrompem a linha quando o intervalo entre duas leituras válidas excede o mesmo limite.
A interface deriva a separação dos horários salvos e pode mostrá-la em sessões anteriores quando
os dois pontos estão na janela.

O Release compilou com zero avisos e erros. Passaram **110 testes unitários e 65 de integração**,
com seis casos opt-in separados. Um relógio simulado exercitou a retomada após o prazo e verificou
que a ocorrência foi salva sem uma leitura adicional. Uma sessão de teste com lacuna foi reaberta
do SQLite pelo Histórico. O gráfico preservou os pontos nos dois lados e o PDF foi exportado e
reaberto. Os casos de parada do supervisor, inclusive `suspend` simulado, confirmaram saída do
filho ocioso, término do supervisor e fechamento do canal da prova.

O instalador padrão local da revisão é
`.artifacts/installer/1.0.0-preview.7-3ac284564bbb4ad4a87923df9f4b3528/GCMMonitor-Setup.exe`.
SHA-256 `2C8FDDA9179C29DEB18FF7C314D6AF0916B934F52E8967F65E1A8ECDA0858834`.
A variante isolada de laboratório passou nos três testes de atualização da prévia 6 para a 7.
O ensaio verificou histórico, preferências, runtime incluído e desinstalação com preservação e
remoção seletiva de dados. Evidências em
`.artifacts/installer/upgrade-c090a29e9d1344eeb7acdeea2d0e2ca7/`.

Em 10 de novembro de 2026, o motor publicado dessa revisão passou em cinco minutos no notebook
com os onze módulos, em banco temporário. Foram 3.715 medições, 427 registros reabertos,
512 amostras no maior snapshot, SQLite com `quick_check` igual a `ok` e PDF de 12 páginas
reaberto. A sessão ocorreu na tomada, em paralelo com o ensaio do instalador isolado. O PDF e a
base de teste foram temporários. Resumo sanitizado e TRX em
`.artifacts/validation/notebook-e734c75f2b0a408cac29d6eafeda2887/`.

### Roteiro do CT-052 passivo no Modern Standby

O roteiro usa cinco minutos de CPU e RAM com o motor publicado, banco isolado, leitura do log
Sistema e ação manual do técnico. Ele aguarda duas leituras válidas de RAM e anuncia a prontidão.
O caso `BeforeDeadline` indica suspensão 75 s antes do prazo e retorno 35 s antes do prazo.
No caso `AfterDeadline`, a suspensão é prevista 30 s antes do prazo e o retorno, 12 s depois.
Os horários são informados com antecedência. O controle `Awake` executa a coleta com o notebook
acordado.

```powershell
./scripts/dev.ps1 Build -Configuration Release
$engine = '<publicação>/engine/GcmMonitor.Engine.exe'
./scripts/validate-passive-suspension.ps1 -Expected Awake -PublishedEngine $engine
./scripts/validate-passive-suspension.ps1 -Expected BeforeDeadline -PublishedEngine $engine
./scripts/validate-passive-suspension.ps1 -Expected AfterDeadline -PublishedEngine $engine
```

Os eventos Kernel-Power 506 e 507 devem compartilhar o `ScenarioInstanceIdV2`. O roteiro também
exige o par de transições 566 para a mesma fase `Sleep`, com duração mínima de 20 s. Uma lacuna
de amostragem sozinha, um evento sem par ou somente tela apagada não aprova suspensão. No retorno
antes do prazo, a série de RAM deve ter amostras nos dois lados da fase `Sleep` e uma ocorrência
correspondente. No retorno depois do prazo, a sessão deve terminar com a ocorrência e sem leitura
adicional. O ensaio verifica a cobertura da média, Histórico, `quick_check` e reabertura do PDF.

O banco e o PDF contêm inventário local e ficam na pasta ignorada `.artifacts/validation/` junto
do TRX, dos eventos selecionados e dos resumos sem identificação. Os dois cenários de retorno
dependem de ação presencial.

O controle `Awake` passou em 10 de novembro de 2026 com o motor `046b2c1` em Windows 11 build
26200. A sessão levou 300,15 s e registrou 121 amostras válidas de RAM, com 298,50 s de cobertura.
O log não registrou eventos de suspensão no intervalo, e o banco não tinha lacunas de RAM acima
de 15 s. O Histórico reabriu a sessão, o `quick_check` retornou `ok` e o PDF de duas páginas foi
reaberto. Evidências locais em
`.artifacts/validation/passive-suspension-01a37ad0a73d4b69842dce4b4c85b1f2/`.

Sete cenários sintéticos exercitaram a correlação, incluindo retorno antes e depois do prazo e
rejeição de tela apagada, cenários diferentes, ausência de ocorrência e leitura após o prazo.
A leitura do log também identificou a fase `Sleep` de 30,79 s da prova ociosa anterior na tomada,
cruzada com dados simulados de coleta. A suíte Release passou com 110 testes unitários e 65 de
integração. Sete casos opt-in ficaram fora da suíte rápida, incluindo o novo roteiro.

Na primeira tentativa física de retomada antes do prazo, em 10 de novembro de 2026, o notebook
estava na tomada. O log registrou cinco sessões de Modern Standby durante a intervenção do
técnico. Quatro entraram em `Sleep` por 0,763 s, 1,029 s, 1,033 s e 1,013 s. A última saiu de
`Screen Off` sem entrada em `Sleep`. A coleta terminou em 300,17 s, com 118 leituras válidas de
RAM e cobertura de 295,59 s. Nenhum intervalo de RAM passou de 15 s. Histórico, `quick_check`
e PDF de duas páginas passaram. O cenário `BeforeDeadline` ficou **inconclusivo**, pois não houve
uma fase `Sleep` sustentada nem uma lacuna correspondente. Evidências locais em
`.artifacts/validation/passive-suspension-ca03d8e6b040483cafb0cb6b80d9146b/`.

Na primeira tentativa de retorno após o prazo, em bateria, a sessão terminou às 19h51min51s.
O Windows registrou a entrada em `Sleep` às 19h52min00,788s e a saída às 19h52min56,530s,
totalizando 55,742 s. A coleta já havia terminado, com 120 leituras válidas de RAM, `quick_check`
igual a `ok` e PDF de duas páginas reaberto. A prova de retomada ficou fora da janela da coleta.
O aviso original saía a 30 s do fim. O roteiro passou a informar os dois horários com 90 s
de antecedência em relação ao momento previsto para suspender. Evidências da coleta em
`.artifacts/validation/passive-suspension-57ce8c3e36a34ee5bc08cb42cf541793/`.

A repetição de `AfterDeadline` na bateria passou. O Windows registrou 52,217 s em `Sleep`.
A última medição ocorreu às 20h03min56,772s, antes da entrada na fase de baixo consumo.
Após o retorno, o GCM registrou a lacuna às 20h04min50,875s e encerrou a sessão, sem gravar
outra medição. Foram reabertos o Histórico, 112 amostras válidas de RAM e um PDF de duas páginas.
O `quick_check` retornou `ok`. Evidências em
`.artifacts/validation/passive-suspension-828bb81e4d8943b3b933fa75d29f109f/`.

A repetição de `BeforeDeadline` na bateria também passou. O Windows registrou 47,129 s em
`Sleep`. As amostras de RAM imediatamente antes e depois da suspensão ficaram separadas
por 51,145 s. O GCM registrou a lacuna, retomou a coleta e encerrou a sessão em 300,17 s.
Foram preservadas 101 leituras válidas de RAM e 246,37 s de cobertura da média. O Histórico,
o `quick_check` e a reabertura do PDF de duas páginas passaram. Evidências em
`.artifacts/validation/passive-suspension-6ec6748316384e31a956a20e9d321c49/`.

As duas provas confirmam a parte passiva do CT-052 neste notebook em bateria, com o motor
publicado `046b2c1`. A primeira tentativa na tomada segue inconclusiva. A validação integral
do CT-052 depende também da prova de contenção e retomada do supervisor.

## B. Supervisão, alertas e recuperação térmica

- Manter o estresse físico bloqueado. O pacote padrão fica sem supervisor de laboratório, auxiliar
  térmico direto e driver. O supervisor experimental executa somente um filho ocioso. Manter o
  recurso desmarcado por padrão, inclusive após seleção de todos os módulos.
- Usar simulações identificadas para consentimento, proposta de extensão da sessão, linha de base,
  período de carga simulado e recuperação. Cobrir alerta de atenção com decisão registrada,
  condição crítica com parada obrigatória, perda térmica, leitura repetida ou atrasada, recuperação
  incompleta e lacunas entre fases. Exercitar `Parar estresse` e `Cancelar diagnóstico` em separado.
- Na recuperação, preservar origem e horários, separar fase observada de horário da captura e
  apresentar valores absolutos e diferença em relação à linha de base quando comparáveis. Omitir
  percentuais quando faltarem amostras ou condições comparáveis. A análise de recuperação descreve
  o comportamento observado e não classifica a saúde do equipamento por uma curva isolada.
- Exercitar Job Object, heartbeat, prazo, perda de comunicação, fechamento da UI e falhas de
  persistência com simulação ou filho ocioso. Medir separadamente recepção do sinal, pedido de
  parada e saída confirmada pelo kernel. Preservar os dados gravados e exigir que qualquer retomada
  registre a lacuna e nunca reinicie carga automaticamente.

### Idade da consulta na prova térmica ociosa

O protocolo 6 da prova ociosa transporta os horários monotônicos de início e fim de cada
consulta sintética. O supervisor usa a mesma validação de sequência e janela do canal passivo.
O prazo da prova vence seis segundos após o início da consulta, incluindo a espera antes da
entrega. Uma fixture inicial vencida impede a criação do filho. Se o prazo expirar enquanto o
filho ainda está suspenso na inicialização, o supervisor o encerra sem iniciar sua execução.
Uma nova mensagem após o vencimento não reativa o watchdog.

Esse prazo é um parâmetro da prova com dados sintéticos. A qualificação do frescor do sensor
físico e a definição de um prazo de proteção para carga seguem separadas.

Em 10 de novembro de 2026, uma consulta sintética com quatro segundos de idade no envio
deixou 1,904 s de execução do filho ocioso. O processo saiu antes da confirmação terminal
`SensorLost`. Uma consulta inicial com sete segundos foi recusada sem criação de processo.
Os testes unitários verificaram que a renovação usa o tempo restante da nova consulta e que
mensagens depois do vencimento não reabrem o prazo. O Release compilou sem avisos e erros.
Passaram 110 testes unitários e 67 de integração, com sete casos opt-in fora da suíte rápida.

A prova adicional `DurationCompleted`, com a observação nativa do Windows ligada e a alimentação
estável, confirmou a saída do filho ocioso em 4,026 s. Evidências locais em
`.artifacts/validation/thermal-queued-query.json` e
`.artifacts/validation/windows-observation-5c779e4846dd46bdac71486b44375470/`.

### Pedido de parada antes da confirmação de saída

Quando o filho já está associado ao observador, o primeiro sinal de suspensão, mudança de
alimentação ou indisponibilidade solicita a parada ali mesmo. Se a energia mudar antes do
aviso de suspensão, o motivo registrado continua sendo o primeiro sinal e o pedido de parada já
foi feito. O pedido geral de parada também ocorre antes de agendar a espera assíncrona pela
saída. O encerramento continua confirmado pelo processo antes da mensagem terminal.

O teste de integração usa um filho criado suspenso e confere que a parada já foi solicitada
quando a tarefa de confirmação é devolvida ao chamador. O roteiro de transição nativa agora
exige evidência do pedido direto tanto para suspensão quanto para mudança de alimentação.
A repetição física das duas transições no notebook é necessária para medir o efeito na margem.

O Release compilou sem avisos e erros. Passaram 110 testes unitários e 68 de integração, com
sete casos opt-in fora da suíte rápida. A prova adicional com o Windows sob observação e
alimentação estável confirmou a saída do filho ocioso em 4,013 s pelo prazo configurado.
Evidências em `.artifacts/validation/windows-observation-5f860fdecfaa40c0be3b50973847a9cf/`.

### O que as provas de energia demonstraram

| Cenário | Evidência e limite |
| --- | --- |
| Remoção da alimentação externa | Notificação nativa reconhecida e filho ocioso encerrado. O resultado vale para o teste ocioso. |
| Modern Standby, protocolo 4 | Uma tentativa expirou. Em outra, a saída ocorreu 39,159 s após o evento 506, perto da retomada. O evento 506 marca o início da sessão de Modern Standby e pode corresponder à etapa `Screen Off`. |
| Modern Standby, protocolo 5, bateria | Três saídas ociosas antecederam a fase `Sleep` em 2,012 ms, 1,402 ms e 3,407 ms. Os tempos entre aviso e saída foram 5,655 ms, 5,586 ms e 4,097 ms. Houve também uma tentativa inconclusiva por prazo. |
| Modern Standby, protocolo 5, tomada | O filho saiu 5,831 ms após o aviso. A saída ficou 0,134 ms depois do início de `Sleep` na correlação com SleepStudy e evento 566. A antecedência não foi demonstrada. |

O protocolo 5 solicita a parada no próprio aviso de suspensão. As margens de poucos milissegundos
ficam expostas à latência de entrega do aviso, ao escalonamento sob carga, à saída do processo e à
incerteza da correlação entre relógios e eventos. A saída de um filho ocioso não garante a saída de
um processo de carga antes de `Sleep`. Repetir em tomada e bateria, registrar falhas e transições
rápidas, confirmar que a coleta registra a lacuna e que a retomada não reinicia o executor. Qualquer
proposta de mitigação deve definir o critério verificável e demonstrar sua margem antes de considerar
carga. O CT-052 integral segue pendente. [Cronologia e evidências](../testes/VALIDACAO_FRESCOR_TERMICO_NOTEBOOK.md).

### Gate de proteção automática

Qualificar por combinação verificável de fonte, sensor, família de hardware, firmware e
configuração térmica, versão do Windows, modelo de suspensão e condições de energia. Uma
combinação desconhecida permanece com estresse bloqueado e pode usar as leituras informativas
disponíveis. A qualificação exige fonte de limite verificável, idade e atualização física
caracterizadas, perda ou congelamento detectáveis, margem de parada demonstrada e contenção
verificável. A janela de 6 s do canal passivo verifica a idade da consulta por software. A
conversão do DTS e o alvo observado de 94 °C no notebook (`TjMax` de 100 °C com offset de 6 °C)
contextualizam o sensor. Nenhum desses números está aprovado como limiar de proteção do GCM.

| Requisito | Dependência e prova |
| --- | --- |
| Consentimento, padrão desligado, prazo, isolamento, heartbeat, cancelamento, registro e bloqueio de retomada | A lógica independe do modelo da CPU. Exercitar com testes de contrato e filho ocioso. A confirmação de encerramento e recuperação depende também do ambiente Windows e dos casos físicos aplicáveis. |
| Detecção da fonte essencial, perda, atraso, repetição e condição crítica | Injetar dados sintéticos para provar a resposta da lógica. Caracterizar em hardware a identidade, o comportamento e a atualização física da fonte que a proteção utilizará. |
| Limites, margem térmica e parada em transições | Dependem do conjunto sensor, hardware, firmware e Windows. Um perfil pode cobrir uma família quando o equipamento confirmar os parâmetros e capacidades de que ele depende e houver prova representativa. O nome comercial da CPU é insuficiente para aplicar um perfil. |

### Avaliação de estresse com acompanhamento do técnico

Acompanhamento e parada manual podem complementar os mecanismos comprovados exigidos pela seção
6.2. Um modo em que o técnico interpreta tudo e a carga continua sem proteção automática viola
os critérios atuais. Reação humana, travamento da interface, perda silenciosa de sensor e
transições de energia podem ocorrer antes de uma intervenção. Esse modo não foi implementado.

Se houver interesse em explicitar um futuro modo de laboratório na Mestre, a proposta para
revisão é permitir interpretação humana dos resultados e manter observabilidade térmica
qualificada, limites automáticos, consentimento específico, prazo, Job Object, watchdog, parada
manual, corte em condição crítica ou perda de sinal essencial, persistência e bloqueio de retomada
automática. Seria uma revisão documentada dos requisitos e das evidências, antes de qualquer
implementação que mude política. A decisão atual é preservar a especificação aprovada.

| Área a revalidar antes de eventual mudança de política | Casos principais |
| --- | --- |
| Pré-requisitos térmicos, autorização, fases, extensão e duração | CT-022, CT-043 a CT-045, CT-054 |
| Parada manual, cancelamento, falha de sensor, perda de UI/supervisor e energia | CT-046 a CT-053 |
| Efeitos colaterais, privacidade, sessão interrompida, banco inacessível e remoção com carga | CT-007, CT-055 a CT-060 |
| Resultado/PDF com fases e sessões prolongadas | CT-061 a CT-068, CT-076 |

## Registro de aprovação e liberação

| Estado | O que deve constar |
| --- | --- |
| Implementado | Revisão, pacote, dependências e modo disponível. |
| Testado no notebook | Ensaio físico executado, versão, configuração, resultado e limites. Prova com processo ocioso identificada como tal. |
| Aprovado por simulação | Cenário reproduzível, resultado e mecanismos exercitados, com dados sintéticos identificados. |
| Pendente de validação física | Critério em aberto, evidência faltante e impacto na liberação. Resultados inconclusivos permanecem aqui. |
| Liberado para utilização | Uso passivo de laboratório na prévia, com as capacidades expostas e limitações registradas. Canal direto restrito a ensaio local com consentimento. Carga física bloqueada e distribuição final condicionada à seção 13.5. |

Publicar apenas resumos sanitizados e referências à revisão. Banco, CSV bruto, registros de energia
do equipamento e PDFs com inventário real permanecem locais.
