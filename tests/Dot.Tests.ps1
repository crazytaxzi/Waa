$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$data = Join-Path $env:TEMP ('waa-dot-test-' + [Guid]::NewGuid().ToString('N'))
Import-Module (Join-Path $root 'src\Waa.psm1') -Force
. (Join-Path $root 'src\ReportIntake.ps1')
. (Join-Path $root 'src\DotTracking.ps1')

function Assert-Dot($Condition,[string]$Message) {
    if (-not $Condition) { throw "DOT TEST FAILED: $Message" }
}

try {
    Initialize-Waa $root $data -SkipStartupBackup | Out-Null
    Initialize-WaaReportIntake $data
    Initialize-WaaDotTracking
    $fixture = Join-Path $PSScriptRoot 'fixtures\DOT Table_data.csv'
    $raw = Read-WaaTextFile $fixture
    $parsed = ConvertFrom-WaaDotText $raw
    Assert-Dot ($parsed.errors.Count -eq 0) 'fixture should parse without errors'
    Assert-Dot ($parsed.rows.Count -eq 4) '8 Tableau measure rows must collapse to 4 trailers'
    $oldest = @($parsed.rows | Where-Object { $_.trailer -eq '001001' })[0]
    Assert-Dot ($oldest.last_dot_date -eq '2023-01-31') 'Last DOT date should normalize'
    Assert-Dot ($oldest.source_days_since_last_dot -eq -928) 'source measure should be preserved as evidence'
    $import = Import-WaaDotReport -Raw $raw -Filename 'DOT Table_data.csv'
    Assert-Dot $import.imported 'first DOT import should insert data'
    $current = Get-WaaDotTracking
    Assert-Dot ($current.rows.Count -eq 4) 'current DOT view should contain 4 trailers'
    Assert-Dot ($current.rows[0].trailer -eq '001001') 'default server order should be oldest Last DOT first'
    Assert-Dot ([string]$current.rows[0].due_date -eq '2024-01-31') 'DOT due date should be inspection date plus 365 days'
    $expectedOverdue = [int](([datetime]::Today - [datetime]'2023-01-31').TotalDays) - 365
    Assert-Dot ([int]$current.rows[0].days_overdue -eq $expectedOverdue) 'days overdue must equal days since inspection minus 365'

    Set-WaaDotHidden -Trailer '001001' -Hidden $true | Out-Null
    $current = Get-WaaDotTracking
    $hidden = @($current.rows | Where-Object { $_.trailer -eq '001001' })[0]
    Assert-Dot ([int]$hidden.hidden -eq 1) 'hide preference should persist separately from source evidence'

    Set-WaaDotLocation -CustomerKey 'CODE:20001' -Customer '20001 - SAMPLE SHARED CUSTOMER' -LocationLabel 'Sample customer site' -MilesFrom83501 '2.5' | Out-Null
    $current = Get-WaaDotTracking
    $mapped = @($current.rows | Where-Object { $_.customer_key -eq 'CODE:20001' })
    Assert-Dot ($mapped.Count -gt 1) 'customer mapping should apply to every trailer at that customer'
    Assert-Dot ([double]$mapped[0].miles_from_83501 -eq 2.5) 'stored distance should round-trip'

    $downloads = Join-Path $data 'Downloads'
    [IO.Directory]::CreateDirectory($downloads) | Out-Null
    $downloadDot = Join-Path $downloads 'DOT Table.csv'
    Copy-Item -LiteralPath $fixture -Destination $downloadDot
    Assert-Dot ((Get-WaaNewestDownload $downloads dot).Name -eq 'DOT Table.csv') 'Downloads watcher should identify DOT exports'
    $scan = Invoke-WaaDownloadsScan -DownloadsPath $downloads
    Assert-Dot ($scan.results.dot.status -eq 'Current') 'automatic DOT intake should recognize an already-imported matching report'

    $duplicate = Import-WaaDotReport -Raw $raw -Filename 'DOT Table_data.csv'
    Assert-Dot (-not $duplicate.imported) 'duplicate DOT import should be idempotent'
    Write-Host 'DOT tests passed.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction SilentlyContinue }
}
