Set-StrictMode -Version Latest

# PTA is intentionally a dedicated paste-only intake path. Keep parsing in memory,
# stage the complete snapshot once, and let SQLite perform identity resolution and
# history writes set-wise inside a single transaction.

function ConvertTo-WaaPtaSqlLiteral {
    param([AllowNull()]$Value)

    if ($null -eq $Value) { return 'NULL' }
    if ($Value -is [bool]) { return $(if ($Value) { '1' } else { '0' }) }
    if ($Value -is [byte] -or
        $Value -is [int16] -or
        $Value -is [int] -or
        $Value -is [long] -or
        $Value -is [single] -or
        $Value -is [double] -or
        $Value -is [decimal]) {
        return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
    }

    return "'" + ([string]$Value).Replace("'", "''").Replace([string][char]0, '') + "'"
}

function Get-WaaPtaSha256 {
    param([Parameter(Mandatory = $true)][string]$Text)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Convert-WaaPtaDate {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $date = [datetime]::MinValue
    $styles = [Globalization.DateTimeStyles]::AssumeLocal
    if ([datetime]::TryParse($Text, [Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$date)) {
        return $date.ToString('s')
    }
    return $null
}

function Test-WaaPtaSeparatorRow {
    param([Parameter(Mandatory = $true)][string[]]$Cells)

    if ($Cells.Count -eq 0) { return $false }
    foreach ($cell in $Cells) {
        if (([string]$cell).Trim() -notmatch '^:?-{2,}:?$') { return $false }
    }
    return $true
}

function Test-WaaPtaHeaderRow {
    param([Parameter(Mandatory = $true)][string[]]$Cells)

    if ($Cells.Count -eq 0) { return $false }
    $first = [regex]::Replace(([string]$Cells[0]).Trim().ToLowerInvariant(), '[^a-z0-9]', '')
    return $first -in @('truck', 'trucknumber', 'truckno', 'unit', 'unitnumber')
}

function ConvertFrom-WaaPtaText {
    param([Parameter(Mandatory = $true)][string]$Raw)

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $validRows = [Collections.Generic.List[object]]::new()
    $warnings = [Collections.Generic.List[string]]::new()
    $sample = [Collections.Generic.List[object]]::new()
    $reader = [IO.StringReader]::new($Raw)
    $sourceRows = 0

    try {
        while ($true) {
            $line = $reader.ReadLine()
            if ($null -eq $line) { break }
            if ([string]::IsNullOrWhiteSpace($line)) { continue }

            $sourceRows++
            $trimmed = $line.Trim()
            [string[]]$cells = @()

            if ($line.IndexOf("`t", [StringComparison]::Ordinal) -ge 0) {
                $cells = $line.Split([char[]]@([char]9), [StringSplitOptions]::None)
                for ($i = 0; $i -lt $cells.Length; $i++) {
                    $cells[$i] = $cells[$i].Trim()
                }
            }
            elseif ($trimmed.StartsWith('|')) {
                $body = $trimmed.Trim('|')
                $parts = [regex]::Split($body, '(?<!\\)\|')
                $cells = [string[]]::new($parts.Length)
                for ($i = 0; $i -lt $parts.Length; $i++) {
                    $cells[$i] = ([string]$parts[$i]).Trim().Replace('\_', '_').Replace('\|', '|')
                }
            }
            else {
                $parts = [regex]::Split($line, ',(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)')
                $cells = [string[]]::new($parts.Length)
                for ($i = 0; $i -lt $parts.Length; $i++) {
                    $cells[$i] = ([string]$parts[$i]).Trim(' ', '"')
                }
            }

            if (Test-WaaPtaSeparatorRow -Cells $cells) { continue }
            if (Test-WaaPtaHeaderRow -Cells $cells) { continue }

            if ($cells.Count -lt 11) {
                [void]$warnings.Add("Row $sourceRows has $($cells.Count) columns; expected 11.")
                continue
            }

            # PTA has exactly 11 authoritative fields. Ignore any accidental trailing
            # empty export columns rather than making every downstream operation pay for them.
            $row = [string[]]::new(11)
            for ($i = 0; $i -lt 11; $i++) {
                $row[$i] = ([string]$cells[$i]).Trim()
            }

            [void]$validRows.Add($row)
            if ($sample.Count -lt 8) { [void]$sample.Add($row) }
        }
    }
    finally {
        $reader.Dispose()
        $watch.Stop()
    }

    $errors = [Collections.Generic.List[string]]::new()
    if ($validRows.Count -eq 0) { [void]$errors.Add('No valid PTA data rows were detected.') }

    return @{
        rows = $validRows
        sample = $sample
        warnings = $warnings
        errors = $errors
        total_rows = $sourceRows
        valid_rows = $validRows.Count
        hash = Get-WaaPtaSha256 -Text $Raw
        parse_ms = [math]::Round($watch.Elapsed.TotalMilliseconds, 1)
    }
}

function Get-WaaPtaPreview {
    param(
        [Parameter(Mandatory = $true)][string]$Raw,
        [string]$Filename = 'PTA paste'
    )

    $parsed = ConvertFrom-WaaPtaText -Raw $Raw
    return @{
        type = 'pta'
        parser_version = '2.0.0-bulk'
        total_rows = $parsed.total_rows
        valid_rows = $parsed.valid_rows
        warnings = @($parsed.warnings)
        errors = @($parsed.errors)
        sample = @($parsed.sample)
        hash = $parsed.hash
        filename = $Filename
        parse_ms = $parsed.parse_ms
    }
}

function Import-WaaPtaData {
    param(
        [Parameter(Mandatory = $true)][string]$Raw,
        [string]$Filename = 'PTA paste'
    )

    $totalWatch = [Diagnostics.Stopwatch]::StartNew()
    $parsed = ConvertFrom-WaaPtaText -Raw $Raw
    if ($parsed.errors.Count -gt 0) { throw ($parsed.errors -join '; ') }

    $hashSql = ConvertTo-WaaPtaSqlLiteral $parsed.hash
    $existing = @(Invoke-Sql "SELECT id FROM import_batches WHERE source_hash=$hashSql LIMIT 1;" -Json)
    if ($existing.Count -gt 0) {
        throw 'Duplicate import: this exact source was already committed.'
    }

    $previewSummary = @{
        type = 'pta'
        parser_version = '2.0.0-bulk'
        total_rows = $parsed.total_rows
        valid_rows = $parsed.valid_rows
        warnings = @($parsed.warnings)
        errors = @()
        hash = $parsed.hash
        filename = $Filename
        parse_ms = $parsed.parse_ms
    }

    $summarySql = ConvertTo-WaaPtaSqlLiteral ($previewSummary | ConvertTo-Json -Compress -Depth 6)
    $rawSql = ConvertTo-WaaPtaSqlLiteral $Raw
    $filenameSql = ConvertTo-WaaPtaSqlLiteral $Filename
    $warningCount = $parsed.warnings.Count

    $sql = [Text.StringBuilder]::new([Math]::Max(32768, $parsed.valid_rows * 420))
    [void]$sql.AppendLine('BEGIN IMMEDIATE;')
    [void]$sql.AppendLine(
        "INSERT INTO import_batches(source_hash,import_type,parser_version,filename,source_type,raw_source,row_count,warning_count,error_count,summary_json) " +
        "VALUES($hashSql,'pta','2.0.0-bulk',$filenameSql,'user',$rawSql,$($parsed.valid_rows),$warningCount,0,$summarySql);"
    )

    [void]$sql.AppendLine(@'
CREATE TEMP TABLE temp_waa_pta_stage(
  row_no INTEGER NOT NULL,
  truck TEXT,
  division TEXT,
  driver_code TEXT,
  pta_raw TEXT,
  pta_at TEXT,
  sentinel INTEGER NOT NULL,
  operational_status TEXT,
  planning_status TEXT,
  operational_note TEXT,
  driver_type TEXT,
  location TEXT,
  source_numeric_1 TEXT,
  source_numeric_2 TEXT
);
'@)

    [void]$sql.Append('INSERT INTO temp_waa_pta_stage VALUES ')
    $firstTuple = $true
    $rowNumber = 0
    foreach ($rowObject in $parsed.rows) {
        $rowNumber++
        [string[]]$row = $rowObject
        $rawPta = $row[3]
        $ptaAt = Convert-WaaPtaDate -Text $rawPta
        $driverCode = $row[2]
        $status = $row[4]
        $sentinel = if ([string]::IsNullOrWhiteSpace($driverCode) -and
            $rawPta -match '^12/31/26\s+23:59$' -and
            $status -match '^(Shop|TruckPrep|Reserved|ClaimsHold|Clean_QA|GoodToGo)$') { 1 } else { 0 }

        $values = @(
            $rowNumber, $row[0], $row[1], $driverCode, $rawPta, $ptaAt, $sentinel,
            $status, $row[5], $row[6], $row[7], $row[8], $row[9], $row[10]
        ) | ForEach-Object { ConvertTo-WaaPtaSqlLiteral $_ }

        if (-not $firstTuple) { [void]$sql.Append(',') }
        $firstTuple = $false
        [void]$sql.Append('(' + ($values -join ',') + ')')
    }
    [void]$sql.AppendLine(';')

    # Reuse an existing unique identity when the same alias value already points to
    # exactly one driver, matching the historical WAA identity behavior without a
    # process-per-row lookup.
    [void]$sql.AppendLine(@'
INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed)
SELECT existing.driver_id,'pta_code',codes.driver_code,0
FROM (SELECT DISTINCT driver_code FROM temp_waa_pta_stage WHERE trim(driver_code)<>'') codes
JOIN (
  SELECT alias_value,MIN(driver_id) driver_id
  FROM driver_aliases
  GROUP BY alias_value COLLATE NOCASE
  HAVING COUNT(DISTINCT driver_id)=1
) existing ON existing.alias_value=codes.driver_code COLLATE NOCASE
WHERE NOT EXISTS(
  SELECT 1 FROM driver_aliases p
  WHERE p.alias_type='pta_code' AND p.alias_value=codes.driver_code COLLATE NOCASE
)
ON CONFLICT(alias_type,alias_value) DO NOTHING;

INSERT INTO drivers(full_name,pta_code)
SELECT codes.driver_code,codes.driver_code
FROM (SELECT DISTINCT driver_code FROM temp_waa_pta_stage WHERE trim(driver_code)<>'') codes
WHERE NOT EXISTS(
  SELECT 1 FROM driver_aliases p
  WHERE p.alias_type='pta_code' AND p.alias_value=codes.driver_code COLLATE NOCASE
)
AND NOT EXISTS(
  SELECT 1 FROM driver_aliases a
  WHERE a.alias_value=codes.driver_code COLLATE NOCASE
)
AND NOT EXISTS(
  SELECT 1 FROM drivers d
  WHERE d.pta_code=codes.driver_code COLLATE NOCASE
);

INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed)
SELECT d.id,'pta_code',codes.driver_code,0
FROM (SELECT DISTINCT driver_code FROM temp_waa_pta_stage WHERE trim(driver_code)<>'') codes
JOIN drivers d ON d.pta_code=codes.driver_code COLLATE NOCASE
WHERE NOT EXISTS(
  SELECT 1 FROM driver_aliases p
  WHERE p.alias_type='pta_code' AND p.alias_value=codes.driver_code COLLATE NOCASE
)
ON CONFLICT(alias_type,alias_value) DO NOTHING;
'@)

    [void]$sql.AppendLine(
        "INSERT INTO identity_issues(import_batch_id,alias_type,alias_value,issue_type,detail) " +
        "SELECT (SELECT id FROM import_batches WHERE source_hash=$hashSql),'pta_code',codes.driver_code,'unmatched','PTA row not linked' " +
        "FROM (SELECT DISTINCT driver_code FROM temp_waa_pta_stage WHERE trim(driver_code)<>'') codes " +
        "WHERE NOT EXISTS(SELECT 1 FROM driver_aliases p WHERE p.alias_type='pta_code' AND p.alias_value=codes.driver_code COLLATE NOCASE);"
    )

    [void]$sql.AppendLine(
        "INSERT INTO pta_observations(" +
        "driver_id,truck,division,driver_code,pta_raw,pta_at,actionable,operational_status,planning_status,operational_note,driver_type,location,source_numeric_1,source_numeric_2,source,import_batch_id) " +
        "SELECT " +
        "(SELECT p.driver_id FROM driver_aliases p WHERE p.alias_type='pta_code' AND p.alias_value=s.driver_code COLLATE NOCASE LIMIT 1)," +
        "s.truck,s.division,s.driver_code,s.pta_raw,s.pta_at," +
        "CASE WHEN s.sentinel=1 THEN 0 WHEN trim(coalesce(s.driver_code,''))='' THEN 0 " +
        "WHEN EXISTS(SELECT 1 FROM driver_aliases p WHERE p.alias_type='pta_code' AND p.alias_value=s.driver_code COLLATE NOCASE) THEN 1 ELSE 0 END," +
        "s.operational_status,s.planning_status,s.operational_note,s.driver_type,s.location,s.source_numeric_1,s.source_numeric_2,'import'," +
        "(SELECT id FROM import_batches WHERE source_hash=$hashSql) " +
        "FROM temp_waa_pta_stage s ORDER BY s.row_no;"
    )

    [void]$sql.AppendLine(
        "INSERT INTO truck_history(driver_id,truck,observed_at,import_batch_id,source) " +
        "SELECT p.driver_id,s.truck,CURRENT_TIMESTAMP,(SELECT id FROM import_batches WHERE source_hash=$hashSql),'pta' " +
        "FROM temp_waa_pta_stage s " +
        "JOIN driver_aliases p ON p.alias_type='pta_code' AND p.alias_value=s.driver_code COLLATE NOCASE " +
        "WHERE trim(coalesce(s.driver_code,''))<>'' ORDER BY s.row_no;"
    )

    $auditJsonSql = ConvertTo-WaaPtaSqlLiteral (@{
        type = 'pta'
        rows = $parsed.valid_rows
        parser = '2.0.0-bulk'
    } | ConvertTo-Json -Compress)
    [void]$sql.AppendLine(
        "INSERT INTO audit_history(action,entity_type,entity_id,detail_json) " +
        "VALUES('import_committed','import_batch',(SELECT id FROM import_batches WHERE source_hash=$hashSql),$auditJsonSql);"
    )
    [void]$sql.AppendLine('DROP TABLE temp_waa_pta_stage;')
    [void]$sql.AppendLine('COMMIT;')
    [void]$sql.AppendLine("SELECT id FROM import_batches WHERE source_hash=$hashSql;")

    $dbWatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $result = @(Invoke-Sql $sql.ToString() -Json -AllowWrite)
    }
    catch {
        if ($_.Exception.Message -match '(?i)(UNIQUE constraint failed: import_batches.source_hash)') {
            throw 'Duplicate import: this exact source was already committed.'
        }
        throw
    }
    finally {
        $dbWatch.Stop()
        $totalWatch.Stop()
    }

    if ($result.Count -eq 0) { throw 'PTA import committed but the import batch could not be verified.' }

    return @{
        id = [int]$result[0].id
        type = 'pta'
        rows = $parsed.valid_rows
        warnings = @($parsed.warnings)
        parser_version = '2.0.0-bulk'
        parse_ms = $parsed.parse_ms
        db_ms = [math]::Round($dbWatch.Elapsed.TotalMilliseconds, 1)
        total_ms = [math]::Round($totalWatch.Elapsed.TotalMilliseconds, 1)
    }
}
