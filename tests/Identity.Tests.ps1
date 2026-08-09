[CmdletBinding()]
param([string]$SqlitePath)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($SqlitePath) { $env:WAA_SQLITE_TEST = $SqlitePath }

Import-Module (Join-Path $root 'src/Waa.psm1') -Force
. (Join-Path $root 'src/ReportParsing.ps1')
. (Join-Path $root 'src/ReportIntake.ps1')
. (Join-Path $root 'src/Conversation.ps1')

function Assert-Identity($Condition,[string]$Message) {
    if (-not $Condition) { throw "IDENTITY TEST FAILED: $Message" }
    Write-Host "PASS $Message" -ForegroundColor Green
}

$data = Join-Path ([IO.Path]::GetTempPath()) ('waa-identity-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($data) | Out-Null
try {
    Initialize-Waa $root $data | Out-Null
    Initialize-WaaReportIntake $data

    # Rolling first: Group by (copy) splits once only. RATB + full name + derived PTA + unit
    # must become one canonical driver before a later PTA paste arrives.
    $rolling = @'
Group by  (copy)	Unit Code	Week Start Date	Rolling 7 Day Start Date	[Rolling 7 Day Engine Time]/60	[Rolling 7 Day Idle Time]/60	Measure Names
RATB Bruce D Ratcliff	221678	08/09/2026	08/03/2026	50	5	Idle %
'@ -replace '\t',"`t"
    Import-WaaManagedReport -Canonical $rolling -Filename 'rolling-ratcliff.csv' -Type idle | Out-Null
    Repair-WaaDriverIdentity | Out-Null

    $rat = @(Invoke-Sql @'
SELECT d.id,d.full_name,d.pta_code,
       max(CASE WHEN a.alias_type='dispatch_code' THEN a.alias_value END) dispatch_code,
       max(CASE WHEN a.alias_type='pta_code' THEN a.alias_value END) pta_alias
FROM drivers d LEFT JOIN driver_aliases a ON a.driver_id=d.id
WHERE d.full_name='Bruce D Ratcliff'
GROUP BY d.id,d.full_name,d.pta_code;
'@ -Json)
    Assert-Identity ($rat.Count -eq 1 -and $rat[0].dispatch_code -eq 'RATB' -and $rat[0].pta_code -eq 'RATCLIFB' -and $rat[0].pta_alias -eq 'RATCLIFB') 'RATB, Bruce D Ratcliff, and RATCLIFB are one canonical identity'
    $ratId = [int]$rat[0].id
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM truck_history WHERE driver_id=$ratId AND truck='221678';" -Json)[0].c -ge 1) 'Rolling Unit Code becomes assignment history'

    $pta = @'
| Truck | Division | Driver Code | PTA | Operational Status | Planning Status | Operational Note | Driver Type | Location | N1 | N2 |
|---|---|---|---|---|---|---|---|---|---|---|
| 221678 | 305 | RATCLIFB | 08/11/26 15:30 | Available | Preplan | SWAP | Solo | SPO | 1 | 0 |
'@
    Import-WaaData $pta 'pta-ratcliff.txt' pta | Out-Null
    Repair-WaaDriverIdentity | Out-Null
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM pta_observations WHERE driver_id=$ratId AND truck='221678';" -Json)[0].c -eq 1) 'later PTA attaches to the existing Ratcliff driver'

    # PTA first: old placeholder must be upgraded/merged when exact rolling evidence arrives.
    $elyPta = @'
| Truck | Division | Driver Code | PTA | Operational Status | Planning Status | Operational Note | Driver Type | Location | N1 | N2 |
|---|---|---|---|---|---|---|---|---|---|---|
| 251650 | 005 | ELYB | 08/12/26 09:00 | Loaded | Preplan | SWAP | Solo | LAX | 2 | 0 |
'@
    Import-WaaData $elyPta 'pta-ely.txt' pta | Out-Null
    $elyRolling = @'
Group by  (copy)	Unit Code	Week Start Date	Rolling 7 Day Start Date	[Rolling 7 Day Engine Time]/60	[Rolling 7 Day Idle Time]/60	Measure Names
ELY2 Bruce Ely	251650	08/09/2026	08/03/2026	40	4	Idle %
'@ -replace '\t',"`t"
    Import-WaaManagedReport -Canonical $elyRolling -Filename 'rolling-ely.csv' -Type idle | Out-Null
    Repair-WaaDriverIdentity | Out-Null
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM drivers WHERE full_name='Bruce Ely' AND pta_code='ELYB';" -Json)[0].c -eq 1) 'PTA-first and Rolling-first order cannot fragment Bruce Ely'
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM drivers WHERE pta_code='ELYB' OR full_name='ELYB';" -Json)[0].c -eq 1) 'obsolete ELYB placeholder is reconciled rather than left as a second driver'

    # Some real PTA codes use more than the first-name initial when the surname leaves room.
    # The current-unit bridge may reconcile that observed code only when both latest snapshots
    # are one-to-one and the code is structurally compatible with the full name.
    $iraRolling = @'
Group by  (copy)\tUnit Code\tWeek Start Date\tRolling 7 Day Start Date\t[Rolling 7 Day Engine Time]/60\t[Rolling 7 Day Idle Time]/60\tMeasure Names
IRAJ Ira Jones\t223861\t08/09/2026\t08/03/2026\t45\t9\tIdle %
'@ -replace '\\t',"`t"
    Import-WaaManagedReport -Canonical $iraRolling -Filename 'rolling-ira-jones.csv' -Type idle | Out-Null

    $iraPta = @'
| Truck | Division | Driver Code | PTA | Operational Status | Planning Status | Operational Note | Driver Type | Location | N1 | N2 |
|---|---|---|---|---|---|---|---|---|---|---|
| 223861 | 007 | JONESIRA | 08/12/26 11:00 | Loaded | UC Sent |  | Solo | JOL | 4 | 0 |
'@
    Import-WaaData $iraPta 'pta-ira-jones.txt' pta | Out-Null
    Repair-WaaDriverIdentity | Out-Null

    $ira = @(Invoke-Sql "SELECT id,full_name,pta_code FROM drivers WHERE full_name='Ira Jones';" -Json)
    Assert-Identity ($ira.Count -eq 1 -and $ira[0].pta_code -eq 'JONESIRA') 'observed extended PTA code JONESIRA becomes Ira Jones canonical PTA code'
    $iraId = [int]$ira[0].id
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM pta_observations WHERE driver_id=$iraId AND truck='223861' AND driver_code='JONESIRA';" -Json)[0].c -eq 1) 'same-unit PTA observation is rehomed to Ira Jones'
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM drivers d LEFT JOIN truck_history t ON t.driver_id=d.id WHERE t.truck='223861' AND (d.full_name='Ira Jones' OR d.full_name='JONESIRA');" -Json)[0].c -ge 1 -and (Invoke-Sql "SELECT count(*) FROM drivers WHERE full_name='JONESIRA';").Trim() -eq '0') 'Ira Jones and JONESIRA do not remain split workflow identities'
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM driver_aliases WHERE driver_id=$iraId AND alias_type='pta_code' AND alias_value='JONESIRA';" -Json)[0].c -eq 1) 'observed JONESIRA PTA alias is attached to Ira Jones'
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM driver_aliases WHERE driver_id=$iraId AND alias_type='pta_code' AND alias_value='JONESI';" -Json)[0].c -eq 0) 'unobserved short JONESI candidate is retired after exact PTA evidence'

    # Unit is corroborating evidence only. Two named Rolling identities on the same unit
    # must never be collapsed even when one name is compatible with the PTA code.
    $sharedRolling = @'
Group by  (copy)\tUnit Code\tWeek Start Date\tRolling 7 Day Start Date\t[Rolling 7 Day Engine Time]/60\t[Rolling 7 Day Idle Time]/60\tMeasure Names
A101 Ann Smith\t400400\t08/16/2026\t08/10/2026\t30\t3\tIdle %
A102 Amy Smith\t400400\t08/16/2026\t08/10/2026\t30\t3\tIdle %
'@ -replace '\\t',"`t"
    Import-WaaManagedReport -Canonical $sharedRolling -Filename 'rolling-shared-unit.csv' -Type idle | Out-Null
    $sharedPta = @'
| Truck | Division | Driver Code | PTA | Operational Status | Planning Status | Operational Note | Driver Type | Location | N1 | N2 |
|---|---|---|---|---|---|---|---|---|---|---|
| 400400 | 007 | SMITHANN | 08/17/26 09:00 | Loaded | Preplan |  | Solo | JOL | 0 | 0 |
'@
    Import-WaaData $sharedPta 'pta-shared-unit.txt' pta | Out-Null
    Repair-WaaDriverIdentity | Out-Null
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM drivers WHERE full_name IN('Ann Smith','Amy Smith');" -Json)[0].c -eq 2) 'shared unit never merges two real named drivers'
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM drivers WHERE full_name='SMITHANN' OR pta_code='SMITHANN';" -Json)[0].c -eq 1) 'ambiguous shared-unit PTA identity remains separate for manual resolution'

    # Two different real names that derive to the same PTA key are never guessed together.
    $collision = @'
Group by  (copy)	Unit Code	Week Start Date	Rolling 7 Day Start Date	[Rolling 7 Day Engine Time]/60	[Rolling 7 Day Idle Time]/60	Measure Names
A100 Alice Anderson	300001	08/09/2026	08/03/2026	20	2	Idle %
A200 Amy Anderson	300002	08/09/2026	08/03/2026	20	2	Idle %
'@ -replace '\t',"`t"
    Import-WaaManagedReport -Canonical $collision -Filename 'rolling-collision.csv' -Type idle | Out-Null
    Repair-WaaDriverIdentity | Out-Null
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM drivers WHERE full_name IN('Alice Anderson','Amy Anderson');" -Json)[0].c -eq 2) 'derived PTA collision does not merge different real names'
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM identity_issues WHERE status='open' AND alias_type='pta_code' AND alias_value='ANDERSOA' AND issue_type='ambiguous';" -Json)[0].c -eq 1) 'derived PTA collision is surfaced for manual resolution'
    Assert-Identity ((Invoke-Sql "SELECT count(*) c FROM audit_history WHERE action='identity_evidence';" -Json)[0].c -eq 0) 'automatic identity reconciliation does not manufacture driver activity'

    Write-Host 'IDENTITY TESTS PASSED' -ForegroundColor Cyan
}
finally {
    Remove-Module Waa -Force -ErrorAction SilentlyContinue
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
    Remove-Item Env:WAA_SQLITE_TEST -ErrorAction SilentlyContinue
}
