# Validação passiva no notebook de laboratório

O ambiente desta rodada, em 7 de novembro de 2026, é um único notebook com Windows 11 x64,
build 26200. A referência é a
Especificação Mestre v1.0, em especial as seções 12 a 15. O ensaio usa a prévia 5, o motor
self-contained da publicação de prévia e bancos temporários por sessão. A instalação e o
histórico em uso têm caminhos próprios.

## Roteiro e evidências

1. Confirmar a versão do motor e a alimentação. Usar a política de energia vigente.
2. Executar a suíte Release local e o ensaio de cinco minutos.
3. Executar 30, 60 e 240 minutos em sequência. Cada duração usa os onze módulos e os discos
   detectados, com análise detalhada da bateria e do Wi-Fi passivo.
4. Registrar memória residente, memória privada, tempo de CPU do motor, tamanho do SQLite e
   leitura do histórico durante a gravação. Ao terminar, conferir duração, limite de amostras e
   de mensagens, maior intervalo entre leituras válidas de RAM, reabertura e `PRAGMA quick_check`.
5. Exportar a sessão reaberta e verificar que o PDF pode ser lido. Banco e PDF de cada ensaio
   ficam na pasta temporária do teste. Os resumos e checkpoints sanitizados ficam no workspace.

O roteiro está em [scripts/validate-notebook.ps1](../../scripts/validate-notebook.ps1).
Memória, cadência e tamanho do banco são observações deste notebook. Tolerâncias de precisão
e limites operacionais exigem critérios próprios, conforme a seção 13.2.

## Resultados desta rodada

| Duração planejada | Tempo observado | Medições no SQLite | Maior mensagem | PDF reaberto | Resultado |
| --- | ---: | ---: | ---: | ---: | --- |
| 5 min | 302,84 s | 3.641 | 284.566 bytes | 13 páginas | Aprovado |
| 30 min | 1.800,97 s | 22.349 | 288.113 bytes | 13 páginas | Aprovado |
| 1 h | 3.600,37 s | 44.563 | 286.272 bytes | 13 páginas | Aprovado |
| 4 h | 14.400,66 s | 178.629 | 288.295 bytes | 13 páginas | Aprovado |

A coleta de cinco minutos alcançou o limite de 512 amostras por atualização. Foram reabertos
433 registros, o SQLite passou na checagem de integridade e o maior intervalo observado entre
amostras válidas de RAM foi de 12 s. Os módulos indicaram coleta parcial quando as fontes
tinham limitações, incluindo sensores e SMART. A evidência local está em
`.artifacts/validation/passive-5min.json`.

As duas sessões seguintes passaram na checagem do SQLite, na consulta concorrente do histórico
e na reabertura dos 433 registros. O limite de 512 amostras por atualização foi respeitado.
Os picos observados de memória privada do motor foram de 41,6 MiB em 30 min e 38,9 MiB em 1 h.
Os bancos finais ocuparam 9,01 MiB e 16,91 MiB, respectivamente.

A sessão de quatro horas fez 239 consultas de histórico durante a coleta e recebeu
33.110 mensagens. O pico observado de memória privada do motor foi de 41,49 MiB e o pico
residente foi de 94,11 MiB. A última amostra de memória privada foi de 37,55 MiB. O SQLite
chegou a 64,74 MiB, passou na checagem de integridade e reabriu 433 registros. O maior
intervalo entre amostras válidas de RAM foi de 8,10 s. O PDF reaberto tinha 224.330 bytes.
Os três ensaios longos estão em `.artifacts/validation/notebook-9456c018fb7b43a4bd7343244ba055d6/`.

A suíte Release executada neste notebook teve 92 testes unitários e 58 integrados aprovados.
A comparação exploratória de CPU com o contador PDH equivalente teve quatro pares, diferença
absoluta média de 0,29 ponto percentual e máxima de 0,46 ponto percentual. O ensaio registra
a diferença observada e não estabelece tolerância de liberação.

## Atualização e uso no notebook

O instalador normal atualizou a prévia 3 para a prévia 5 no mesmo diretório. O atalho e o
registro por usuário apontaram para a versão atualizada. Os hashes dos binários instalados
conferiram com a publicação, e o banco e os arquivos de preferências foram preservados.
O motor instalado passou em uma leitura real de RAM, cancelamento e reabertura com banco
temporário. Foram 150 testes na suíte padrão, quatro execuções de duração e uma prova adicional
do motor instalado. A atualização foi registrada em `update-installed-summary.json` junto aos
resultados de duração.

**Referência para uso passivo neste notebook** — `1.0.0-preview.5`. O pacote ensaiado está em
`.artifacts/installer/1.0.0-preview.5-700f43adbf1c4ea79a0ac58ab5fa99d6/GCMMonitor-Setup.exe`.
É um instalador local sem assinatura digital do produto. O SHA-256 é
`F58E04683AB3CEB6E51E957191D02BEF439455CFE2BF06E2879C7F9923826C5B`.

## Limites da validação

| Critérios | Evidência ou condição |
| --- | --- |
| CT-001, CT-004 a CT-006, CT-008 a CT-009 | A prévia 5 documenta primeira abertura, navegação e as duas escolhas de desinstalação em dados de ensaio. O uso do runtime incluído foi exercitado com o caminho do SDK retirado do ambiente |
| CT-002, CT-073 | Uma máquina recém-instalada sem .NET e o Windows 10 ficam fora do ambiente desta rodada |
| CT-017, CT-022 a CT-024 e CT-026 | Aprovação de precisão e de sensores depende de fontes comparáveis. Um estado parcial ou indisponível registra a capacidade efetivamente exposta |
| CT-032 | A consulta complementar de programas de inicialização segue pendente |
| CT-035 | Medição ativa de Wi-Fi fica bloqueada nesta rodada. O diagnóstico usa as fontes passivas |
| CT-038 a CT-041 | Durações de 5 min, 30 min, 1 h e 4 h aprovadas para coleta passiva neste notebook. Banco e PDF reabertos em todas |
| CT-076 | Evidência local de quatro horas com memória do motor, crescimento do SQLite e canal limitados conforme os resultados acima. A renderização WPF tem ensaio separado na suíte Release |
| CT-007, CT-043 a CT-047, CT-049 a CT-052, CT-060 sob carga | Estresse bloqueado no produto. Resultados de contenção com processos ociosos e de falhas simuladas valem para os respectivos subcasos |

O relatório da [prévia 5](V1.0-PREVIEW.5.md) e a [matriz anterior](RASTREABILIDADE_V0.4.md)
registram as evidências complementares. A liberação de estresse exige a política térmica e
todos os P0 correspondentes. A conclusão dos ensaios passivos qualifica apenas o comportamento
observado neste equipamento.
