Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Root = $null
$script:DataRoot = $null
$script:Db = $null
$script:Sqlite = $null
$script:ReadOnly = $false
$script:SqlProcess = $null
$script:SqlLock = New-Object object

function Stop-WaaSqlSession {
    if ($null -eq $script:SqlProcess) { return }
    try {
        if (-not $script:SqlProcess.HasExited) {
            $script:SqlProcess.StandardInput.WriteLine('.quit')
            $script:SqlProcess.StandardInput.Flush()
            if (-not $script:SqlProcess.WaitForExit(1000)) { $script:SqlProcess.Kill() }
        }
    }
    catch { }
    finally {
        try { $script:SqlProcess.Dispose() } catch { }
        $script:SqlProcess = $null
    }
}

function Start-WaaSqlSession {
    Stop-WaaSqlSession
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $script:Sqlite
    $escapedDb = $script:Db.Replace('"','\"')
    $start.Arguments = "-batch `"$escapedDb`""
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $false
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'Unable to start the bundled SQLite runtime.' }
    $process.StandardInput.AutoFlush = $true
    $process.StandardInput.WriteLine('.bail off')
    $process.StandardInput.WriteLine('.log stdout')
    $process.StandardInput.WriteLine('.timeout 5000')
    $process.StandardInput.WriteLine('PRAGMA foreign_keys=ON;')
    $marker = '__WAA_SQL_READY__'
    $process.StandardInput.WriteLine(".print $marker")
    while ($process.StandardOutput.ReadLine() -ne $marker) {
        if ($process.HasExited) { throw 'The bundled SQLite runtime stopped during startup.' }
    }
    $script:SqlProcess = $process
}

function ConvertTo-SqlLiteral {
    param([AllowNull()]$Value)
    if ($null -eq $Value) { return 'NULL' }
    if ($Value -is [bool]) { if ($Value) { return '1' } else { return '0' } }
    if ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int] -or $Value -is [long] -or
        $Value -is [single] -or $Value -is [double] -or $Value -is [decimal]) {
        return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
    }
    return "'" + ([string]$Value).Replace("'", "''").Replace([string][char]0, '') + "'"
}

function Invoke-Sql {
    param([Parameter(Mandatory = $true)][string]$Sql, [switch]$Json, [switch]$AllowWrite)
    if ($script:ReadOnly -and $AllowWrite) { throw 'Database integrity mode is read-only. Restore a valid backup.' }
    [Threading.Monitor]::Enter($script:SqlLock)
    try {
        if ($null -eq $script:SqlProcess -or $script:SqlProcess.HasExited) { Start-WaaSqlSession }
        $marker = '__WAA_SQL_END_' + [Guid]::NewGuid().ToString('N') + '__'
        $script:SqlProcess.StandardInput.WriteLine($(if ($Json) { '.mode json' } else { '.mode list' }))
        $script:SqlProcess.StandardInput.WriteLine('.headers off')
        $script:SqlProcess.StandardInput.WriteLine($Sql)
        $script:SqlProcess.StandardInput.WriteLine(".print $marker")
        $lines = New-Object 'Collections.Generic.List[string]'
        while ($true) {
            $line = $script:SqlProcess.StandardOutput.ReadLine()
            if ($null -eq $line) { throw 'The bundled SQLite runtime stopped unexpectedly.' }
            if ($line -eq $marker) { break }
            [void]$lines.Add($line)
        }
        $errors = @($lines | Where-Object { $_ -match '^(Parse error|Runtime error|Error:)' })
        if ($errors.Count) { throw "SQLite error: $($errors -join ' ')" }
        $output = @($lines | Where-Object { $_ -notmatch '^\(\d+\) ' })
    }
    finally { [Threading.Monitor]::Exit($script:SqlLock) }
    if (-not $Json) { return ($output -join "`n") }
    $text = ($output -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return @() }
    $parsed = ConvertFrom-Json -InputObject $text
    if ($parsed -is [System.Array]) {
        foreach ($item in $parsed) {
            if ($item -is [System.Array]) { foreach ($nested in $item) { Write-Output $nested } }
            else { Write-Output $item }
        }
    }
    else { Write-Output $parsed }
}

function Add-Audit {
    param([string]$Action, [string]$Entity, [AllowNull()]$Id, [AllowNull()]$Detail)
    $values = @($Action, $Entity, $Id, ($Detail | ConvertTo-Json -Compress -Depth 8)) | ForEach-Object { ConvertTo-SqlLiteral $_ }
    Invoke-Sql "INSERT INTO audit_history(action,entity_type,entity_id,detail_json) VALUES($($values -join ','));" -AllowWrite | Out-Null
}

function Backup-Waa {
    param([string]$Reason = 'manual')
    $directory = Join-Path $script:DataRoot 'backups'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss-fff')
    $destination = Join-Path $directory "waa-$stamp-$Reason.db"
    $safe = $destination.Replace("'", "''")
    Invoke-Sql ".backup '$safe'" | Out-Null
    if ($Reason -eq 'startup') {
        @(Get-ChildItem -LiteralPath $directory -Filter 'waa-*-startup.db' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -Skip 10) |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
    }
    return @{ path = $destination; name = [IO.Path]::GetFileName($destination) }
}

