# v0.4 — relatório do canal térmico passivo

Ensaio local de 06/11/2026. Branch `codex/v0.4-canal-termico`; implementação exercitada no
commit `ad56ce3`. A Especificação Mestre v1.0 permanece intacta. **O estresse continua bloqueado.**

## O que foi entregue

- Auxiliar temporário e autenticado, exclusivamente para ler sensores de núcleo e pacote Intel.
  O motor e o controlador do aplicativo continuam sem elevação. O consentimento é por sessão,
  seguido da confirmação normal do Windows.
- Leituras diretas com unidade, componente, fonte e horário de recebimento no pipeline existente:
  Monitoramento, Resultados e reabertura pelo Histórico. Sem novos caminhos de carga.
- Rejeição de frames inválidos, repetidos, atrasados ou com identificação incoerente. Sem valor
  inventado na ausência da fonte. A coleta restante continua quando a fonte térmica falha.
- Preparação opcional no build de laboratório. Nenhum componente instalado neste ciclo. Usa o
  PawnIO 2.2.0 já autorizado e a biblioteca LHM 0.9.6 do arquivo oficial verificado.

## Testes executados

| Verificação | Resultado |
|---|---|
| Restore travado e compilação Release da solução | Aprovados; zero avisos e erros na compilação |
| Compilação separada do auxiliar | Aprovada; zero avisos e erros |
| Suíte rápida local | 90 unitários + 48 de integração aprovados; dois ensaios longos ignorados por opt-in |
| Sessão térmica passiva de 5 min, executada separadamente | Aprovada no notebook com a implementação corrigida |
| CI Windows do commit de implementação | [Aprovado, incluindo o build do auxiliar sem instalar driver](https://github.com/Taconi2003/GCM-MONITOR/actions/runs/42331205010) |
| Comparação de precisão com referência independente | Pendente |
| Estresse real e proteções sob carga | Não executados |

**139 testes distintos aprovados neste ciclo**, incluindo o ensaio local opt-in. O ensaio anterior
de cinco minutos com todos os módulos não foi repetido. A suíte rápida verificou identidade, limite
do frame, nível de representação do cliente, sequência, rejeição de alegação de segurança, idade,
metadados, ausência do auxiliar e continuidade da coleta de RAM.

O ensaio de hardware usou o cliente real e o motor separado, com CPU, RAM, Sensores e Energia, e
reabriu o resultado pelo modelo de apresentação do Histórico. O banco foi temporário e separado do
histórico de uso. Duração de sessão: **300,257 s**; duração do ensaio com reabertura: **302,226 s**.
Foram gravadas **572 leituras diretas** em **11 séries**, todas com valor, sem estresse. A reabertura
recuperou **99 leituras diretas na janela de apresentação** e reconstruiu os **11 gráficos**. As demais
leituras ficaram gravadas; a janela não exibe o conteúdo integral do banco. O maior snapshot teve
512 amostras no total; o motor enviou 336 mensagens. Os valores e identificadores permanecem locais.

## Problemas encontrados e correções

- O primeiro ensaio de cinco minutos terminou sem temperaturas diretas; os módulos independentes
  continuaram. A opção de pipe `CurrentUserOnly` no cliente Windows verifica também o nível de
  elevação. O canal passou a conferir a mesma conta por SID e a identidade dos processos, com
  `Identification` no cliente para impedir o uso do token elevado pelo receptor. A nova execução
  obteve temperaturas. [Documentação da Microsoft](https://learn.microsoft.com/dotnet/api/system.io.pipes.pipeoptions).
- O teste inicial de frame grande escreveu o cabeçalho antes de iniciar a leitura em um pipe sem
  buffer explícito e expirou. O teste final arma a leitura primeiro e verifica recusa antes do payload.
- O primeiro restore travado rejeitou as referências de projeto novas. Os lockfiles foram
  atualizados e revisados; somente referências internas mudaram, e o restore travado passou.

## Limites e próximos passos

É coleta observacional em um notebook. Não comprova precisão, frescor de firmware, disponibilidade
em outros fabricantes, perda térmica física sob carga ou adequação de um limiar de segurança. O modo
atual é de laboratório, sem empacotamento no instalador. Se o usuário não autorizar o auxiliar, o
GCM segue com as fontes normais. Não há novo serviço ou alterações em energia, ventoinha, política
do Windows ou permissões do driver.

O próximo passo é comparar o mesmo sensor com uma referência, caracterizar perda de leitura e
definir a política por hardware. A integração com o supervisor de estresse só deve avançar com
proteções verificáveis e testes de laboratório dos P0. CT-022 permanece pendente de precisão e a
matriz de estresse permanece bloqueada.

## Reproduzir o ensaio de hardware

Prepare o auxiliar conforme a [arquitetura e instruções](../arquitetura/CANAL_TERMICO_V0.4.md). Em um
PowerShell normal, com confirmação do Windows apenas para o auxiliar:

```powershell
$env:DOTNET_ROOT = "$PWD\.tools\dotnet"
$env:DOTNET_CLI_HOME = "$PWD\.tools\home"
$env:NUGET_PACKAGES = "$PWD\.tools\nuget"
$env:GCM_RUN_THERMAL_VALIDATION = '1'
try {
    & .\.tools\dotnet\dotnet.exe test tests/GcmMonitor.IntegrationTests -c Release --no-build --no-restore `
        --filter 'FullyQualifiedName~FiveMinuteDirectThermalSession' --logger trx --results-directory '.artifacts/TestResults'
} finally { Remove-Item Env:GCM_RUN_THERMAL_VALIDATION }
```

Saídas em `.artifacts/thermal-validation/session-*/`; o banco bruto e o TRX local não são publicados.
O opt-in aciona apenas esta prova passiva e não é um controle de liberação de estresse.
