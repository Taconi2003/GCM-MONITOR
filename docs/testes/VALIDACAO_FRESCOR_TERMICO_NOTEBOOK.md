# Idade da consulta térmica e provas de energia no notebook

Rodada de 8 de novembro de 2026. Branch `codex/validacao-frescor-termico`, baseada na prévia 7.
O trabalho cobre o auxiliar térmico de laboratório e o supervisor ocioso. A Especificação
Mestre v1.0 define as condições de liberação de carga.

## Canal térmico

O receptor passou a verificar a idade desde o início da consulta no auxiliar. O canal 2 usa
o mesmo contador monotônico do Windows nos dois processos. Uma consulta com atraso superior
a 6 s, relógio regressivo, ordem temporal impossível ou sequência inválida fecha a fonte
térmica. O coletor apresenta lacunas e mantém as outras fontes. Os horários dos pontos ainda
representam o recebimento. O limite de 6 s pertence ao canal passivo e inclui consulta,
transporte e consumo. [Contador de alta resolução do Windows](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps).

A revisão da implementação Intel do LibreHardwareMonitor 0.9.6 confirmou que ela verifica
a validade da leitura do registrador e anula a temperatura de pacote ou núcleo se a consulta
falhar. A idade da consulta é distinta da idade do valor gerado pelo sensor físico. O canal
trata ainda valores fisicamente impossíveis e múltiplos registros de pacote para a mesma CPU
como leitura ambígua ou inválida. [Implementação Intel da biblioteca 0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/Hardware/Cpu/IntelCpu.cs).

## Prova ociosa de alimentação e suspensão

O supervisor identifica se a primeira evidência veio da notificação nativa ou da consulta
periódica de alimentação. O roteiro de laboratório cria um filho ocioso por até 15 s para
cada transição e confirma sua saída pelo processo correspondente. O modo estável dura 4 s.
Ele grava o resultado agregado e distingue a expiração do cronômetro do evento solicitado.

Em PowerShell 7, com a solução compilada em Release e o técnico presente no notebook, execute
cada prova separadamente. Aguarde a confirmação de que a prova está pronta antes de mudar a
alimentação ou usar **Suspender** no menu do Windows. Retome normalmente após a suspensão.

```powershell
./scripts/validate-windows-observation.ps1 -Expected DurationCompleted
./scripts/validate-windows-observation.ps1 -Expected PowerChanged
./scripts/validate-windows-observation.ps1 -Expected Suspended
```

