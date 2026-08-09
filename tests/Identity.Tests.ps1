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
    Initialize-WaaConversation

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

    Write-Host 'IDENTITY TESTS PASSED' -ForegroundColor Cyan
}
finally {
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
    Remove-Item Env:WAA_SQLITE_TEST -ErrorAction SilentlyContinue
}
