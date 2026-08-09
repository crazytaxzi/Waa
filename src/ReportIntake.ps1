Set-StrictMode -Version Latest

function ConvertTo-WaaSqlLiteral([AllowNull()]$Value) {
  if ($null -eq $Value) { return 'NULL' }
  if ($Value -is [bool]) { return $(if ($Value) { '1' } else { '0' }) }
  if ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int] -or $Value -is [long] -or $Value -is [single] -or $Value -is [double] -or $Value -is [decimal]) {
    return [Convert]::ToString($Value,[Globalization.CultureInfo]::InvariantCulture)
  }
  return "'" + ([string]$Value).Replace("'","''").Replace([string][char]0,'') + "'"
}

function Get-WaaDownloadsPath {
  try {
    $shell = New-Object -ComObject Shell.Application
    $folder = $shell.NameSpace('shell:Downloads')
    if ($folder -and $folder.Self -and $folder.Self.Path -and (Test-Path $folder.Self.Path)) { return $folder.Self.Path }
  } catch { }
  $fallback = Join-Path $env:USERPROFILE 'Downloads'
  if (Test-Path $fallback) { return $fallback }
  return $fallback
}

function Get-WaaReportRoot {
  $root = Join-Path $script:DataRoot 'reports'
  [IO.Directory]::CreateDirectory((Join-Path $root 'idle')) | Out-Null
  [IO.Directory]::CreateDirectory((Join-Path $root 'missing-bol')) | Out-Null
  return $root
}

function Initialize-WaaReportIntake {
  Get-WaaReportRoot | Out-Null
  Invoke-Sql @'
CREATE TABLE IF NOT EXISTS report_intake_status(
  report_type TEXT PRIMARY KEY,
  downloads_path TEXT,
  source_name TEXT,
  source_path TEXT,
  source_modified_utc TEXT,
  source_hash TEXT,
  managed_path TEXT,
  import_batch_id INTEGER REFERENCES import_batches(id),
  status TEXT NOT NULL DEFAULT 'Waiting',
  detail TEXT,
  scanned_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
INSERT OR IGNORE INTO report_intake_status(report_type,status) VALUES('idle','Waiting'),('bol','Waiting');
'@ -AllowWrite | Out-Null
}

function Get-WaaFileSha256([string]$Path) {
  $sha=[Security.Cryptography.SHA256]::Create(); $stream=[IO.File]::OpenRead($Path)
  try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() }
  finally { $stream.Dispose(); $sha.Dispose() }
}

function Get-WaaTextSha256([string]$Text) {
  $sha=[Security.Cryptography.SHA256]::Create()
  try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-','').ToLowerInvariant() }
  finally { $sha.Dispose() }
}

function Read-WaaTextFile([string]$Path) {
  $bytes=[IO.File]::ReadAllBytes($Path)
  if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xff -and $bytes[1] -eq 0xfe) { return [Text.Encoding]::Unicode.GetString($bytes).TrimStart([char]0xfeff) }
  if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xfe -and $bytes[1] -eq 0xff) { return [Text.Encoding]::BigEndianUnicode.GetString($bytes).TrimStart([char]0xfeff) }
  if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf) { return [Text.Encoding]::UTF8.GetString($bytes,3,$bytes.Length-3) }
  return [Text.Encoding]::UTF8.GetString($bytes)
}

function Get-WaaZipEntryText($Zip,[string]$Name) {
  $entry=$Zip.GetEntry($Name); if(!$entry){return $null}
  $stream=$entry.Open(); $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true)
  try { return $reader.ReadToEnd() } finally { $reader.Dispose(); $stream.Dispose() }
}

function Get-WaaColumnIndexFromRef([string]$Ref) {
  if($Ref -notmatch '^([A-Za-z]+)'){return -1}; $n=0
  foreach($ch in $Matches[1].ToUpperInvariant().ToCharArray()){$n=($n*26)+([int]$ch-[int][char]'A'+1)}
  return $n-1
}

