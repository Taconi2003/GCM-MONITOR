# v0.4 — proteção ambiental em prova ociosa

Validação local de 07/11/2026. A branch `codex/v0.4-protecao-ambiental` incorpora o
[canal térmico passivo](RELATORIO_CANAL_TERMICO_V0.4.md). O código desta etapa está no commit `d94d63a`.

## Implementação

- O protocolo 2 do supervisor aceita uma série térmica sintética na prova ociosa. A primeira
  mensagem precisa incluir núcleo e pacote com valores. As mensagens seguintes preservam identidade,
  metadados, sequência e disponibilidade do conjunto inicial. Atraso ou perda encerra a prova.
- O prazo térmico interno de 6 s corre separado do heartbeat de 3 s e da duração da prova. Uma
  mensagem que chega após o prazo fica sem efeito. Os tempos pertencem ao ensaio com dados sintéticos.
- O supervisor lê a alimentação inicial pelo Windows e observa mudanças a cada 500 ms. O callback
  nativo registra suspensão ou retomada como evento terminal. Fonte de energia desconhecida ou
  falha de registro impede a partida da prova com observação habilitada. O supervisor usa as APIs
  oficiais do Windows para registrar e remover a inscrição.
- A confirmação do início segue em paralelo com os prazos do filho. O supervisor confirma a saída
  pelo handle do Job antes de enviar a parada. A demora de leitura pelo controlador pode atrasar o
  recebimento da mensagem terminal, mas o prazo do processo segue no supervisor.
- A prova cria somente o filho ocioso que o próprio supervisor contém. O modo de laboratório
  registra se a observação nativa foi habilitada e se um evento observado motivou a parada.

## Testes e evidências locais

| Verificação | Resultado |
|---|---|
| Compilação Release | Zero avisos e erros |
| Suíte rápida | 91 testes unitários e 54 de integração aprovados. Dois ensaios longos reservados a execução separada |
| Perda de leitura sintética com heartbeat válido | Parada `SensorLost` em cerca de 5,99 s a partir da confirmação inicial, com saída verificada |
| Núcleo ausente após amostra sintética válida | Saída confirmada e motivo `SensorLost` |
| Cobertura inicial incompleta | Prova recusada antes da criação do filho |
| Registro nativo e alimentação inicial | Exercitados no notebook. Prova terminou pela duração configurada durante estado estável |
| Controlador demorou 1,6 s para ler | Filho de 250 ms saiu em cerca de 259 ms. A confirmação chegou depois |
| Sessão passiva real com onze módulos, executada separadamente | Aprovada em 300,512 s. Gravou 3.702 medições e reabriu 436 registros, com janela de até 512 amostras |
| CI Windows da implementação | [Aprovado](https://github.com/Taconi2003/GCM-MONITOR/actions/runs/42333634221) |

A etapa teve 146 testes distintos aprovados. A regressão de onze módulos usou as fontes passivas
padrão e o banco temporário de teste. A sessão térmica com auxiliar elevado foi executada na etapa
anterior e tem [evidência própria](RELATORIO_CANAL_TERMICO_V0.4.md).

## Limites desta etapa

A validação em laboratório ainda precisa exercitar suspensão e mudança física de alimentação. A
consulta por amostragem pode perder transições entre leituras. As provas térmicas usaram dados
sintéticos e o limite de idade pertence ao protocolo do ensaio. A comparação da temperatura real com
referência e a política térmica por hardware permanecem pendentes. CT-049, CT-050, CT-051 e CT-052
seguem bloqueados para estresse real, e CT-053 segue pendente para mudança de alimentação.

## Repetição

Execute a suíte Release do repositório. A variável abaixo exige que a inscrição nativa seja
exercitada na máquina local. Sem ela, o teste também aceita a recusa segura em ambientes sem suporte.

```powershell
$env:GCM_REQUIRE_WINDOWS_OBSERVATION = '1'
try { .\scripts\dev.ps1 Test -Configuration Release }
finally { Remove-Item Env:GCM_REQUIRE_WINDOWS_OBSERVATION }
```

As saídas agregadas ficam em `.artifacts/validation/`. O relatório versionado registra apenas
estados, contagens e tempos. A implementação preserva o bloqueio de estresse da Especificação Mestre.

## Referências

- [Registro de suspensão e retomada](https://learn.microsoft.com/en-us/windows/desktop/api/powerbase/nf-powerbase-powerregistersuspendresumenotification)
- [Estrutura e callback de notificação](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/ns-powrprof-device_notify_subscribe_parameters)
- [Estado de alimentação do Windows](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status)
