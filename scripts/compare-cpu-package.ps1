param(
    [Parameter(Mandatory)][string]$ProbeCapture,
    [Parameter(Mandatory)][string]$ReferenceCsv,
    [ValidateRange(1,10000)][int]$MaximumOffsetMilliseconds = 1200
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture

function Read-Timestamp($Value) {
    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime() }
    if ($Value -is [DateTime] -and $Value.Kind -ne [DateTimeKind]::Unspecified) {
        return ([DateTimeOffset]$Value).ToUniversalTime()
    }
    $parsed = [DateTimeOffset]::MinValue
    if ($Value -isnot [string] -or $Value -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$' -or
        ![DateTimeOffset]::TryParse($Value, $culture, [Globalization.DateTimeStyles]::None, [ref]$parsed)) {
        throw 'Informe horários ISO 8601 com fuso explícito.'
    }
    $parsed.ToUniversalTime()
}

function Read-Celsius($Value) {
    $number = 0.0
    if (![double]::TryParse([string]::Format($culture, '{0}', $Value), [Globalization.NumberStyles]::Float, $culture, [ref]$number) -or
        ![double]::IsFinite($number) -or $number -le -273.15) {
        throw 'Temperatura inválida. Informe graus Celsius com ponto decimal.'
    }
    $number
}

$capture = Get-Content -LiteralPath $ProbeCapture -Raw | ConvertFrom-Json
if ($capture.SchemaVersion -ne 1 -or $capture.IsPrivate -ne $true -or $capture.StressExecuted -ne $false -or
    $capture.SafetyQualified -ne $false -or $null -ne $capture.ErrorType -or !$capture.Frames) {
    throw 'Use uma captura passiva válida gerada pela sonda do GCM.'
}
$packageId = $null
$previous = $null
$package = @(foreach ($frame in $capture.Frames) {
    $time = Read-Timestamp $frame.ReceivedAt
    if ($null -ne $previous -and $time -le $previous) { throw 'Os horários da sonda precisam ser crescentes.' }
    $previous = $time
    $sensors = @($frame.Sensors | Where-Object Role -eq 'Package')
    if ($sensors.Count -gt 1) { throw 'Há mais de um pacote de CPU no quadro. A comparação exige uma identidade única.' }
    if ($sensors.Count -eq 0) { continue }
    $sensor = $sensors[0]
    if (!$sensor.HardwareId -or !$sensor.SensorId -or $sensor.Unit -ne '°C') { throw 'Identidade ou unidade do pacote inválida.' }
    $identity = "$($sensor.HardwareId)|$($sensor.SensorId)"
    if ($null -ne $packageId -and $identity -cne $packageId) { throw 'A identidade do sensor mudou durante a captura.' }
    $packageId = $identity
    if ($null -ne $sensor.Value) { [pscustomobject]@{ Time = $time; Celsius = Read-Celsius $sensor.Value } }
})

$rows = @(Import-Csv -LiteralPath $ReferenceCsv)
if (!$rows.Count -or 'Timestamp' -notin $rows[0].PSObject.Properties.Name -or 'Celsius' -notin $rows[0].PSObject.Properties.Name) {
    throw 'Use um CSV com as colunas Timestamp e Celsius, restrito a um único sensor de pacote.'
}
$reference = @($rows | ForEach-Object {
    [pscustomobject]@{ Time = Read-Timestamp $_.Timestamp; Celsius = Read-Celsius $_.Celsius }
} | Sort-Object Time)
for ($i = 1; $i -lt $reference.Count; $i++) {
    if ($reference[$i].Time -eq $reference[$i-1].Time) { throw 'A referência contém horários duplicados.' }
}

$referenceFirst = $reference.Count -le $package.Count
$primary = $reference
$secondary = $package
if (!$referenceFirst) {
    $primary = $package
    $secondary = $reference
}
$index = 0
# A série menor dirige a busca para que amostras densas não antecipem o par mais próximo.
$pairs = @(foreach ($sample in $primary) {
    $start = $sample.Time.AddMilliseconds(-$MaximumOffsetMilliseconds)
    $end = $sample.Time.AddMilliseconds($MaximumOffsetMilliseconds)
    while ($index -lt $secondary.Count -and $secondary[$index].Time -lt $start) { $index++ }
    $bestIndex = -1
    $bestOffset = [double]::PositiveInfinity
    for ($j = $index; $j -lt $secondary.Count -and $secondary[$j].Time -le $end; $j++) {
        $offset = [math]::Abs(($secondary[$j].Time - $sample.Time).TotalMilliseconds)
        if ($offset -lt $bestOffset) {
            $bestOffset = $offset
            $bestIndex = $j
        }
    }
    if ($bestIndex -lt 0) { continue }
    $difference = $sample.Celsius - $secondary[$bestIndex].Celsius
    if ($referenceFirst) { $difference = -$difference }
    [pscustomobject]@{ OffsetMs = $bestOffset; Signed = $difference; Absolute = [math]::Abs($difference) }
    $index = $bestIndex + 1
})

$maximum = $null
$mean = $null
$signed = $null
$offset = $null
if ($pairs.Count) {
    $stats = $pairs.Absolute | Measure-Object -Maximum -Average
    $maximum = [math]::Round($stats.Maximum, 2)
    $mean = [math]::Round($stats.Average, 2)
    $signed = [math]::Round(($pairs.Signed | Measure-Object -Average).Average, 2)
    $offset = [math]::Round(($pairs.OffsetMs | Measure-Object -Maximum).Maximum, 1)
}

[pscustomobject]@{
    ProbeFrames = $capture.Frames.Count
    ValidPackageReadings = $package.Count
    ReferenceReadings = $reference.Count
    Pairs = $pairs.Count
    UnmatchedReferenceReadings = $reference.Count - $pairs.Count
    MaximumAllowedOffsetMs = $MaximumOffsetMilliseconds
    LargestObservedOffsetMs = $offset
    MeanSignedDifferenceC = $signed
    MeanAbsoluteDifferenceC = $mean
    MaximumAbsoluteDifferenceC = $maximum
    StressExecuted = $false
    SafetyQualified = $false
} | ConvertTo-Json