function Read-WaaXlsxSheets([string]$Path) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $zip=[IO.Compression.ZipFile]::OpenRead($Path)
  try {
    $shared=@(); $ssText=Get-WaaZipEntryText $zip 'xl/sharedStrings.xml'
    if($ssText){
      [xml]$ss=$ssText
      foreach($si in $ss.SelectNodes("//*[local-name()='si']")){
        $parts=@($si.SelectNodes(".//*[local-name()='t']") | ForEach-Object { $_.'#text' })
        $shared += ($parts -join '')
      }
    }
    [xml]$wb=(Get-WaaZipEntryText $zip 'xl/workbook.xml'); [xml]$rels=(Get-WaaZipEntryText $zip 'xl/_rels/workbook.xml.rels')
    $relMap=@{}; foreach($r in $rels.SelectNodes("//*[local-name()='Relationship']")){$relMap[$r.Id]=$r.Target}
    $sheets=@()
    foreach($sheet in $wb.SelectNodes("//*[local-name()='sheet']")){
      $rid=$sheet.GetAttribute('id','http://schemas.openxmlformats.org/officeDocument/2006/relationships'); $target=$relMap[$rid]
      if(!$target){continue}; if($target.StartsWith('/')){$entryName=$target.TrimStart('/')}elseif($target.StartsWith('xl/')){$entryName=$target}else{$entryName='xl/'+$target.TrimStart('/')}
      $xmlText=Get-WaaZipEntryText $zip $entryName; if(!$xmlText){continue}; [xml]$sx=$xmlText; $rows=@()
      foreach($rowNode in $sx.SelectNodes("//*[local-name()='sheetData']/*[local-name()='row']")){
        $values=@{}; $max=-1
        foreach($cell in $rowNode.SelectNodes("./*[local-name()='c']")){
          $idx=Get-WaaColumnIndexFromRef ([string]$cell.r); if($idx -lt 0){continue}; if($idx -gt $max){$max=$idx}
          $type=[string]$cell.t; $value=''
          if($type -eq 'inlineStr'){$value=(@($cell.SelectNodes(".//*[local-name()='t']")|ForEach-Object{$_.'#text'}) -join '')}
          else {
            $v=$cell.SelectSingleNode("./*[local-name()='v']"); if($v){$value=[string]$v.InnerText}
            if($type -eq 's' -and $value -match '^\d+$'){ $si=[int]$value; if($si -lt $shared.Count){$value=[string]$shared[$si]} }
            elseif($type -eq 'b'){ $value=$(if($value -eq '1'){'TRUE'}else{'FALSE'}) }
          }
          $values[$idx]=$value
        }
        if($max -ge 0){$arr=New-Object string[] ($max+1); for($i=0;$i-le$max;$i++){if($values.ContainsKey($i)){$arr[$i]=[string]$values[$i]}else{$arr[$i]=''}}; $rows += ,$arr}
      }
      $sheets += ,@{name=[string]$sheet.name;rows=$rows}
    }
    return $sheets
  } finally { $zip.Dispose() }
}

function Normalize-WaaHeader([AllowNull()][string]$Text) { return ([regex]::Replace(([string]$Text).Trim().ToLowerInvariant(),'[^a-z0-9]','')) }
function Find-WaaColumn([object[]]$Headers,[string[]]$Aliases) {
  for($i=0;$i-lt$Headers.Count;$i++){ $h=Normalize-WaaHeader ([string]$Headers[$i]); foreach($a in $Aliases){if($h -eq (Normalize-WaaHeader $a)){return $i}} }
  return -1
}
function Convert-WaaExcelDate([string]$Value) {
  $n=0.0
  if([double]::TryParse($Value,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$n) -and $n -ge 20000 -and $n -le 100000){try{return [datetime]::FromOADate($n).ToString('MM/dd/yyyy HH:mm')}catch{}}
  return $Value
}
function Clean-WaaField([AllowNull()]$Value){return ([string]$Value).Replace("`t",' ').Replace("`r",' ').Replace("`n",' ').Trim()}

