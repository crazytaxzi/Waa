from pathlib import Path

root = Path.cwd()
parsing_path = root / 'src' / 'ReportParsing.ps1'
tests_path = root / 'tests' / 'Identity.Tests.ps1'

text = parsing_path.read_text(encoding='utf-8')

helper_marker = "function Get-WaaDriverIdentitySql {"
if helper_marker not in text:
    raise SystemExit('identity SQL marker not found')

helpers = r'''function Get-WaaPtaNameSignature {
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

'''
text = text.replace(helper_marker, helpers + helper_marker, 1)

repair_marker = "function Repair-WaaDriverIdentity {"
if repair_marker not in text:
    raise SystemExit('repair marker not found')

observed_helper = r'''function Set-WaaObservedPtaCode {
    param(
        [Parameter(Mandatory=$true)][int]$DriverId,
        [Parameter(Mandatory=$true)][string]$PtaCode
    )

    if ($DriverId -le 0) { return $false }
    $driverRows = @(Invoke-Sql "SELECT id,full_name,pta_code FROM drivers WHERE id=$DriverId;" -Json)
    if ($driverRows.Count -eq 0) { return $false }

    $driver = $driverRows[0]
    $actual = Normalize-WaaPtaCode $PtaCode
    if (-not (Test-WaaPtaCodeMatchesName ([string]$driver.full_name) $actual)) { return $false }

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

'''
text = text.replace(repair_marker, observed_helper + repair_marker, 1)

count_marker = "    $ambiguous = 0\n    $evidenceCount = 0"
if count_marker not in text:
    raise SystemExit('repair counter marker not found')
text = text.replace(count_marker, "    $ambiguous = 0\n    $evidenceCount = 0\n    $assignmentLinks = 0", 1)

bridge_marker = "    # Unit Code is assignment evidence. Backfill the standard assignment history from idle"
if bridge_marker not in text:
    raise SystemExit('assignment backfill marker not found')

bridge = r'''    # Current unit is corroborating snapshot evidence, never permanent driver identity.
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

'''
text = text.replace(bridge_marker, bridge + bridge_marker, 1)

return_marker = "return @{ evidence=$evidenceCount; merged=$merged; ambiguous=$ambiguous; elapsed_ms=[math]::Round($watch.Elapsed.TotalMilliseconds,1) }"
if return_marker not in text:
    raise SystemExit('repair return marker not found')
text = text.replace(return_marker, "return @{ evidence=$evidenceCount; merged=$merged; assignment_links=$assignmentLinks; ambiguous=$ambiguous; elapsed_ms=[math]::Round($watch.Elapsed.TotalMilliseconds,1) }", 1)

parsing_path.write_text(text, encoding='utf-8', newline='\n')

tests = tests_path.read_text(encoding='utf-8')
test_marker = "    # Two different real names that derive to the same PTA key are never guessed together."
if test_marker not in tests:
    raise SystemExit('identity test insertion marker not found')

new_tests = r'''    # Some real PTA codes use more than the first-name initial when the surname leaves room.
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

'''
tests = tests.replace(test_marker, new_tests + test_marker, 1)
tests_path.write_text(tests, encoding='utf-8', newline='\n')
