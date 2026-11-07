# Changelog

## 1.0.0-preview.3 — 07/11/2026

- Instalador Windows x64 por usuário com runtime .NET 10 incluído.
- Proteção de instalação em uso e desinstalação com opção de preservar dados.
- Ensaio isolado de instalação, execução do motor e remoção.

## 1.0.0-preview.2 — 07/11/2026

- Exportação PDF em Resultados e Histórico, com identificação de sessões parciais e dados sintéticos.
- Estatísticas, ocorrências, observações salvas e gráficos que preservam lacunas da coleta.
- Exportação registrada no esquema 5. Arquivos externos permanecem no destino escolhido.

## 1.0.0-preview.1 — 07/11/2026

- Histórico paginado no SQLite, filtros sobre todas as sessões e total com tamanho do banco.
- Migração aditiva para esquema 4 e preferências persistentes por usuário.
- Retenção com prazo selecionável e limpeza confirmada das sessões finalizadas.

## Continuação 0.4.0 — proteção ambiental, 07/11/2026

- Prova ociosa observa notificações de suspensão e estado de alimentação do Windows.
- Prazo térmico separado do heartbeat com verificação de continuidade de amostras sintéticas.
- Prazos do filho independentes do recebimento da confirmação de início.

## Continuação 0.4.0 — canal térmico passivo, 06/11/2026

- Leitura direta opcional de núcleos Intel e pacote da CPU, com auxiliar temporário e confirmação do Windows.
- Canal autenticado e limitado, com mesma conta, isolamento do token elevado, heartbeat e recusa de leituras antigas.
- Temperaturas com fonte e horário nos gráficos, resultados e histórico; falhas não interrompem módulos independentes.
- Protocolo do motor 5; banco e cinco telas preservados. Sem instalação automática de driver ou liberação de estresse.

## Continuação 0.4.0 — validação térmica, 06/11/2026

- Prova passiva local com LHM 0.9.6/PawnIO 2.2.0: temperaturas acessíveis com elevação específica.
- Ferramenta de laboratório separada, arquivos oficiais verificados e capturas locais privadas.
- Correção de `Distance to TjMax`, excluída dos gráficos de temperatura absoluta; teste de regressão.
- Driver instalado somente no notebook autorizado; sem empacotamento, elevação da UI ou carga real.
- Precisão, frescor, política térmica, integração e P0 sob carga continuam pendentes.

## 0.4.0 — 06/11/2026

- Infraestrutura de supervisão; estresse real permanece bloqueado por falta de validação térmica/P0.
- Biblioteca Execution com Job anônimo, KILL_ON_JOB_CLOSE, associação atômica antes da execução e parada confirmada.
- Supervisor separado para prova ociosa: heartbeat, identidade de pares, nonce, sequência e mensagens de 16 KiB.
- Recusa de mensagens inválidas, incompletas, atrasadas ou de carga; nenhum gerador de estresse nesta versão.
- Contratos de fases/tempos/consentimento e extensão explícita, sem liberação de carga por consentimento isolado.
- Avaliação de bloqueio persistida por sessão, resultado com motivos e protocolo passivo 4; histórico anterior legível.
- Testes de processos ociosos, queda de supervisor, isolamento e coexistência com coleta passiva.
- Sensores, política térmica, execução/fases reais e P0 sob carga seguem pendentes; entrega não certificada para clientes.

## 0.3.0 — 06/11/2026

- Diagnósticos avançados passivos com registros, fontes, qualidade e limites por capacidade.
- Inventário de GPU e PDH por engine; sensores WMI externos/ACPI sem instalação de drivers.
- Confiabilidade de discos por associação Storage; ausência de SMART não significa integridade.
- Bateria detalhada, capacidades relativas protegidas, desgaste e autonomia estimada por consumo.
- Wi-Fi passivo, PnP/drivers, eventos limitados, energia/plano e histórico local Windows Update.
- Consultas pontuais sem espera artificial, falhas isoladas e invalidação de séries indisponíveis.
- SQLite schema 3 aditivo, registros duráveis e resultados hidratados ao reabrir; IPC 3 limitado.
- Seleção dos onze módulos no Início, resultados expansíveis e testes determinísticos/integrados.
- Zero ambíguo do provedor WMI externo tratado como indisponível. Estresse continua bloqueado.
- Banda Wi-Fi, SMART bruto, sensores em hardware real e matriz ampla permanecem não validados.

## 0.2.0 — 06/11/2026

- Coleta passiva real de CPU, RAM/comprometimento/paginação, processos e tráfego de discos.
- Inventário Windows/hardware e associação de discos físicos a volumes; metadados indisponíveis são explícitos.
- Durações reais, agendamento de referência de 2 s/5 s, falhas isoladas, lacunas e cancelamento parcial.
- Estatísticas ponderadas e rankings por PID + início, incluindo processos encerrados e cobertura observada.
- SQLite schema 2 com amostras, módulos, ocorrências e agregados normalizados; recuperação sem retomar estresse.
- Protocolo IPC 2 com verificação do PID do motor, separação de origem e proteção contra sessões concorrentes.
- Gráficos com janela limitada, estatísticas e Top 10 nos resultados; sete módulos avançados indisponíveis na etapa.
- Fixtures restritas aos testes; execução normal usa banco real distinto do demonstrativo v0.1.
- Estresse, PDF e instalador continuam bloqueados/pendentes conforme o plano aprovado.

## 0.1.0 — 06/11/2026

- Estrutura C#/.NET 10, WPF/XAML, MVVM, DI e nove projetos com responsabilidades separadas.
- Cinco telas navegáveis, tema escuro azul-marinho/ciano e seleção dos 11 módulos no Início.
- Motor demonstrativo em processo separado, named pipe local versionado e cancelamento.
- Modelos e contratos de sessão, módulos, leituras, achados e persistência.
- Fundação SQLite para fixtures, transições, notas e histórico com exclusão protegida.
- Dados sintéticos identificados; relógio da reprodução acelerado; nenhum diagnóstico real.
- Estresse bloqueado por contrato, interface e executável reservado.
- Testes de contratos, apresentação, SQLite, processos e renderização WPF; documentação de build e limites.

Especificação funcional permanece na versão 1.0, preservada sem alteração de conteúdo.
