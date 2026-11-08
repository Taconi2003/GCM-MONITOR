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
periódica que chegou primeiro. A prova de suspensão exige a notificação.

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

## Escopo de aprovação

A leitura direta pelo canal 2 passou no notebook. A comparação de precisão do CPU Package
ainda precisa de uma série mais longa com horários de aquisição da referência. Os ensaios
anteriores do HWMonitor estão no [relatório de comparação](COMPARACAO_CPU_PACKAGE_NOTEBOOK.md).
Seguem pendentes a qualificação do frescor do sensor, os limites por hardware e as transições
físicas de alimentação e suspensão S0 com o técnico presente. O estresse real permanece bloqueado.