Cada execução cria `.artifacts/validation/windows-observation-*/` com TRX, saída local e, se o
supervisor reportou a parada, `result.json`. A prova exige saída confirmada e o motivo solicitado.
Em uma mudança de energia, `WindowsCallbackObserved` distingue a notificação da leitura
periódica que chegou primeiro. Desde o protocolo 4 do supervisor ocioso, a prova de suspensão
exige `WindowsSuspension=Entering`. Uma notificação de retomada ainda dispara a parada,
mas registra `Resuming` e deixa o ensaio sem aprovação. A confirmação de saída acompanha
o resultado, junto com `ReadyAtUtc` e `WorkerExitedAtUtc`, horário de saída informado pelo
Windows. No build 26200 deste notebook, o provedor `Microsoft-Windows-Kernel-Power`
identifica a entrada e a saída do Modern Standby pelos eventos 506 e 507. Compare os
horários desses eventos com a evidência da prova após retomar a sessão. Essa correlação
registra a ordem dos eventos observáveis. O Windows pode pausar processos desktop durante
o Modern Standby.
[Aviso de entrada em suspensão](https://learn.microsoft.com/en-us/windows/win32/power/pbt-apmsuspend),
[aviso de retomada](https://learn.microsoft.com/en-us/windows/win32/power/pbt-apmresumeautomatic)
e [fases do Modern Standby](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/prepare-software-for-modern-standby).

No protocolo 5, o aviso de suspensão solicita o encerramento do Job diretamente, antes
de despachar as tarefas de confirmação. O controlador continua verificando a saída do filho.
`WindowsTiming` registra o recebimento do sinal, a tentativa de parada e seu retorno.
O roteiro exige que a tentativa direta tenha sido aceita na prova de entrada em suspensão.
Compare `WorkerExitedAtUtc`, informado pelo Windows, com os eventos independentes de energia.
`WorkerExitMilliseconds` mede até a confirmação pelo supervisor, que pode ocorrer após a retomada.
[Encerramento de Job Objects](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-terminatejobobject).

## Testes executados

| Prova | Resultado neste notebook |
| --- | --- |
| Restore travado, solução Release e auxiliar | Concluídos. Compilações com zero avisos e erros. O auxiliar foi preparado na cópia de laboratório para a próxima execução com consentimento. |
| Suíte Release | 108 testes unitários e 63 de integração aprovados. Seis casos ficaram fora da suíte padrão. Os casos novos cobrem idade antes do recebimento, rejeição de consultas atrasadas ou incoerentes e incompatibilidade com o protocolo anterior. |
| Mesmo contador no Windows | O contador reportado pelo subprocesso ficou dentro da janela medida pelo controlador. A frequência observada foi 10 MHz. |
| Alimentação estável | Notificação inicial registrada. Saída do filho confirmada em 4,021 s, com `DurationCompleted` e sem evento de mudança. |
| Controle negativo de mudança de energia | O teste pediu `PowerChanged` com a fonte estável. Após 15,025 s, confirmou a saída do filho, registrou `DurationCompleted` e recusou a aprovação. |
| Leitura passiva no contexto normal | A sonda concluiu 30 ciclos, com zero valores numéricos. A prova com confirmação do Windows foi feita separadamente e está registrada abaixo. |
| Capacidade térmica Intel | A consulta passiva de CPUID confirmou que a CPU anuncia sensor térmico digital e gerenciamento térmico de pacote. A leitura direta do sinal térmico ativo segue pendente. [Manual Intel, volume 3B, §17.9](https://cdrdv2-public.intel.com/874250/253669-090-sdm-vol-3b.pdf). |
| Suspensão disponível | `powercfg /a` identificou Modern Standby conectado (S0), com S3 indisponível. A prova física deve confirmar o comportamento do supervisor nesse modo. [Estados de Modern Standby](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/modern-standby-states). |

Os resultados do supervisor ficam em `.artifacts/validation/windows-observation-a8b7c775ebd042cc85c88ea920dcb0f2/`
e `.artifacts/validation/windows-observation-5199c081063448c484b39becf8c54186/`. O resumo da
sonda sem elevação está em `.artifacts/thermal-validation/unprivileged-5ac2057f89bb4d639ffdef8a23f60bd3/summary.json`.
As capturas brutas e os registros identificáveis ficam no notebook.

## Sessão passiva de uma hora com a prévia 7 publicada

O motor do instalador de revisão da prévia 7, commit `ff0bf37`, passou em uma hora de coleta
com os 11 módulos no notebook. Essa prova estende a evidência da prévia 7, em banco temporário
separado do histórico de uso. O canal térmico de laboratório foi validado pela suíte acima e
exige ensaio físico próprio com a cópia preparada.

| Medida | Resultado |
| --- | ---: |
| Duração observada | 3.600,59 s |
| Medições gravadas no SQLite | 44.702 |
| Registros reabertos | 430 |
| Atualizações recebidas | 8.293 |
| Maior janela de amostras por atualização | 512 |
| Maior intervalo entre leituras válidas de RAM | 7,31 s |
| Pico de memória privada do motor | 39,99 MiB |
| Tamanho final do SQLite | 16,97 MiB |
| Páginas do PDF reaberto | 12 |

O SQLite passou na verificação de integridade. O resumo e o TRX ficam em
`.artifacts/validation/notebook-3d252b61f0a94663985d61a8180e1a51/`. Foram 173 execuções
aprovadas na rodada, contando os 171 testes da suíte, a prova ociosa em estado estável
e a sessão de uma hora. O controle negativo recusou a aprovação como previsto.

## Prova física passiva do canal 2

Na sessão acompanhada de 8 de novembro de 2026, o auxiliar foi iniciado pela confirmação
normal do Windows. O controlador executou com privilégios normais. O teste de integração do
canal 2 passou em 303,33 s com CPU, RAM, Sensores e Energia. O SQLite registrou 572 temperaturas
diretas válidas em 11 séries, incluindo 52 leituras de CPU Package. O `quick_check` retornou
`ok`. O Histórico reabriu 99 leituras da janela de apresentação e 11 gráficos térmicos.

Durante a mesma sessão, foram observados 21 valores correntes de CPU Package no HWMonitor.
Cada observação teve os horários UTC de início e fim da captura da janela. A comparação
usou o recebimento da medição no GCM e exigiu que as duas extremidades da janela da captura
estivessem a até 1.200 ms desse horário. Cada amostra foi usada uma vez.

| Medida | Resultado |
| --- | ---: |
| Pares dentro da janela | 7 de 21 observações |
| Maior diferença entre horário do GCM e o ponto médio da captura | 1.039,16 ms |
| Maior separação considerando as duas extremidades | 1.096,16 ms |
| Diferença absoluta média | 2,71 °C |
| Maior diferença absoluta | 8 °C |

Os horários delimitam a observação na tela. A cadência de atualização interna do HWMonitor
e a idade da leitura física ainda precisam ser determinadas. A amostra é exploratória e
não estabelece tolerância de precisão ou de proteção térmica. Os dados brutos ficaram no
notebook. O teste e os resumos locais estão em `.artifacts/TestResults/thermal-protocol2/`
e `.artifacts/thermal-validation/session-93de5c0a66164f1d9a8e5d5ba7e4117b/`.

## Revisão sem intervenção física

Na prévia 7, Windows 11 x64 build 26200, a solução compilou em Release com zero avisos
e erros. Passaram 108 testes unitários e 64 de integração, com seis casos optativos fora
da suíte padrão. As provas nativas adicionais usaram o protocolo 4 do supervisor.

| Caso | Resultado local de 8 de novembro de 2026 |
| --- | --- |
| CT-052 e CT-053 · Alimentação estável | O supervisor confirmou o encerramento do filho ocioso em 4,029 s, com `DurationCompleted` e sem sinal de transição. |
| CT-052 · Controle negativo de suspensão | Com o notebook acordado, o supervisor encerrou o filho ocioso em 15,011 s. O teste pediu `Suspended`, recebeu `DurationCompleted` e recusou a aprovação como previsto. |
| CT-060 e CT-059 · Banco inacessível | Após o primeiro lote real de RAM, um bloqueio exclusivo no arquivo temporário provocou `SQLITE_CANTOPEN` (14). O motor encerrou. Liberado o arquivo, a recuperação reabriu a sessão como interrompida, preservou medições e estatísticas confirmadas e permitiu reabrir o PDF parcial. |
| CT-022 · Comparação térmica | O roteiro passou em dez verificações fictícias e reproduziu os resultados anteriores com os registros locais. Agora também compara as extremidades das janelas registradas. Consulte o [relatório de comparação](COMPARACAO_CPU_PACKAGE_NOTEBOOK.md). |
| CT-054 · Sinal de proteção térmica do pacote | A sonda avulsa passou a oferecer leitura passiva opcional do registrador Intel. O teste sem elevação completou 30 ciclos, registrou zero tentativas de ler o registrador e explicou a falta de privilégios. A captura elevada segue pendente. Consulte o [roteiro da sonda](VALIDACAO_TERMICA_V0.4.md#sinal-térmico-atual-do-pacote-intel). |

As duas provas ociosas escreveram em `.artifacts/validation/windows-observation-bdcac5d5f79844a98412239f722fb812/`
e `.artifacts/validation/windows-observation-f3d9f6b0f61446c8a507a05e7f8fdc9e/`.
Na primeira execução com o horário de saída, o teste encerrou sem resultado porque o
controlador não tinha mantido aberto o identificador do processo. As duas provas foram
repetidas após a correção e registraram o horário de saída informado pelo Windows.
A sonda sem elevação gravou em `.artifacts/thermal-validation/20261109T025745Z-b78cf989332a4fd299c8520f04fd00e2/`.
O teste `InaccessibleDatabaseStopsTheOwnedEngineAndRecoversCommittedReadings` está no TRX
da suíte de integração em `.artifacts/TestResults/`. O ensaio cobre perda de acesso
em coleta passiva. A falta real de espaço e o cenário sob carga seguem pendentes.
[Código de resultado do SQLite](https://www.sqlite.org/rescode.html#cantopen).

## Ensaios acompanhados de 9 de novembro de 2026

Prévia 7, revisão `9c22e8e`, Windows 11 x64 build 26200 e protocolo 4 do supervisor.
Os ensaios físicos de energia e suspensão usaram somente um processo ocioso.

| Prova | Resultado neste notebook |
| --- | --- |
| CT-053 · Tomada para bateria | A primeira janela de 15 s terminou sem transição observada. Na repetição, o Windows notificou a troca e o supervisor confirmou a saída do filho em 2,681 s desde seu início, com motivo `PowerChanged`. O subcaso ocioso foi aprovado. |
| CT-052 · Modern Standby S0, primeira tentativa | O evento 506 marcou a entrada em S0 14,864 s após a prontidão. O filho saiu 0,078 s depois desse evento, com motivo `DurationCompleted`. O teste não registrou a notificação de entrada. |
| CT-052 · Modern Standby S0, segunda tentativa | O evento 506 ocorreu 4,614 s após a prontidão. O supervisor registrou `Suspended` com notificação `Entering` e confirmou a saída do filho. A saída ocorreu 39,159 s após o evento 506 e 0,287 s antes do evento 507 de retomada. |

A segunda tentativa satisfez o critério de notificação e saída eventual do roteiro.
A ordem dos eventos mostra que o processo ainda existia depois da entrada em S0.
O subcaso de encerramento antes da entrada em suspensão fica reprovado neste notebook.
O critério completo CT-052, que também inclui o comportamento da coleta e do estresse,
continua bloqueado. Nenhuma carga artificial foi executada.

Os arquivos `result.json` e TRX estão em `.artifacts/validation/windows-observation-7cb80ebd3a614f8fa1c3eaf3ea8d53fd/`
para alimentação e em `.artifacts/validation/windows-observation-8a17bbfcb81643068c5b2fc40095baa3/`
e `.artifacts/validation/windows-observation-85487851cac74d79b17ab0dcaa29cce3/` para suspensão.
As duas pastas de suspensão também contêm `windows-events.json` com a cronologia agregada.

### Sinal térmico do pacote Intel

Após a confirmação normal do Windows, a sonda avulsa completou 30 ciclos passivos com
LibreHardwareMonitorLib 0.9.6 e PawnIO 2.2.0.0. As 30 consultas do sinal térmico foram
válidas e reportaram estado inativo no instante da leitura. Houve 30 temperaturas de
CPU Package, com 14 valores distintos, e 330 leituras candidatas considerando pacote
e núcleos. A maior duração observada da consulta específica do sinal foi 0,143 ms.
O sinal ativo e a precisão contra uma referência com horários de aquisição continuam
sem validação. A captura privada e o resumo ficam em
`.artifacts/thermal-validation/20261110T010712Z-1e74c3a78f8c48979be8c15614717cd0/`.

## Repetição com parada direta no protocolo 5

Em 9 de novembro de 2026, no mesmo notebook em bateria, a primeira repetição encerrou
com `DurationCompleted` e sem notificação. O Windows registrou uma transição breve e
uma segunda entrada no fim da janela. O resultado está em
`.artifacts/validation/windows-observation-732c0d8401eb421693782148368942eb/`.

Na segunda repetição, o aviso `Entering` disparou a parada direta e o teste confirmou
a saída. O evento 506 marcou o início da sessão Modern Standby. O relatório SleepStudy
classificou os primeiros 296,726 ms da transição como `Screen Off`. A duração coincidiu
com a transição do evento 566 para `Sleep` neste notebook. A fase `Sleep` durou
19,521 s. [Estados do Modern Standby](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/modern-standby-states)
e [relatório SleepStudy](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/modern-standby-sleepstudy).

| Medida da segunda repetição | Resultado |
| --- | ---: |
| Aviso recebido após o evento 506 | 288,255 ms |
| Pedido direto após o aviso | 0,802 ms |
| Saída do filho após o aviso | 5,655 ms |
| Saída do filho antes do início da fase `Sleep`, por correlação com o evento 566 | 2,012 ms |
| Saída do filho antes do evento 507 de retomada | 19,523 s |

A prova confirma a resposta direta e o encerramento durante `Screen Off` nesta
repetição ociosa. A margem observada foi estreita. O encerramento anterior ao evento 506
segue sem comprovação, e o CT-052 integral continua bloqueado. O resultado, a cronologia
agregada e o SleepStudy privado ficam em
`.artifacts/validation/windows-observation-d4ea4fb052234d3bbffb9e3547e85a54/`.

Na primeira execução da suíte, a verificação de isolamento falhou ao aguardar o filho
por uma referência de processo que não mantinha o identificador de espera. O teste foi
ajustado para confirmar a saída pelo identificador nativo mantido para o filho. O teste
continua verificando que a segunda solicitação é ignorada e que o processo alheio
permanece ativo. O build final em Release concluiu com zero avisos e erros.
A suíte final passou com 108 testes unitários e 64 de integração. Seis casos optativos
ficaram fora da execução padrão, incluindo a prova física executada separadamente.

## Repetibilidade em bateria e referências térmicas

Ainda em 9 de novembro de 2026, foram executadas mais duas suspensões manuais em
bateria, com a mesma revisão do supervisor e sem carga. Os dois filhos ociosos
encerraram após a notificação de entrada e antes da fase `Sleep`. O evento 566 foi
correlacionado com a duração de `Screen Off` no SleepStudy.

| Ensaio | Saída após o aviso | Saída antes de `Sleep` |
| --- | ---: | ---: |
| Prova positiva anterior | 5,655 ms | 2,012 ms |
| Primeira repetição | 5,586 ms | 1,402 ms |
| Segunda repetição | 4,097 ms | 3,407 ms |

As duas repetições mantiveram a ordem observada. As três margens são estreitas e
foram observadas em um único notebook em bateria. O controle anterior que terminou
pelo cronômetro continua registrado separadamente. O CT-052 integral exige
evidência adicional sobre a coleta e as proteções sob carga.

O TRX, o resultado e a cronologia agregada de cada repetição ficam em
`.artifacts/validation/windows-observation-e899271154574489a1bdb6334e958f30/` e
`.artifacts/validation/windows-observation-6138b05ef92f4e82988a42cf6d943fb1/`.
A segunda pasta guarda o SleepStudy privado que abrange ambas.

O Windows identificou a família Samsung Galaxy Book2 550XED e o Intel Core i5-1235U.
A Intel publica 100 °C de temperatura máxima de junção para o processador. O datasheet
da família descreve um ajuste de plataforma que pode reduzir o alvo do controle térmico
e especifica erro de até ±5 °C do sensor digital na faixa operacional. Esse erro é uma
especificação do sensor físico. A comparação do GCM inclui ainda o tempo da consulta,
a idade da leitura e a incerteza da ferramenta de referência.
[Família Galaxy Book2 na Samsung](https://www.samsung.com/br/support/model/NP550XED-KF2BR/),
[especificação Intel i5-1235U](https://www.intel.com/content/www/us/en/products/sku/226261/intel-core-i51235u-processor-12m-cache-up-to-4-40-ghz/specifications.html)
e [datasheet Intel, seção 4.1.3](https://cdrdv2-public.intel.com/655258/655258-011.pdf).

A sonda avulsa, após confirmação do Windows, leu o registrador `TEMPERATURE_TARGET` uma
vez na assinatura CPUID `0x906A4`. A leitura registrou TjMax de 100 °C, deslocamento de
6 °C, alvo ajustado de 94 °C e janela de média térmica desativada. O CPU Package variou
de 41 a 54 °C, com média de 42,77 °C em 30 pontos passivos. As 30 consultas do sinal
térmico foram válidas e o sinal estava inativo em todas. O resumo fica em
`.artifacts/thermal-validation/20261110T012550Z-d7edb26b826c4a15a3ee59cbc7e971d3/`.

Os 94 °C descrevem a configuração observada do controle interno do processador.
Para qualificar um limiar do GCM, ainda é preciso medir a concordância temporal e
térmica com uma referência, o aquecimento entre amostras e o tempo de parada em
condições representativas. A sonda permaneceu exclusivamente passiva e o build
concluiu com zero avisos e erros. O controle sem elevação completou 30 ciclos,
com zero tentativas de ler o sinal e alvo térmico indisponível. O resultado está em
`.artifacts/thermal-validation/target-unprivileged-eea62861e495441a81321be2c7a20f6d/`.

## Ensaio ocioso na tomada

Em 9 de novembro de 2026, o protocolo 5 aceitou a parada direta após o aviso
`Entering`. O processo ocioso saiu 5,831 ms após o aviso. O SleepStudy confirmou
a alimentação pela tomada e separou 319,660 ms de `Screen Off` de 30,793 s de `Sleep`.
Na correlação com o evento 566, a marca de saída ficou 0,134 ms depois da marca de início
de `Sleep`. Com essa diferença, a prova não confirma saída anterior à fase de baixo consumo.
O resultado nativo, o TRX, a cronologia agregada e o SleepStudy privado ficam em
`.artifacts/validation/windows-observation-10069fc8550e4727bf65fda219874ca3/`.

## Escopo de aprovação

A leitura direta pelo canal 2 passou no notebook. O registro paralelo do HWiNFO
forneceu horários de software para comparar 30 leituras de CPU Package. A
divergência máxima de 11 °C e a idade efetiva de aquisição ainda precisam ser
investigadas. Os ensaios estão no [relatório de comparação](COMPARACAO_CPU_PACKAGE_NOTEBOOK.md).
Houve três observações de parada ociosa durante `Screen Off` em bateria. Seguem
pendentes a qualificação do frescor do sensor, os limites de segurança do GCM e a
confirmação de parada anterior a `Sleep` na tomada e sob carga.
O estresse real permanece bloqueado.

A rodada seguinte aprovou quatro horas de coleta passiva na prévia 7. O resultado e as
condições de uso em laboratório estão na [estratégia de validação em duas frentes](../arquitetura/ESTRATEGIA_VALIDACAO_DUAS_FRENTES.md#quatro-horas-na-prévia-7).
