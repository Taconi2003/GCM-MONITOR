param(
    [Parameter(Mandatory)][string]$HwInfoCsv,
    [Parameter(Mandatory)][string]$CpuGroup,
    [Parameter(Mandatory)][string]$TimeZoneId,
    [string]$TemperatureColumn = 'CPU Package [°C]',
    [string]$DateTimeFormat = 'd.M.yyyy H:m:s.fff'
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$zone = [TimeZoneInfo]::FindSystemTimeZoneById($TimeZoneId)
$path = (Resolve-Path -LiteralPath $HwInfoCsv).ProviderPath
$parser = [Microsoft.VisualBasic.FileIO.TextFieldParser]::new($path)
try {
    $parser.SetDelimiters(',')
    $labels = $parser.ReadFields()
    if (!$labels -or $labels[0] -cne 'Date' -or $labels[1] -cne 'Time') {
        throw 'Use o registro de sensores do HWiNFO com as colunas Date e Time.'
    }
    $rows = @(while (!$parser.EndOfData) {
        $fields = $parser.ReadFields()
        if ($fields.Count -ne $labels.Count) { throw 'Linha do CSV com quantidade incorreta de colunas.' }
        if ($fields[0] -ceq 'Date' -and $fields[1] -ceq 'Time') {
            for ($i = 2; $i -lt $labels.Count; $i++) {
                if ($fields[$i] -cne $labels[$i]) { throw 'O cabeçalho repetido não corresponde à ordem das colunas.' }
            }
            break
        }
        [pscustomobject]@{ Fields = $fields }
    })
    if (!$rows.Count -or $parser.EndOfData) { throw 'Finalize a gravação para obter as amostras e o rodapé de grupos.' }
    $groups = $parser.ReadFields()
    if ($groups.Count -ne $labels.Count -or !$parser.EndOfData) { throw 'Rodapé de grupos inválido.' }
}
finally { $parser.Dispose() }

$columns = @(for ($i = 2; $i -lt $labels.Count; $i++) {
    if ($labels[$i] -ceq $TemperatureColumn -and $groups[$i] -ceq $CpuGroup) { $i }
})
if ($columns.Count -ne 1) { throw 'O grupo informado precisa conter uma única coluna de temperatura do pacote.' }

$previous = [DateTimeOffset]::MinValue
$samples = @(foreach ($row in $rows) {
    $fields = $row.Fields
    $local = [DateTime]::ParseExact(($fields[0] + ' ' + $fields[1]), $DateTimeFormat, $culture)
    if ($local.Kind -ne [DateTimeKind]::Unspecified -or $zone.IsAmbiguousTime($local) -or $zone.IsInvalidTime($local)) {
        throw 'O horário local não pode ser associado a um único instante no fuso informado.'
    }
    $utc = [TimeZoneInfo]::ConvertTimeToUtc($local, $zone)
    $timestamp = [DateTimeOffset]$utc
    if ($timestamp -le $previous) { throw 'Os horários do HWiNFO precisam ser crescentes.' }
    $previous = $timestamp
    $celsius = 0.0
    if (![double]::TryParse($fields[$columns[0]], [Globalization.NumberStyles]::Float, $culture, [ref]$celsius) -or
        ![double]::IsFinite($celsius) -or $celsius -le -273.15) {
        throw 'Temperatura inválida. Use a coluna em graus Celsius com ponto decimal.'
    }
    [pscustomobject]@{ Timestamp = $timestamp.ToString('o'); Celsius = $celsius.ToString('R', $culture) }
})
$samples | ConvertTo-Csv -NoTypeInformation
