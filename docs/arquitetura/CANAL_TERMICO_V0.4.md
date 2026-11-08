# v0.4 — canal de temperatura passiva da CPU

O GCM pode receber temperaturas de núcleo e pacote de um auxiliar temporário. A interface e o motor
mantêm privilégios normais. Essa integração é de laboratório, **somente para observação passiva**;
não aprova precisão, política térmica ou os P0 do estresse. A especificação mestre, as cinco telas e
o bloqueio de carga permanecem.

## Ativação e fluxo

1. O build Release padrão funciona sem o auxiliar. O laboratório prepara uma cópia local com
   `scripts/thermal-probe.ps1 -PrepareBridge`. O script verifica arquivo, biblioteca e driver
   revisados, incluindo assinatura do driver, e copia binários apenas para o build ignorado pelo Git.
2. Com **Temperaturas e ventoinhas** selecionado, o GCM oferece a leitura opcional no início da
   sessão. Se o técnico recusar, segue com as fontes que já estão disponíveis. Se aceitar, a
   confirmação normal do Windows ainda é necessária; o motor não instala software.
3. O protocolo do motor 5 carrega apenas a intenção de usar o auxiliar. Ele rejeita a intenção em
   demonstração ou sem o módulo selecionado. O caminho do auxiliar é fixo em `engine/thermal`,
   dentro do build; nenhuma mensagem fornece caminho, comandos de sistema ou argumentos livres.
4. O motor tenta iniciar o auxiliar uma vez por sessão, na primeira leitura do módulo. O auxiliar
   em `tools/GcmMonitor.SensorProbe` opera em modo `--bridge`, sem escrever arquivos ou banco.
   O modo antigo `--output` continua sendo uma prova passiva separada.
5. Temperaturas usam `°C`, horário de recebimento no motor, componente e fonte identificados.
   O pipeline existente grava no SQLite e apresenta as séries em Monitoramento e Resultados, com
   reabertura pelo Histórico. A coleta por WMI, ACPI e GPU por PDH continua independente.

## Privilégios e canal local

- Servidor com ACL explícita para o SID da conta, primeira instância e handles não herdáveis. Cliente
  conecta em `.` com `TokenImpersonationLevel.Identification`: o servidor não pode usar o token
  elevado do auxiliar para executar operações. A conta dona do pipe é conferida pelo cliente.
- Ambos os lados verificam PID e instante de início do processo esperado. A identificação inclui
  sessão, versão e nonce de 256 bits. O auxiliar exige início autenticado e verifica os hashes da
  biblioteca 0.9.6 e do driver PawnIO 2.2.0 revisados antes de abrir o hardware.
- A operação privilegiada é leitura pela biblioteca, com apenas a CPU habilitada. O protocolo
  aceita `start`, `heartbeat` e `stop` na fase apropriada. Não há escrita de parâmetro, ajuste de
  ventoinha/energia, instalação de driver, execução de outro processo ou comando de carga no auxiliar.
- A decisão de elevar permanece com o Windows. Este caminho requer a mesma conta nos dois lados;
  credenciais de outra conta não abrem esse canal. Não há serviço persistente nem alteração da ACL
  do PawnIO. O protótipo de laboratório não integra uma distribuição assinada.

## Qualidade e encerramento

- Mensagens de até 32 KiB antes da alocação e até 64 sensores. Campos desconhecidos, identidades
  incoerentes, sequência repetida ou fora de ordem, tempo regressivo e alegação de qualificação
  de segurança invalidam a fonte. Não há reconexão automática na mesma sessão.
- Só entram núcleos Intel individuais e `CPU Package` reconhecidos, com metadados plausíveis.
  Agregados, distância até TjMax, sensores sem identificação e outros fabricantes não entram nessa
  fonte direta. TjMax é metadado da conversão, nunca limite operacional universal.
- O auxiliar amostra a cada 2 s. O motor usa a última amostra nova na cadência de referência de
  5 s e evita usar duas vezes a mesma sequência. O protocolo 2 do canal térmico transporta o
  início e o fim da consulta no contador monotônico compartilhado pelo Windows. O receptor
  recusa sequência com tempos incoerentes ou idade superior a 6 s desde o início, incluindo
  atrasos de transporte e de consumo. Um auxiliar do protocolo anterior é recusado no início.
  Os pontos de gráfico ainda usam o horário de recebimento. O limite mede a consulta feita
  pelo software e não qualifica a atualização do firmware.
- Permissão recusada, fonte ausente, conexão interrompida, valor nulo ou atraso não produzem zero
  artificial nem repetição de temperatura. Séries conhecidas recebem lacuna; valores anteriores
  permanecem no histórico. A indisponibilidade da fonte direta não desativa outros módulos.
- Heartbeat de 2 s, limite de 8 s para mensagem/ausência, duração máxima de 4,1 h. Ao fechar o canal,
  o auxiliar cancela a leitura e fecha a biblioteca. Se a chamada nativa travar, o auxiliar tem
  um prazo adicional de 2 s para terminar por conta própria; o motor aguarda saída do processo que
  iniciou. Esses prazos são da coleta passiva e não constituem aprovação de parada sob estresse.

## Uso no notebook de laboratório

Com arquivo oficial e driver já revisados conforme a [prova de acesso](../testes/VALIDACAO_TERMICA_V0.4.md):

```powershell
.\scripts\dev.ps1 Restore
.\scripts\dev.ps1 Build -Configuration Release
.\scripts\thermal-probe.ps1 -PrepareBridge
.\scripts\dev.ps1 Run -Configuration Release
```

Selecione **Temperaturas e ventoinhas**, inicie a sessão e, se desejar a leitura direta, confirme
na interface e no Windows. Se a fonte não estiver disponível, a sessão continua normalmente. A
ferramenta não baixa/instala o driver. Não copiar esse build como distribuição para clientes.

## Referências técnicas

- [PipeOptions — verificação de elevação no Windows](https://learn.microsoft.com/dotnet/api/system.io.pipes.pipeoptions).
- [Níveis de representação no Windows](https://learn.microsoft.com/en-us/windows/win32/secauthz/impersonation-levels).
- [NamedPipeClientStream — opções do cliente](https://learn.microsoft.com/dotnet/api/system.io.pipes.namedpipeclientstream.-ctor).
- [Contador monotônico de alta resolução do Windows](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps).
- [Fontes, versões, hashes e remoção do PawnIO](../testes/VALIDACAO_TERMICA_V0.4.md).
