Set-StrictMode -Version Latest

function ConvertTo-WaaIdentitySqlLiteral {
    param([AllowNull()]$Value)
    if ($null -eq $Value) { return 'NULL' }
    if ($Value -is [bool]) { if ($Value) { return '1' } else { return '0' } }
    if ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int] -or $Value -is [long] -or
        $Value -is [single] -or $Value -is [double] -or $Value -is [decimal]) {
        return [Convert]::ToString($Value,[Globalization.CultureInfo]::InvariantCulture)
    }
    return "'" + ([string]$Value).Replace("'","''").Replace([string][char]0,'') + "'"
}

function Normalize-WaaDriverName {
    param([AllowNull()][string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return '' }
    return ([regex]::Replace($Name.Trim(),'\s+',' ')).Normalize([Text.NormalizationForm]::FormKC).ToUpperInvariant()
}

function Test-WaaDriverPlaceholder {
    param([AllowNull()][string]$Name,[AllowNull()][string]$PtaCode)
    $normalized = Normalize-WaaDriverName $Name
    if ([string]::IsNullOrWhiteSpace($normalized) -or $normalized -eq 'UNKNOWN') { return $true }
    if (-not [string]::IsNullOrWhiteSpace($PtaCode) -and $normalized -eq (Normalize-WaaDriverName $PtaCode)) { return $true }
    return $false
}

function Get-WaaPtaNameSignature {
    param([AllowNull()][string]$FullName)

    $parts = @(([string]$FullName).Trim() -split '\s+' | Where-Object { $_ })
    if ($parts.Count -lt 2) { return $null }

    $first = [Text.RegularExpressions.Regex]::Replace(
        $parts[0].Normalize([Text.NormalizationForm]::FormD),
        '\p{Mn}',
        ''
    )
    $first = [Text.RegularExpressions.Regex]::Replace($first.ToUpperInvariant(), '[^A-Z0-9]', '')

    $surnameParts = @($parts[1..($parts.Count - 1)] | Where-Object { $_.Length -gt 1 })
    if ($surnameParts.Count -eq 0) { return $null }

    $surname = ($surnameParts -join '')
    $surname = [Text.RegularExpressions.Regex]::Replace(
        $surname.Normalize([Text.NormalizationForm]::FormD),
        '\p{Mn}',
        ''
    )
    $surname = [Text.RegularExpressions.Regex]::Replace($surname.ToUpperInvariant(), '[^A-Z0-9]', '')
    if ([string]::IsNullOrWhiteSpace($first) -or [string]::IsNullOrWhiteSpace($surname)) { return $null }

    $surnamePrefix = $surname.Substring(0, [Math]::Min(7, $surname.Length))
    return @{
        surname_prefix = $surnamePrefix
        first_name = $first
        short_code = $surnamePrefix + $first.Substring(0, 1)
    }
}

function Normalize-WaaPtaCode {
    param([AllowNull()][string]$PtaCode)
    if ([string]::IsNullOrWhiteSpace($PtaCode)) { return '' }

    $normalized = [Text.RegularExpressions.Regex]::Replace(
        $PtaCode.Trim().Normalize([Text.NormalizationForm]::FormD),
        '\p{Mn}',
        ''
    )
    return [Text.RegularExpressions.Regex]::Replace($normalized.ToUpperInvariant(), '[^A-Z0-9]', '')
}

function Test-WaaPtaCodeMatchesName {
    param(
        [AllowNull()][string]$FullName,
        [AllowNull()][string]$PtaCode
    )

    $signature = Get-WaaPtaNameSignature $FullName
    if ($null -eq $signature) { return $false }

    $code = Normalize-WaaPtaCode $PtaCode
    if ([string]::IsNullOrWhiteSpace($code) -or $code.Length -gt 8) { return $false }

    $surnamePrefix = [string]$signature.surname_prefix
    if (-not $code.StartsWith($surnamePrefix, [StringComparison]::Ordinal)) { return $false }

    $suffix = $code.Substring($surnamePrefix.Length)
    if ([string]::IsNullOrWhiteSpace($suffix)) { return $false }

    $first = [string]$signature.first_name
    if ($suffix.Length -gt $first.Length) { return $false }
    return $first.StartsWith($suffix, [StringComparison]::Ordinal)
}

function Get-WaaDriverIdentitySql {
    param(
        [AllowNull()][string]$DispatchCode,
        [AllowNull()][string]$FullName,
        [switch]$SkipPtaLink
    )

    $dispatch = ([string]$DispatchCode).Trim()
    $name = [regex]::Replace(([string]$FullName).Trim(),'\s+',' ')
    if ([string]::IsNullOrWhiteSpace($dispatch) -or [string]::IsNullOrWhiteSpace($name)) { return '' }

    $pta = Convert-DriverCode $name
    $dispatchSql = ConvertTo-WaaIdentitySqlLiteral $dispatch
    $nameSql = ConvertTo-WaaIdentitySqlLiteral $name
    $ptaSql = ConvertTo-WaaIdentitySqlLiteral $pta
    $uniqueName = "(SELECT MIN(id) FROM drivers WHERE full_name=$nameSql COLLATE NOCASE HAVING COUNT(*)=1)"

    if ($SkipPtaLink -or [string]::IsNullOrWhiteSpace($pta)) {
        $target = "COALESCE((SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE LIMIT 1),$uniqueName)"
        $targetAfter = "COALESCE((SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE LIMIT 1),$uniqueName,(SELECT id FROM drivers WHERE full_name=$nameSql COLLATE NOCASE ORDER BY id DESC LIMIT 1))"
    }
    else {
        $safePtaAlias = "(SELECT a.driver_id FROM driver_aliases a JOIN drivers d ON d.id=a.driver_id WHERE a.alias_type='pta_code' AND a.alias_value=$ptaSql COLLATE NOCASE AND (d.full_name='Unknown' OR d.full_name=$ptaSql COLLATE NOCASE OR (d.full_name=$nameSql COLLATE NOCASE AND (SELECT count(*) FROM drivers n WHERE n.full_name=$nameSql COLLATE NOCASE)=1)) LIMIT 1)"
        $safePtaColumn = "(SELECT d.id FROM drivers d WHERE d.pta_code=$ptaSql COLLATE NOCASE AND (d.full_name='Unknown' OR d.full_name=$ptaSql COLLATE NOCASE OR (d.full_name=$nameSql COLLATE NOCASE AND (SELECT count(*) FROM drivers n WHERE n.full_name=$nameSql COLLATE NOCASE)=1)) LIMIT 1)"
        $target = "COALESCE((SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE LIMIT 1),$uniqueName,$safePtaAlias,$safePtaColumn)"
        $targetAfter = "COALESCE((SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE LIMIT 1),$uniqueName,$safePtaAlias,$safePtaColumn,(SELECT id FROM drivers WHERE full_name=$nameSql COLLATE NOCASE ORDER BY id DESC LIMIT 1))"
    }

    $sql = [Text.StringBuilder]::new(2600)
    [void]$sql.AppendLine("INSERT INTO drivers(full_name) SELECT $nameSql WHERE $target IS NULL;")
    [void]$sql.AppendLine("INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed) SELECT $targetAfter,'dispatch_code',$dispatchSql,0 WHERE $targetAfter IS NOT NULL ON CONFLICT(alias_type,alias_value) DO NOTHING;")
    if ([string]::IsNullOrWhiteSpace($pta)) {
        [void]$sql.AppendLine("UPDATE drivers SET full_name=$nameSql WHERE id=(SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE LIMIT 1) AND (full_name='Unknown' OR full_name=$dispatchSql COLLATE NOCASE);")
    }
    else {
        [void]$sql.AppendLine("UPDATE drivers SET full_name=$nameSql WHERE id=(SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE LIMIT 1) AND (full_name='Unknown' OR full_name=$dispatchSql COLLATE NOCASE OR full_name=$ptaSql COLLATE NOCASE);")
    }

    if (-not $SkipPtaLink -and -not [string]::IsNullOrWhiteSpace($pta)) {
        [void]$sql.AppendLine("INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed) SELECT (SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE LIMIT 1),'pta_code',$ptaSql,0 WHERE NOT EXISTS(SELECT 1 FROM driver_aliases WHERE alias_type='pta_code' AND alias_value=$ptaSql COLLATE NOCASE) AND NOT EXISTS(SELECT 1 FROM drivers d WHERE d.pta_code=$ptaSql COLLATE NOCASE AND d.id<>(SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE LIMIT 1)) ON CONFLICT(alias_type,alias_value) DO NOTHING;")
        [void]$sql.AppendLine("UPDATE drivers AS target SET pta_code=$ptaSql WHERE target.id=(SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE LIMIT 1) AND target.pta_code IS NULL AND NOT EXISTS(SELECT 1 FROM drivers d WHERE d.pta_code=$ptaSql COLLATE NOCASE AND d.id<>target.id);")
        [void]$sql.AppendLine("INSERT INTO identity_issues(alias_type,alias_value,issue_type,detail) SELECT 'pta_code',$ptaSql,'ambiguous','Derived PTA code is already owned by conflicting real-name evidence; automatic merge refused.' WHERE EXISTS(SELECT 1 FROM driver_aliases p JOIN drivers d ON d.id=p.driver_id WHERE p.alias_type='pta_code' AND p.alias_value=$ptaSql COLLATE NOCASE AND p.driver_id<>(SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE LIMIT 1) AND d.full_name<>'Unknown' AND d.full_name<>$ptaSql COLLATE NOCASE AND d.full_name<>$nameSql COLLATE NOCASE) AND NOT EXISTS(SELECT 1 FROM identity_issues WHERE status='open' AND alias_type='pta_code' AND alias_value=$ptaSql COLLATE NOCASE AND issue_type='ambiguous');")
    }

    return $sql.ToString()
}

function Get-WaaHeaderIndex {
    param([object[]]$Header,[string[]]$Names)
    for ($i=0; $i -lt $Header.Count; $i++) {
        $value = [regex]::Replace(([string]$Header[$i]).Trim().ToLowerInvariant(),'[^a-z0-9]','')
        foreach ($name in $Names) {
            if ($value -eq [regex]::Replace($name.Trim().ToLowerInvariant(),'[^a-z0-9]','')) { return $i }
        }
    }
    return -1
}

function Add-WaaIdentityEvidence {
    param(
        [Parameter(Mandatory=$true)]$List,
        [AllowNull()][string]$DispatchCode,
        [AllowNull()][string]$FullName,
        [AllowNull()][string]$Truck,
        [string]$Source
    )

    $code = ([string]$DispatchCode).Trim()
    $name = [regex]::Replace(([string]$FullName).Trim(),'\s+',' ')
    if ([string]::IsNullOrWhiteSpace($code) -or [string]::IsNullOrWhiteSpace($name)) { return }
    if ($code.Length -gt 6) { return }

    [void]$List.Add(@{
        dispatch_code = $code
        full_name = $name
        truck = ([string]$Truck).Trim()
        source = $Source
    })
}

function Get-WaaDriverIdentityEvidence {
    param([switch]$IncludeHistoricalRaw)

    $evidence = [Collections.Generic.List[object]]::new()

    # Raw report replay is a versioned migration path for legacy databases, not routine
    # startup work. Current imports normalize aliases as they are committed.
    if ($IncludeHistoricalRaw) {
        $imports = @(Invoke-Sql "SELECT id,import_type,raw_source FROM import_batches WHERE import_type IN ('idle','bol') ORDER BY id;" -Json)
        foreach ($import in $imports) {
            $rows = @(Split-ImportRows ([string]$import.raw_source))
            if ($rows.Count -lt 2) { continue }
            $header = @($rows[0])

            if ([string]$import.import_type -eq 'idle') {
                $group = Get-WaaHeaderIndex $header @('Group by  (copy)','Group by (copy)','Driver')
                $unit = Get-WaaHeaderIndex $header @('Unit Code','Unit','Truck')
                $measure = Get-WaaHeaderIndex $header @('Measure Names','Measure Name')
                if ($group -lt 0) { continue }

                for ($rowIndex=1; $rowIndex -lt $rows.Count; $rowIndex++) {
                    $row = @($rows[$rowIndex])
                    if ($group -ge $row.Count) { continue }
                    if ($measure -ge 0 -and $measure -lt $row.Count -and [string]$row[$measure] -ne 'Idle %') { continue }

                    $cell = [regex]::Replace(([string]$row[$group]).Trim(),'\s+',' ')
                    # Rolling 7-Day contract: <=6 character dispatch code, one boundary,
                    # then the complete driver name regardless of how many name parts follow.
                    if ($cell -notmatch '^([^\s]{1,6})\s+(.+?)\s*$') { continue }
                    $truck = if ($unit -ge 0 -and $unit -lt $row.Count) { [string]$row[$unit] } else { '' }
                    Add-WaaIdentityEvidence $evidence $Matches[1] $Matches[2] $truck 'rolling-7-day'
                }
            }
            else {
                $code = Get-WaaHeaderIndex $header @('Last Dispatch Driver cd','Last Dispatch Driver Code','Driver cd')
                $name = Get-WaaHeaderIndex $header @('Last Dispatch Driver nm','Last Dispatch Driver Name','Driver Name')
                if ($code -lt 0 -or $name -lt 0) { continue }
                for ($rowIndex=1; $rowIndex -lt $rows.Count; $rowIndex++) {
                    $row = @($rows[$rowIndex])
                    if ($code -ge $row.Count -or $name -ge $row.Count) { continue }
                    Add-WaaIdentityEvidence $evidence ([string]$row[$code]) ([string]$row[$name]) '' 'missing-bol'
                }
            }
        }
    }

    # Keep confirmed normalized database names as evidence even if an old raw import was removed.
    $known = @(Invoke-Sql "SELECT a.alias_value dispatch_code,d.full_name,d.pta_code FROM driver_aliases a JOIN drivers d ON d.id=a.driver_id WHERE a.alias_type='dispatch_code';" -Json)
    foreach ($row in $known) {
        if (-not (Test-WaaDriverPlaceholder ([string]$row.full_name) ([string]$row.pta_code))) {
            Add-WaaIdentityEvidence $evidence ([string]$row.dispatch_code) ([string]$row.full_name) '' 'database'
        }
    }

    return $evidence
}

function Merge-WaaDriverRecords {
    param([int]$WinnerId,[int]$LoserId,[string]$PtaCode)
    if ($WinnerId -le 0 -or $LoserId -le 0 -or $WinnerId -eq $LoserId) { return }

    $winner = @(Invoke-Sql "SELECT id,full_name FROM drivers WHERE id=$WinnerId;" -Json)
    $loser = @(Invoke-Sql "SELECT id,full_name FROM drivers WHERE id=$LoserId;" -Json)
    if ($winner.Count -eq 0 -or $loser.Count -eq 0) { return }

    $hasCalls = [int](Invoke-Sql "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='driver_call_sessions';") -gt 0
    $ptaSql = ConvertTo-WaaIdentitySqlLiteral $PtaCode
    $detailSql = ConvertTo-WaaIdentitySqlLiteral (@{
        winner=$WinnerId; loser=$LoserId; pta_code=$PtaCode
        winner_name=[string]$winner[0].full_name; loser_name=[string]$loser[0].full_name
    } | ConvertTo-Json -Compress)

    $sql = [Text.StringBuilder]::new(14000)
    [void]$sql.AppendLine('BEGIN IMMEDIATE;')
    [void]$sql.AppendLine("INSERT INTO idle_periods(driver_id,truck,period_start,period_end,engine_hours,idle_hours,import_batch_id) SELECT $WinnerId,truck,period_start,period_end,engine_hours,idle_hours,import_batch_id FROM idle_periods WHERE driver_id=$LoserId ON CONFLICT(driver_id,period_start,period_end) DO UPDATE SET truck=excluded.truck,engine_hours=excluded.engine_hours,idle_hours=excluded.idle_hours,import_batch_id=excluded.import_batch_id;")
    [void]$sql.AppendLine("DELETE FROM idle_periods WHERE driver_id=$LoserId;")
    foreach ($table in @('pta_observations','truck_history','missing_bols','driver_notes','reminders','timers')) {
        [void]$sql.AppendLine("UPDATE $table SET driver_id=$WinnerId WHERE driver_id=$LoserId;")
    }

    [void]$sql.AppendLine(@"
UPDATE driver_work_items
SET cycle_key=CASE WHEN cycle_key IS NULL OR cycle_key='' THEN (SELECT cycle_key FROM driver_work_items WHERE driver_id=$LoserId) ELSE cycle_key END,
    home_checked=max(home_checked,coalesce((SELECT home_checked FROM driver_work_items WHERE driver_id=$LoserId),0)),
    expected_work=CASE WHEN expected_work='Unknown' THEN coalesce((SELECT expected_work FROM driver_work_items WHERE driver_id=$LoserId),'Unknown') ELSE expected_work END,
    home_status=CASE WHEN home_status='Unknown' THEN coalesce((SELECT home_status FROM driver_work_items WHERE driver_id=$LoserId),'Unknown') ELSE home_status END,
    home_reason=coalesce(nullif(home_reason,''),(SELECT home_reason FROM driver_work_items WHERE driver_id=$LoserId)),
    ontime_status=CASE WHEN ontime_status='Unknown' THEN coalesce((SELECT ontime_status FROM driver_work_items WHERE driver_id=$LoserId),'Unknown') ELSE ontime_status END,
    ontime_reason=coalesce(nullif(ontime_reason,''),(SELECT ontime_reason FROM driver_work_items WHERE driver_id=$LoserId)),
    ontime_checked_at=coalesce(ontime_checked_at,(SELECT ontime_checked_at FROM driver_work_items WHERE driver_id=$LoserId)),
    preplan_reviewed=max(preplan_reviewed,coalesce((SELECT preplan_reviewed FROM driver_work_items WHERE driver_id=$LoserId),0)),
    preplan_response=CASE WHEN preplan_response='Unknown' THEN coalesce((SELECT preplan_response FROM driver_work_items WHERE driver_id=$LoserId),'Unknown') ELSE preplan_response END,
    preplan_note=coalesce(nullif(preplan_note,''),(SELECT preplan_note FROM driver_work_items WHERE driver_id=$LoserId)),
    routing_checked=max(routing_checked,coalesce((SELECT routing_checked FROM driver_work_items WHERE driver_id=$LoserId),0)),
    routing_status=CASE WHEN routing_status='Unknown' THEN coalesce((SELECT routing_status FROM driver_work_items WHERE driver_id=$LoserId),'Unknown') ELSE routing_status END,
    routing_note=coalesce(nullif(routing_note,''),(SELECT routing_note FROM driver_work_items WHERE driver_id=$LoserId)),
    safety_note_id=coalesce(safety_note_id,(SELECT safety_note_id FROM driver_work_items WHERE driver_id=$LoserId)),
    safety_mentioned_at=coalesce(safety_mentioned_at,(SELECT safety_mentioned_at FROM driver_work_items WHERE driver_id=$LoserId)),
    include_transition=max(include_transition,coalesce((SELECT include_transition FROM driver_work_items WHERE driver_id=$LoserId),0)),
    transition_note=coalesce(nullif(transition_note,''),(SELECT transition_note FROM driver_work_items WHERE driver_id=$LoserId)),
    updated_at=CURRENT_TIMESTAMP
WHERE driver_id=$WinnerId AND EXISTS(SELECT 1 FROM driver_work_items WHERE driver_id=$LoserId);
DELETE FROM driver_work_items WHERE driver_id=$LoserId AND EXISTS(SELECT 1 FROM driver_work_items WHERE driver_id=$WinnerId);
UPDATE driver_work_items SET driver_id=$WinnerId WHERE driver_id=$LoserId;
"@)

    if ($hasCalls) {
        [void]$sql.AppendLine(@"
UPDATE driver_call_sessions AS w
SET fuel_status=CASE WHEN w.fuel_status='Unknown' THEN coalesce((SELECT l.fuel_status FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key),'Unknown') ELSE w.fuel_status END,
    fuel_note=coalesce(nullif(w.fuel_note,''),(SELECT l.fuel_note FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key)),
    driver_eta=coalesce(nullif(w.driver_eta,''),(SELECT l.driver_eta FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key)),
    eta_status=CASE WHEN w.eta_status='Unknown' THEN coalesce((SELECT l.eta_status FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key),'Unknown') ELSE w.eta_status END,
    eta_note=coalesce(nullif(w.eta_note,''),(SELECT l.eta_note FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key)),
    idle_plan=coalesce(nullif(w.idle_plan,''),(SELECT l.idle_plan FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key)),
    load_help_status=CASE WHEN w.load_help_status='Unknown' THEN coalesce((SELECT l.load_help_status FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key),'Unknown') ELSE w.load_help_status END,
    load_help_note=coalesce(nullif(w.load_help_note,''),(SELECT l.load_help_note FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key)),
    conversation_wrap=coalesce(nullif(w.conversation_wrap,''),(SELECT l.conversation_wrap FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key)),
    completed_at=coalesce(w.completed_at,(SELECT l.completed_at FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key)),
    updated_at=CURRENT_TIMESTAMP
WHERE w.driver_id=$WinnerId AND EXISTS(SELECT 1 FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key);
DELETE FROM driver_call_sessions WHERE driver_id=$LoserId AND cycle_key IN (SELECT cycle_key FROM driver_call_sessions WHERE driver_id=$WinnerId);
UPDATE driver_call_sessions SET driver_id=$WinnerId WHERE driver_id=$LoserId;
"@)
    }

    [void]$sql.AppendLine("DELETE FROM driver_aliases WHERE driver_id=$LoserId AND EXISTS(SELECT 1 FROM driver_aliases w WHERE w.driver_id=$WinnerId AND w.alias_type=driver_aliases.alias_type AND w.alias_value=driver_aliases.alias_value COLLATE NOCASE);")
    [void]$sql.AppendLine("UPDATE driver_aliases SET driver_id=$WinnerId WHERE driver_id=$LoserId;")
    [void]$sql.AppendLine("UPDATE audit_history SET entity_id='$WinnerId' WHERE entity_type='driver' AND entity_id='$LoserId';")
    [void]$sql.AppendLine("UPDATE drivers SET pta_code=NULL WHERE id=$LoserId;")
    [void]$sql.AppendLine("UPDATE drivers SET pta_code=$ptaSql WHERE id=$WinnerId AND (pta_code IS NULL OR pta_code=$ptaSql COLLATE NOCASE);")
    [void]$sql.AppendLine("INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed) VALUES($WinnerId,'pta_code',$ptaSql,0) ON CONFLICT(alias_type,alias_value) DO UPDATE SET driver_id=$WinnerId;")
    [void]$sql.AppendLine("UPDATE identity_issues SET status='resolved' WHERE status='open' AND alias_type='pta_code' AND alias_value=$ptaSql COLLATE NOCASE;")
    [void]$sql.AppendLine("DELETE FROM drivers WHERE id=$LoserId;")
    [void]$sql.AppendLine("INSERT INTO audit_history(action,entity_type,entity_id,detail_json) VALUES('identity_merged','identity','$WinnerId',$detailSql);")
    [void]$sql.AppendLine('COMMIT;')
    Invoke-Sql $sql.ToString() -AllowWrite | Out-Null
}

function Set-WaaObservedPtaCode {
    param(
        [Parameter(Mandatory=$true)][int]$DriverId,
        [Parameter(Mandatory=$true)][string]$PtaCode
    )

    if ($DriverId -le 0) { return $false }
    $driverRows = @(Invoke-Sql "SELECT id,full_name,pta_code,EXISTS(SELECT 1 FROM driver_aliases a WHERE a.driver_id=drivers.id AND a.alias_type='pta_code' AND a.alias_value=$(ConvertTo-WaaIdentitySqlLiteral (Normalize-WaaPtaCode $PtaCode)) COLLATE NOCASE) observed_alias FROM drivers WHERE id=$DriverId;" -Json)
    if ($driverRows.Count -eq 0) { return $false }

    $driver = $driverRows[0]
    $actual = Normalize-WaaPtaCode $PtaCode
    if (-not (Test-WaaPtaCodeMatchesName ([string]$driver.full_name) $actual)) { return $false }
    if ((Normalize-WaaPtaCode ([string]$driver.pta_code)) -eq $actual -and [int]$driver.observed_alias -eq 1) { return $false }

    $actualSql = ConvertTo-WaaIdentitySqlLiteral $actual
    $conflicts = [int](Invoke-Sql @"
SELECT count(*) FROM (
  SELECT id FROM drivers WHERE id<>$DriverId AND pta_code=$actualSql COLLATE NOCASE
  UNION
  SELECT driver_id id FROM driver_aliases
  WHERE driver_id<>$DriverId AND alias_type='pta_code' AND alias_value=$actualSql COLLATE NOCASE
);
"@)
    if ($conflicts -gt 0) { return $false }

    $signature = Get-WaaPtaNameSignature ([string]$driver.full_name)
    $shortCode = if ($null -ne $signature) { [string]$signature.short_code } else { '' }
    $cleanupSql = ''
    if (-not [string]::IsNullOrWhiteSpace($shortCode) -and $shortCode -ne $actual) {
        $shortSql = ConvertTo-WaaIdentitySqlLiteral $shortCode
        $cleanupSql = @"
DELETE FROM driver_aliases
WHERE driver_id=$DriverId AND alias_type='pta_code' AND confirmed=0
  AND alias_value=$shortSql COLLATE NOCASE
  AND NOT EXISTS(
    SELECT 1 FROM pta_observations p
    WHERE p.driver_code=driver_aliases.alias_value COLLATE NOCASE
  );
"@
    }

    Invoke-Sql @"
BEGIN IMMEDIATE;
UPDATE drivers SET pta_code=$actualSql WHERE id=$DriverId;
INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed)
VALUES($DriverId,'pta_code',$actualSql,0)
ON CONFLICT(alias_type,alias_value) DO UPDATE SET driver_id=excluded.driver_id;
$cleanupSql
UPDATE identity_issues SET status='resolved'
WHERE status='open' AND alias_type='pta_code' AND alias_value=$actualSql COLLATE NOCASE;
COMMIT;
"@ -AllowWrite | Out-Null
    return $true
}

function Repair-WaaDriverIdentity {
    param([switch]$IncludeHistoricalRaw)

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $evidence = @(Get-WaaDriverIdentityEvidence -IncludeHistoricalRaw:$IncludeHistoricalRaw)
    $byDispatch = @{}
    foreach ($item in $evidence) {
        $key = ([string]$item.dispatch_code).ToUpperInvariant()
        if (-not $byDispatch.ContainsKey($key)) { $byDispatch[$key] = [Collections.Generic.List[object]]::new() }
        [void]$byDispatch[$key].Add($item)
    }

    # Pre-compute derived PTA collisions across all current exact report evidence. If two
    # different real names derive to the same eight-character PTA code, WAA will still keep
    # their dispatch/name identities but will not attach that PTA code to either automatically.
    $ptaNames = @{}
    foreach ($entry in $byDispatch.GetEnumerator()) {
        $items = @($entry.Value)
        $names = @($items | ForEach-Object { Normalize-WaaDriverName ([string]$_.full_name) } | Where-Object { $_ } | Select-Object -Unique)
        if ($names.Count -ne 1) { continue }
        $pta = Convert-DriverCode ([string]$items[-1].full_name)
        if ([string]::IsNullOrWhiteSpace($pta)) { continue }
        $key = $pta.ToUpperInvariant()
        if (-not $ptaNames.ContainsKey($key)) { $ptaNames[$key] = [Collections.Generic.List[string]]::new() }
        if (-not $ptaNames[$key].Contains([string]$names[0])) { [void]$ptaNames[$key].Add([string]$names[0]) }
    }

    $sql = [Text.StringBuilder]::new([Math]::Max(8192,$byDispatch.Count * 1800))
    [void]$sql.AppendLine('BEGIN IMMEDIATE;')
    $ambiguous = 0
    $evidenceCount = 0
    $assignmentLinks = 0

    foreach ($entry in $byDispatch.GetEnumerator()) {
        $items = @($entry.Value)
        $names = @($items | ForEach-Object { Normalize-WaaDriverName ([string]$_.full_name) } | Where-Object { $_ } | Select-Object -Unique)
        $dispatch = [string]$entry.Key
        $dispatchSql = ConvertTo-WaaIdentitySqlLiteral $dispatch

        if ($names.Count -ne 1) {
            $candidateSql = ConvertTo-WaaIdentitySqlLiteral (($items | ForEach-Object { [string]$_.full_name } | Select-Object -Unique) | ConvertTo-Json -Compress)
            [void]$sql.AppendLine("INSERT INTO identity_issues(alias_type,alias_value,issue_type,candidates_json,detail) SELECT 'dispatch_code',$dispatchSql,'ambiguous',$candidateSql,'The same dispatch code has conflicting full-name evidence; automatic merge refused.' WHERE NOT EXISTS(SELECT 1 FROM identity_issues WHERE status='open' AND alias_type='dispatch_code' AND alias_value=$dispatchSql COLLATE NOCASE AND issue_type='ambiguous');")
            $ambiguous++
            continue
        }

        $selected = $items[-1]
        $name = [regex]::Replace(([string]$selected.full_name).Trim(),'\s+',' ')
        $pta = Convert-DriverCode $name
        if ([string]::IsNullOrWhiteSpace($pta)) { continue }
        $ptaKey = $pta.ToUpperInvariant()
        $ptaCollision = $ptaNames.ContainsKey($ptaKey) -and $ptaNames[$ptaKey].Count -gt 1

        if ($ptaCollision) {
            [void]$sql.AppendLine((Get-WaaDriverIdentitySql -DispatchCode $dispatch -FullName $name -SkipPtaLink))
            $ptaSql = ConvertTo-WaaIdentitySqlLiteral $pta
            $candidateSql = ConvertTo-WaaIdentitySqlLiteral (($ptaNames[$ptaKey] | ConvertTo-Json -Compress))
            [void]$sql.AppendLine("INSERT INTO identity_issues(alias_type,alias_value,issue_type,candidates_json,detail) SELECT 'pta_code',$ptaSql,'ambiguous',$candidateSql,'Multiple real driver names derive to the same PTA code; automatic merge refused.' WHERE NOT EXISTS(SELECT 1 FROM identity_issues WHERE status='open' AND alias_type='pta_code' AND alias_value=$ptaSql COLLATE NOCASE AND issue_type='ambiguous');")
            $ambiguous++
        }
        else {
            [void]$sql.AppendLine((Get-WaaDriverIdentitySql -DispatchCode $dispatch -FullName $name))
        }
        $evidenceCount++
    }
    [void]$sql.AppendLine('COMMIT;')
    Invoke-Sql $sql.ToString() -AllowWrite | Out-Null

    # Second pass handles databases made by old versions where a named Rolling/BOL driver
    # and a PTA-only placeholder already exist as separate records.
    $drivers = @(Invoke-Sql 'SELECT id,full_name,pta_code FROM drivers ORDER BY id;' -Json)
    $aliases = @(Invoke-Sql "SELECT driver_id,alias_type,alias_value FROM driver_aliases WHERE alias_type IN ('dispatch_code','pta_code');" -Json)
    $driverById = @{}; $dispatchCount = @{}; $ptaOwners = @{}
    foreach ($driver in $drivers) { $driverById[[int]$driver.id] = $driver }
    foreach ($alias in $aliases) {
        $id = [int]$alias.driver_id
        if ([string]$alias.alias_type -eq 'dispatch_code') {
            if (-not $dispatchCount.ContainsKey($id)) { $dispatchCount[$id] = 0 }
            $dispatchCount[$id]++
        }
        else {
            $key = ([string]$alias.alias_value).ToUpperInvariant()
            if (-not $ptaOwners.ContainsKey($key)) { $ptaOwners[$key] = [Collections.Generic.List[int]]::new() }
            if (-not $ptaOwners[$key].Contains($id)) { [void]$ptaOwners[$key].Add($id) }
        }
    }
    foreach ($driver in $drivers) {
        if (-not [string]::IsNullOrWhiteSpace([string]$driver.pta_code)) {
            $key = ([string]$driver.pta_code).ToUpperInvariant()
            if (-not $ptaOwners.ContainsKey($key)) { $ptaOwners[$key] = [Collections.Generic.List[int]]::new() }
            if (-not $ptaOwners[$key].Contains([int]$driver.id)) { [void]$ptaOwners[$key].Add([int]$driver.id) }
        }
    }

    $namedByPta = @{}
    foreach ($driver in $drivers) {
        if (Test-WaaDriverPlaceholder ([string]$driver.full_name) ([string]$driver.pta_code)) { continue }
        $pta = Convert-DriverCode ([string]$driver.full_name)
        if ([string]::IsNullOrWhiteSpace($pta)) { continue }
        $key = $pta.ToUpperInvariant()
        if (-not $namedByPta.ContainsKey($key)) { $namedByPta[$key] = [Collections.Generic.List[int]]::new() }
        [void]$namedByPta[$key].Add([int]$driver.id)
    }

    $merged = 0
    foreach ($entry in $namedByPta.GetEnumerator()) {
        $pta = [string]$entry.Key
        $namedIds = @($entry.Value | Select-Object -Unique)
        if ($namedIds.Count -ne 1) {
            $ptaSql = ConvertTo-WaaIdentitySqlLiteral $pta
            [void](Invoke-Sql "INSERT INTO identity_issues(alias_type,alias_value,issue_type,detail) SELECT 'pta_code',$ptaSql,'ambiguous','Multiple real driver names derive to the same PTA code; automatic merge refused.' WHERE NOT EXISTS(SELECT 1 FROM identity_issues WHERE status='open' AND alias_type='pta_code' AND alias_value=$ptaSql COLLATE NOCASE AND issue_type='ambiguous');" -AllowWrite)
            continue
        }

        $winnerId = [int]$namedIds[0]
        $owners = if ($ptaOwners.ContainsKey($pta)) { @($ptaOwners[$pta] | Select-Object -Unique) } else { @() }
        foreach ($owner in $owners) {
            $loserId = [int]$owner
            if ($loserId -eq $winnerId -or -not $driverById.ContainsKey($loserId)) { continue }
            $loser = $driverById[$loserId]
            $loserDispatches = if ($dispatchCount.ContainsKey($loserId)) { [int]$dispatchCount[$loserId] } else { 0 }
            if (Test-WaaDriverPlaceholder ([string]$loser.full_name) $pta -and $loserDispatches -eq 0) {
                Merge-WaaDriverRecords $winnerId $loserId $pta
                $merged++
            }
        }
    }

    # Reconcile observed PTA-code families to a real name without depending on the
    # driver's current truck. Some valid codes extend the usual surname + first initial
    # form (JONESI -> JONESIRA). A code is safe only when exactly one real driver record
    # is structurally compatible with it; placeholder owners with no dispatch identity
    # are then merged into that driver. Ambiguous prefixes remain untouched.
    $familyDrivers = @(Invoke-Sql 'SELECT id,full_name,pta_code FROM drivers ORDER BY id;' -Json)
    $familyAliases = @(Invoke-Sql "SELECT driver_id,alias_value FROM driver_aliases WHERE alias_type='pta_code';" -Json)
    $familyDispatchCounts = @{}
    foreach($row in @(Invoke-Sql "SELECT driver_id,count(*) alias_count FROM driver_aliases WHERE alias_type='dispatch_code' GROUP BY driver_id;" -Json)){$familyDispatchCounts[[int]$row.driver_id]=[int]$row.alias_count}
    $observedCodeCandidates = @($familyDrivers | ForEach-Object { Normalize-WaaPtaCode ([string]$_.pta_code) }) +
                              @($familyAliases | ForEach-Object { Normalize-WaaPtaCode ([string]$_.alias_value) })
    $observedCodes = @($observedCodeCandidates | Where-Object { $_ } | Sort-Object Length,@{Expression={$_}} -Unique)
    foreach($actualCode in $observedCodes){
        $compatible=@($familyDrivers|Where-Object{-not(Test-WaaDriverPlaceholder ([string]$_.full_name) ([string]$_.pta_code))-and(Test-WaaPtaCodeMatchesName ([string]$_.full_name) $actualCode)})
        $compatibleIds=@($compatible|ForEach-Object{[int]$_.id}|Select-Object -Unique)
        if($compatibleIds.Count-ne1){continue}
        $winnerId=[int]$compatibleIds[0]
        $ownerCandidates = @($familyDrivers | Where-Object { (Normalize-WaaPtaCode ([string]$_.pta_code)) -eq $actualCode } | ForEach-Object { [int]$_.id }) +
                           @($familyAliases | Where-Object { (Normalize-WaaPtaCode ([string]$_.alias_value)) -eq $actualCode } | ForEach-Object { [int]$_.driver_id })
        $owners = @($ownerCandidates | Select-Object -Unique)
        foreach($ownerId in $owners){
            $loserId=[int]$ownerId
            if($loserId-eq$winnerId){continue}
            $loser=@($familyDrivers|Where-Object{[int]$_.id-eq$loserId})
            if($loser.Count-ne1){continue}
            $dispatches=if($familyDispatchCounts.ContainsKey($loserId)){$familyDispatchCounts[$loserId]}else{0}
            if((Test-WaaDriverPlaceholder ([string]$loser[0].full_name) ([string]$loser[0].pta_code))-and$dispatches-eq0){
                Merge-WaaDriverRecords $winnerId $loserId $actualCode
                $merged++
            }
        }
        if(Set-WaaObservedPtaCode -DriverId $winnerId -PtaCode $actualCode){$assignmentLinks++}
    }

    # Current unit is corroborating snapshot evidence, never permanent driver identity.
    # It may bridge a real Rolling/BOL identity to a PTA-only identity only when both
    # latest report snapshots are one-to-one on the same unit and the observed PTA code
    # is structurally compatible with the driver's full name. Any ambiguity refuses merge.
    $latestIdleAssignments = @(Invoke-Sql @'
WITH latest_batch AS (
  SELECT max(id) id FROM import_batches WHERE import_type='idle'
), ranked AS (
  SELECT i.driver_id,i.truck,i.period_end,i.id,
         row_number() OVER(PARTITION BY i.driver_id ORDER BY i.period_end DESC,i.id DESC) rn
  FROM idle_periods i
  WHERE i.import_batch_id=(SELECT id FROM latest_batch)
    AND i.driver_id IS NOT NULL AND trim(coalesce(i.truck,''))<>''
)
SELECT r.driver_id,r.truck,d.full_name,d.pta_code
FROM ranked r
JOIN drivers d ON d.id=r.driver_id
WHERE r.rn=1;
'@ -Json)

    $latestPtaAssignments = @(Invoke-Sql @'
WITH latest_batch AS (
  SELECT max(id) id FROM import_batches WHERE import_type='pta'
)
SELECT DISTINCT p.driver_id,p.truck,p.driver_code,d.full_name,d.pta_code,
       (SELECT count(*) FROM driver_aliases a
        WHERE a.driver_id=p.driver_id AND a.alias_type='dispatch_code') dispatch_count
FROM pta_observations p
JOIN drivers d ON d.id=p.driver_id
WHERE p.import_batch_id=(SELECT id FROM latest_batch)
  AND p.driver_id IS NOT NULL
  AND trim(coalesce(p.truck,''))<>''
  AND trim(coalesce(p.driver_code,''))<>'';
'@ -Json)

    $idleByTruck = @{}
    foreach ($row in $latestIdleAssignments) {
        $truck = ([string]$row.truck).Trim().ToUpperInvariant()
        if ([string]::IsNullOrWhiteSpace($truck)) { continue }
        if (-not $idleByTruck.ContainsKey($truck)) { $idleByTruck[$truck] = [Collections.Generic.List[object]]::new() }
        if (@($idleByTruck[$truck] | Where-Object { [int]$_.driver_id -eq [int]$row.driver_id }).Count -eq 0) {
            [void]$idleByTruck[$truck].Add($row)
        }
    }

    $ptaByTruck = @{}
    $ptaCodeTrucks = @{}
    foreach ($row in $latestPtaAssignments) {
        $truck = ([string]$row.truck).Trim().ToUpperInvariant()
        $code = Normalize-WaaPtaCode ([string]$row.driver_code)
        if ([string]::IsNullOrWhiteSpace($truck) -or [string]::IsNullOrWhiteSpace($code)) { continue }

        if (-not $ptaByTruck.ContainsKey($truck)) { $ptaByTruck[$truck] = [Collections.Generic.List[object]]::new() }
        if (@($ptaByTruck[$truck] | Where-Object { [int]$_.driver_id -eq [int]$row.driver_id -and (Normalize-WaaPtaCode ([string]$_.driver_code)) -eq $code }).Count -eq 0) {
            [void]$ptaByTruck[$truck].Add($row)
        }

        if (-not $ptaCodeTrucks.ContainsKey($code)) { $ptaCodeTrucks[$code] = [Collections.Generic.List[string]]::new() }
        if (-not $ptaCodeTrucks[$code].Contains($truck)) { [void]$ptaCodeTrucks[$code].Add($truck) }
    }

    foreach ($truck in @($idleByTruck.Keys)) {
        if (-not $ptaByTruck.ContainsKey($truck)) { continue }

        $idleCandidates = @($idleByTruck[$truck])
        $ptaCandidates = @($ptaByTruck[$truck])
        $ptaCodes = @($ptaCandidates | ForEach-Object { Normalize-WaaPtaCode ([string]$_.driver_code) } | Where-Object { $_ } | Select-Object -Unique)
        if ($idleCandidates.Count -ne 1 -or $ptaCandidates.Count -ne 1 -or $ptaCodes.Count -ne 1) { continue }

        $named = $idleCandidates[0]
        $ptaRow = $ptaCandidates[0]
        $actualCode = [string]$ptaCodes[0]
        if (-not $ptaCodeTrucks.ContainsKey($actualCode) -or $ptaCodeTrucks[$actualCode].Count -ne 1) { continue }
        if (Test-WaaDriverPlaceholder ([string]$named.full_name) ([string]$named.pta_code)) { continue }
        if (-not (Test-WaaPtaCodeMatchesName ([string]$named.full_name) $actualCode)) { continue }

        $winnerId = [int]$named.driver_id
        $loserId = [int]$ptaRow.driver_id
        if ($winnerId -eq $loserId) {
            if (Set-WaaObservedPtaCode -DriverId $winnerId -PtaCode $actualCode) { $assignmentLinks++ }
            continue
        }

        $sameName = (Normalize-WaaDriverName ([string]$named.full_name)) -eq (Normalize-WaaDriverName ([string]$ptaRow.full_name))
        $placeholder = Test-WaaDriverPlaceholder ([string]$ptaRow.full_name) $actualCode
        $safeLoser = $sameName -or ($placeholder -and [int]$ptaRow.dispatch_count -eq 0)
        if (-not $safeLoser) { continue }

        Merge-WaaDriverRecords $winnerId $loserId $actualCode
        if (Set-WaaObservedPtaCode -DriverId $winnerId -PtaCode $actualCode) { $assignmentLinks++ }
    }

    # Unit Code is assignment evidence. Backfill the standard assignment history from idle
    # measurements so the Driver/Workflow view does not display Unknown when Rolling already
    # supplied the unit. Truck is never used to decide who a driver is.
    Invoke-Sql @'
INSERT INTO truck_history(driver_id,truck,observed_at,import_batch_id,source)
SELECT i.driver_id,i.truck,coalesce(b.imported_at,CURRENT_TIMESTAMP),i.import_batch_id,'idle'
FROM idle_periods i
LEFT JOIN import_batches b ON b.id=i.import_batch_id
WHERE i.driver_id IS NOT NULL AND trim(coalesce(i.truck,''))<>''
AND NOT EXISTS(
  SELECT 1 FROM truck_history t
  WHERE t.driver_id=i.driver_id AND t.truck=i.truck
    AND coalesce(t.import_batch_id,-1)=coalesce(i.import_batch_id,-1) AND t.source='idle'
);
'@ -AllowWrite | Out-Null

    $watch.Stop()
    return @{ evidence=$evidenceCount; merged=$merged; assignment_links=$assignmentLinks; ambiguous=$ambiguous; elapsed_ms=[math]::Round($watch.Elapsed.TotalMilliseconds,1) }
}

function Invoke-WaaIdentityRepairIfNeeded {
    param([switch]$DataChanged)

    $version = '4'
    $priorVersion = [string](Invoke-Sql "SELECT value FROM settings WHERE key='identity_repair_version';")
    $migrationRequired = $priorVersion -ne $version
    if (-not $DataChanged -and -not $migrationRequired) {
        return @{ skipped=$true; version=$version; evidence=0; merged=0; assignment_links=0; ambiguous=0; elapsed_ms=0 }
    }

    $result = Repair-WaaDriverIdentity -IncludeHistoricalRaw:$migrationRequired
    $versionSql = ConvertTo-WaaIdentitySqlLiteral $version
    Invoke-Sql "INSERT INTO settings(key,value,updated_at) VALUES('identity_repair_version',$versionSql,CURRENT_TIMESTAMP) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=CURRENT_TIMESTAMP;" -AllowWrite | Out-Null
    $result['skipped'] = $false
    $result['version'] = $version
    return $result
}
