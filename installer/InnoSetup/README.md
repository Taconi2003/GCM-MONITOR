# Distribuição Windows — planejada para v1.0

A v0.1 entrega um build de desenvolvimento, sem instalador e sem edição portátil.
Não há script `.iss` executável nesta etapa: implementar e validar instalação/desinstalação
pertence à v1.0 na seção 14 da Especificação Mestre.

Contrato a preservar na implementação futura:

1. Publicar App e Engine com .NET 10 **self-contained**, RID `win-x64`, incluindo os runtimes
   e dependências de cada processo. Não usar publicação de arquivo único sem validar os componentes nativos.
2. Empacotar com Inno Setup como `GCMMonitor-Setup.exe`, instalação por usuário,
   `PrivilegesRequired=lowest`, destino `%LOCALAPPDATA%\Programs\GCM Monitor\`.
3. Registrar atalhos e Aplicativos instalados. Validar arquitetura e requisitos do Windows antes de instalar.
4. Preservar/migrar dados próprios em atualização somente com procedimento testado.
5. Na remoção, encerrar sessões/processos próprios, avisar sobre dados não exportados e oferecer
   preservar/remover dados locais. Validar caminhos e propriedade antes de qualquer remoção recursiva.
6. Nunca remover PDFs externos, dados de terceiros, processos alheios ou alterar Windows/energia/drivers.
7. Executar CT-001 a CT-007 e demais P0/P1 aplicáveis, incluindo máquina sem runtime instalado.

Um comando `dotnet publish` isolado não é um instalador nem comprova que o motor, o banco,
as permissões ou a desinstalação estão corretos. Publicação, pacote e testes do instalador
**não foram executados na v0.1**.
