# Relatório de entrega — v0.3.0

Data local: 06/11/2026 (America/Sao_Paulo). Ambiente de desenvolvimento anonimizado DEV-LOCAL-01,
Windows 11 x64 build 26200, SDK .NET 10.0.403. Referência funcional: Especificação Mestre v1.0,
seção 14, sem alteração do documento. Código de produto/testes: commit `8666d64`.
Branch: `codex/v0.3-diagnosticos-avancados`, baseada na v0.2. Sem merge na main. CI do código aprovado: https://github.com/Taconi2003/GCM-MONITOR/actions/runs/42323188135.

## Implementado

Os onze módulos são selecionáveis no Início e executam consultas por capacidade. Mantidos tema
escuro, cinco telas, MVVM/DI, motor separado, SQLite, gráficos reais, cancelamento e recuperação.

- GPU: modelos/drivers instalados e PDH por engine/LUID físico, sem associação presumida ao modelo.
- Temperaturas/RPM: adaptador WMI externo LibreHardwareMonitor existente, com identidade pai/sensor;
  ACPI como zona térmica separada. Sem instalar/iniciar provedor ou driver. Zero externo ambíguo é indisponível.
- Armazenamento: indicadores Storage de temperatura/desgaste/horas/erros por associação ao disco
  selecionado. SMART bruto/limiares específicos e integridade não determinados.
- Bateria: carga, capacidades quando expostas, desgaste e ciclos; metadados associados por instância
  completa+Tag, unidades relativas protegidas, autonomia por consumo somente em descarga válida.
- Wi-Fi: estado/sinal/enlace/canal e reconexões amostradas; não grava SSID/BSSID/perfil nem gera tráfego.
  Banda não é inferida pelo canal. Análise detalhada permanece passiva.
- PnP/drivers instalados, eventos selecionados de sete dias, alimentação/plano/mudanças e WUA informativo
  por histórico local/reinicialização. Sem busca ativa, download, instalação, reparo ou mudança de energia.
- Sessões pontuais encerram ao concluir; consultas já concluídas não viram parciais só porque outro módulo
  foi cancelado. Falha de fonte invalida séries anteriores, sem inserir pontos fictícios.
- IPC 3 com registros limitados; SQLite schema 3 aditivo e registros completos coletados persistidos
  transacionalmente. Resultados carregam registros duráveis ao concluir/reabrir a sessão.

## Verificações efetivamente executadas

Build Release final: **zero avisos e zero erros**. Restore travado aprovado após atualizar deliberadamente
os lockfiles para a nova dependência oficial de eventos. NuGet audit direto/transitivo: nenhum aviso
conhecido na consulta. Não é garantia de ausência de vulnerabilidades.

Suíte rápida final: **61 testes unitários aprovados; 22 integrados aprovados; 1 ensaio longo explicitamente
ignorado nessa execução**. Ensaio longo executado separadamente: consultar o resumo abaixo e o arquivo
sanitizado. Contagem distinta final e evidências individuais em `RESULTADOS_V0.3.json`.

Cobertura exercitada:

- Cálculos CPU/RAM/processos, médias ponderadas, PID reutilizado/processos encerrados, durações por relógio
  determinístico, falhas isoladas, cancelamento e persistência indisponível herdados da v0.2.
- Desgaste/autonomia da bateria, valores desconhecidos/relativos e associação por instância+Tag;
  sensores por Parent/Identifier, zona ACPI não reclassificada, zero ambíguo e desaparecimento da fonte.
- Associação PnP por IDs completos, inclusive prefixos longos; agrupamento GPU por adaptador/engine,
  rejeição de agregado inválido; Wi-Fi ausente/removido, reconexões e mudanças de energia simuladas.
- Filtros de eventos por origem+ID/janela, sessão pontual sem espera, preservação de módulo pontual concluído
  ao cancelar outro e regressão de preparação PDH quando SMART está indisponível.
- Leituras passivas reais locais de inventário, CPU/RAM/discos/processos e fontes avançadas; motor pontual
  real, notas/registros reabertos, migração schema 2→3, origem mista rejeitada e exclusão transacional.