function Initialize-Waa {
    param([string]$Root, [string]$DataRoot, [switch]$SkipStartupBackup)
    $script:Root = $Root
    $script:DataRoot = $DataRoot
    [IO.Directory]::CreateDirectory($DataRoot) | Out-Null
    $script:Db = Join-Path $DataRoot 'waa.db'
    $script:Sqlite = if ($env:WAA_SQLITE_TEST) { $env:WAA_SQLITE_TEST } else { Join-Path $Root 'runtime/sqlite/sqlite3.exe' }
    if (-not (Test-Path -LiteralPath $script:Sqlite)) { throw "Bundled SQLite runtime missing: $script:Sqlite" }
    Start-WaaSqlSession
    if ((Test-Path -LiteralPath $script:Db) -and -not $SkipStartupBackup) { Backup-Waa 'startup' | Out-Null }

    $schema = @'
PRAGMA foreign_keys=ON;
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
BEGIN IMMEDIATE;
CREATE TABLE IF NOT EXISTS schema_version(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS drivers(id INTEGER PRIMARY KEY, full_name TEXT NOT NULL DEFAULT 'Unknown', pta_code TEXT, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, UNIQUE(pta_code));
CREATE TABLE IF NOT EXISTS driver_aliases(id INTEGER PRIMARY KEY, driver_id INTEGER NOT NULL REFERENCES drivers(id), alias_type TEXT NOT NULL, alias_value TEXT NOT NULL COLLATE NOCASE, confirmed INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, UNIQUE(alias_type,alias_value));
CREATE TABLE IF NOT EXISTS import_batches(id INTEGER PRIMARY KEY, source_hash TEXT NOT NULL UNIQUE, imported_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, import_type TEXT NOT NULL, parser_version TEXT NOT NULL, filename TEXT, source_type TEXT, raw_source TEXT NOT NULL, row_count INTEGER NOT NULL, warning_count INTEGER NOT NULL DEFAULT 0, error_count INTEGER NOT NULL DEFAULT 0, summary_json TEXT);
CREATE TABLE IF NOT EXISTS identity_issues(id INTEGER PRIMARY KEY, import_batch_id INTEGER REFERENCES import_batches(id), alias_type TEXT, alias_value TEXT, issue_type TEXT NOT NULL, candidates_json TEXT, status TEXT NOT NULL DEFAULT 'open', detail TEXT, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS truck_history(id INTEGER PRIMARY KEY, driver_id INTEGER REFERENCES drivers(id), truck TEXT NOT NULL, observed_at TEXT NOT NULL, import_batch_id INTEGER REFERENCES import_batches(id), source TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS pta_observations(id INTEGER PRIMARY KEY, driver_id INTEGER REFERENCES drivers(id), truck TEXT, division TEXT, driver_code TEXT, pta_raw TEXT, pta_at TEXT, actionable INTEGER NOT NULL DEFAULT 0, operational_status TEXT, planning_status TEXT, operational_note TEXT, driver_type TEXT, location TEXT, source_numeric_1 TEXT, source_numeric_2 TEXT, observed_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, source TEXT NOT NULL, import_batch_id INTEGER REFERENCES import_batches(id));
CREATE TABLE IF NOT EXISTS idle_periods(id INTEGER PRIMARY KEY, driver_id INTEGER REFERENCES drivers(id), truck TEXT, period_start TEXT NOT NULL, period_end TEXT NOT NULL, engine_hours REAL NOT NULL, idle_hours REAL NOT NULL, import_batch_id INTEGER REFERENCES import_batches(id), UNIQUE(driver_id,period_start,period_end));
CREATE TABLE IF NOT EXISTS missing_bols(id INTEGER PRIMARY KEY, driver_id INTEGER REFERENCES drivers(id), order_number TEXT, empty_call_date TEXT, origin TEXT, destination TEXT, mileage TEXT, bol_type TEXT, raw_fields_json TEXT NOT NULL, first_seen_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, last_seen_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, mentioned_at TEXT, import_batch_id INTEGER REFERENCES import_batches(id));
CREATE TABLE IF NOT EXISTS driver_work_items(driver_id INTEGER PRIMARY KEY REFERENCES drivers(id), cycle_key TEXT, home_checked INTEGER NOT NULL DEFAULT 0, expected_work TEXT NOT NULL DEFAULT 'Unknown', home_status TEXT NOT NULL DEFAULT 'Unknown', home_reason TEXT, ontime_status TEXT NOT NULL DEFAULT 'Unknown', ontime_reason TEXT, ontime_checked_at TEXT, preplan_reviewed INTEGER NOT NULL DEFAULT 0, preplan_response TEXT NOT NULL DEFAULT 'Unknown', preplan_note TEXT, routing_checked INTEGER NOT NULL DEFAULT 0, routing_status TEXT NOT NULL DEFAULT 'Unknown', routing_note TEXT, safety_note_id INTEGER, safety_mentioned_at TEXT, include_transition INTEGER NOT NULL DEFAULT 0, transition_note TEXT, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS driver_notes(id INTEGER PRIMARY KEY, driver_id INTEGER NOT NULL REFERENCES drivers(id), note TEXT NOT NULL, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS reminders(id INTEGER PRIMARY KEY, driver_id INTEGER NOT NULL REFERENCES drivers(id), text TEXT NOT NULL, due_at TEXT NOT NULL, completed_at TEXT, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS timers(id INTEGER PRIMARY KEY, driver_id INTEGER NOT NULL REFERENCES drivers(id), label TEXT NOT NULL, target_at TEXT NOT NULL, completed_at TEXT, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS transition_drafts(id INTEGER PRIMARY KEY CHECK(id=1), body TEXT NOT NULL, is_manual INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS safety_notes(id INTEGER PRIMARY KEY, note TEXT NOT NULL UNIQUE, active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE IF NOT EXISTS audit_history(id INTEGER PRIMARY KEY, occurred_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, action TEXT NOT NULL, entity_type TEXT NOT NULL, entity_id TEXT, detail_json TEXT);
CREATE INDEX IF NOT EXISTS idx_alias ON driver_aliases(alias_value);
CREATE INDEX IF NOT EXISTS idx_pta_driver_time ON pta_observations(driver_id,observed_at DESC);
CREATE INDEX IF NOT EXISTS idx_truck_time ON truck_history(truck,observed_at DESC);
CREATE INDEX IF NOT EXISTS idx_truck_driver_time ON truck_history(driver_id,observed_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS idx_idle_driver_end ON idle_periods(driver_id,period_end DESC);
CREATE INDEX IF NOT EXISTS idx_bol_driver ON missing_bols(driver_id,mentioned_at);
CREATE INDEX IF NOT EXISTS idx_notes_driver_created ON driver_notes(driver_id,created_at DESC);
CREATE INDEX IF NOT EXISTS idx_reminder_due ON reminders(completed_at,due_at);
CREATE INDEX IF NOT EXISTS idx_reminder_driver_due ON reminders(driver_id,completed_at,due_at);
CREATE INDEX IF NOT EXISTS idx_audit_driver_time ON audit_history(entity_type,entity_id,occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_time ON audit_history(occurred_at DESC);
INSERT OR IGNORE INTO schema_version(version) VALUES(1);
INSERT OR IGNORE INTO transition_drafts(id,body) VALUES(1,'No Open ACE/ACI''s');
INSERT OR IGNORE INTO safety_notes(note) VALUES
('Keep a six-second following distance and expand it in poor conditions.'),
('Use GOAL: Get Out And Look before every blind-side backing move.'),
('Scan mirrors every five to eight seconds and keep an escape route open.'),
('Three points of contact prevents avoidable slips and falls.'),
('Slow down before the curve; never depend on braking through it.'),
('Secure loose items before movement and verify the load after the first stop.'),
('If fatigue appears, stop safely—alertness cannot be negotiated.'),
('Check tires, lights, coupling, and brakes before every departure.');
COMMIT;
'@
    Invoke-Sql $schema -AllowWrite | Out-Null
    $integrity = (Invoke-Sql 'PRAGMA integrity_check;').Trim()
    if ($integrity -ne 'ok') { $script:ReadOnly = $true }
    return @{ db = $script:Db; integrity = $integrity; read_only = $script:ReadOnly }
}

$ExecutionContext.SessionState.Module.OnRemove = { Stop-WaaSqlSession }

function Get-Sha256 {
    param([string]$Text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Convert-DriverCode {
    param([string]$FullName)
    $parts = @($FullName.Trim() -split '\s+' | Where-Object { $_ })
    if ($parts.Count -lt 2) { return $null }
    $first = $parts[0]
    $surname = (@($parts[1..($parts.Count - 1)] | Where-Object { $_.Length -gt 1 }) -join '')
    $surname = [Text.RegularExpressions.Regex]::Replace($surname.Normalize([Text.NormalizationForm]::FormD), '\p{Mn}', '')
    $surname = [Text.RegularExpressions.Regex]::Replace($surname.ToUpperInvariant(), '[^A-Z0-9]', '')
    if ([string]::IsNullOrWhiteSpace($surname)) { return $null }
    return $surname.Substring(0, [Math]::Min(7, $surname.Length)) + $first.Substring(0, 1).ToUpperInvariant()
}

function Find-Driver {
    param([string]$AliasType, [string]$Value, [AllowNull()][string]$FullName)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $valueSql = ConvertTo-SqlLiteral $Value
    $rows = @(Invoke-Sql "SELECT DISTINCT driver_id FROM driver_aliases WHERE alias_value=$valueSql COLLATE NOCASE;" -Json)
    if ($rows.Count -eq 1) { return [int]$rows[0].driver_id }
    if ($rows.Count -gt 1 -or [string]::IsNullOrWhiteSpace($FullName)) { return $null }
    $name = if ($FullName -eq 'Unknown') { $Value } else { $FullName }
    $nameSql = ConvertTo-SqlLiteral $name
    $ptaSql = if ($AliasType -eq 'pta_code') { ConvertTo-SqlLiteral $Value } else { 'NULL' }
    Invoke-Sql "INSERT INTO drivers(full_name,pta_code) VALUES($nameSql,$ptaSql);" -AllowWrite | Out-Null
    $id = [int](Invoke-Sql 'SELECT max(id) FROM drivers;')
    $typeSql = ConvertTo-SqlLiteral $AliasType
    Invoke-Sql "INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed) VALUES($id,$typeSql,$valueSql,0);" -AllowWrite | Out-Null
    return $id
}

function Parse-Date {
    param([AllowNull()][string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $number = 0.0
    if ([double]::TryParse($Text,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number) -and
        $number -ge 20000 -and $number -le 100000) {
        try { return [datetime]::FromOADate($number).ToString('s') } catch { }
    }
    $date = [datetime]::MinValue
    $styles = [Globalization.DateTimeStyles]::AssumeLocal
    if ([datetime]::TryParse($Text, [Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$date)) { return $date.ToString('s') }
    return $null
}

function Split-ImportRows {
    param([AllowNull()][string]$Raw)
    $rows = [Collections.Generic.List[object]]::new()
    if ([string]::IsNullOrWhiteSpace($Raw)) { return $rows }
    $reader = [IO.StringReader]::new($Raw)
    try {
        while ($true) {
            $line = $reader.ReadLine()
            if ($null -eq $line) { break }
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $trimmed = $line.Trim()
            if ($line.IndexOf("`t", [StringComparison]::Ordinal) -ge 0) { $cells = [regex]::Split($line, "`t") }
            elseif ($trimmed.StartsWith('|')) { $cells = @($trimmed.Trim('|') -split '(?<!\\)\|' | ForEach-Object { $_.Trim().Replace('\_', '_').Replace('\|', '|') }) }
            else { $cells = @($line -split ',(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)' | ForEach-Object { $_.Trim(' ', '"') }) }
            if (($cells -join '') -match '^[-: ]+$') { continue }
            [void]$rows.Add([object[]]$cells)
        }
    }
    finally { $reader.Dispose() }
    return $rows
}

function Test-PtaSeparatorRow {
    param([string[]]$Cells)
    if ($Cells.Count -eq 0) { return $false }
    foreach ($cell in $Cells) { if (([string]$cell).Trim() -notmatch '^:?-{2,}:?$') { return $false } }
    return $true
}

function Test-PtaHeaderRow {
    param([string[]]$Cells)
    if ($Cells.Count -eq 0) { return $false }
    $first = [regex]::Replace(([string]$Cells[0]).Trim().ToLowerInvariant(), '[^a-z0-9]', '')
    return $first -in @('truck', 'trucknumber', 'truckno', 'unit', 'unitnumber')
}

function ConvertFrom-PtaText {
    param([string]$Raw)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $rows = [Collections.Generic.List[object]]::new()
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
                for ($i = 0; $i -lt $cells.Length; $i++) { $cells[$i] = $cells[$i].Trim() }
            }
            elseif ($trimmed.StartsWith('|')) {
                $parts = [regex]::Split($trimmed.Trim('|'), '(?<!\\)\|')
                $cells = [string[]]::new($parts.Length)
                for ($i = 0; $i -lt $parts.Length; $i++) { $cells[$i] = ([string]$parts[$i]).Trim().Replace('\_', '_').Replace('\|', '|') }
            }
            else {
                $parts = [regex]::Split($line, ',(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)')
                $cells = [string[]]::new($parts.Length)
                for ($i = 0; $i -lt $parts.Length; $i++) { $cells[$i] = ([string]$parts[$i]).Trim(' ', '"') }
            }
            if (Test-PtaSeparatorRow $cells) { continue }
            if (Test-PtaHeaderRow $cells) { continue }
            if ($cells.Count -lt 11) {
                [void]$warnings.Add("Row $sourceRows has $($cells.Count) columns; expected 11.")
                continue
            }
            $row = [string[]]::new(11)
            for ($i = 0; $i -lt 11; $i++) { $row[$i] = ([string]$cells[$i]).Trim() }
            [void]$rows.Add($row)
            if ($sample.Count -lt 8) { [void]$sample.Add($row) }
        }
    }
    finally {
        $reader.Dispose()
        $watch.Stop()
    }
    $errors = [Collections.Generic.List[string]]::new()
    if ($rows.Count -eq 0) { [void]$errors.Add('No valid PTA data rows were detected.') }
    return @{
        rows = $rows; sample = $sample; warnings = $warnings; errors = $errors; total_rows = $sourceRows;
        valid_rows = $rows.Count; hash = Get-Sha256 $Raw; parse_ms = [math]::Round($watch.Elapsed.TotalMilliseconds, 1)
    }
}

function Get-PtaPreview {
    param([string]$Raw, [string]$Filename = 'PTA paste')
    $parsed = ConvertFrom-PtaText $Raw
    return @{
        type = 'pta'; parser_version = '2.0.0-bulk'; total_rows = $parsed.total_rows; valid_rows = $parsed.valid_rows;
        warnings = @($parsed.warnings); errors = @($parsed.errors); sample = @($parsed.sample); hash = $parsed.hash;
        filename = $Filename; parse_ms = $parsed.parse_ms
    }
}

function Import-PtaBulk {
    param([string]$Raw, [string]$Filename = 'PTA paste')
    $totalWatch = [Diagnostics.Stopwatch]::StartNew()
    $parsed = ConvertFrom-PtaText $Raw
    if ($parsed.errors.Count -gt 0) { throw ($parsed.errors -join '; ') }
    $hashSql = ConvertTo-SqlLiteral $parsed.hash
    if (@(Invoke-Sql "SELECT id FROM import_batches WHERE source_hash=$hashSql LIMIT 1;" -Json).Count -gt 0) {
        throw 'Duplicate import: this exact source was already committed.'
    }

    $summary = @{
        type = 'pta'; parser_version = '2.0.0-bulk'; total_rows = $parsed.total_rows; valid_rows = $parsed.valid_rows;
        warnings = @($parsed.warnings); errors = @(); hash = $parsed.hash; filename = $Filename; parse_ms = $parsed.parse_ms
    }
    $summarySql = ConvertTo-SqlLiteral ($summary | ConvertTo-Json -Compress -Depth 6)
    $rawSql = ConvertTo-SqlLiteral $Raw
    $filenameSql = ConvertTo-SqlLiteral $Filename
    $sql = [Text.StringBuilder]::new([Math]::Max(32768, $parsed.valid_rows * 420))
    [void]$sql.AppendLine('BEGIN IMMEDIATE;')
    [void]$sql.AppendLine("INSERT INTO import_batches(source_hash,import_type,parser_version,filename,source_type,raw_source,row_count,warning_count,error_count,summary_json) VALUES($hashSql,'pta','2.0.0-bulk',$filenameSql,'user',$rawSql,$($parsed.valid_rows),$($parsed.warnings.Count),0,$summarySql);")
    [void]$sql.AppendLine(@'
CREATE TEMP TABLE temp_waa_pta_stage(
 row_no INTEGER NOT NULL, truck TEXT, division TEXT, driver_code TEXT, pta_raw TEXT, pta_at TEXT,
 sentinel INTEGER NOT NULL, operational_status TEXT, planning_status TEXT, operational_note TEXT,
 driver_type TEXT, location TEXT, source_numeric_1 TEXT, source_numeric_2 TEXT
);
'@)
    [void]$sql.Append('INSERT INTO temp_waa_pta_stage VALUES ')
    $firstTuple = $true
    $rowNumber = 0
    foreach ($rowObject in $parsed.rows) {
        $rowNumber++
        [string[]]$row = $rowObject
        $rawPta = $row[3]
        $ptaAt = Parse-Date $rawPta
        $driverCode = $row[2]
        $status = $row[4]
        $sentinel = if ([string]::IsNullOrWhiteSpace($driverCode) -and $rawPta -match '^12/31/26\s+23:59$' -and $status -match '^(Shop|TruckPrep|Reserved|ClaimsHold|Clean_QA|GoodToGo)$') { 1 } else { 0 }
        $values = @($rowNumber,$row[0],$row[1],$driverCode,$rawPta,$ptaAt,$sentinel,$status,$row[5],$row[6],$row[7],$row[8],$row[9],$row[10]) | ForEach-Object { ConvertTo-SqlLiteral $_ }
        if (-not $firstTuple) { [void]$sql.Append(',') }
        $firstTuple = $false
        [void]$sql.Append('(' + ($values -join ',') + ')')
    }
    [void]$sql.AppendLine(';')

    [void]$sql.AppendLine(@'
INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed)
SELECT existing.driver_id,'pta_code',codes.driver_code,0
FROM (SELECT DISTINCT driver_code FROM temp_waa_pta_stage WHERE trim(driver_code)<>'') codes
JOIN (
  SELECT alias_value,MIN(driver_id) driver_id FROM driver_aliases
  GROUP BY alias_value COLLATE NOCASE HAVING COUNT(DISTINCT driver_id)=1
) existing ON existing.alias_value=codes.driver_code COLLATE NOCASE
WHERE NOT EXISTS(SELECT 1 FROM driver_aliases p WHERE p.alias_type='pta_code' AND p.alias_value=codes.driver_code COLLATE NOCASE)
ON CONFLICT(alias_type,alias_value) DO NOTHING;

INSERT INTO drivers(full_name,pta_code)
SELECT codes.driver_code,codes.driver_code
FROM (SELECT DISTINCT driver_code FROM temp_waa_pta_stage WHERE trim(driver_code)<>'') codes
WHERE NOT EXISTS(SELECT 1 FROM driver_aliases p WHERE p.alias_type='pta_code' AND p.alias_value=codes.driver_code COLLATE NOCASE)
AND NOT EXISTS(SELECT 1 FROM driver_aliases a WHERE a.alias_value=codes.driver_code COLLATE NOCASE)
AND NOT EXISTS(SELECT 1 FROM drivers d WHERE d.pta_code=codes.driver_code COLLATE NOCASE);

INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed)
SELECT d.id,'pta_code',codes.driver_code,0
FROM (SELECT DISTINCT driver_code FROM temp_waa_pta_stage WHERE trim(driver_code)<>'') codes
JOIN drivers d ON d.pta_code=codes.driver_code COLLATE NOCASE
WHERE NOT EXISTS(SELECT 1 FROM driver_aliases p WHERE p.alias_type='pta_code' AND p.alias_value=codes.driver_code COLLATE NOCASE)
ON CONFLICT(alias_type,alias_value) DO NOTHING;
'@)

    [void]$sql.AppendLine("INSERT INTO identity_issues(import_batch_id,alias_type,alias_value,issue_type,detail) SELECT (SELECT id FROM import_batches WHERE source_hash=$hashSql),'pta_code',codes.driver_code,'unmatched','PTA row not linked' FROM (SELECT DISTINCT driver_code FROM temp_waa_pta_stage WHERE trim(driver_code)<>'') codes WHERE NOT EXISTS(SELECT 1 FROM driver_aliases p WHERE p.alias_type='pta_code' AND p.alias_value=codes.driver_code COLLATE NOCASE);")
    [void]$sql.AppendLine("INSERT INTO pta_observations(driver_id,truck,division,driver_code,pta_raw,pta_at,actionable,operational_status,planning_status,operational_note,driver_type,location,source_numeric_1,source_numeric_2,source,import_batch_id) SELECT (SELECT p.driver_id FROM driver_aliases p WHERE p.alias_type='pta_code' AND p.alias_value=s.driver_code COLLATE NOCASE LIMIT 1),s.truck,s.division,s.driver_code,s.pta_raw,s.pta_at,CASE WHEN s.sentinel=1 THEN 0 WHEN trim(coalesce(s.driver_code,''))='' THEN 0 WHEN EXISTS(SELECT 1 FROM driver_aliases p WHERE p.alias_type='pta_code' AND p.alias_value=s.driver_code COLLATE NOCASE) THEN 1 ELSE 0 END,s.operational_status,s.planning_status,s.operational_note,s.driver_type,s.location,s.source_numeric_1,s.source_numeric_2,'import',(SELECT id FROM import_batches WHERE source_hash=$hashSql) FROM temp_waa_pta_stage s ORDER BY s.row_no;")
    [void]$sql.AppendLine("INSERT INTO truck_history(driver_id,truck,observed_at,import_batch_id,source) SELECT p.driver_id,s.truck,CURRENT_TIMESTAMP,(SELECT id FROM import_batches WHERE source_hash=$hashSql),'pta' FROM temp_waa_pta_stage s JOIN driver_aliases p ON p.alias_type='pta_code' AND p.alias_value=s.driver_code COLLATE NOCASE WHERE trim(coalesce(s.driver_code,''))<>'' ORDER BY s.row_no;")
    $auditSql = ConvertTo-SqlLiteral (@{ type='pta'; rows=$parsed.valid_rows; parser='2.0.0-bulk' } | ConvertTo-Json -Compress)
    [void]$sql.AppendLine("INSERT INTO audit_history(action,entity_type,entity_id,detail_json) VALUES('import_committed','import_batch',(SELECT id FROM import_batches WHERE source_hash=$hashSql),$auditSql);")
    [void]$sql.AppendLine('DROP TABLE temp_waa_pta_stage;')
    [void]$sql.AppendLine('COMMIT;')
    [void]$sql.AppendLine("SELECT id FROM import_batches WHERE source_hash=$hashSql;")

    $dbWatch = [Diagnostics.Stopwatch]::StartNew()
    try { $result = @(Invoke-Sql $sql.ToString() -Json -AllowWrite) }
    catch {
        if ($_.Exception.Message -match '(?i)UNIQUE constraint failed: import_batches.source_hash') { throw 'Duplicate import: this exact source was already committed.' }
        throw
    }
    finally {
        $dbWatch.Stop()
        $totalWatch.Stop()
    }
    if ($result.Count -eq 0) { throw 'PTA import committed but the import batch could not be verified.' }
    return @{
        id=[int]$result[0].id; type='pta'; rows=$parsed.valid_rows; warnings=@($parsed.warnings); parser_version='2.0.0-bulk';
        parse_ms=$parsed.parse_ms; db_ms=[math]::Round($dbWatch.Elapsed.TotalMilliseconds,1); total_ms=[math]::Round($totalWatch.Elapsed.TotalMilliseconds,1)
    }
}

function Get-ImportPreview {
    param([string]$Raw, [string]$Filename, [string]$RequestedType = 'auto')
    $type = $RequestedType
    if ($type -eq 'auto') {
        $firstLine = (($Raw -split "`r?`n", 2)[0])
        if ($firstLine -match 'Last Dispatch Driver|Missing BOL') { $type = 'bol' }
        elseif ($firstLine -match 'Rolling 7 Day|Measure Names|Engine Time') { $type = 'idle' }
        else { $type = 'pta' }
    }
    if ($type -eq 'pta') { return Get-PtaPreview $Raw $Filename }

    $rows = Split-ImportRows $Raw
    $warnings = [Collections.Generic.List[string]]::new()
    $errors = [Collections.Generic.List[string]]::new()
    $sample = [Collections.Generic.List[object]]::new()
    $valid = 0
    $needed = if ($type -eq 'bol') { 29 } else { 7 }
    foreach ($row in $rows) {
        if (($row -join ' ') -match '^(Unit Code|Order|Group by)') { continue }
        if ($row.Count -lt $needed) { [void]$warnings.Add("Row has $($row.Count) columns; expected $needed"); continue }
        $valid++
        if ($sample.Count -lt 8) { [void]$sample.Add($row) }
    }
    if ($valid -eq 0) { [void]$errors.Add('No valid data rows were detected.') }
    return @{ type=$type; parser_version='1.0.0'; total_rows=$rows.Count; valid_rows=$valid; warnings=@($warnings); errors=@($errors); sample=@($sample); hash=Get-Sha256 $Raw; filename=$Filename }
}

function Import-WaaData {
    param([string]$Raw, [string]$Filename, [string]$RequestedType = 'auto')
    $preview = Get-ImportPreview $Raw $Filename $RequestedType
    if ($preview.errors.Count -gt 0) { throw ($preview.errors -join '; ') }
    if ($preview.type -eq 'pta') { return Import-PtaBulk $Raw $Filename }
    $managedImporter = Get-Command Import-WaaManagedReport -ErrorAction SilentlyContinue
    if ($null -ne $managedImporter) {
        return Import-WaaManagedReport -Canonical $Raw -Filename $Filename -Type $preview.type
    }

    # Compatibility fallback for callers that import only Waa.psm1. The complete application
    # loads ReportIntake.ps1 and uses its single-transaction managed importer above.
    $hashSql = ConvertTo-SqlLiteral $preview.hash
    if ((Invoke-Sql "SELECT count(*) c FROM import_batches WHERE source_hash=$hashSql;" -Json)[0].c -gt 0) { throw 'Duplicate import: this exact source was already committed.' }
    $rows = Split-ImportRows $Raw
    $rawSql = ConvertTo-SqlLiteral $Raw
    $typeSql = ConvertTo-SqlLiteral $preview.type
    $fileSql = ConvertTo-SqlLiteral $Filename
    $summarySql = ConvertTo-SqlLiteral ($preview | ConvertTo-Json -Compress -Depth 8)
    Invoke-Sql "BEGIN IMMEDIATE; INSERT INTO import_batches(source_hash,import_type,parser_version,filename,source_type,raw_source,row_count,warning_count,error_count,summary_json) VALUES($hashSql,$typeSql,'1.0.0',$fileSql,'user',$rawSql,$($preview.valid_rows),$($preview.warnings.Count),0,$summarySql); COMMIT;" -AllowWrite | Out-Null
    $batchId = [int](Invoke-Sql 'SELECT max(id) FROM import_batches;')
    try {
        foreach ($row in $rows) {
            if (($row -join ' ') -match '^(Unit Code|Order)' -or ($row -join '') -match '^[-: ]+$') { continue }
            if ($preview.type -eq 'idle' -and $row.Count -ge 7) {
                $header = $rows[0]
                $map = @{}
                for ($i=0; $i -lt $header.Count; $i++) { $map[$header[$i].Trim()] = $i }
                if (-not $map.ContainsKey('Measure Names') -or $row[$map['Measure Names']] -ne 'Idle %') { continue }
                $parts = $row[$map['Group by  (copy)']].Trim() -split ' ',2
                if ($parts.Count -lt 2) { continue }
                $driver = Find-Driver 'dispatch_code' $parts[0] $parts[1]
                $start = Parse-Date $row[$map['Rolling 7 Day Start Date']]
                $end = Parse-Date $row[$map['Week Start Date']]
                $engine=0.0; $idle=0.0
                if (-not [double]::TryParse($row[$map['[Rolling 7 Day Engine Time]/60']],[ref]$engine) -or -not [double]::TryParse($row[$map['[Rolling 7 Day Idle Time]/60']],[ref]$idle)) { throw 'Invalid idle hours' }
                $values = @($driver,$row[$map['Unit Code']],$start,$end,$engine,$idle,$batchId) | ForEach-Object { ConvertTo-SqlLiteral $_ }
                Invoke-Sql "INSERT INTO idle_periods(driver_id,truck,period_start,period_end,engine_hours,idle_hours,import_batch_id) VALUES($($values -join ','));" -AllowWrite | Out-Null
            }
            elseif ($preview.type -eq 'bol' -and $row.Count -ge 29) {
                $header = $rows[0]
                $map = @{}
                for ($i=0; $i -lt $header.Count; $i++) { $map[$header[$i].Trim()] = $i }
                if ($map.ContainsKey('Last Dispatch Driver cd')) { $code=$row[$map['Last Dispatch Driver cd']]; $name=$row[$map['Last Dispatch Driver nm']] }
                else { $code=$row[27]; $name=$row[28] }
                $driver = Find-Driver 'dispatch_code' $code $name
                $order=$row[0]
                $date=if($map.ContainsKey('Empty Call Date')){$row[$map['Empty Call Date']]}else{$row[1]}
                $origin=if($map.ContainsKey('Origin')){$row[$map['Origin']]}else{$row[2]}
                $destination=if($map.ContainsKey('Destination')){$row[$map['Destination']]}else{$row[3]}
                $rawJson=ConvertTo-SqlLiteral ($row | ConvertTo-Json -Compress)
                $values=@($driver,$order,(Parse-Date $date),$origin,$destination,$rawJson,$batchId) | ForEach-Object { ConvertTo-SqlLiteral $_ }
                Invoke-Sql "INSERT INTO missing_bols(driver_id,order_number,empty_call_date,origin,destination,raw_fields_json,import_batch_id) VALUES($($values -join ','));" -AllowWrite | Out-Null
            }
        }
    }
    catch { Invoke-Sql "DELETE FROM import_batches WHERE id=$batchId;" -AllowWrite | Out-Null; throw }
    Add-Audit 'import_committed' 'import_batch' $batchId @{type=$preview.type;rows=$preview.valid_rows}
    return @{id=$batchId;type=$preview.type;rows=$preview.valid_rows;warnings=$preview.warnings}
}

function Get-Dashboard {
    $sql = @'
WITH ranked AS (SELECT i.*,row_number() OVER(PARTITION BY driver_id ORDER BY period_end DESC,id DESC) rn FROM idle_periods i),
s AS (SELECT i.*,CASE WHEN i.engine_hours=0 THEN NULL ELSE round(i.idle_hours*100.0/i.engine_hours,2) END p FROM ranked i WHERE rn=1),
recent AS (SELECT *,lag(period_end) OVER(PARTITION BY driver_id ORDER BY period_start) previous_end FROM ranked WHERE rn<=4),
d28 AS (SELECT driver_id,sum(engine_hours) e,sum(idle_hours) i,count(*) n,
  sum(CASE WHEN previous_end IS NOT NULL AND julianday(period_start)-julianday(previous_end)<>1 THEN 1 ELSE 0 END) gaps,
  sum(CASE WHEN julianday(period_end)-julianday(period_start)=6 THEN 1 ELSE 0 END) valid_weeks
  FROM recent GROUP BY driver_id)
SELECT d.id,d.full_name,d.pta_code,s.truck,s.engine_hours engine7,s.idle_hours idle7,s.p p7,
CASE WHEN d28.e=0 OR d28.n<4 OR d28.gaps>0 OR d28.valid_weeks<4 THEN NULL ELSE round(d28.i*100.0/d28.e,2) END p28,
d28.e engine28,d28.n weeks28,CASE WHEN d28.n=4 AND d28.gaps=0 AND d28.valid_weeks=4 THEN 'Complete' ELSE 'Partial Data' END coverage28,
CASE WHEN d28.n<4 THEN CAST(d28.n AS TEXT)||'/4 weekly reports'
     WHEN d28.valid_weeks<4 THEN 'A source period is not seven days'
     WHEN d28.gaps>0 THEN 'Weekly reports are not consecutive'
     WHEN d28.e=0 THEN 'No engine-hour data' ELSE 'Four consecutive weekly reports' END coverage28_detail,
EXISTS(SELECT 1 FROM driver_call_sessions c WHERE c.driver_id=d.id AND trim(coalesce(c.idle_plan,''))<>'') coached
FROM drivers d JOIN s ON s.driver_id=d.id LEFT JOIN d28 ON d28.driver_id=d.id;
'@
    $drivers=@(Invoke-Sql $sql -Json)
    $history=@(Invoke-Sql "SELECT period_end,round(sum(idle_hours)*100.0/nullif(sum(engine_hours),0),2) p7 FROM idle_periods GROUP BY period_end ORDER BY period_end;" -Json)
    $history28=@(Invoke-Sql @'
WITH fleet_week AS (
  SELECT period_end,sum(engine_hours) engine_hours,sum(idle_hours) idle_hours
  FROM idle_periods GROUP BY period_end
), marked AS (
  SELECT *,lag(period_end) OVER(ORDER BY period_end) previous_end FROM fleet_week
), rolling AS (
  SELECT period_end,
         sum(engine_hours) OVER(ORDER BY period_end ROWS BETWEEN 3 PRECEDING AND CURRENT ROW) engine28,
         sum(idle_hours) OVER(ORDER BY period_end ROWS BETWEEN 3 PRECEDING AND CURRENT ROW) idle28,
         count(*) OVER(ORDER BY period_end ROWS BETWEEN 3 PRECEDING AND CURRENT ROW) weeks,
         sum(CASE WHEN previous_end IS NOT NULL AND julianday(period_end)-julianday(previous_end)<>7 THEN 1 ELSE 0 END)
           OVER(ORDER BY period_end ROWS BETWEEN 3 PRECEDING AND CURRENT ROW) gaps
  FROM marked
)
SELECT period_end,weeks,gaps,CASE WHEN weeks=4 AND gaps=0 THEN round(idle28*100.0/nullif(engine28,0),2) END p28
FROM rolling ORDER BY period_end;
'@ -Json)
    $over=@($drivers | Where-Object {$null -ne $_.p7 -and [double]$_.p7 -gt 50}).Count
    $coachedOver=@($drivers | Where-Object {$null -ne $_.p7 -and [double]$_.p7 -gt 50 -and [int]$_.coached-eq1}).Count
    $coachedPercent=if($over-gt0){[math]::Round($coachedOver*100.0/$over,1)}else{$null}
    # Exact 0% and 100% weekly values are retained as source data but excluded from
    # comparative Top 5 rankings as likely telemetry/reporting edge cases. This guard
    # intentionally does not alter fleet history or any weighted 28-day calculation.
    $valid=@($drivers | Where-Object {$null -ne $_.p7 -and [double]$_.p7 -gt 0 -and [double]$_.p7 -lt 100} | Sort-Object {[double]$_.p7})
    $complete28=@($drivers|Where-Object{$_.coverage28-eq'Complete'}).Count
    $latestFleet28=if($history28.Count){$history28[-1]}else{$null}
    return @{drivers=$drivers;heroes=@($valid|Select-Object -First 5);training=@($valid|Sort-Object {[double]$_.p7} -Descending|Select-Object -First 5);over50=$over;coaching=@{coached=$coachedOver;eligible=$over;percent=$coachedPercent};history7=$history;history28=$history28;coverage28=@{complete_drivers=$complete28;tracked_drivers=$drivers.Count;fleet_weeks=$(if($null-ne$latestFleet28){[int]$latestFleet28.weeks}else{0});fleet_ready=($null-ne$latestFleet28-and$null-ne$latestFleet28.p28)}}
}

function Get-CurrentDrivers {
    $sql=@'
WITH p AS (SELECT *,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn FROM pta_observations WHERE driver_id IS NOT NULL),
t AS (SELECT *,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn FROM truck_history)
SELECT d.id,d.full_name,d.pta_code,coalesce(nullif(trim(p.truck),''),t.truck) truck,p.division,p.pta_at,p.pta_raw,p.actionable,p.operational_status,p.planning_status,p.operational_note,p.driver_type,p.location,p.source,p.observed_at
FROM drivers d LEFT JOIN p ON p.driver_id=d.id AND p.rn=1 LEFT JOIN t ON t.driver_id=d.id AND t.rn=1;
'@
    return Invoke-Sql $sql -Json
}

function Get-CurrentDriver {
    param([int]$Id)
    $sql=@"
WITH p AS (
  SELECT *,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn
  FROM pta_observations WHERE driver_id=$Id
), t AS (
  SELECT *,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn
  FROM truck_history WHERE driver_id=$Id
)
SELECT d.id,d.full_name,d.pta_code,coalesce(nullif(trim(p.truck),''),t.truck) truck,p.division,p.pta_at,p.pta_raw,
       p.actionable,p.operational_status,p.planning_status,p.operational_note,p.driver_type,p.location,p.source,p.observed_at
FROM drivers d LEFT JOIN p ON p.driver_id=d.id AND p.rn=1 LEFT JOIN t ON t.driver_id=d.id AND t.rn=1
WHERE d.id=$Id;
"@
    $rows=@(Invoke-Sql $sql -Json)
    if(!$rows.Count){return $null}
    return $rows[0]
}

function Get-DriverCard {
    param([int]$Id)
    $sql=@"
WITH p AS (
  SELECT *,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn
  FROM pta_observations WHERE driver_id=$Id
), t AS (
  SELECT *,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn
  FROM truck_history WHERE driver_id=$Id
), current_driver AS (
  SELECT d.id,d.full_name,d.pta_code,coalesce(nullif(trim(p.truck),''),t.truck) truck,p.division,p.pta_at,p.pta_raw,
         p.actionable,p.operational_status,p.planning_status,p.operational_note,p.driver_type,p.location,p.source,p.observed_at
  FROM drivers d LEFT JOIN p ON p.driver_id=d.id AND p.rn=1 LEFT JOIN t ON t.driver_id=d.id AND t.rn=1
  WHERE d.id=$Id
)
SELECT json_object(
  'driver',json(coalesce((SELECT json_object('id',id,'full_name',full_name,'pta_code',pta_code,'truck',truck,'division',division,
    'pta_at',pta_at,'pta_raw',pta_raw,'actionable',actionable,'operational_status',operational_status,'planning_status',planning_status,
    'operational_note',operational_note,'driver_type',driver_type,'location',location,'source',source,'observed_at',observed_at) FROM current_driver),'null')),
  'idle',json(coalesce((SELECT json_group_array(json_object('period_start',period_start,'period_end',period_end,'engine_hours',engine_hours,
    'idle_hours',idle_hours,'percent',percent)) FROM (SELECT period_start,period_end,engine_hours,idle_hours,
    round(idle_hours*100.0/nullif(engine_hours,0),2) percent FROM idle_periods WHERE driver_id=$Id ORDER BY period_end DESC LIMIT 12)),'[]')),
  'bols',json(coalesce((SELECT json_group_array(json_object('id',id,'order_number',order_number,'empty_call_date',empty_call_date,
    'origin',origin,'destination',destination,'mileage',mileage,'bol_type',bol_type,'first_seen_at',first_seen_at,'last_seen_at',last_seen_at,
    'mentioned_at',mentioned_at)) FROM (SELECT * FROM missing_bols WHERE driver_id=$Id ORDER BY empty_call_date DESC)),'[]')),
  'work',json(coalesce((SELECT json_object('driver_id',driver_id,'cycle_key',cycle_key,'home_checked',home_checked,'expected_work',expected_work,
    'home_status',home_status,'home_reason',home_reason,'ontime_status',ontime_status,'ontime_reason',ontime_reason,'ontime_checked_at',ontime_checked_at,
    'preplan_reviewed',preplan_reviewed,'preplan_response',preplan_response,'preplan_note',preplan_note,'routing_checked',routing_checked,
    'routing_status',routing_status,'routing_note',routing_note,'safety_note_id',safety_note_id,'safety_mentioned_at',safety_mentioned_at,
    'include_transition',include_transition,'transition_note',transition_note,'updated_at',updated_at) FROM driver_work_items WHERE driver_id=$Id),'null')),
  'notes',json(coalesce((SELECT json_group_array(json_object('id',id,'driver_id',driver_id,'note',note,'created_at',created_at))
    FROM (SELECT * FROM driver_notes WHERE driver_id=$Id ORDER BY created_at DESC)),'[]')),
  'reminders',json(coalesce((SELECT json_group_array(json_object('id',id,'driver_id',driver_id,'text',text,'due_at',due_at,
    'completed_at',completed_at,'created_at',created_at)) FROM (SELECT * FROM reminders WHERE driver_id=$Id ORDER BY completed_at,due_at)),'[]')),
  'timers',json(coalesce((SELECT json_group_array(json_object('id',id,'driver_id',driver_id,'label',label,'target_at',target_at,
    'completed_at',completed_at,'created_at',created_at)) FROM (SELECT * FROM timers WHERE driver_id=$Id ORDER BY completed_at,target_at)),'[]')),
  'audit',json(coalesce((SELECT json_group_array(json_object('id',id,'occurred_at',occurred_at,'action',action,'entity_type',entity_type,
    'entity_id',entity_id,'detail_json',detail_json)) FROM (SELECT * FROM audit_history WHERE entity_type='driver' AND entity_id='$Id'
    ORDER BY occurred_at DESC,id DESC LIMIT 50)),'[]'))
);
"@
    $json=[string](Invoke-Sql $sql)
    if([string]::IsNullOrWhiteSpace($json)){throw 'Driver not found'}
    $card=ConvertFrom-Json -InputObject $json
    if($null -eq $card.driver){throw 'Driver not found'}
    return $card
}

function Get-DriverFollowups {
    param([int]$Id)
    $sql=@"
SELECT json_object(
  'notes',json(coalesce((SELECT json_group_array(json_object('id',id,'driver_id',driver_id,'note',note,'created_at',created_at)) FROM (SELECT * FROM driver_notes WHERE driver_id=$Id ORDER BY created_at DESC)),'[]')),
  'reminders',json(coalesce((SELECT json_group_array(json_object('id',id,'driver_id',driver_id,'text',text,'due_at',due_at,'completed_at',completed_at,'created_at',created_at)) FROM (SELECT * FROM reminders WHERE driver_id=$Id ORDER BY completed_at,due_at)),'[]')),
  'timers',json(coalesce((SELECT json_group_array(json_object('id',id,'driver_id',driver_id,'label',label,'target_at',target_at,'completed_at',completed_at,'created_at',created_at)) FROM (SELECT * FROM timers WHERE driver_id=$Id ORDER BY completed_at,target_at)),'[]'))
);
"@
    return ConvertFrom-Json -InputObject ([string](Invoke-Sql $sql))
}

function Save-DriverAction {
    param([int]$Id,[hashtable]$Body)
    $action=[string]$Body.action
    switch($action){
        'assign_truck' {
            $current=Get-CurrentDriver $Id
            if($null -eq $current){throw 'Driver not found'}
            if(-not [string]::IsNullOrWhiteSpace([string]$current.truck)){throw "Driver is already associated with truck $($current.truck)."}
            $truck=([string]$Body.value).Trim().ToUpperInvariant()
            if($truck -notmatch '^[A-Z0-9][A-Z0-9 ._-]{0,23}$'){throw 'Truck number must be 1-24 letters, numbers, spaces, periods, underscores, or hyphens.'}
            $truckSql=ConvertTo-SqlLiteral $truck
            Invoke-Sql "INSERT INTO truck_history(driver_id,truck,observed_at,source) VALUES($Id,$truckSql,CURRENT_TIMESTAMP,'manual');" -AllowWrite|Out-Null
            $Body.value=$truck
        }
        'pta' {
            $pta=Parse-Date ([string]$Body.value); if(!$pta){throw 'Invalid PTA date'}
            $current=Get-CurrentDriver $Id
            $values=@($Id,$current.truck,$current.division,$current.pta_code,$Body.value,$pta,$current.operational_status,$current.planning_status,$current.operational_note,$current.driver_type,$current.location)|ForEach-Object{ConvertTo-SqlLiteral $_}
            Invoke-Sql "INSERT INTO pta_observations(driver_id,truck,division,driver_code,pta_raw,pta_at,actionable,operational_status,planning_status,operational_note,driver_type,location,source) VALUES($($values[0..5]-join ','),1,$($values[6..10]-join ','),'manual');" -AllowWrite|Out-Null
        }
        'note' {$text=([string]$Body.text).Trim();if(!$text){throw 'Note text is required'};$q=ConvertTo-SqlLiteral $text;Invoke-Sql "INSERT INTO driver_notes(driver_id,note) VALUES($Id,$q);" -AllowWrite|Out-Null}
        'reminder' {$text=([string]$Body.text).Trim();$due=Parse-Date $Body.due_at;if (-not $text -or -not $due) {throw 'Reminder text and a valid due time are required'};$t=ConvertTo-SqlLiteral $text;$d=ConvertTo-SqlLiteral $due;Invoke-Sql "INSERT INTO reminders(driver_id,text,due_at) VALUES($Id,$t,$d);" -AllowWrite|Out-Null}
        'delete_note' {$item=[int]$Body.item_id;$changed=[int](Invoke-Sql "DELETE FROM driver_notes WHERE id=$item AND driver_id=$Id;SELECT changes();" -AllowWrite);if($changed-ne1){throw 'Driver note not found'}}
        'delete_reminder' {$item=[int]$Body.item_id;$changed=[int](Invoke-Sql "DELETE FROM reminders WHERE id=$item AND driver_id=$Id;SELECT changes();" -AllowWrite);if($changed-ne1){throw 'Driver reminder not found'}}
        'delete_timer' {$item=[int]$Body.item_id;$changed=[int](Invoke-Sql "DELETE FROM timers WHERE id=$item AND driver_id=$Id;SELECT changes();" -AllowWrite);if($changed-ne1){throw 'Driver timer not found'}}
        'timer' {$t=ConvertTo-SqlLiteral $Body.label;$d=ConvertTo-SqlLiteral(Parse-Date $Body.target_at);Invoke-Sql "INSERT INTO timers(driver_id,label,target_at) VALUES($Id,$t,$d);" -AllowWrite|Out-Null}
        'bol_mentioned' {Invoke-Sql "UPDATE missing_bols SET mentioned_at=CASE WHEN mentioned_at IS NULL THEN CURRENT_TIMESTAMP ELSE NULL END WHERE id=$([int]$Body.item_id) AND driver_id=$Id;" -AllowWrite|Out-Null}
        'complete_reminder' {Invoke-Sql "UPDATE reminders SET completed_at=CASE WHEN completed_at IS NULL THEN CURRENT_TIMESTAMP ELSE NULL END WHERE id=$([int]$Body.item_id) AND driver_id=$Id;" -AllowWrite|Out-Null}
        'snooze_reminder' {Invoke-Sql "UPDATE reminders SET due_at=datetime(due_at,'+1 day'),completed_at=NULL WHERE id=$([int]$Body.item_id) AND driver_id=$Id;" -AllowWrite|Out-Null}
        'complete_timer' {Invoke-Sql "UPDATE timers SET completed_at=CASE WHEN completed_at IS NULL THEN CURRENT_TIMESTAMP ELSE NULL END WHERE id=$([int]$Body.item_id) AND driver_id=$Id;" -AllowWrite|Out-Null}
        default {
            $allowed=@('home_checked','expected_work','home_status','home_reason','ontime_status','ontime_reason','preplan_reviewed','preplan_response','preplan_note','routing_checked','routing_status','routing_note','safety_note_id','safety_mentioned_at','include_transition','transition_note')
            if($allowed -notcontains $action){throw 'Unknown action'}
            $value=$Body.value
            if($action -eq 'safety_mentioned_at' -and $value){$value=(Get-Date).ToUniversalTime().ToString('s')}
            $q=ConvertTo-SqlLiteral $value
            if($action-eq'ontime_status'){Invoke-Sql "INSERT INTO driver_work_items(driver_id,$action,ontime_checked_at) VALUES($Id,$q,CURRENT_TIMESTAMP) ON CONFLICT(driver_id) DO UPDATE SET $action=excluded.$action,ontime_checked_at=CURRENT_TIMESTAMP,updated_at=CURRENT_TIMESTAMP;" -AllowWrite|Out-Null}
            else{Invoke-Sql "INSERT INTO driver_work_items(driver_id,$action) VALUES($Id,$q) ON CONFLICT(driver_id) DO UPDATE SET $action=excluded.$action,updated_at=CURRENT_TIMESTAMP;" -AllowWrite|Out-Null}
        }
    }
    $auditBody=@{}
    foreach($key in $Body.Keys){if($key-ne'return_followups'){$auditBody[$key]=$Body[$key]}}
    Add-Audit $action 'driver' $Id $auditBody
    if($action-in@('include_transition','transition_note')){Update-TransitionDraft -DriverId $Id|Out-Null}
    if($Body.ContainsKey('return_followups')-and[bool]$Body.return_followups){return Get-DriverFollowups $Id}
    return @{ok=$true}
}

function Get-Organizer {
    $sql=@'
WITH latest_truck AS (
  SELECT driver_id,truck,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn
  FROM truck_history
)
SELECT n.id,'note' item_type,n.driver_id,d.full_name,t.truck,n.note text,NULL due_at,NULL completed_at,n.created_at created_at
FROM driver_notes n JOIN drivers d ON d.id=n.driver_id
LEFT JOIN latest_truck t ON t.driver_id=n.driver_id AND t.rn=1
UNION ALL
SELECT r.id,'reminder',r.driver_id,d.full_name,t.truck,r.text,r.due_at,r.completed_at,r.created_at
FROM reminders r JOIN drivers d ON d.id=r.driver_id
LEFT JOIN latest_truck t ON t.driver_id=r.driver_id AND t.rn=1
ORDER BY 9 DESC;
'@
    return @{drivers=@(Get-CurrentDrivers);items=@(Invoke-Sql $sql -Json)}
}

function Get-DailyActivity {
    param([string]$StartUtc,[string]$EndUtc)
    $start=ConvertTo-SqlLiteral $StartUtc
    $end=ConvertTo-SqlLiteral $EndUtc
    $sql=@"
WITH latest_truck AS (
  SELECT driver_id,truck,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn
  FROM truck_history
)
SELECT a.id,a.occurred_at,a.action,a.entity_type,a.entity_id,a.detail_json,
       d.id driver_id,d.full_name,t.truck
FROM audit_history a
LEFT JOIN drivers d ON a.entity_type='driver' AND d.id=CAST(a.entity_id AS INTEGER)
LEFT JOIN latest_truck t ON t.driver_id=d.id AND t.rn=1
WHERE a.occurred_at >= $start AND a.occurred_at < $end
ORDER BY a.occurred_at DESC,a.id DESC;
"@
    return Invoke-Sql $sql -Json
}

function Remove-DailyActivity {
    param([int]$ActivityId)
    if($ActivityId-le0){throw 'Invalid activity record'}
    $rows=@(Invoke-Sql "SELECT id,entity_type,entity_id FROM audit_history WHERE id=$ActivityId;" -Json)
    if(!$rows.Count){throw 'Activity record not found'}
    $changed=[int](Invoke-Sql "DELETE FROM audit_history WHERE id=$ActivityId;SELECT changes();" -AllowWrite)
    if($changed-ne1){throw 'Activity record could not be deleted'}
    $driverId=$null
    if($rows[0].entity_type-eq'driver'){$driverId=[int]$rows[0].entity_id}
    return @{ok=$true;driver_id=$driverId}
}

function Update-TransitionDraft {
    param([switch]$Force,[int]$DriverId=0)
    $manual=[int](Invoke-Sql 'SELECT is_manual FROM transition_drafts WHERE id=1;')
    if($manual-and-not$Force){
        if($DriverId-le0){return -1}
        $driver=Get-CurrentDriver $DriverId
        $work=@(Invoke-Sql "SELECT include_transition,coalesce(transition_note,'') transition_note FROM driver_work_items WHERE driver_id=$DriverId;" -Json)
        if($null-eq$driver-or-not$work.Count){return -1}
        $draft=@(Invoke-Sql 'SELECT body FROM transition_drafts WHERE id=1;' -Json)[0]
        $body=[string]$draft.body
        $namePattern=[regex]::Escape([string]$driver.full_name)
        $body=[regex]::Replace($body,"(?m)^[^`r`n]* - $namePattern :[^`r`n]*(?:`r?`n)?",'').TrimEnd("`r","`n")
        if([int]$work[0].include_transition-eq1){
            $truck=if([string]::IsNullOrWhiteSpace([string]$driver.truck)){'Unknown'}else{[string]$driver.truck}
            $line="$truck - $($driver.full_name) : $($work[0].transition_note)"
            $body=if([string]::IsNullOrWhiteSpace($body)){$line}else{$body+"`r`n"+$line}
        }
        $q=ConvertTo-SqlLiteral $body
        Invoke-Sql "UPDATE transition_drafts SET body=$q,updated_at=CURRENT_TIMESTAMP WHERE id=1;" -AllowWrite|Out-Null
        return 1
    }
    $rows=@(Invoke-Sql @'
WITH p AS (SELECT driver_id,truck,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn FROM pta_observations WHERE driver_id IS NOT NULL),
t AS (SELECT driver_id,truck,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn FROM truck_history)
SELECT coalesce(nullif(trim(p.truck),''),t.truck,'Unknown') truck,d.full_name,coalesce(w.transition_note,'') transition_note
FROM driver_work_items w JOIN drivers d ON d.id=w.driver_id
LEFT JOIN p ON p.driver_id=d.id AND p.rn=1 LEFT JOIN t ON t.driver_id=d.id AND t.rn=1
WHERE w.include_transition=1 ORDER BY CAST(coalesce(nullif(trim(p.truck),''),t.truck) AS INTEGER),coalesce(nullif(trim(p.truck),''),t.truck),d.full_name;
'@ -Json)
        $lines=@("No Open ACE/ACI's")+@($rows|ForEach-Object{"$($_.truck) - $($_.full_name) : $($_.transition_note)"})
        $body=$lines-join"`r`n";$q=ConvertTo-SqlLiteral $body
        Invoke-Sql "UPDATE transition_drafts SET body=$q,is_manual=0,updated_at=CURRENT_TIMESTAMP WHERE id=1;" -AllowWrite|Out-Null
    return $rows.Count
}

function Get-Transition {
    param([switch]$Regenerate)
    if($Regenerate){
        $count=Update-TransitionDraft -Force
        Add-Audit 'transition_regenerated' 'transition' 1 @{count=$count}
    }
    return (Invoke-Sql 'SELECT * FROM transition_drafts WHERE id=1;' -Json)[0]
}

function Save-Transition {
    param([string]$Body)
    $q=ConvertTo-SqlLiteral $Body
    Invoke-Sql "UPDATE transition_drafts SET body=$q,is_manual=1,updated_at=CURRENT_TIMESTAMP WHERE id=1;" -AllowWrite|Out-Null
    Add-Audit 'transition_saved' 'transition' 1 @{}
    return Get-Transition
}

function Get-DataQuality {
    return @{
        issues=@(Invoke-Sql "SELECT * FROM identity_issues WHERE status='open' ORDER BY created_at DESC;" -Json)
        imports=@(Invoke-Sql 'SELECT id,imported_at,import_type,filename,row_count,warning_count,error_count,source_hash FROM import_batches ORDER BY imported_at DESC;' -Json)
        backups=@(Get-ChildItem (Join-Path $script:DataRoot 'backups') -Filter '*.db' -ErrorAction SilentlyContinue|Sort-Object LastWriteTime -Descending|ForEach-Object{@{name=$_.Name;size=$_.Length;created=$_.LastWriteTimeUtc.ToString('s')}})
        integrity=(Invoke-Sql 'PRAGMA integrity_check;').Trim()
    }
}

function Resolve-Identity {
    param([int]$IssueId,[int]$DriverId)
    $issue=(Invoke-Sql "SELECT * FROM identity_issues WHERE id=$IssueId AND status='open';" -Json)[0]
    if(!$issue){throw 'Issue not found'}
    $t=ConvertTo-SqlLiteral $issue.alias_type;$v=ConvertTo-SqlLiteral $issue.alias_value
    Invoke-Sql "BEGIN;INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed) VALUES($DriverId,$t,$v,1) ON CONFLICT(alias_type,alias_value) DO UPDATE SET driver_id=excluded.driver_id,confirmed=1;UPDATE identity_issues SET status='resolved' WHERE id=$IssueId;COMMIT;" -AllowWrite|Out-Null
    Add-Audit 'identity_linked' 'driver' $DriverId @{issue=$IssueId}
    return @{ok=$true}
}

function Get-SafetyNote {
    param([int]$Except=0)
    $rows=@(Invoke-Sql "SELECT id,note FROM safety_notes WHERE active=1 AND id<>$Except ORDER BY random() LIMIT 1;" -Json)
    if(!$rows.Count){$rows=@(Invoke-Sql 'SELECT id,note FROM safety_notes WHERE active=1 LIMIT 1;' -Json)}
    return $rows[0]
}

function Restore-Waa {
    param([string]$Name)
    if($Name -ne [IO.Path]::GetFileName($Name)){throw 'Invalid backup name'}
    $path=Join-Path (Join-Path $script:DataRoot 'backups') $Name
    if(!(Test-Path -LiteralPath $path)){throw 'Backup not found'}
    Backup-Waa 'pre-restore'|Out-Null
    $safe=$path.Replace("'","''")
    Invoke-Sql ".restore '$safe'"|Out-Null
    return @{ok=$true}
}

Export-ModuleMember -Function Initialize-Waa,Invoke-Sql,ConvertTo-SqlLiteral,Parse-Date,Split-ImportRows,Convert-DriverCode,Get-ImportPreview,Import-WaaData,Get-Dashboard,Get-CurrentDrivers,Get-CurrentDriver,Get-DriverCard,Save-DriverAction,Get-Organizer,Get-DailyActivity,Remove-DailyActivity,Get-Transition,Save-Transition,Get-DataQuality,Resolve-Identity,Get-SafetyNote,Backup-Waa,Restore-Waa