function Get-WaaCanonicalTextFromRows([object[]]$Rows,[ValidateSet('idle','bol')][string]$Type) {
  if(!$Rows -or !$Rows.Count){throw 'The workbook/report contains no rows.'}
  $headerIndex=-1; $map=$null
  for($ri=0;$ri-lt [Math]::Min(60,$Rows.Count);$ri++){
    $h=@($Rows[$ri])
    if($Type -eq 'idle'){
      $m=@{
        group=Find-WaaColumn $h @('Group by  (copy)','Group by (copy)','Driver'); unit=Find-WaaColumn $h @('Unit Code','Truck','Unit');
        week=Find-WaaColumn $h @('Week Start Date'); rolling=Find-WaaColumn $h @('Rolling 7 Day Start Date');
        engine=Find-WaaColumn $h @('[Rolling 7 Day Engine Time]/60','Rolling 7 Day Engine Time/60'); idle=Find-WaaColumn $h @('[Rolling 7 Day Idle Time]/60','Rolling 7 Day Idle Time/60');
        measure=Find-WaaColumn $h @('Measure Names','Measure Name')
      }
      if(@($m.Values|Where-Object{$_ -lt 0}).Count -eq 0){$headerIndex=$ri;$map=$m;break}
    } else {
      $m=@{
        order=Find-WaaColumn $h @('Order #','Order','Order Number'); date=Find-WaaColumn $h @('Empty Call Date');
        origin=Find-WaaColumn $h @('Origin City St','Origin City/State','Origin'); destination=Find-WaaColumn $h @('Destination City St','Destination City/State','Destination');
        mileage=Find-WaaColumn $h @('Loaded Miles','Order Level Order Miles','Miles'); type=Find-WaaColumn $h @('Rev Type','BOL Type','Revenue Type');
        code=Find-WaaColumn $h @('Last Dispatch Driver cd','Last Dispatch Driver Code','Driver cd'); name=Find-WaaColumn $h @('Last Dispatch Driver nm','Last Dispatch Driver Name','Driver Name')
      }
      if($m.order-ge0 -and $m.date-ge0 -and $m.code-ge0 -and $m.name-ge0){$headerIndex=$ri;$map=$m;break}
    }
  }
  if($headerIndex -lt 0){throw "No $Type report header was found in the workbook/report."}
  $out=New-Object System.Collections.Generic.List[string]
  if($Type -eq 'idle'){
    $out.Add((@('Group by  (copy)','Unit Code','Week Start Date','Rolling 7 Day Start Date','[Rolling 7 Day Engine Time]/60','[Rolling 7 Day Idle Time]/60','Measure Names') -join "`t"))
    for($ri=$headerIndex+1;$ri-lt$Rows.Count;$ri++){ $r=@($Rows[$ri]); if(!$r.Count){continue}; $vals=@(
      $(if($map.group-lt$r.Count){$r[$map.group]}else{''}),$(if($map.unit-lt$r.Count){$r[$map.unit]}else{''}),
      (Convert-WaaExcelDate $(if($map.week-lt$r.Count){[string]$r[$map.week]}else{''})),(Convert-WaaExcelDate $(if($map.rolling-lt$r.Count){[string]$r[$map.rolling]}else{''})),
      $(if($map.engine-lt$r.Count){$r[$map.engine]}else{''}),$(if($map.idle-lt$r.Count){$r[$map.idle]}else{''}),$(if($map.measure-lt$r.Count){$r[$map.measure]}else{''})
    ) | ForEach-Object { Clean-WaaField $_ }; if(($vals -join '').Trim()){$out.Add(($vals -join "`t"))} }
  } else {
    $headers=@('Order','Empty Call Date','Origin','Destination','Mileage','BOL Type','Last Dispatch Driver cd','Last Dispatch Driver nm')+@(9..29|ForEach-Object{"Source $_"}); $out.Add(($headers -join "`t"))
    for($ri=$headerIndex+1;$ri-lt$Rows.Count;$ri++){ $r=@($Rows[$ri]); if(!$r.Count){continue}; $vals=@(
      $(if($map.order-lt$r.Count){$r[$map.order]}else{''}),(Convert-WaaExcelDate $(if($map.date-lt$r.Count){[string]$r[$map.date]}else{''})),
      $(if($map.origin-ge0-and$map.origin-lt$r.Count){$r[$map.origin]}else{''}),$(if($map.destination-ge0-and$map.destination-lt$r.Count){$r[$map.destination]}else{''}),
      $(if($map.mileage-ge0-and$map.mileage-lt$r.Count){$r[$map.mileage]}else{''}),$(if($map.type-ge0-and$map.type-lt$r.Count){$r[$map.type]}else{''}),
      $(if($map.code-lt$r.Count){$r[$map.code]}else{''}),$(if($map.name-lt$r.Count){$r[$map.name]}else{''})
    ) | ForEach-Object { Clean-WaaField $_ }; if(!$vals[0] -and !$vals[6] -and !$vals[7]){continue}; $vals += @('')*21; $out.Add(($vals -join "`t")) }
  }
  if($out.Count -lt 2){throw "The $Type report header was found, but no data rows were found."}
  return ($out -join "`r`n")
}

