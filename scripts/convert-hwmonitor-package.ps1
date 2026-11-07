param(
    [Parameter(Mandatory)][string]$HwMonitorCsv,
    [Parameter(Mandatory)][string]$CpuGroup,
    [Parameter(Mandatory)][string]$LastSampleAt
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$lastTime = [DateTimeOffset]::MinValue
if ($LastSampleAt -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$' -or
    ![DateTimeOffset]::TryParse($LastSampleAt, $culture, [Globalization.DateTimeStyles]::None, [ref]$lastTime)) {
    throw 'Informe o horário da última amostra em ISO 8601 com fuso explícito.'
}

function Read-Number([string]$Value) {
    $number = 0.0
    if (![double]::TryParse($Value, [Globalization.NumberStyles]::Float, $culture, [ref]$number) -or
        ![double]::IsFinite($number)) { throw 'O CSV contém um valor numérico inválido.' }
    $number
}

[Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
$path = (Resolve-Path -LiteralPath $HwMonitorCsv).ProviderPath
$parser = [Microsoft.VisualBasic.FileIO.TextFieldParser]::new($path, [Text.Encoding]::GetEncoding(1252), $true)
try {
    $parser.SetDelimiters(',')
    $groups = $parser.ReadFields()
    $labels = $parser.ReadFields()
    if (!$groups -or !$labels -or $labels[0] -cne 'Time [s]') { throw 'Formato de CSV do HWMonitor inválido.' }

    $group = ''
    $columns = @(for ($i = 1; $i -lt $labels.Count; $i++) {
        if ($i -lt $groups.Count -and $groups[$i]) { $group = $groups[$i] }
        if ($group -ceq $CpuGroup -and $labels[$i] -ceq 'Package Temperature [°C]') { $i }
    })
    if ($columns.Count -ne 1) { throw 'O grupo informado precisa conter uma única coluna de temperatura do pacote.' }

    $previous = -1.0
    $samples = @(while (!$parser.EndOfData) {
        $fields = $parser.ReadFields()
        if ($fields.Count -ne $labels.Count) { throw 'Linha do CSV com número de colunas diferente do cabeçalho.' }
        $elapsed = Read-Number $fields[0]
        $celsius = Read-Number $fields[$columns[0]]
        if ($elapsed -lt 0 -or $elapsed -le $previous -or $celsius -le -273.15) {
            throw 'A sequência de tempos ou a temperatura do pacote é inválida.'
        }
        $previous = $elapsed
        [pscustomobject]@{ Elapsed = $elapsed; Celsius = $celsius }
    })
    if (!$samples.Count) { throw 'O CSV não contém amostras.' }
}
finally { $parser.Dispose() }

$samples | ForEach-Object {
    [pscustomobject]@{
        Timestamp = $lastTime.ToUniversalTime().AddSeconds($_.Elapsed - $previous).ToString('o')
        Celsius = $_.Celsius.ToString('R', $culture)
    }
} | ConvertTo-Csv -NoTypeInformation
