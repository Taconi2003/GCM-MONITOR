# Validação passiva no notebook de laboratório

O ambiente desta rodada é um único notebook com Windows 11 x64, build 26200. A referência é a
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

A coleta de cinco minutos alcançou o limite de 512 amostras por atualização. Foram reabertos
433 registros, o SQLite passou na checagem de integridade e o maior intervalo observado entre
amostras válidas de RAM foi de 12 s. Os módulos indicaram coleta parcial quando as fontes
tinham limitações, incluindo sensores e SMART. A evidência local está em
`.artifacts/validation/passive-5min.json`.

## Limites da validação

| Critérios | Evidência ou condição |
| --- | --- |
| CT-001, CT-004 a CT-006, CT-008 a CT-009 | A prévia 5 documenta primeira abertura, navegação e as duas escolhas de desinstalação em dados de ensaio. O uso do runtime incluído foi exercitado com o caminho do SDK retirado do ambiente |
| CT-002, CT-073 | Uma máquina recém-instalada sem .NET e o Windows 10 ficam fora do ambiente desta rodada |
| CT-017, CT-022 a CT-024 e CT-026 | Aprovação de precisão e de sensores depende de fontes comparáveis. Um estado parcial ou indisponível registra a capacidade efetivamente exposta |
| CT-035 | Medição ativa de Wi-Fi fica bloqueada nesta rodada. O diagnóstico usa as fontes passivas |
| CT-039 a CT-041 e CT-076 | Dependem da conclusão das durações reais e da revisão dos recursos observados. O roteiro instrumenta o motor, o histórico e o canal consumido pela interface |
| CT-007, CT-043 a CT-047, CT-049 a CT-052, CT-060 sob carga | Estresse bloqueado no produto. Resultados de contenção com processos ociosos e de falhas simuladas valem para os respectivos subcasos |

O relatório da [prévia 5](V1.0-PREVIEW.5.md) e a [matriz anterior](RASTREABILIDADE_V0.4.md)
registram as evidências complementares. A liberação de estresse exige a política térmica e
todos os P0 correspondentes. A conclusão dos ensaios passivos qualifica apenas o comportamento
observado neste equipamento.
