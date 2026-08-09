Set-StrictMode -Version Latest

function Get-WaaConversationCycle {
    param([Parameter(Mandatory = $true)][int]$DriverId)

    $driver = Get-CurrentDriver $DriverId
    if ($null -eq $driver) { throw 'Driver not found' }

    $truck = [string]$driver.truck
    if ([string]::IsNullOrWhiteSpace($truck)) { $truck = 'NO-TRUCK' }

    $anchor = 'UNANCHORED'
    $rawAnchor = [string]$driver.pta_at
    if (-not [string]::IsNullOrWhiteSpace($rawAnchor)) {
        $parsedAnchor = [datetime]::MinValue
        if ([datetime]::TryParse($rawAnchor, [ref]$parsedAnchor)) {
            $anchor = $parsedAnchor.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
        }
        elseif ($rawAnchor.Length -ge 10) { $anchor = $rawAnchor.Substring(0, 10) }
    }

    return ($truck + '|' + $anchor)
}

function Get-WaaConversation {
    param([Parameter(Mandatory = $true)][int]$DriverId)

    $cycle = Get-WaaConversationCycle -DriverId $DriverId
    $cycleSql = ConvertTo-SqlLiteral $cycle
    $rows = @(
        Invoke-Sql (
            "INSERT OR IGNORE INTO driver_call_sessions(driver_id,cycle_key) VALUES($DriverId,$cycleSql);" +
            "SELECT * FROM driver_call_sessions WHERE driver_id=$DriverId AND cycle_key=$cycleSql LIMIT 1;"
        ) -Json -AllowWrite
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
        'conversation_wrap',
        'completed_at'
    )
    if ($allowed -notcontains $field) { throw 'Unknown conversation field' }

    $session = Get-WaaConversation -DriverId $DriverId
    $sessionId = [int]$session.id
    $valueSql = if ($field -eq 'completed_at') {
        if ([bool]$Body.value) { 'CURRENT_TIMESTAMP' } else { 'NULL' }
    }
    else { ConvertTo-SqlLiteral $Body.value }

    $detail = @{
        field = $field
        session_id = $sessionId
        cycle_key = [string]$session.cycle_key
    } | ConvertTo-Json -Compress
    $detailSql = ConvertTo-SqlLiteral $detail
    $rows = @(Invoke-Sql "BEGIN;UPDATE driver_call_sessions SET $field=$valueSql,updated_at=CURRENT_TIMESTAMP WHERE id=$sessionId;INSERT INTO audit_history(action,entity_type,entity_id,detail_json) VALUES('call_flow_update','driver','$DriverId',$detailSql);COMMIT;SELECT * FROM driver_call_sessions WHERE id=$sessionId;" -Json -AllowWrite)
    return $rows[0]
}
