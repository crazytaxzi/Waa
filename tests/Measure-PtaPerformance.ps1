[CmdletBinding()]
param(
    [int]$Rows = 500,
    [string]$SqlitePath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($SqlitePath) { $env:WAA_SQLITE_TEST = $SqlitePath }

Import-Module (Join-Path $root 'src/Waa.psm1') -Force

$dataRoot = Join-Path ([IO.Path]::GetTempPath()) ('waa-pta-perf-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($dataRoot) | Out-Null

try {
    Initialize-Waa $root $dataRoot | Out-Null

    $builder = [Text.StringBuilder]::new($Rows * 110)
    [void]$builder.AppendLine("Truck`tDivision`tDriver Code`tPTA`tOperational Status`tPlanning Status`tOperational Note`tDriver Type`tLocation`tN1`tN2")

    for ($i = 1; $i -le $Rows; $i++) {
        $truck = 200000 + $i
        $code = ('P{0:D7}' -f $i)
        $minute = $i % 60
        $pta = '08/11/26 15:{0:D2}' -f $minute
        [void]$builder.AppendLine("$truck`t005`t$code`t$pta`tLoaded`tNo Preplan`t`tSolo`tDAL`t$($i * 10)`t0")
    }

    $raw = $builder.ToString()
    $preview = Get-ImportPreview $raw 'PTA benchmark' 'pta'
    if ($preview.errors.Count -gt 0) { throw ($preview.errors -join '; ') }
    if ($preview.valid_rows -ne $Rows) { throw "Expected $Rows rows, parser returned $($preview.valid_rows)." }

    $result = Import-WaaData $raw 'PTA benchmark' 'pta'
    $stored = [int]((Invoke-Sql 'SELECT count(*) FROM pta_observations;').Trim())
    if ($stored -ne $Rows) { throw "Expected $Rows stored PTA observations, found $stored." }

    Write-Host ''
    Write-Host 'WAA PTA CORE PIPELINE PERFORMANCE' -ForegroundColor Cyan
    Write-Host ('Rows:       {0}' -f $Rows)
    Write-Host ('Parse:      {0} ms' -f $result.parse_ms)
    Write-Host ('Database:   {0} ms' -f $result.db_ms)
    Write-Host ('Total:      {0} ms' -f $result.total_ms)
    if ($result.total_ms -gt 5000) {
        Write-Warning 'PTA import exceeded 5 seconds. Check endpoint protection/disk pressure on this workstation.'
    }
    else {
        Write-Host 'Result:     responsive' -ForegroundColor Green
    }
}
finally {
    Remove-Module Waa -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $dataRoot) { Remove-Item $dataRoot -Recurse -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:WAA_SQLITE_TEST -ErrorAction SilentlyContinue
}
