from pathlib import Path


def read(path):
    return Path(path).read_text(encoding='utf-8').replace('\r\n', '\n')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8', newline='\n')


# Core domain: keep Missing BOL history in SQLite, but define the active operational
# set as exactly the newest imported BOL report. One query owns the dedicated page,
# and the Driver Card uses the same newest-batch rule.
p = 'src/Waa.psm1'
s = read(p)

marker = 'function Get-DriverCard {\n'
if marker not in s:
    raise SystemExit('Get-DriverCard insertion point not found')

fn = r'''function Get-CurrentMissingBols {
    param([int]$DriverId=0)
    $driverFilter=if($DriverId-gt0){"AND b.driver_id=$DriverId"}else{''}
    $sql=@"
WITH latest_bol AS (
  SELECT id FROM import_batches
  WHERE import_type='bol'
  ORDER BY imported_at DESC,id DESC
  LIMIT 1
), t AS (
  SELECT *,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn
  FROM truck_history
)
SELECT b.*,d.full_name,t.truck
FROM missing_bols b
JOIN latest_bol lb ON lb.id=b.import_batch_id
LEFT JOIN drivers d ON d.id=b.driver_id
LEFT JOIN t ON t.driver_id=b.driver_id AND t.rn=1
WHERE 1=1 $driverFilter
ORDER BY b.mentioned_at,b.empty_call_date,b.id;
"@
    return Invoke-Sql $sql -Json
}

'''
s = s.replace(marker, fn + marker, 1)

old = "FROM (SELECT * FROM missing_bols WHERE driver_id=$Id ORDER BY empty_call_date DESC)),'[]')),"
new = "FROM (SELECT * FROM missing_bols WHERE driver_id=$Id AND import_batch_id=(SELECT id FROM import_batches WHERE import_type='bol' ORDER BY imported_at DESC,id DESC LIMIT 1) ORDER BY empty_call_date DESC)),'[]'))," 
if old not in s:
    raise SystemExit('Driver Card Missing BOL query not found')
s = s.replace(old, new, 1)

old_export = 'Get-Dashboard,Get-CurrentDrivers,Get-CurrentDriver,Get-DriverCard,Get-IdleCoachingLog'
new_export = 'Get-Dashboard,Get-CurrentDrivers,Get-CurrentDriver,Get-CurrentMissingBols,Get-DriverCard,Get-IdleCoachingLog'
if old_export not in s:
    raise SystemExit('Waa module export insertion point not found')
s = s.replace(old_export, new_export, 1)
write(p, s)


# Server uses the core current-snapshot query instead of maintaining its own BOL SQL.
p = 'src/Server.ps1'
s = read(p)
old = '''                    elseif ($method -eq 'GET' -and $path -eq '/api/bols') {
                        $bolSql = "WITH t AS(SELECT *,row_number()OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC)rn FROM truck_history) SELECT b.*,d.full_name,t.truck FROM missing_bols b LEFT JOIN drivers d ON d.id=b.driver_id LEFT JOIN t ON t.driver_id=b.driver_id AND t.rn=1 ORDER BY b.mentioned_at,b.empty_call_date;"
                        Send-JsonArray $request.stream 200 @(Invoke-Sql $bolSql -Json) | Out-Null
                    }'''
new = '''                    elseif ($method -eq 'GET' -and $path -eq '/api/bols') {
                        Send-JsonArray $request.stream 200 @(Get-CurrentMissingBols) | Out-Null
                    }'''
if old not in s:
    raise SystemExit('/api/bols route not found')
s = s.replace(old, new, 1)
write(p, s)


# Make the UI state the operational rule plainly.
p = 'web/app.js'
s = read(p)
old = "pageHead('Missing BOLs', 'Persistent driver-specific items for call close-out and follow-up.')"
new = "pageHead('Missing BOLs', 'Current report only. Historical Missing BOL evidence stays preserved without cluttering today’s call work.')"
if old not in s:
    raise SystemExit('Missing BOL page subtitle not found')
s = s.replace(old, new, 1)
write(p, s)


# Regression: historical rows remain stored, but both the core operational query and
# Driver Card expose only rows belonging to the newest BOL import batch.
p = 'tests/Run-Tests.ps1'
s = read(p)
marker = "$did=[int]$cur[0].id;Invoke-Sql \"INSERT INTO idle_periods"
if marker not in s:
    raise SystemExit('test insertion point not found')

test = r'''$did=[int]$cur[0].id;$oldBolBatch=[int](Invoke-Sql "INSERT INTO import_batches(source_hash,import_type,parser_version,filename,source_type,raw_source,row_count) VALUES('test-bol-old','bol','test','Missing BOL old.xlsx','test','old',1);SELECT last_insert_rowid();");Invoke-Sql "INSERT INTO missing_bols(driver_id,order_number,raw_fields_json,import_batch_id) VALUES($did,'OLD-BOL','{}',$oldBolBatch);" -AllowWrite|Out-Null;$currentBolBatch=[int](Invoke-Sql "INSERT INTO import_batches(source_hash,import_type,parser_version,filename,source_type,raw_source,row_count) VALUES('test-bol-current','bol','test','Missing BOL current.xlsx','test','current',1);SELECT last_insert_rowid();");Invoke-Sql "INSERT INTO missing_bols(driver_id,order_number,raw_fields_json,import_batch_id) VALUES($did,'CURRENT-BOL','{}',$currentBolBatch);" -AllowWrite|Out-Null;$currentBols=@(Get-CurrentMissingBols);Assert ($currentBols.Count-eq1-and$currentBols[0].order_number-eq'CURRENT-BOL') 'Missing BOL operational query shows only newest report snapshot';$bolCard=Get-DriverCard $did;Assert ($bolCard.bols.Count-eq1-and$bolCard.bols[0].order_number-eq'CURRENT-BOL') 'Driver Card shows only Missing BOLs from newest report snapshot';Assert ((Invoke-Sql "SELECT count(*) FROM missing_bols WHERE driver_id=$did;").Trim()-eq'2') 'older Missing BOL rows remain preserved as historical evidence';Invoke-Sql "INSERT INTO idle_periods'''
s = s.replace(marker, test, 1)
write(p, s)
