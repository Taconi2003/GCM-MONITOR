# v0.4 — prova passiva de acesso térmico

Ensaio local de 06/11/2026, continuação da infraestrutura v0.4. **Não libera estresse.** A especificação
mestre permanece intacta. Acesso ao repositório confirmado antes das alterações; branch
`codex/v0.4-validacao-termica`.

## Resultado e alterações

- WMI LibreHardwareMonitor não estava disponível. A consulta ACPI sem elevação também não forneceu
  temperaturas utilizáveis. Isso não prova defeito dos sensores.
- Com autorização expressa do proprietário, foi instalado **PawnIO 2.2.0** oficial. Instalador e
  arquivo de driver instalado apresentaram assinatura válida.
- **LibreHardwareMonitorLib 0.9.6**, extraída do arquivo oficial .NET 10 verificado por SHA-256,
  conseguiu consultar temperaturas em um processo elevado exclusivo da prova.
- Foram obtidas **30 amostras / 330 leituras diretas**, de dez sensores de núcleo e um de pacote,
  sem valores ausentes. Sem elevação, as 30 amostras não tiveram valores numéricos.
- Não houve carga, ajuste de parâmetros de sensores, energia ou ventoinhas, nem alteração de
  configuração de segurança do Windows ou permissão do dispositivo.
- O coletor WMI foi corrigido: `Distance to TjMax` não entra nas séries de temperatura absoluta.
  Um registro explica a exclusão: margem térmica não é temperatura do componente.
- `tools/GcmMonitor.SensorProbe` permite repetir leitura CPU-only, fora da solução/aplicativo.
  O script verifica o arquivo oficial, extrai uma cópia nova, confere a biblioteca de saída e,
  antes da elevação, exige a versão/hash/assinatura do driver revisado.

A aplicação principal continua sem elevação. Esta prova **ainda não conecta as leituras diretas aos
gráficos, motor ou supervisor**. A integração WMI existente continua indicando indisponibilidade
quando seu provedor está ausente. Não foi instalado serviço GCM nem iniciado o aplicativo gráfico LHM.

## Ensaios efetivamente executados

| Verificação | Resultado neste ciclo |
|---|---|
| Build Release da solução | Aprovado; zero erros/avisos |
| Testes unitários | 75 aprovados |
| Testes de integração Windows | 46 aprovados; 1 ensaio longo explicitamente ignorado |
| Prova preliminar sem elevação | 30 amostras, nenhum valor numérico, sem erro de execução |
| Prova preliminar elevada | 30 amostras; 330 leituras diretas numéricas; nenhuma ausente |
| Ferramenta versionada, sem elevação | 30 amostras; nenhum valor numérico; captura concluída |
| Ferramenta versionada, elevada | Não executada: segunda solicitação UAC cancelada; não repetida |
| Estresse real / proteções físicas sob carga | Não executados |
| Comparação do mesmo sensor com ferramenta de referência | Pendente |

As provas preliminares usam um executável local anterior à ferramenta versionada. Não se atribui o
resultado elevado ao executável final sem executá-lo. O ensaio passivo de cinco minutos da entrega
anterior não foi repetido neste ciclo e não entra nessa contagem.

A regressão nova falhou inicialmente por esperar apenas uma medida após perda da fonte: existe
também o aviso geral de indisponibilidade. A asserção foi corrigida para conferir a série da CPU e a
ausência de valores fictícios em todas as medidas. O analisador xUnit exigiu sua sobrecarga própria
de filtro. Ambos os problemas foram corrigidos; a suíte final passou.

## Limites da evidência

O timestamp significa recebimento após `Update`, não idade fornecida pelo firmware. O relógio
monotônico registra duração/intervalo das chamadas, sem garantir frescor. Na prova elevada
preliminar, a maior chamada durou aproximadamente 178,47 ms, o maior intervalo entre conclusões foi
2,220 s e o intervalo entre primeira e última amostra foi 62,409 s.

A revisão do código identificou que `Core Max`/`Core Average` podem conservar o valor anterior quando
nenhum núcleo oferece leitura na atualização. `Distance to TjMax` é uma diferença. Esses sensores são
identificados e excluídos dos **candidatos para validação posterior** da ferramenta. Núcleos/pacote
precisam de comparação e ensaios de falha. Sensores desconhecidos não são candidatos automaticamente.
O WMI não garante frescor e não autoriza carga.

TjMax exposto pelo sensor não vira limiar de parada universal. Política por hardware, cobertura dos
sensores essenciais, supervisão independente, perda/atraso de leitura, energia, suspensão e falhas
reais sob carga continuam pendentes. `SafetyQualified=false` e `StressReleasePolicy.IsReleased=false`
permanecem. CT-022 e os CT integrais de estresse não receberam aprovação por esta prova de acesso.

## Repetição local

Requer Windows x64 e SDK de `global.json`. A ferramenta não baixa nem instala componentes. Obtenha o
[arquivo oficial LHM 0.9.6 .NET 10](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.NET.10.zip)
e coloque-o em `.tools/sensors/lhm-0.9.6/LibreHardwareMonitor.NET.10.zip`.