- Encerramento apenas do motor criado pelo teste e recuperação de dados; supervisor recusa gerar carga.
- 26 cenários de renderização WPF em 1440×960 e 1000×700 lógicos, incluindo evidências avançadas expansíveis,
  sem erros de binding. Capturas usam fixtures sintéticas identificadas. Revisão ao vivo: abertura, cinco telas,
  estados sem sessão e estresse bloqueado; nenhum diagnóstico foi iniciado manualmente nessa conferência.

**Ensaio final de cinco minutos:** 84 testes distintos aprovados no total. Ensaio final: 300,514 s reais, 3580 medições persistidas, 432 registros reabertos; máximo de 512 pontos e 282718 bytes por snapshot (limite IPC 1 MiB). Onze módulos selecionados, atividade contínua de disco validada; estado final Concluída com limitações por módulo. Nenhum estresse.

CAPACIDADES_V0.3.json registra a primeira consulta por fonte: PDH ainda pode estar preparando o intervalo. VALIDACAO_PASSIVA_V0.3.json contém os estados finais da sessão, incluindo 528 leituras válidas de atividade de disco. Banco do ensaio é temporário e isolado; identificadores e registros brutos não foram versionados.
A janela da UI é limitada; estatísticas/amostras e registros coletados são gravados no banco.

## Problemas encontrados e tratamento

1. O contrato WMI LibreHardwareMonitor v0.9.4 converte null em zero. Integração trata esse zero como
   ambíguo/indisponível. Sem promessa de RPM zero ou sensor térmico a zero e sem autorização de estresse.
2. O primeiro ensaio detectou ausência de amostras contínuas de disco quando SMART estava indisponível
   e PDH ainda aguardava a segunda leitura. Corrigido no motor (`8666d64`), teste determinístico acrescentado
   e ensaio final repetido exigindo estatísticas reais de atividade de disco.
3. Tentativa de recompilar durante o primeiro ensaio encontrou DLLs do motor em uso (MSB3026/MSB3027).
   A coleta terminou; build repetido em sequência passou sem avisos/erros. Não foi suprimido o erro.
4. Fontes locais de temperatura/RPM e indicadores SMART não estavam acessíveis. São indisponibilidade,
   não evidência de defeito ou saúde. Eventos excederam a janela por log: resultado explicitamente parcial.
5. A integração GitHub permite leitura/push, mas a criação de PR retornou 403: Resource not accessible by integration.
   A branch permanece revisável sem alteração da main.

## Limitações e próximos passos

- **Não validado em hardware real:** temperatura CPU/GPU/RPM pelo provedor externo e leitura SMART
  equivalente em dispositivos compatíveis. Necessária comparação do mesmo sensor/indicador com referência.
- GPU PDH foi exercitado localmente; não há tolerância de precisão aprovada, correlação LUID-modelo,
  VRAM validada ou classificação integrada/dedicada. Fonte externa não tem frescor atestado para segurança.
- Wi-Fi sem banda validada, ping/perda ou teste ativo. Sem permissões, dados ficam indisponíveis.
  WUA local não determina todas as atualizações pendentes. Drivers só mostram versões instaladas.
- Bateria: descarga/autonomia real não foi forçada; fórmulas e casos de descarga usam fixtures.
- Eventos limitados a sete dias e 128 por log, PnP a 512. Histórico lista 100 sessões; paginação/retencão
  configurável, configurações definitivas, PDF e instalador continuam na v1.0.
- Sem ensaio real de 30 min/1 h/4 h, suspensão física, múltiplas GPUs ou matriz Windows 10/11 completa.
- Nenhum estresse foi executado ou liberado. Próximo marco é PoC de observabilidade/sensores em laboratório
  e v0.4 condicionada a todos os P0 de estresse. Esta entrega não certifica uso em clientes.

Os 77 CT integrais da v1.0 permanecem pendentes de sua matriz completa. Evidências parciais estão em
`RASTREABILIDADE_V0.3.md`; não atribuir aprovação geral a partir de testes locais desta etapa.
Fontes primárias e escolhas de integração: `../arquitetura/V0.3.md`. Build/uso/estrutura no README.
