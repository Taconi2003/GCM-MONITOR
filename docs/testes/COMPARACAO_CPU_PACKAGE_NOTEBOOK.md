# Comparação passiva do CPU Package no notebook

Rodada de 7 de novembro de 2026, no notebook de laboratório com Windows 11 x64 build 26200.
A Especificação Mestre v1.0 continua sendo a referência funcional. O estresse físico permanece
bloqueado. A sonda de laboratório faz somente a leitura passiva da CPU.

## Instalação e coleta

- Atualização da instalação habitual de `1.0.0-preview.5` para `1.0.0-preview.6`, pelo pacote
  aprovado na revisão visual. Os arquivos executáveis conferiram com o pacote e o banco SQLite
  e as preferências mantiveram seus hashes durante a atualização.
- O teste `InstalledEngineUsesBundledRuntimeAndReopensPassiveSession` passou com o executável da
  instalação habitual. O motor coletou uma medição real de memória e reabriu a sessão passiva
  em um banco temporário, separado do histórico de uso.
- A sonda LibreHardwareMonitorLib 0.9.6, com o PawnIO 2.2.0 já instalado e a confirmação normal
  do Windows, completou duas novas rodadas de 30 amostras. Em ambas havia um CPU Package por
  amostra e 11 leituras candidatas por ciclo contando os núcleos. O maior intervalo entre o
  início de duas leituras foi de 2,224 s entre as duas rodadas.

## Comparação com HWMonitor 1.68

A ferramenta `scripts/compare-cpu-package.ps1` reproduziu a comparação exploratória da prévia 6
a partir das quatro observações visuais registradas com horário UTC na coluna **Value**, em
**CPU → Temperatures → Package**.

| Medida | Resultado |
| --- | ---: |
| Leituras de CPU Package na captura comparada | 30 |
| Observações de referência pareadas | 4 de 4 |
| Maior separação entre os horários registrados | 1.037,1 ms |
| Diferença média com sinal (GCM − HWMonitor) | −2,5 °C |
| Diferença absoluta média | 3,0 °C |
| Maior diferença absoluta | 5,0 °C |

O primeiro CSV trazia uma única amostra com tempo relativo de 0,5 s e ficou fora da comparação.
O segundo CSV trouxe 457 leituras e terminou no tempo relativo de 261,5 s. Como o HWMonitor não
gravou a hora absoluta, associamos a última amostra a três horários do mesmo arquivo, separados
por até 1,918 s. Todos os cenários encontraram 30 pares da segunda rodada do GCM.

| Horário atribuído à última amostra | Pares | Maior separação estimada | Diferença média com sinal | Diferença absoluta média | Maior diferença absoluta |
| --- | ---: | ---: | ---: | ---: | ---: |
| Criação do CSV | 30 | 451,6 ms | −0,83 °C | 2,77 °C | 12 °C |
| Meio do intervalo | 30 | 407,8 ms | −1,47 °C | 3,53 °C | 16 °C |
| Última gravação do CSV | 30 | 408,9 ms | −0,90 °C | 2,90 °C | 10 °C |

Esses horários correspondem à geração do arquivo. O instante real de cada leitura pode estar
fora da janela estimada. Os resultados mostram como a escolha do horário altera a diferença
observada. A hora do GCM representa a recepção após a consulta e a da referência visual
representa a observação da tela. A precisão e a idade efetiva do valor no sensor ainda precisam
ser qualificadas.

## Reproduzir com dados locais

1. Use o roteiro da [sonda térmica](VALIDACAO_TERMICA_V0.4.md) para gerar `private-capture.json`.
2. Prepare um CSV local com as colunas `Timestamp,Celsius` e apenas o valor corrente da
   temperatura do pacote da CPU. Use ISO 8601 com fuso explícito e ponto decimal em graus
   Celsius. Para o CSV do HWMonitor, use o conversor abaixo com o nome exato do grupo da CPU
   no primeiro cabeçalho e a hora da última amostra. Registre como essa hora foi determinada.
3. Execute a comparação no PowerShell 7. O limite de 1.200 ms é a janela de pareamento usada
   nesta rodada. Ele mede separação entre horários registrados.

```powershell
./scripts/convert-hwmonitor-package.ps1 `
  -HwMonitorCsv '<arquivo-local-do-hwmonitor.csv>' `
  -CpuGroup '<nome-do-grupo-da-cpu>' `
  -LastSampleAt '<horário-ISO-8601-com-fuso>' |
  Set-Content '.artifacts/thermal-validation/<rodada>/referencia.csv'

