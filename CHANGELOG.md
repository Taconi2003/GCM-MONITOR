# Changelog

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