function Get-WaaCanonicalReportText([string]$Path,[ValidateSet('idle','bol')][string]$Type) {
  if([IO.Path]::GetExtension($Path).ToLowerInvariant() -eq '.xlsx'){
    foreach($sheet in @(Read-WaaXlsxSheets $Path)){try{return Get-WaaCanonicalTextFromRows @($sheet.rows) $Type}catch{}}
    throw "No worksheet in $([IO.Path]::GetFileName($Path)) matches the $Type report structure."
  }
  $raw=Read-WaaTextFile $Path; $rows=Split-ImportRows $raw
  return Get-WaaCanonicalTextFromRows @($rows) $Type
}

function Add-WaaDriverSql([string]$Code,[string]$Name) {
  $code=Clean-WaaField $Code; $name=Clean-WaaField $Name; if(!$code){return ''}
  $pta=if($name){Convert-DriverCode $name}else{$null}; $cq=ConvertTo-WaaSqlLiteral $code; $nq=ConvertTo-WaaSqlLiteral $(if($name){$name}else{$code}); $pq=ConvertTo-WaaSqlLiteral $pta
  return @"
INSERT INTO drivers(full_name)
SELECT $nq WHERE NOT EXISTS(SELECT 1 FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$cq COLLATE NOCASE)
AND NOT EXISTS(SELECT 1 FROM drivers WHERE full_name=$nq COLLATE NOCASE)
AND ($pq IS NULL OR NOT EXISTS(SELECT 1 FROM driver_aliases WHERE alias_type='pta_code' AND alias_value=$pq COLLATE NOCASE));
INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed)
SELECT COALESCE(
 (SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$cq COLLATE NOCASE LIMIT 1),
 (SELECT id FROM drivers WHERE full_name=$nq COLLATE NOCASE ORDER BY id LIMIT 1),
 (SELECT driver_id FROM driver_aliases WHERE alias_type='pta_code' AND alias_value=$pq COLLATE NOCASE LIMIT 1)
),'dispatch_code',$cq,0
WHERE NOT EXISTS(SELECT 1 FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$cq COLLATE NOCASE);
"@
}

