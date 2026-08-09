Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Root = $null; $script:DataRoot = $null; $script:Db = $null; $script:Sqlite = $null; $script:ReadOnly = $false

function ConvertTo-SqlLiteral([AllowNull()]$Value) {
  if ($null -eq $Value) { return 'NULL' }
  if ($Value -is [bool]) { if ($Value) { return '1' } else { return '0' } }
  if ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) { return ([Convert]::ToString($Value,[Globalization.CultureInfo]::InvariantCulture)) }
  return "'" + ([string]$Value).Replace("'","''").Replace([string][char]0,'') + "'"
}
function Invoke-Sql([string]$Sql, [switch]$Json, [switch]$AllowWrite) {
  if ($script:ReadOnly -and $AllowWrite) { throw 'Database integrity mode is read-only. Restore a valid backup.' }
  $args = @('-batch','-bail',$script:Db)
  if ($Json) { $args += '-json' }
  $input = ".timeout 5000`nPRAGMA foreign_keys=ON;`n$Sql"
  $out = $input | & $script:Sqlite @args 2>&1
  if ($LASTEXITCODE -ne 0) { throw "SQLite error: $out" }
  if ($Json) {
    $text = ($out -join "`n").Trim()
    if (!$text) { return @() }
    $parsed = ConvertFrom-Json -InputObject $text
    if ($parsed -is [System.Array]) {
      foreach ($item in $parsed) {
        if ($item -is [System.Array]) { foreach ($nested in $item) { Write-Output $nested } }
        else { Write-Output $item }
      }
    } else { Write-Output $parsed }
    return
  }
  return ($out -join "`n")
}
function Add-Audit([string]$Action,[string]$Entity,[AllowNull()]$Id,[AllowNull()]$Detail) {
  $q = @($Action,$Entity,$Id,($Detail | ConvertTo-Json -Compress -Depth 8)) | ForEach-Object { ConvertTo-SqlLiteral $_ }
  Invoke-Sql "INSERT INTO audit_history(action,entity_type,entity_id,detail_json) VALUES($($q -join ','));" -AllowWrite | Out-Null
}
function Backup-Waa([string]$Reason='manual') {
  $dir=Join-Path $script:DataRoot 'backups'; [IO.Directory]::CreateDirectory($dir)|Out-Null
  $stamp=(Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss-fff'); $dest=Join-Path $dir "waa-$stamp-$Reason.db"
  $safe=$dest.Replace("'","''"); Invoke-Sql ".backup '$safe'" | Out-Null
  return @{ path=$dest; name=[IO.Path]::GetFileName($dest) }
}
function Initialize-Waa([string]$Root,[string]$DataRoot) {
  $script:Root=$Root; $script:DataRoot=$DataRoot; [IO.Directory]::CreateDirectory($DataRoot)|Out-Null
  $script:Db=Join-Path $DataRoot 'waa.db'; $script:Sqlite=if($env:WAA_SQLITE_TEST){$env:WAA_SQLITE_TEST}else{Join-Path $Root 'runtime/sqlite/sqlite3.exe'}
  if (!(Test-Path $script:Sqlite)) { throw "Bundled SQLite runtime missing: $script:Sqlite" }
  if (Test-Path $script:Db) { Backup-Waa 'startup' | Out-Null }
  $schema=@'
PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;
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
CREATE INDEX IF NOT EXISTS idx_alias ON driver_aliases(alias_value); CREATE INDEX IF NOT EXISTS idx_pta_driver_time ON pta_observations(driver_id,observed_at DESC); CREATE INDEX IF NOT EXISTS idx_truck_time ON truck_history(truck,observed_at DESC); CREATE INDEX IF NOT EXISTS idx_idle_driver_end ON idle_periods(driver_id,period_end DESC); CREATE INDEX IF NOT EXISTS idx_bol_driver ON missing_bols(driver_id,mentioned_at); CREATE INDEX IF NOT EXISTS idx_reminder_due ON reminders(completed_at,due_at);
INSERT OR IGNORE INTO schema_version(version) VALUES(1);
INSERT OR IGNORE INTO transition_drafts(id,body) VALUES(1,'No Open ACE/ACI''s');
INSERT OR IGNORE INTO safety_notes(note) VALUES ('Keep a six-second following distance and expand it in poor conditions.'),('Use GOAL: Get Out And Look before every blind-side backing move.'),('Scan mirrors every five to eight seconds and keep an escape route open.'),('Three points of contact prevents avoidable slips and falls.'),('Slow down before the curve; never depend on braking through it.'),('Secure loose items before movement and verify the load after the first stop.'),('If fatigue appears, stop safely—alertness cannot be negotiated.'),('Check tires, lights, coupling, and brakes before every departure.');
COMMIT;
'@
  Invoke-Sql $schema -AllowWrite | Out-Null
  $integrity=(Invoke-Sql 'PRAGMA integrity_check;').Trim(); if($integrity -ne 'ok'){ $script:ReadOnly=$true }
  return @{db=$script:Db; integrity=$integrity; read_only=$script:ReadOnly}
}
function Get-Sha256([string]$Text){ $sha=[Security.Cryptography.SHA256]::Create(); try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-','').ToLowerInvariant() } finally {$sha.Dispose()} }
function Convert-DriverCode([string]$FullName) {
  $parts=@($FullName.Trim() -split '\s+' | Where-Object {$_}); if($parts.Count -lt 2){return $null}; $first=$parts[0]; $surname=(@($parts[1..($parts.Count-1)]|Where-Object{$_.Length-gt1}) -join '')
  $surname=[Text.RegularExpressions.Regex]::Replace($surname.Normalize([Text.NormalizationForm]::FormD),'\p{Mn}',''); $surname=[Text.RegularExpressions.Regex]::Replace($surname.ToUpperInvariant(),'[^A-Z0-9]','')
  return $surname.Substring(0,[Math]::Min(7,$surname.Length)) + $first.Substring(0,1).ToUpperInvariant()
}
function Find-Driver([string]$AliasType,[string]$Value,[string]$FullName) {
  if(!$Value){return $null}; $v=ConvertTo-SqlLiteral $Value; [object[]]$rows=@(Invoke-Sql "SELECT DISTINCT driver_id FROM driver_aliases WHERE alias_value=$v COLLATE NOCASE;" -Json)
  if($rows.Count -eq 1){return [int]$rows[0].driver_id}; if($rows.Count -gt 1){return $null}
  if($FullName){
    $name=if($FullName -eq 'Unknown'){$Value}else{$FullName}; $n=ConvertTo-SqlLiteral $name; $pc=if($AliasType -eq 'pta_code'){ConvertTo-SqlLiteral $Value}else{'NULL'}
    Invoke-Sql "INSERT INTO drivers(full_name,pta_code) VALUES($n,$pc);" -AllowWrite|Out-Null; $id=[int](Invoke-Sql 'SELECT max(id) FROM drivers;')
    $t=ConvertTo-SqlLiteral $AliasType; Invoke-Sql "INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed) VALUES($id,$t,$v,0);" -AllowWrite|Out-Null; return $id
  }; return $null
}
function Parse-Date([string]$Text){ $d=[datetime]::MinValue; $styles=[Globalization.DateTimeStyles]::AssumeLocal; if([datetime]::TryParse($Text,[Globalization.CultureInfo]::InvariantCulture,$styles,[ref]$d)){return $d.ToString('s')}; return $null }
function Split-ImportRows([string]$Raw){
  $lines=@($Raw -split "`r?`n" | Where-Object {$_.Trim()}); $result=@(); foreach($line in $lines){
    if($line.Contains("`t")){ $cells=@([regex]::Split($line,"`t")) } elseif($line.Trim().StartsWith('|')){$cells=@($line.Trim().Trim('|') -split '(?<!\\)\|' | ForEach-Object {$_.Trim().Replace('\_','_').Replace('\|','|')})} else {$cells=@($line -split ',(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)' | ForEach-Object {$_.Trim(' ','"')})}
    if(($cells -join '') -match '^[-: ]+$'){continue}; $result += ,$cells
  }; Write-Output -NoEnumerate $result
}
function Get-ImportPreview([string]$Raw,[string]$Filename,[string]$RequestedType='auto'){
  $rows=Split-ImportRows $Raw; $type=$RequestedType; if($type -eq 'auto'){
    $head=if($rows.Count){$rows[0] -join ' '}else{''}; if($head -match 'Last Dispatch Driver|Missing BOL' -or ($rows.Count -and $rows[0].Count -eq 29)){$type='bol'} elseif($head -match 'Rolling 7 Day|Measure Names|Engine Time'){$type='idle'} else {$type='pta'}
  }
  $warnings=@(); $errors=@(); $valid=0; $sample=@(); foreach($r in $rows){if(($r -join ' ') -match '^(Truck|Unit Code|Order|Group by)'){continue}; $needed=if($type -eq 'pta'){11}elseif($type -eq 'bol'){29}else{7}; if($r.Count -lt $needed){$warnings += "Row has $($r.Count) columns; expected $needed";continue};$valid++;if($sample.Count-lt 8){$sample+=,@($r)}}
  if($valid -eq 0){$errors += 'No valid data rows were detected.'}
  return @{type=$type; parser_version='1.0.0'; total_rows=$rows.Count; valid_rows=$valid; warnings=$warnings; errors=$errors; sample=$sample; hash=Get-Sha256 $Raw; filename=$Filename}
}
function Import-WaaData([string]$Raw,[string]$Filename,[string]$RequestedType='auto'){
  $p=Get-ImportPreview $Raw $Filename $RequestedType; if($p.errors.Count){throw ($p.errors -join '; ')}; $h=ConvertTo-SqlLiteral $p.hash
  if((Invoke-Sql "SELECT count(*) c FROM import_batches WHERE source_hash=$h;" -Json)[0].c -gt 0){throw 'Duplicate import: this exact source was already committed.'}
  $rows=Split-ImportRows $Raw; $rawq=ConvertTo-SqlLiteral $Raw; $typeq=ConvertTo-SqlLiteral $p.type; $fileq=ConvertTo-SqlLiteral $Filename; $summary=ConvertTo-SqlLiteral ($p|ConvertTo-Json -Compress -Depth 8)
  $sql="BEGIN IMMEDIATE; INSERT INTO import_batches(source_hash,import_type,parser_version,filename,source_type,raw_source,row_count,warning_count,error_count,summary_json) VALUES($h,$typeq,'1.0.0',$fileq,'user',$rawq,$($p.valid_rows),$($p.warnings.Count),0,$summary); COMMIT;"
  Invoke-Sql $sql -AllowWrite|Out-Null; $bid=[int](Invoke-Sql 'SELECT max(id) FROM import_batches;')
  try {
    foreach($r in $rows){
      if(($r -join ' ') -match '^(Truck|Unit Code|Order)' -or ($r -join '') -match '^[-: ]+$'){continue}
      if($p.type -eq 'pta' -and $r.Count -ge 11){
        $truck=$r[0].Trim();$code=$r[2].Trim();$rawPta=$r[3].Trim();$pta=Parse-Date $rawPta;$status=$r[4].Trim();$sentinel=(!$code -and $rawPta -match '12/31/26 23:59' -and $status -match '^(Shop|TruckPrep|Reserved|ClaimsHold|Clean_QA|GoodToGo)$');$driver=$null;if($code){$driver=Find-Driver 'pta_code' $code 'Unknown'}
        if(!$driver -and $code){Invoke-Sql "INSERT INTO identity_issues(import_batch_id,alias_type,alias_value,issue_type,detail) VALUES($bid,'pta_code',$(ConvertTo-SqlLiteral $code),'unmatched','PTA row not linked');" -AllowWrite|Out-Null}
        $vals=@($driver,$truck,$r[1],$code,$rawPta,$pta,([int](!$sentinel -and $driver)),$status,$r[5],$r[6],$r[7],$r[8],$r[9],$r[10],$bid)|ForEach-Object{ConvertTo-SqlLiteral $_}
        Invoke-Sql "INSERT INTO pta_observations(driver_id,truck,division,driver_code,pta_raw,pta_at,actionable,operational_status,planning_status,operational_note,driver_type,location,source_numeric_1,source_numeric_2,source,import_batch_id) VALUES($($vals[0..13]-join ','),'import',$($vals[14]));" -AllowWrite|Out-Null
        if($driver){Invoke-Sql "INSERT INTO truck_history(driver_id,truck,observed_at,import_batch_id,source) VALUES($driver,$($vals[1]),CURRENT_TIMESTAMP,$bid,'pta');" -AllowWrite|Out-Null}
      } elseif($p.type -eq 'idle' -and $r.Count -ge 7){
        $header=$rows[0];$map=@{};for($i=0;$i-lt$header.Count;$i++){$map[$header[$i].Trim()]=$i};if(!$map.ContainsKey('Measure Names')-or$r[$map['Measure Names']]-ne'Idle %'){continue};$driverText=$r[$map['Group by  (copy)']].Trim();$parts=$driverText -split ' ',2;if($parts.Count-lt2){continue};$driver=Find-Driver 'dispatch_code' $parts[0] $parts[1];$start=Parse-Date $r[$map['Rolling 7 Day Start Date']];$end=Parse-Date $r[$map['Week Start Date']];$eng=0.0;$idle=0.0;if(![double]::TryParse($r[$map['[Rolling 7 Day Engine Time]/60']],[ref]$eng)-or![double]::TryParse($r[$map['[Rolling 7 Day Idle Time]/60']],[ref]$idle)){throw'Invalid idle hours'}
        $v=@($driver,$r[$map['Unit Code']],$start,$end,$eng,$idle,$bid)|ForEach-Object{ConvertTo-SqlLiteral $_};Invoke-Sql "INSERT INTO idle_periods(driver_id,truck,period_start,period_end,engine_hours,idle_hours,import_batch_id) VALUES($($v-join ','));" -AllowWrite|Out-Null
      } elseif($p.type -eq 'bol' -and $r.Count -ge 29){
        $header=$rows[0];$map=@{};for($i=0;$i-lt $header.Count;$i++){$map[$header[$i].Trim()]=$i}; if($map.ContainsKey('Last Dispatch Driver cd')){$code=$r[$map['Last Dispatch Driver cd']];$name=$r[$map['Last Dispatch Driver nm']]}else{$code=$r[27];$name=$r[28]};$driver=Find-Driver 'dispatch_code' $code $name
        $order=$r[0];$date=if($map.ContainsKey('Empty Call Date')){$r[$map['Empty Call Date']]}else{$r[1]};$origin=if($map.ContainsKey('Origin')){$r[$map['Origin']]}else{$r[2]};$dest=if($map.ContainsKey('Destination')){$r[$map['Destination']]}else{$r[3]};$rawJson=ConvertTo-SqlLiteral ($r|ConvertTo-Json -Compress)
        $v=@($driver,$order,(Parse-Date $date),$origin,$dest,$rawJson,$bid)|ForEach-Object{ConvertTo-SqlLiteral $_};Invoke-Sql "INSERT INTO missing_bols(driver_id,order_number,empty_call_date,origin,destination,raw_fields_json,import_batch_id) VALUES($($v-join ','));" -AllowWrite|Out-Null
      }
    }
  } catch { Invoke-Sql "DELETE FROM import_batches WHERE id=$bid;" -AllowWrite|Out-Null; throw }
  Add-Audit 'import_committed' 'import_batch' $bid @{type=$p.type;rows=$p.valid_rows}; return @{id=$bid;type=$p.type;rows=$p.valid_rows;warnings=$p.warnings}
}
function Get-Dashboard {
  $sql=@'
WITH latest AS (SELECT driver_id,max(period_end) e FROM idle_periods GROUP BY driver_id), s AS (SELECT i.*,CASE WHEN i.engine_hours=0 THEN NULL ELSE round(i.idle_hours*100.0/i.engine_hours,2) END p FROM idle_periods i JOIN latest l ON l.driver_id=i.driver_id AND l.e=i.period_end), ranked AS (SELECT i.*,row_number() OVER(PARTITION BY driver_id ORDER BY period_end DESC) rn FROM idle_periods i), recent AS (SELECT *,lag(period_end) OVER(PARTITION BY driver_id ORDER BY period_start) previous_end FROM ranked WHERE rn<=4), d28 AS (SELECT driver_id,sum(engine_hours) e,sum(idle_hours) i,count(*) n,sum(CASE WHEN previous_end IS NOT NULL AND period_start<=previous_end THEN 1 ELSE 0 END) overlaps FROM recent GROUP BY driver_id) SELECT d.id,d.full_name,d.pta_code,s.truck,s.engine_hours engine7,s.idle_hours idle7,s.p p7,CASE WHEN d28.e=0 OR d28.n<4 OR d28.overlaps>0 THEN NULL ELSE round(d28.i*100.0/d28.e,2) END p28,d28.e engine28,d28.n weeks28,CASE WHEN d28.n=4 AND d28.overlaps=0 THEN 'Complete' ELSE 'Partial Data' END coverage28 FROM drivers d JOIN s ON s.driver_id=d.id LEFT JOIN d28 ON d28.driver_id=d.id;
'@
  [object[]]$drivers=@(Invoke-Sql $sql -Json); [object[]]$history=@(Invoke-Sql "SELECT period_end,round(sum(idle_hours)*100.0/nullif(sum(engine_hours),0),2) p7 FROM idle_periods GROUP BY period_end ORDER BY period_end;" -Json)
  $over=@($drivers|Where-Object{$null-ne$_.p7-and [double]$_.p7-gt50}).Count; $valid=@($drivers|Where-Object{$null-ne$_.p7}|Sort-Object {[double]$_.p7}); return @{drivers=$drivers;heroes=@($valid|Select-Object -First 5);training=@($valid|Sort-Object {[double]$_.p7} -Descending|Select-Object -First 5);over50=$over;history7=$history;history28=$history}
}
function Get-CurrentDrivers {
  $sql=@'
WITH p AS (SELECT *,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn FROM pta_observations WHERE driver_id IS NOT NULL), t AS (SELECT *,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn FROM truck_history) SELECT d.id,d.full_name,d.pta_code,coalesce(p.truck,t.truck) truck,p.division,p.pta_at,p.pta_raw,p.actionable,p.operational_status,p.planning_status,p.operational_note,p.driver_type,p.location,p.source,p.observed_at FROM drivers d LEFT JOIN p ON p.driver_id=d.id AND p.rn=1 LEFT JOIN t ON t.driver_id=d.id AND t.rn=1;
'@; return Invoke-Sql $sql -Json
}
function Get-DriverCard([int]$Id){
  $driver=@(Get-CurrentDrivers|Where-Object{[int]$_.id-eq$Id})|Select-Object -First 1;if(!$driver){throw 'Driver not found'}
  [object[]]$idle=@(Invoke-Sql "SELECT period_start,period_end,engine_hours,idle_hours,round(idle_hours*100.0/nullif(engine_hours,0),2) percent FROM idle_periods WHERE driver_id=$Id ORDER BY period_end DESC LIMIT 12;" -Json)
  [object[]]$bol=@(Invoke-Sql "SELECT * FROM missing_bols WHERE driver_id=$Id ORDER BY empty_call_date DESC;" -Json);[object[]]$work=@(Invoke-Sql "SELECT * FROM driver_work_items WHERE driver_id=$Id;" -Json)
  [object[]]$notes=@(Invoke-Sql "SELECT * FROM driver_notes WHERE driver_id=$Id ORDER BY created_at DESC;" -Json);[object[]]$rem=@(Invoke-Sql "SELECT * FROM reminders WHERE driver_id=$Id ORDER BY completed_at,due_at;" -Json);[object[]]$timers=@(Invoke-Sql "SELECT * FROM timers WHERE driver_id=$Id ORDER BY completed_at,target_at;" -Json);[object[]]$audit=@(Invoke-Sql "SELECT * FROM audit_history WHERE entity_type='driver' AND entity_id='$Id' ORDER BY occurred_at DESC LIMIT 50;" -Json)
  return @{driver=$driver;idle=$idle;bols=$bol;work=if($work.Count){$work[0]}else{$null};notes=$notes;reminders=$rem;timers=$timers;audit=$audit}
}
function Save-DriverAction([int]$Id,[hashtable]$Body){
  $action=[string]$Body.action; switch($action){
    'pta' {$pta=Parse-Date ([string]$Body.value);if(!$pta){throw 'Invalid PTA date'};$current=(Get-CurrentDrivers|Where-Object{[int]$_.id-eq$Id}|Select-Object -First 1);$v=@($Id,$current.truck,$current.division,$current.pta_code,$Body.value,$pta,$current.operational_status,$current.planning_status,$current.operational_note,$current.driver_type,$current.location)|ForEach-Object{ConvertTo-SqlLiteral $_};Invoke-Sql "INSERT INTO pta_observations(driver_id,truck,division,driver_code,pta_raw,pta_at,actionable,operational_status,planning_status,operational_note,driver_type,location,source) VALUES($($v[0..5]-join ','),1,$($v[6..10]-join ','),'manual');" -AllowWrite|Out-Null}
    'note' {$q=ConvertTo-SqlLiteral $Body.text;Invoke-Sql "INSERT INTO driver_notes(driver_id,note) VALUES($Id,$q);" -AllowWrite|Out-Null}
    'reminder' {$t=ConvertTo-SqlLiteral $Body.text;$d=ConvertTo-SqlLiteral (Parse-Date $Body.due_at);Invoke-Sql "INSERT INTO reminders(driver_id,text,due_at) VALUES($Id,$t,$d);" -AllowWrite|Out-Null}
    'timer' {$t=ConvertTo-SqlLiteral $Body.label;$d=ConvertTo-SqlLiteral (Parse-Date $Body.target_at);Invoke-Sql "INSERT INTO timers(driver_id,label,target_at) VALUES($Id,$t,$d);" -AllowWrite|Out-Null}
    'bol_mentioned' {Invoke-Sql "UPDATE missing_bols SET mentioned_at=CASE WHEN mentioned_at IS NULL THEN CURRENT_TIMESTAMP ELSE NULL END WHERE id=$([int]$Body.item_id) AND driver_id=$Id;" -AllowWrite|Out-Null}
    'complete_reminder' {Invoke-Sql "UPDATE reminders SET completed_at=CASE WHEN completed_at IS NULL THEN CURRENT_TIMESTAMP ELSE NULL END WHERE id=$([int]$Body.item_id) AND driver_id=$Id;" -AllowWrite|Out-Null}
    'complete_timer' {Invoke-Sql "UPDATE timers SET completed_at=CASE WHEN completed_at IS NULL THEN CURRENT_TIMESTAMP ELSE NULL END WHERE id=$([int]$Body.item_id) AND driver_id=$Id;" -AllowWrite|Out-Null}
    default {$allowed=@('home_checked','expected_work','home_status','home_reason','ontime_status','ontime_reason','preplan_reviewed','preplan_response','preplan_note','routing_checked','routing_status','routing_note','safety_note_id','safety_mentioned_at','include_transition','transition_note');if($allowed-notcontains$action){throw 'Unknown action'};$value=$Body.value;if($action-eq'safety_mentioned_at'-and$value){$value=(Get-Date).ToUniversalTime().ToString('s')};$q=ConvertTo-SqlLiteral $value;Invoke-Sql "INSERT INTO driver_work_items(driver_id,$action) VALUES($Id,$q) ON CONFLICT(driver_id) DO UPDATE SET $action=excluded.$action,updated_at=CURRENT_TIMESTAMP;" -AllowWrite|Out-Null}
  };Add-Audit $action 'driver' $Id $Body;return Get-DriverCard $Id
}
function Get-Transition([switch]$Regenerate){if($Regenerate){$rows=Invoke-Sql "WITH t AS (SELECT *,row_number() OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC) rn FROM truck_history) SELECT t.truck,d.full_name,w.transition_note FROM driver_work_items w JOIN drivers d ON d.id=w.driver_id LEFT JOIN t ON t.driver_id=d.id AND t.rn=1 WHERE w.include_transition=1 ORDER BY CAST(t.truck AS INTEGER),t.truck;" -Json;$lines=@("No Open ACE/ACI's")+@($rows|ForEach-Object{"$($_.truck) - $($_.full_name) : $($_.transition_note)"});$body=$lines-join"`r`n";$q=ConvertTo-SqlLiteral $body;Invoke-Sql "UPDATE transition_drafts SET body=$q,is_manual=0,updated_at=CURRENT_TIMESTAMP WHERE id=1;" -AllowWrite|Out-Null;Add-Audit 'transition_regenerated' 'transition' 1 @{count=$rows.Count}};return (Invoke-Sql 'SELECT * FROM transition_drafts WHERE id=1;' -Json)[0]}
function Save-Transition([string]$Body){$q=ConvertTo-SqlLiteral $Body;Invoke-Sql "UPDATE transition_drafts SET body=$q,is_manual=1,updated_at=CURRENT_TIMESTAMP WHERE id=1;" -AllowWrite|Out-Null;Add-Audit 'transition_saved' 'transition' 1 @{};return Get-Transition}
function Get-DataQuality{return @{issues=[object[]]@(Invoke-Sql "SELECT * FROM identity_issues WHERE status='open' ORDER BY created_at DESC;" -Json);imports=[object[]]@(Invoke-Sql 'SELECT id,imported_at,import_type,filename,row_count,warning_count,error_count,source_hash FROM import_batches ORDER BY imported_at DESC;' -Json);backups=[object[]]@(Get-ChildItem (Join-Path $script:DataRoot 'backups') -Filter '*.db' -ErrorAction SilentlyContinue|Sort-Object LastWriteTime -Descending|ForEach-Object{@{name=$_.Name;size=$_.Length;created=$_.LastWriteTimeUtc.ToString('s')}});integrity=(Invoke-Sql 'PRAGMA integrity_check;').Trim()}}
function Resolve-Identity([int]$IssueId,[int]$DriverId){$i=(Invoke-Sql "SELECT * FROM identity_issues WHERE id=$IssueId AND status='open';" -Json)[0];if(!$i){throw'Issue not found'};$t=ConvertTo-SqlLiteral $i.alias_type;$v=ConvertTo-SqlLiteral $i.alias_value;Invoke-Sql "BEGIN;INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed) VALUES($DriverId,$t,$v,1) ON CONFLICT(alias_type,alias_value) DO UPDATE SET driver_id=excluded.driver_id,confirmed=1;UPDATE identity_issues SET status='resolved' WHERE id=$IssueId;COMMIT;" -AllowWrite|Out-Null;Add-Audit 'identity_linked' 'driver' $DriverId @{issue=$IssueId};return @{ok=$true}}
function Get-SafetyNote([int]$Except=0){$r=Invoke-Sql "SELECT id,note FROM safety_notes WHERE active=1 AND id<>$Except ORDER BY random() LIMIT 1;" -Json;if(!$r.Count){$r=Invoke-Sql 'SELECT id,note FROM safety_notes WHERE active=1 LIMIT 1;' -Json};return $r[0]}
function Restore-Waa([string]$Name){if($Name-ne[IO.Path]::GetFileName($Name)){throw'Invalid backup name'};$path=Join-Path (Join-Path $script:DataRoot 'backups') $Name;if(!(Test-Path $path)){throw'Backup not found'};Backup-Waa 'pre-restore'|Out-Null;$safe=$path.Replace("'","''");Invoke-Sql ".restore '$safe'"|Out-Null;return @{ok=$true}}
Export-ModuleMember -Function Initialize-Waa,Invoke-Sql,Convert-DriverCode,Get-ImportPreview,Import-WaaData,Get-Dashboard,Get-CurrentDrivers,Get-DriverCard,Save-DriverAction,Get-Transition,Save-Transition,Get-DataQuality,Resolve-Identity,Get-SafetyNote,Backup-Waa,Restore-Waa