./scripts/compare-cpu-package.ps1 `
  -ProbeCapture '.artifacts/thermal-validation/<rodada>/private-capture.json' `
  -ReferenceCsv '.artifacts/thermal-validation/<rodada>/referencia.csv' `
  -MaximumOffsetMilliseconds 1200
```

O pareamento percorre a série com menos leituras em ordem cronológica e usa a leitura restante
mais próxima da outra série dentro da janela, uma vez por amostra. Com zero pares, as
estatísticas de diferença ficam vazias. O relatório imprime somente contagens, diferenças e
intervalos. Capturas e CSVs brutos permanecem locais.

### Comparar janelas de leitura

Quando a referência registra o começo e o fim de cada observação, use as colunas
`StartUtc,EndUtc,Celsius` no lugar de `Timestamp,Celsius`. As janelas devem estar livres
de sobreposição e ter fuso explícito. O parâmetro `-UseProbeQueryWindow` usa o intervalo
entre o início da consulta na sonda e o recebimento do valor. Sem o parâmetro, o horário
da sonda continua sendo o recebimento, para reproduzir as comparações anteriores.

```powershell
./scripts/compare-cpu-package.ps1 -ProbeCapture '<captura-local.json>' `
  -ReferenceCsv '<janelas-da-referencia.csv>' -UseProbeQueryWindow -MaximumOffsetMilliseconds 1200
```

Um par só entra se a maior separação possível entre as extremidades das duas janelas
caber no limite. A saída distingue `LargestObservedOffsetMs`, que compara os pontos
médios, de `LargestConservativeOffsetMs`, que considera as extremidades. Uma janela de
captura da tela delimita quando a leitura foi observada. Seu uso ainda deixa em aberto
o momento de aquisição pelo programa de referência e a atualização do sensor físico.

Em 8 de novembro, a revisão reproduziu as quatro comparações anteriores no modo original.
Usando a janela da sonda, a comparação visual manteve 4 pares e 3,00 °C de diferença absoluta
média. Os três cenários que estimam horários pelo arquivo mantiveram 30 pares cada e
apresentaram médias de 2,53, 3,43 e 2,90 °C. São reanálises das mesmas séries, e os horários
absolutos de aquisição do HWMonitor seguem sem comprovação. Dez verificações com dados
fictícios cobriram a compatibilidade, os limites do pareamento e a rejeição de janelas
ambíguas ou inválidas.

## Testes desta rodada

- Suíte local em Release com 101 testes unitários e 62 de integração aprovados. Cinco
  casos opcionais ficaram fora da suíte padrão. O caso do motor instalado foi executado
  e aprovado separadamente, totalizando 164 testes .NET aprovados nesta rodada.
- Os scripts passaram em 11 verificações com dados fictícios. A conversão foi verificada com
  grupos de hardware distintos, cabeçalhos ambíguos e tempo relativo repetido. O pareamento
  foi verificado com referência esparsa e densa, fusos diferentes, amostras ausentes, janela
  temporal, ausência de pares, horários inválidos ou duplicados, temperatura inválida e
  identidade do pacote ambígua ou alterada.

## Próximo ensaio

O próximo ensaio do CT-022 precisa registrar os horários absolutos na referência e ampliar a
observação para avaliar a concordância e a cadência. A qualificação para proteção física
também depende de conhecer a atualização do sensor, confirmar os sinais de limitação térmica e
validar a parada por falha no equipamento. O relatório da [prévia 6](V1.0-PREVIEW.6.md) registra
separadamente as evidências da simulação e do supervisor com processo ocioso.

### Referência com horários no próprio CSV

Em 9 de novembro, a edição portátil HWiNFO64 8.34-5870 foi obtida no
repositório do autor e conferida localmente com assinatura Authenticode válida da
REALiX. A janela de sensores exibiu `CPU Inteiro` no grupo Intel DTS. Para a
comparação, selecione a coluna desse grupo, preserve os horários do CSV e converta
apenas o valor atual para `Timestamp,Celsius`, com o fuso explícito. O formato
RFC 3339/ISO 8601 está disponível na configuração de registro de sensores.
O arquivo bruto fica em pasta local ignorada pelo Git.
[Arquivo do autor](https://sourceforge.net/projects/hwinfo/files/Windows_Portable/hwi_834.zip/download),
[formatos de horário do HWiNFO](https://www.hwinfo.com/version-history/) e
[identidade do CPU Package DTS](https://www.hwinfo.com/forum/threads/hwinfo-cpu-temperature.5962/).
