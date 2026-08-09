Set-StrictMode -Version Latest

function ConvertTo-WaaConversationSqlLiteral {
    param([AllowNull()]$Value)

    if ($null -eq $Value) { return 'NULL' }
    if ($Value -is [bool]) {
        if ($Value) { return '1' }
        return '0'
    }
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

function Initialize-WaaConversation {
    $schema = @'
CREATE TABLE IF NOT EXISTS driver_call_sessions(
  id INTEGER PRIMARY KEY,
  driver_id INTEGER NOT NULL REFERENCES drivers(id),
  cycle_key TEXT NOT NULL,
  opened_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  fuel_status TEXT NOT NULL DEFAULT 'Unknown',
  fuel_note TEXT,
  driver_eta TEXT,
  eta_status TEXT NOT NULL DEFAULT 'Unknown',
  eta_note TEXT,
  idle_plan TEXT,
  load_help_status TEXT NOT NULL DEFAULT 'Unknown',
  load_help_note TEXT,
  conversation_wrap TEXT,
  UNIQUE(driver_id,cycle_key)
);
CREATE INDEX IF NOT EXISTS idx_call_sessions_driver ON driver_call_sessions(driver_id,updated_at DESC);
'@
    Invoke-Sql $schema -AllowWrite | Out-Null
}

function Get-WaaConversationCycle {
    param([Parameter(Mandatory = $true)][int]$DriverId)

    $driver = Get-CurrentDriver $DriverId
    if ($null -eq $driver) { throw 'Driver not found' }

    $truck = [string]$driver.truck
    if ([string]::IsNullOrWhiteSpace($truck)) { $truck = 'NO-TRUCK' }

    $anchor = [string]$driver.pta_at
    if (-not [string]::IsNullOrWhiteSpace($anchor) -and $anchor.Length -ge 10) {
        $anchor = $anchor.Substring(0, 10)
    }
    else {
        $anchor = 'UNANCHORED'
    }

    return ($truck + '|' + $anchor)
}

function Get-WaaConversation {
    param([Parameter(Mandatory = $true)][int]$DriverId)

    Initialize-WaaConversation
    $cycle = Get-WaaConversationCycle -DriverId $DriverId
    $cycleSql = ConvertTo-WaaConversationSqlLiteral $cycle

    Invoke-Sql (
        "INSERT OR IGNORE INTO driver_call_sessions(driver_id,cycle_key) VALUES(" +
        "$DriverId,$cycleSql);"
    ) -AllowWrite | Out-Null

    $rows = @(
        Invoke-Sql (
            "SELECT * FROM driver_call_sessions WHERE driver_id=$DriverId AND cycle_key=$cycleSql LIMIT 1;"
        ) -Json
    )
    if ($rows.Count -eq 0) { throw 'Unable to open driver call session' }

    return $rows[0]
}

function Save-WaaConversation {
    param(
        [Parameter(Mandatory = $true)][int]$DriverId,
        [Parameter(Mandatory = $true)][hashtable]$Body
    )

    $field = [string]$Body.field
    $allowed = @(
        'fuel_status',
        'fuel_note',
        'driver_eta',
        'eta_status',
        'eta_note',
        'idle_plan',
        'load_help_status',
        'load_help_note',
        'conversation_wrap'
    )
    if ($allowed -notcontains $field) { throw 'Unknown conversation field' }

    $session = Get-WaaConversation -DriverId $DriverId
    $sessionId = [int]$session.id
    $valueSql = ConvertTo-WaaConversationSqlLiteral $Body.value

    Invoke-Sql "UPDATE driver_call_sessions SET $field=$valueSql,updated_at=CURRENT_TIMESTAMP WHERE id=$sessionId;" -AllowWrite | Out-Null

    $detail = @{
        field = $field
        session_id = $sessionId
        cycle_key = [string]$session.cycle_key
    } | ConvertTo-Json -Compress
    $detailSql = ConvertTo-WaaConversationSqlLiteral $detail
    Invoke-Sql "INSERT INTO audit_history(action,entity_type,entity_id,detail_json) VALUES('call_flow_update','driver','$DriverId',$detailSql);" -AllowWrite | Out-Null

    return Get-WaaConversation -DriverId $DriverId
}
