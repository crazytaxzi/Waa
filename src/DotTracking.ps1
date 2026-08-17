Set-StrictMode -Version Latest

$script:WaaDotOriginZip = '83501'

function Initialize-WaaDotTracking {
    $schema = @'
CREATE TABLE IF NOT EXISTS dot_snapshots(
  id INTEGER PRIMARY KEY,
  import_batch_id INTEGER NOT NULL REFERENCES import_batches(id),
  trailer TEXT NOT NULL,
  status TEXT,
  description TEXT,
  last_dot_date TEXT,
  responsible_csr TEXT,
  responsible_csr_supervisor TEXT,
  t2_date TEXT,
  customer TEXT,
  customer_key TEXT,
  kma TEXT,
  source_days_since_last_dot REAL,
  UNIQUE(import_batch_id,trailer)
);
CREATE INDEX IF NOT EXISTS idx_dot_batch_age ON dot_snapshots(import_batch_id,last_dot_date,trailer);
CREATE INDEX IF NOT EXISTS idx_dot_customer ON dot_snapshots(customer_key);
CREATE TABLE IF NOT EXISTS dot_preferences(
  trailer TEXT PRIMARY KEY,
  hidden INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);CREATE TABLE IF NOT EXISTS dot_location_map(
  customer_key TEXT PRIMARY KEY,
  customer TEXT,
  location_label TEXT,
  miles_from_83501 REAL,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
INSERT OR IGNORE INTO settings(key,value) VALUES('dot_origin_zip','83501');
'@
    Invoke-Sql $schema -AllowWrite | Out-Null
}

function Add-WaaDotAudit {
    param([string]$Action,[string]$Entity,[string]$Id,$Detail)
    $values = @($Action,$Entity,$Id,($Detail | ConvertTo-Json -Compress -Depth 6)) | ForEach-Object { ConvertTo-WaaSqlLiteral $_ }
    Invoke-Sql "INSERT INTO audit_history(action,entity_type,entity_id,detail_json) VALUES($($values -join ','));" -AllowWrite | Out-Null
}

function Normalize-WaaDotHeader {
    param([AllowNull()][string]$Value)
    return [regex]::Replace(([string]$Value).Trim().ToLowerInvariant(),'[^a-z0-9]','')
}

function Get-WaaDotProperty {
    param($Row,[string[]]$Names)
    foreach ($property in $Row.PSObject.Properties) {
        $header = Normalize-WaaDotHeader $property.Name
        foreach ($name in $Names) {
            if ($header -eq (Normalize-WaaDotHeader $name)) { return [string]$property.Value }
        }
    }
    return ''
}

function Normalize-WaaDotCustomerKey {
    param([AllowNull()][string]$Customer)
    $text = [regex]::Replace(([string]$Customer).Trim(),'\s+',' ')
    if ($text -match '^([A-Za-z0-9]+)\s*-') { return ('CODE:' + $Matches[1].ToUpperInvariant()) }
    return ('NAME:' + $text.ToUpperInvariant())
}
function Convert-WaaDotDate {
    param([AllowNull()][string]$Value)
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $date = [datetime]::MinValue
    $styles = [Globalization.DateTimeStyles]::AllowWhiteSpaces
    if ([datetime]::TryParse($text,[Globalization.CultureInfo]::InvariantCulture,$styles,[ref]$date)) {
        return $date.ToString('yyyy-MM-dd')
    }
    if ([datetime]::TryParse($text,[Globalization.CultureInfo]::CurrentCulture,$styles,[ref]$date)) {
        return $date.ToString('yyyy-MM-dd')
    }
    return $null
}

function ConvertFrom-WaaDotText {
    param([Parameter(Mandatory=$true)][string]$Raw)
    $warnings = [Collections.Generic.List[string]]::new()
    $errors = [Collections.Generic.List[string]]::new()
    $records = [Collections.Generic.List[object]]::new()
    $clean = $Raw.TrimStart([char]0xfeff)
    if ([string]::IsNullOrWhiteSpace($clean)) {
        [void]$errors.Add('DOT report is empty.')
        return @{ rows=@(); warnings=@($warnings); errors=@($errors) }
    }
    $first = ($clean -split "`r?`n",2)[0]
    $tabCount = @($first.ToCharArray() | Where-Object { $_ -eq "`t" }).Count
    $commaCount = @($first.ToCharArray() | Where-Object { $_ -eq ',' }).Count
    $delimiter = if ($tabCount -gt $commaCount) { "`t" } else { ',' }
    try { $source = @($clean | ConvertFrom-Csv -Delimiter $delimiter) }
    catch { [void]$errors.Add('DOT CSV could not be parsed: ' + $_.Exception.Message); return @{ rows=@(); warnings=@($warnings); errors=@($errors) } }
    if ($source.Count -eq 0) { [void]$errors.Add('DOT report contains no data rows.'); return @{ rows=@(); warnings=@($warnings); errors=@($errors) } }
    $groups = [ordered]@{}
    foreach ($row in $source) {
        $trailer = (Get-WaaDotProperty $row @('Trailer','Trailer Number','Unit')).Trim()
        if ([string]::IsNullOrWhiteSpace($trailer)) { continue }
        if (-not $groups.Contains($trailer)) { $groups[$trailer] = [Collections.Generic.List[object]]::new() }
        [void]$groups[$trailer].Add($row)
    }
    foreach ($trailer in $groups.Keys) {
        $rows = @($groups[$trailer])
        $base = $rows[0]
        $lastRaw = Get-WaaDotProperty $base @('Last DOT Date','Last DOT','DOT Date')
        $lastDot = Convert-WaaDotDate $lastRaw
        if ($null -eq $lastDot) {
            [void]$warnings.Add("Trailer $trailer has no valid Last DOT Date and was kept with unknown age.")
        }
        $customer = Get-WaaDotProperty $base @('Customer')
        $sourceDays = $null
        foreach ($measureRow in $rows) {
            $measure = Normalize-WaaDotHeader (Get-WaaDotProperty $measureRow @('Measure Names','Measure Name'))
            if ($measure -eq 'dayssincelastdot') {
                $number = 0.0
                $value = Get-WaaDotProperty $measureRow @('Measure Values','Measure Value')
                if ([double]::TryParse($value,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number)) { $sourceDays = $number }
                break
            }
        }
        [void]$records.Add([ordered]@{
            trailer=$trailer
            status=Get-WaaDotProperty $base @('Statuses','Status')
            description=Get-WaaDotProperty $base @('Description')
            last_dot_date=$lastDot
            responsible_csr=Get-WaaDotProperty $base @('Responsible CSR','CSR')
            responsible_csr_supervisor=Get-WaaDotProperty $base @('Responsible CSR Supervisor','CSR Supervisor')
            t2_date=Convert-WaaDotDate (Get-WaaDotProperty $base @('T2 Date'))
            customer=$customer
            customer_key=Normalize-WaaDotCustomerKey $customer
            kma=Get-WaaDotProperty $base @('KMA')
            source_days_since_last_dot=$sourceDays
        })
    }
    if ($records.Count -eq 0) { [void]$errors.Add('No trailer rows were detected in the DOT report.') }
    return @{ rows=@($records); warnings=@($warnings); errors=@($errors); source_rows=$source.Count }
}
function Import-WaaDotReport {
    param([Parameter(Mandatory=$true)][string]$Raw,[string]$Filename='DOT report')
    Initialize-WaaDotTracking
    $hash = Get-WaaTextSha256 $Raw
    $hashSql = ConvertTo-WaaSqlLiteral $hash
    $existing = @(Invoke-Sql "SELECT id,import_type FROM import_batches WHERE source_hash=$hashSql LIMIT 1;" -Json)
    if ($existing.Count -gt 0) {
        if ([string]$existing[0].import_type -ne 'dot') { throw 'This file hash already belongs to another import type.' }
        return @{ status='Current'; imported=$false; import_batch_id=[int]$existing[0].id; detail='DOT report is already imported.' }
    }
    $parsed = ConvertFrom-WaaDotText $Raw
    if ($parsed.errors.Count -gt 0) { throw ($parsed.errors -join '; ') }
    $filenameSql = ConvertTo-WaaSqlLiteral $Filename
    $rawSql = ConvertTo-WaaSqlLiteral $Raw
    $summary = @{ source_rows=$parsed.source_rows; trailers=$parsed.rows.Count; origin_zip=$script:WaaDotOriginZip } | ConvertTo-Json -Compress
    $summarySql = ConvertTo-WaaSqlLiteral $summary
    $sql = [Text.StringBuilder]::new(24000)
    [void]$sql.AppendLine('BEGIN IMMEDIATE;')
    [void]$sql.AppendLine("INSERT INTO import_batches(source_hash,import_type,parser_version,filename,source_type,raw_source,row_count,warning_count,error_count,summary_json) VALUES($hashSql,'dot','1.0.0',$filenameSql,'automatic-download',$rawSql,$($parsed.rows.Count),$($parsed.warnings.Count),0,$summarySql);")
    foreach ($row in $parsed.rows) {
        $v = @($row.trailer,$row.status,$row.description,$row.last_dot_date,$row.responsible_csr,$row.responsible_csr_supervisor,$row.t2_date,$row.customer,$row.customer_key,$row.kma,$row.source_days_since_last_dot) | ForEach-Object { ConvertTo-WaaSqlLiteral $_ }
        [void]$sql.AppendLine("INSERT INTO dot_snapshots(import_batch_id,trailer,status,description,last_dot_date,responsible_csr,responsible_csr_supervisor,t2_date,customer,customer_key,kma,source_days_since_last_dot) VALUES((SELECT id FROM import_batches WHERE source_hash=$hashSql),$($v -join ','));")
    }
    [void]$sql.AppendLine('COMMIT;')
    try { Invoke-Sql $sql.ToString() -AllowWrite | Out-Null }
    catch { try { Invoke-Sql 'ROLLBACK;' -AllowWrite | Out-Null } catch { }; throw }
    $batchId = [int](Invoke-Sql "SELECT id FROM import_batches WHERE source_hash=$hashSql LIMIT 1;")
    return @{ status='Imported'; imported=$true; import_batch_id=$batchId; rows=$parsed.rows.Count; warnings=@($parsed.warnings); detail="$($parsed.rows.Count) DOT trailers imported." }
}
function Import-WaaDotFile {
    param([Parameter(Mandatory=$true)][string]$Path,[string]$Filename)
    if (-not (Test-Path -LiteralPath $Path)) { throw "DOT report not found: $Path" }
    $extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension -notin @('.csv','.txt')) { throw 'DOT automatic intake currently accepts CSV or text exports.' }
    $raw = Read-WaaTextFile $Path
    if ([string]::IsNullOrWhiteSpace($Filename)) { $Filename = [IO.Path]::GetFileName($Path) }
    return Import-WaaDotReport -Raw $raw -Filename $Filename
}

