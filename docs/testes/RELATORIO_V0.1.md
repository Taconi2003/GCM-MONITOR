# Relatório de entrega — GCM Monitor v0.1.0

Data: **06/11/2026**, fuso **America/Sao_Paulo**.
Código validado: `2cc2169` (mesmas fontes executadas no build final; commits posteriores apenas documentais).
Fonte obrigatória: Especificação Mestre v1.0, lida integralmente e preservada em `docs/`.
O acesso de leitura/escrita ao repositório `Taconi2003/GCM-MONITOR` foi confirmado antes das alterações.

## Funcionalidades implementadas

- Solução .NET 10 com sete projetos de produto e dois de testes, sem dependências circulares.
- Interface WPF/XAML, MVVM com CommunityToolkit, DI e navegação lateral com **exatamente cinco telas**.
- Tema exclusivamente escuro azul-marinho, realces azul/ciano, cartões e estados com texto além de cor.
- Início: cinco resumos de hardware fictício, 11 módulos, habilitar/desabilitar/restaurar,
  quatro durações aprovadas, seleção individual de discos e opções detalhadas dependentes dos módulos.
- Estresse separado, desligado e bloqueado na UI, no contrato e no executável reservado.
- Motor demonstrativo em processo separado por sessão; named pipe restrito ao usuário,
  protocolo versionado, validação, limite de mensagem e cancelamento idempotente.
- Monitoramento: estado, etapa, tempo/progresso **simulados**, gráficos sintéticos de CPU/RAM/discos
  com eixos e unidades, ausência de séries quando não há duas amostras válidas e cancelamento.
- Resultados: estados vazio/ativo/concluído/parcial/erro, contadores, achados cautelosos,
  cartões expansíveis dos módulos, ocorrências da coleta e observações persistidas por sessão.
- Histórico: sessões sintéticas em ordem decrescente, filtros por computador/data/status,
  abertura, cenários concluído/cancelado/interrompido/erro e exclusão confirmada apenas de sessões terminais.
- Configurações: aparência/cadência/proteções fixas, preferências ilustrativas, pasta de relatório,
  informação de armazenamento e limites desta etapa.
- Fundação SQLite com WAL, transações, snapshots, transições e notas separadas; recuperação sintética
  marca interrupção sem retomar processos. Dados demonstrativos separados do futuro banco real.
- Scripts reproduzíveis, lockfiles, inventário de dependências, documentação e workflow Windows de CI.

## Verificações efetivamente executadas

Ambiente: Windows x64, build `10.0.26200.0`; SDK **10.0.403** isolado no workspace.
A identificação do ambiente de desenvolvimento não representa validação da matriz de plataformas.

| Verificação | Resultado efetivo |
|---|---|
| Restore final `--locked-mode` | Aprovado, nove projetos |
| Build final Release | Aprovado, **zero avisos e zero erros** |
| Testes unitários | **27 aprovados**, zero falhas/ignorados |
| Testes de integração | **11 aprovados**, zero falhas/ignorados |
| Total automatizado final | **38 aprovados** |
| Auditoria NuGet, diretos e transitivos | Nenhuma vulnerabilidade conhecida apontada pelas fontes consultadas naquele momento |
| Revisão do aplicativo aberto | Navegação pelas cinco telas, estados vazios, histórico com quatro fixtures e abertura de resultado fictício |
| Verificação visual de imagens renderizadas | Início, monitoramento com séries, resultados parciais e histórico em janela menor revisados |
| Estresse real | **Não executado**; não existe gerador de carga nesta versão |

Execução final dos testes: **16:35:20–16:35:39 (UTC−03)**. O sumário por teste foi extraído
dos TRX e registrado em [RESULTADOS_V0.1.json](RESULTADOS_V0.1.json), sem nome da máquina nem caminhos pessoais.
Os tempos dos casos constam desse arquivo. As 22 capturas geradas pelo teste atual abrangem:
cinco telas vazias em dois tamanhos, resultados concluído/cancelado/interrompido/erro em dois tamanhos,
histórico preenchido em dois tamanhos e gráficos em dois tamanhos.

Cobertura automatizada:

- Catálogo fechado, durações, estresse bloqueado, seleção vazia, módulos duplicados/desconhecidos,
  opções dependentes, seleção de disco e transições válidas/inválidas.
- Qualidade das medições e ausência de valores fictícios para sensores indisponíveis.
- Navegação preserva seleção e sessão ativa; segunda execução bloqueada; cancelamento e confirmação de exclusão.
- Persistência real em SQLite: reabertura, transições, notas com acentos/parâmetros, WAL,
  biblioteca nativa corrigida, recuperação sintética, proteção de sessão ativa e arquivo externo sentinela.