```powershell
# Sem elevação: preserva null quando não há acesso ao driver.
.\scripts\thermal-probe.ps1

# Somente com PawnIO revisado instalado e autorizado; usuário responde ao UAC normal.
.\scripts\thermal-probe.ps1 -Elevated
```

O comando elevado retorna após iniciar o processo, sem confirmar conclusão. Após aproximadamente
um minuto, confira `Completed`/`ErrorType` em `summary.json` no caminho informado. Chamadas nativas
são síncronas; a ferramenta passiva **não é um watchdog de proteção**.

### Sinal térmico atual do pacote Intel

Com o técnico presente e o PawnIO revisado já instalado, execute a captura avulsa opcional.

```powershell
./scripts/thermal-probe.ps1 -Elevated -PackageStatus
```

A opção usa a operação de leitura da biblioteca já revisada para um único pacote Intel que
anuncie gerenciamento térmico via CPUID. Consulta apenas `IA32_PACKAGE_THERM_STATUS`
(`0x1B1`) para este sinal e aplica o mesmo indicador de validade usado pela biblioteca
para a temperatura do pacote. O bit 0 informa se o sinal térmico do pacote estava ativo
no instante da consulta. A captura registra início e fim da chamada e deixa o valor vazio
quando a consulta falha. Um valor inativo descreve aquele instante. O relatório preserva
separadamente o total de tentativas, leituras válidas e sinais ativos.
[Manual Intel, gerenciamento térmico de pacote](https://cdrdv2-public.intel.com/874250/253669-090-sdm-vol-3b.pdf),
[leitura do pacote no LibreHardwareMonitor 0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/Hardware/Cpu/IntelCpu.cs)
e [API de leitura](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/PawnIo/IntelMsr.cs).

O comando sem `-Elevated` permite verificar o tratamento da ausência de privilégios. O
resultado deve trazer `PackageStatusAttempts=0` e o motivo em `PackageStatusUnavailable`.
Esta prova avulsa usa a mesma fonte de baixo nível da temperatura do GCM e deixa pendentes
a comparação independente, a confirmação física do sinal e a qualificação de proteção.

Saídas em `.artifacts/thermal-validation/<execução>/`, ignoradas pelo Git. `private-capture.json`
contém leituras/nomes/IDs de sensores: não publicar. `summary.json` só tem contagens/intervalos/estados,
mas exige revisão antes de publicação. Não há retomada/início automático de carga. A ferramenta não
integra a distribuição instalada do GCM.

## Componentes, revisão e remoção

| Item | Versão e SHA-256 |
|---|---|
| Arquivo LHM .NET 10 | 0.9.6; `29739C4959B01B348FDDAD87664066634BCFD4F46E9BF41E4E916C318BCFDB99` |
| LibreHardwareMonitorLib.dll | 0.9.6.0; `984B539048880D37257A5EDEAB36378152C54943069F400D68A34565292D35DA` |
| PawnIO_setup.exe | 2.2.0; `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032` |
| PawnIO.sys instalado | 2.2.0; `FCA6E7D58B0CF38DBB913A2B9E532F48629145D395F454B16A9F58E97B8D3940` |

Binários LHM examinados não têm assinatura Authenticode; origem conferida pelo arquivo da release e
hash. Não se atribui a eles a assinatura do PawnIO. Instalador PawnIO com assinatura válida; driver
com assinatura Microsoft Windows Hardware Compatibility Publisher válida. Usada edição assinada
oficial, sem `unrestricted`, acesso global de não administradores ou modo de teste do Windows.

**Estado final do notebook:** PawnIO permanece instalado em `C:\Program Files\PawnIO`, driver de
inicialização sob demanda. Sem solicitação de reinicialização. Para remover, encerre ferramentas de
sensores e use **Configurações do Windows → Aplicativos → PawnIO → Desinstalar**, atendendo ao UAC e
às instruções oficiais. Não apague driver manualmente nem altere DriverStore. A remoção não foi
executada/validada neste ensaio. Biblioteca/capturas ficam em pastas locais ignoradas; nada é empacotado.

LHM declara MPL-2.0. PawnIO declara GPL-2.0-or-later com exceção própria; seus módulos/dependências têm
licenças próprias. Isso não encerra a revisão de redistribuição. O instalador é fornecido
separadamente pelo projeto e não foi incorporado ao repositório.

## Próximos passos da v0.4

1. Repetir ferramenta final elevada e comparar o mesmo sensor com referência identificada.
2. Implementar canal privilegiado mínimo, sem elevar a UI, com identidade, limites de mensagens,
   validade/frescor e detecção de falha. Integrar coleta passiva antes da carga.
3. Definir/validar política térmica por hardware e eventos físicos do supervisor.
4. Exercitar os P0 em laboratório; somente a aprovação permite liberar estresse.

## Fontes primárias revisadas

- [Release LHM v0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6),
  [IntelCpu.cs](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/Hardware/Cpu/IntelCpu.cs),
  [Computer.cs](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/Hardware/Computer.cs),
  [PawnIo.cs](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/PawnIo/PawnIo.cs).
- [PawnIO oficial](https://pawnio.eu/), [instalador 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0),
  [licença e exceção](https://github.com/namazso/PawnIO).
- [Microsoft: Win32_TemperatureProbe](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-temperatureprobe):
  `CurrentReading` não é preenchido pela implementação WMI descrita.