function Get-WaaDotTracking {
    Initialize-WaaDotTracking
    $batch = @(Invoke-Sql "SELECT id,filename,imported_at FROM import_batches WHERE import_type='dot' ORDER BY id DESC LIMIT 1;" -Json)
    if ($batch.Count -eq 0) { return @{ origin_zip=$script:WaaDotOriginZip; rows=@(); hidden_count=0; unresolved_locations=0; imported_at=$null; filename=$null } }
    $id = [int]$batch[0].id
    $rows = @(Invoke-Sql @"
SELECT d.trailer,d.status,d.description,d.last_dot_date,d.responsible_csr,d.responsible_csr_supervisor,d.t2_date,
       d.customer,d.customer_key,d.kma,d.source_days_since_last_dot,
       COALESCE(p.hidden,0) hidden,l.location_label,l.miles_from_83501,
       CASE WHEN d.last_dot_date IS NULL THEN NULL ELSE date(d.last_dot_date,'+365 days') END due_date,
       CASE WHEN d.last_dot_date IS NULL THEN NULL ELSE CAST(julianday(date('now','localtime'))-julianday(d.last_dot_date) AS INTEGER) END age_days,
       CASE WHEN d.last_dot_date IS NULL THEN NULL ELSE CAST(julianday(date('now','localtime'))-julianday(d.last_dot_date)-365 AS INTEGER) END days_overdue
FROM dot_snapshots d
LEFT JOIN dot_preferences p ON p.trailer=d.trailer
LEFT JOIN dot_location_map l ON l.customer_key=d.customer_key
WHERE d.import_batch_id=$id
ORDER BY CASE WHEN d.last_dot_date IS NULL THEN 1 ELSE 0 END,d.last_dot_date ASC,d.trailer ASC;
"@ -Json)
    $hidden = @($rows | Where-Object { [int]$_.hidden -eq 1 }).Count
    $unresolved = @($rows | Where-Object { $null -eq $_.miles_from_83501 }).Count
    return @{ origin_zip=$script:WaaDotOriginZip; rows=$rows; hidden_count=$hidden; unresolved_locations=$unresolved; imported_at=$batch[0].imported_at; filename=$batch[0].filename }
}
function Set-WaaDotHidden {
    param([Parameter(Mandatory=$true)][string]$Trailer,[bool]$Hidden)
    Initialize-WaaDotTracking
    $trailer = $Trailer.Trim()
    if ([string]::IsNullOrWhiteSpace($trailer)) { throw 'Trailer is required.' }
    $trailerSql = ConvertTo-WaaSqlLiteral $trailer
    $hiddenValue = if ($Hidden) { 1 } else { 0 }
    Invoke-Sql "INSERT INTO dot_preferences(trailer,hidden,updated_at) VALUES($trailerSql,$hiddenValue,CURRENT_TIMESTAMP) ON CONFLICT(trailer) DO UPDATE SET hidden=excluded.hidden,updated_at=CURRENT_TIMESTAMP;" -AllowWrite | Out-Null
    Add-WaaDotAudit 'dot_visibility' 'trailer' $trailer @{ hidden=$Hidden }
    return @{ ok=$true; trailer=$trailer; hidden=$Hidden }
}

function Set-WaaDotLocation {
    param([Parameter(Mandatory=$true)][string]$CustomerKey,[string]$Customer,[string]$LocationLabel,$MilesFrom83501)
    Initialize-WaaDotTracking
    $key = $CustomerKey.Trim()
    if ([string]::IsNullOrWhiteSpace($key)) { throw 'Customer key is required.' }
    $miles = 0.0
    if (-not [double]::TryParse(([string]$MilesFrom83501),[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$miles)) { throw 'Distance must be a number of miles.' }
    if ($miles -lt 0 -or $miles -gt 5000) { throw 'Distance must be between 0 and 5000 miles.' }
    $values = @($key,$Customer,$LocationLabel,$miles) | ForEach-Object { ConvertTo-WaaSqlLiteral $_ }
    Invoke-Sql "INSERT INTO dot_location_map(customer_key,customer,location_label,miles_from_83501,updated_at) VALUES($($values[0]),$($values[1]),$($values[2]),$($values[3]),CURRENT_TIMESTAMP) ON CONFLICT(customer_key) DO UPDATE SET customer=excluded.customer,location_label=excluded.location_label,miles_from_83501=excluded.miles_from_83501,updated_at=CURRENT_TIMESTAMP;" -AllowWrite | Out-Null
    Add-WaaDotAudit 'dot_location' 'dot_customer' $key @{ customer=$Customer; location=$LocationLabel; miles_from_83501=$miles }
    return @{ ok=$true; customer_key=$key; location_label=$LocationLabel; miles_from_83501=$miles; origin_zip=$script:WaaDotOriginZip }
}
