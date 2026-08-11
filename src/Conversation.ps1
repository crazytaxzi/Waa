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
    $session = Get-WaaLiveCall -DriverId $DriverId -CycleKey $cycle
    if ($null -ne $session) { return $session }
    # Opening a card is a read. Create the default session in LMDB without forcing
    # a synchronous SQLite write onto the UI request path.
    return Set-WaaLiveCallField -DriverId $DriverId -CycleKey $cycle -Field 'fuel_status' -Value 'Unknown' -NoAudit
}

function Get-WaaIdleSnapshot {
    param([Parameter(Mandatory = $true)][int]$DriverId)

    $rows = @(Invoke-Sql @"
SELECT period_end,
       CASE WHEN engine_hours=0 THEN NULL ELSE round(idle_hours*100.0/engine_hours,2) END idle_percent
FROM idle_periods
WHERE driver_id=$DriverId
ORDER BY period_end DESC,id DESC
LIMIT 1;
"@ -Json)
    if (-not $rows.Count) { return @{ percent=$null; period_end=$null } }
    return @{ percent=$rows[0].idle_percent; period_end=$rows[0].period_end }
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

    $cycle = Get-WaaConversationCycle -DriverId $DriverId
    if ($field -eq 'idle_plan') {
        if ([string]::IsNullOrWhiteSpace([string]$Body.value)) {
            return Set-WaaLiveCallField -DriverId $DriverId -CycleKey $cycle -Field $field -Value $Body.value -CaptureIdleSnapshot -IdlePercentSnapshot $null -IdlePeriodEndSnapshot $null
        }
        $snapshot = Get-WaaIdleSnapshot -DriverId $DriverId
        return Set-WaaLiveCallField -DriverId $DriverId -CycleKey $cycle -Field $field -Value $Body.value -CaptureIdleSnapshot -IdlePercentSnapshot $snapshot.percent -IdlePeriodEndSnapshot $snapshot.period_end
    }
    return Set-WaaLiveCallField -DriverId $DriverId -CycleKey $cycle -Field $field -Value $Body.value
}