function Import-WaaManagedReport([string]$Canonical,[string]$Filename,[ValidateSet('idle','bol')][string]$Type) {
  $hash=Get-WaaTextSha256 $Canonical; $hq=ConvertTo-WaaSqlLiteral $hash
  $existing=@(Invoke-Sql "SELECT id FROM import_batches WHERE source_hash=$hq LIMIT 1;" -Json)
  if($existing.Count){return @{status='Current';imported=$false;import_batch_id=[int]$existing[0].id;hash=$hash;detail='Newest report is already imported.'}}
  $rows=Split-ImportRows $Canonical; $header=@($rows[0]); $sql=New-Object Text.StringBuilder
  [void]$sql.AppendLine('BEGIN IMMEDIATE;')
  $rawq=ConvertTo-WaaSqlLiteral $Canonical; $fq=ConvertTo-WaaSqlLiteral $Filename; $tq=ConvertTo-WaaSqlLiteral $Type; $count=[Math]::Max(0,$rows.Count-1)
  [void]$sql.AppendLine("INSERT INTO import_batches(source_hash,import_type,parser_version,filename,source_type,raw_source,row_count,warning_count,error_count) VALUES($hq,$tq,'2.0.0',$fq,'downloads',$rawq,$count,0,0);")
  if($Type -eq 'idle'){
    for($ri=1;$ri-lt$rows.Count;$ri++){ $r=@($rows[$ri]); if($r.Count-lt7 -or $r[6] -ne 'Idle %'){continue}; $driverText=[string]$r[0]; $parts=$driverText -split ' ',2; if($parts.Count-lt2){continue}; $code=$parts[0];$name=$parts[1]; [void]$sql.AppendLine((Add-WaaDriverSql $code $name));
      $cq=ConvertTo-WaaSqlLiteral $code;$truck=ConvertTo-WaaSqlLiteral $r[1];$start=ConvertTo-WaaSqlLiteral (Parse-Date $r[3]);$end=ConvertTo-WaaSqlLiteral (Parse-Date $r[2]);$eng=0.0;$idle=0.0
      if(![double]::TryParse($r[4],[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$eng)){throw "Invalid engine hours on row $($ri+1)"}
      if(![double]::TryParse($r[5],[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$idle)){throw "Invalid idle hours on row $($ri+1)"}
      $engq=ConvertTo-WaaSqlLiteral $eng;$idleq=ConvertTo-WaaSqlLiteral $idle
      [void]$sql.AppendLine("INSERT INTO idle_periods(driver_id,truck,period_start,period_end,engine_hours,idle_hours,import_batch_id) SELECT driver_id,$truck,$start,$end,$engq,$idleq,(SELECT id FROM import_batches WHERE source_hash=$hq) FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$cq COLLATE NOCASE LIMIT 1 ON CONFLICT(driver_id,period_start,period_end) DO UPDATE SET truck=excluded.truck,engine_hours=excluded.engine_hours,idle_hours=excluded.idle_hours,import_batch_id=excluded.import_batch_id;")
    }
  } else {
    for($ri=1;$ri-lt$rows.Count;$ri++){ $r=@($rows[$ri]); if($r.Count-lt8){continue}; $order=[string]$r[0];$code=[string]$r[6];$name=[string]$r[7];if(!$order-and!$code){continue}; [void]$sql.AppendLine((Add-WaaDriverSql $code $name));
      $oq=ConvertTo-WaaSqlLiteral $order;$cq=ConvertTo-WaaSqlLiteral $code;$dq=ConvertTo-WaaSqlLiteral (Parse-Date $r[1]);$origin=ConvertTo-WaaSqlLiteral $r[2];$dest=ConvertTo-WaaSqlLiteral $r[3];$miles=ConvertTo-WaaSqlLiteral $r[4];$bt=ConvertTo-WaaSqlLiteral $r[5];$raw=ConvertTo-WaaSqlLiteral (($r|ConvertTo-Json -Compress))
      $driverExpr="(SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$cq COLLATE NOCASE LIMIT 1)"
      [void]$sql.AppendLine("UPDATE missing_bols SET empty_call_date=$dq,origin=$origin,destination=$dest,mileage=$miles,bol_type=$bt,raw_fields_json=$raw,last_seen_at=CURRENT_TIMESTAMP,import_batch_id=(SELECT id FROM import_batches WHERE source_hash=$hq) WHERE order_number=$oq AND driver_id=$driverExpr;")
      [void]$sql.AppendLine("INSERT INTO missing_bols(driver_id,order_number,empty_call_date,origin,destination,mileage,bol_type,raw_fields_json,import_batch_id) SELECT $driverExpr,$oq,$dq,$origin,$dest,$miles,$bt,$raw,(SELECT id FROM import_batches WHERE source_hash=$hq) WHERE $driverExpr IS NOT NULL AND NOT EXISTS(SELECT 1 FROM missing_bols WHERE order_number=$oq AND driver_id=$driverExpr);")
    }
  }
  [void]$sql.AppendLine("INSERT INTO audit_history(action,entity_type,entity_id,detail_json) VALUES('downloads_import','import_batch',(SELECT id FROM import_batches WHERE source_hash=$hq),$(ConvertTo-WaaSqlLiteral ('{"type":"'+$Type+'","file":"'+$Filename.Replace('"','')+'"}')));")
  [void]$sql.AppendLine('COMMIT;')
  try { Invoke-Sql $sql.ToString() -AllowWrite | Out-Null } catch { try{Invoke-Sql 'ROLLBACK;' -AllowWrite|Out-Null}catch{}; throw }
  $bid=[int](@(Invoke-Sql "SELECT id FROM import_batches WHERE source_hash=$hq;" -Json)[0].id)
  return @{status='Imported';imported=$true;import_batch_id=$bid;hash=$hash;detail="$count source rows processed."}
}

function Set-WaaIntakeStatus([string]$Type,[string]$Downloads,[AllowNull()]$File,[string]$Status,[string]$Detail,[AllowNull()]$Managed,[AllowNull()]$Hash,[AllowNull()]$ImportId) {
  $name=if($File){$File.Name}else{$null};$path=if($File){$File.FullName}else{$null};$mod=if($File){$File.LastWriteTimeUtc.ToString('s')}else{$null}
  $vals=@($Downloads,$name,$path,$mod,$Hash,$Managed,$ImportId,$Status,$Detail,$Type)|ForEach-Object{ConvertTo-WaaSqlLiteral $_}
  Invoke-Sql "INSERT INTO report_intake_status(report_type,downloads_path,source_name,source_path,source_modified_utc,source_hash,managed_path,import_batch_id,status,detail,scanned_at) VALUES($($vals[9]),$($vals[0]),$($vals[1]),$($vals[2]),$($vals[3]),$($vals[4]),$($vals[5]),$($vals[6]),$($vals[7]),$($vals[8]),CURRENT_TIMESTAMP) ON CONFLICT(report_type) DO UPDATE SET downloads_path=excluded.downloads_path,source_name=excluded.source_name,source_path=excluded.source_path,source_modified_utc=excluded.source_modified_utc,source_hash=excluded.source_hash,managed_path=excluded.managed_path,import_batch_id=excluded.import_batch_id,status=excluded.status,detail=excluded.detail,scanned_at=CURRENT_TIMESTAMP;" -AllowWrite|Out-Null
}

function Get-WaaNewestDownload([string]$Downloads,[ValidateSet('idle','bol')][string]$Type) {
  if(!(Test-Path $Downloads)){return $null}; $regex=if($Type-eq'bol'){'(?i)(missing.*bol|bol.*missing|order.*details.*bol)'}else{'(?i)(rolling\s*7|rolling7|7\s*day.*idle|idle.*7\s*day)'}
  return Get-ChildItem $Downloads -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension.ToLowerInvariant() -in @('.xlsx','.csv','.txt') -and $_.BaseName -match $regex } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
}

function Invoke-WaaDownloadsScan([string]$DownloadsPath) {
  Initialize-WaaReportIntake
  $downloads=if($DownloadsPath){$DownloadsPath}else{Get-WaaDownloadsPath}; $reportRoot=Get-WaaReportRoot; $results=@{}
  foreach($type in @('idle','bol')){
    $file=Get-WaaNewestDownload $downloads $type
    if(!$file){Set-WaaIntakeStatus $type $downloads $null 'Waiting' 'No matching report found in Downloads.' $null $null $null;$results[$type]=@{status='Waiting';detail='No matching report found.'};continue}
    try {
      $fileHash=Get-WaaFileSha256 $file.FullName; $old=@(Invoke-Sql "SELECT source_hash,status,managed_path,import_batch_id FROM report_intake_status WHERE report_type=$(ConvertTo-WaaSqlLiteral $type);" -Json)
      if($old.Count -and $old[0].source_hash -eq $fileHash -and $old[0].status -in @('Imported','Current')){$results[$type]=@{status='Current';file=$file.Name;imported=$false};continue}
      $canonical=Get-WaaCanonicalReportText $file.FullName $type; $folder=Join-Path $reportRoot $(if($type-eq'bol'){'missing-bol'}else{'idle'});$stamp=$file.LastWriteTimeUtc.ToString('yyyyMMdd-HHmmss');$managed=Join-Path $folder ($stamp+'_'+$file.Name)
      if(!(Test-Path $managed)){Copy-Item $file.FullName $managed -Force}
      $import=Import-WaaManagedReport $canonical $file.Name $type; Set-WaaIntakeStatus $type $downloads $file $import.status $import.detail $managed $fileHash $import.import_batch_id
      $results[$type]=@{status=$import.status;file=$file.Name;managed=$managed;imported=$import.imported;detail=$import.detail}
    } catch {
      Set-WaaIntakeStatus $type $downloads $file 'Error' $_.Exception.Message $null $(try{Get-WaaFileSha256 $file.FullName}catch{$null}) $null
      $results[$type]=@{status='Error';file=$file.Name;detail=$_.Exception.Message}
    }
  }
  return @{downloads_path=$downloads;results=$results;scanned_at=(Get-Date).ToUniversalTime().ToString('s')}
}

function Get-WaaReportIntakeStatus {
  Initialize-WaaReportIntake; $rows=@(Invoke-Sql 'SELECT * FROM report_intake_status ORDER BY report_type;' -Json); $map=@{}
  foreach($r in $rows){$map[[string]$r.report_type]=$r}
  return @{downloads_path=Get-WaaDownloadsPath;reports_root=Get-WaaReportRoot;idle=$map['idle'];bol=$map['bol']}
}