- Processos reais com **dados sintéticos**: consultas pontuais encerram sem espera artificial,
  cancelamento preserva estado, versão de protocolo incorreta rejeitada antes de criar o banco,
  disco selecionado respeitado e supervisor retorna recusa sem gerar carga.
- Renderização WPF em **1440×960 e 1000×700 unidades lógicas**, presença de pixels visíveis
  e ausência de erros de binding durante os cenários exercitados.

Os testes WPF não substituem inspeção completa de DPI, acessibilidade, múltiplos monitores
ou desempenho em hardware limitado. O workflow foi criado; sua execução remota é registrada
pelo GitHub e não é presumida aprovada por este relatório local.

## Problemas encontrados e corrigidos

| Problema | Correção / evidência |
|---|---|
| Ambiente tinha somente SDK .NET 8 | SDK oficial .NET 10 isolado; build e testes executados com ele |
| Restore inicial trouxe SQLite nativo com advisory de alta severidade | Bundle fixado em 3.0.5; SQLite 3.53.4 resolvido; consulta de versão nativa e auditoria final aprovadas |
| Incompatibilidades iniciais de compilação XAML e imports WPF | Corrigidas antes do build final sem avisos/erros |
| Binding de progresso tentava escrever em propriedade de leitura | Binding unidirecional explícito; abertura e renderização verificadas |
| Repetir cancelamento podia bloquear o canal | Envio idempotente por sessão; teste com cancelamentos repetidos aprovado |
| Fechamento podia reentrar no evento Closing | Fechamento agendado após o evento; teste WPF e fechamento do aplicativo verificados |
| Primeiro teste visual podia produzir imagem transparente | Passou a renderizar o conteúdo e exigir pixels visíveis; capturas efetivas revisadas |
| Títulos, duração e células perdiam legibilidade | Contraste, rótulo da duração e quebra de linhas corrigidos; janela inicial limitada à área útil |

Não há falha conhecida aberta nos testes executados. Isso não significa ausência de defeitos
em cenários não exercitados nem aprovação para uso em clientes.

## Limitações e pendências

- **Não validado em hardware real**: nenhum sensor, SMART, WMI, evento real, driver, bateria,
  Wi-Fi, desempenho, Windows Update ou identificação real foi coletado/validado.
- Duração acelerada é uma reprodução visual; não valida sessões reais de 5 min/30 min/1 h/4 h.
- Segurança de estresse, watchdog, heartbeat, limites térmicos e Job Objects **não implementados/validados**.
  O bloqueio da função permanece obrigatório.
- Persistência é uma fundação de snapshots sintéticos, não o esquema completo de telemetria da v0.2.
  Quedas de energia, disco cheio, concorrência prolongada e corrupção real não foram ensaiados.
- Tabelas e gráficos analíticos completos, Top 10 acumulado real e interpretação por evidência
  dependem das futuras fontes; os detalhes atuais são exemplos/estados visuais.
- PDF está visível e desabilitado; exportação não implementada. Preferências ilustrativas de avisos
  e pasta valem somente durante a execução. Uso em disco/retenção automática/paginação aguardam as próximas etapas.
- Histórico da UI limita a consulta a 100 sessões. Não há exclusão automática ou migração do banco real.
- Inno Setup, self-contained final, assinatura, atualização e desinstalação pertencem à v1.0;
  nenhum instalador foi produzido ou testado. O build de desenvolvimento não é produto portátil.
- A matriz CT-001 a CT-077 **não foi aprovada integralmente**. A [rastreabilidade](RASTREABILIDADE_V0.1.md)
  relaciona evidências parciais desta etapa; os critérios finais da v1.0 continuam pendentes.
- Compatibilidade com Windows 10, outras compilações do Windows 11, DPI real e ensaios longos permanecem pendentes.

## Próximos passos autorizados pelo plano

1. Revisar e integrar esta v0.1, sem anunciar capacidade de diagnóstico real.
2. Na v0.2, implementar fontes básicas uma a uma, motor passivo e persistência normalizada;
   comparar CPU/RAM/processos/discos com referências equivalentes do Windows.
3. Validar isolamento, recuperação, falhas de persistência e cancelamento antes de ampliar coletas.
4. Na v0.3, comprovar capacidades e fontes dos módulos avançados por hardware/API.
5. Manter estresse bloqueado até v0.4 e aprovação dos P0 pertinentes em laboratório.
6. Executar distribuição/PDF/configurações definitivas e matriz completa somente na etapa v1.0.

Para repetir os testes, seguir o README. TRX e capturas completos ficam em `.artifacts/`
(ignorados pelo Git); o sumário sanitizado e duas prévias puramente sintéticas estão versionados.
