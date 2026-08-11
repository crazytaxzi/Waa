Set-StrictMode -Version Latest

$script:WaaLiveStore = $null
$script:WaaLiveDataRoot = $null
$script:WaaLiveLastCheckpoint = [datetime]::MinValue
$script:WaaLiveMutationCount = 0

function Initialize-WaaLmdbInterop {
    param([Parameter(Mandatory=$true)][string]$Root)
    if ('Waa.Native.LmdbStore' -as [type]) { return }

    $runtime = Join-Path $Root 'runtime/lmdb'
    $runningOnWindows = $env:OS -eq 'Windows_NT'
    $library = if ($runningOnWindows) { Join-Path $runtime 'lmdb.dll' } else { Join-Path $runtime 'liblmdb.so' }
    if (-not (Test-Path -LiteralPath $library)) { throw "Bundled LMDB runtime missing: $library" }
    $env:PATH = $runtime + [IO.Path]::PathSeparator + $env:PATH
    if (-not $runningOnWindows) { $env:LD_LIBRARY_PATH = $runtime + [IO.Path]::PathSeparator + [string]$env:LD_LIBRARY_PATH }

    $interopSource = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Waa.Native {
  [StructLayout(LayoutKind.Sequential)]
  internal struct MdbVal { public UIntPtr Size; public IntPtr Data; }

  internal static class LmdbNative {
    const string Lib = "__WAA_LMDB_LIBRARY__";
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_env_create(out IntPtr env);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_env_set_mapsize(IntPtr env, UIntPtr size);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_env_set_maxdbs(IntPtr env, uint dbs);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Ansi)] internal static extern int mdb_env_open(IntPtr env, string path, uint flags, uint mode);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_env_sync(IntPtr env, int force);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern void mdb_env_close(IntPtr env);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_txn_begin(IntPtr env, IntPtr parent, uint flags, out IntPtr txn);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_txn_commit(IntPtr txn);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern void mdb_txn_abort(IntPtr txn);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Ansi)] internal static extern int mdb_dbi_open(IntPtr txn, string name, uint flags, out uint dbi);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_get(IntPtr txn, uint dbi, ref MdbVal key, out MdbVal data);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_put(IntPtr txn, uint dbi, ref MdbVal key, ref MdbVal data, uint flags);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_del(IntPtr txn, uint dbi, ref MdbVal key, IntPtr data);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_cursor_open(IntPtr txn, uint dbi, out IntPtr cursor);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern void mdb_cursor_close(IntPtr cursor);
    [DllImport(Lib, CallingConvention=CallingConvention.Cdecl)] internal static extern int mdb_cursor_get(IntPtr cursor, ref MdbVal key, ref MdbVal data, int op);
  }

  public sealed class LmdbStore : IDisposable {
    const int NotFound = -30798;
    const uint ReadOnly = 0x20000;
    const uint Create = 0x40000;
    readonly object gate = new object();
    IntPtr env;
    uint dbi;

    public LmdbStore(string path, long mapSize) {
      Check(LmdbNative.mdb_env_create(out env), "env_create");
      try {
        Check(LmdbNative.mdb_env_set_mapsize(env, new UIntPtr((ulong)mapSize)), "set_mapsize");
        Check(LmdbNative.mdb_env_set_maxdbs(env, 2), "set_maxdbs");
        Check(LmdbNative.mdb_env_open(env, path, 0, 384), "env_open");
        IntPtr txn;
        Check(LmdbNative.mdb_txn_begin(env, IntPtr.Zero, 0, out txn), "txn_begin");
        try { Check(LmdbNative.mdb_dbi_open(txn, "waa", Create, out dbi), "dbi_open"); Check(LmdbNative.mdb_txn_commit(txn), "txn_commit"); txn = IntPtr.Zero; }
        finally { if (txn != IntPtr.Zero) LmdbNative.mdb_txn_abort(txn); }
      } catch { Dispose(); throw; }
    }

    static void Check(int rc, string operation) { if (rc != 0) throw new InvalidOperationException("LMDB " + operation + " failed (" + rc + ")"); }
    static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value ?? ""); }
    static MdbVal Pin(byte[] bytes, out GCHandle pin) { pin = GCHandle.Alloc(bytes, GCHandleType.Pinned); return new MdbVal { Size = new UIntPtr((ulong)bytes.LongLength), Data = pin.AddrOfPinnedObject() }; }
    static string CopyString(MdbVal value) { int size = checked((int)value.Size.ToUInt64()); byte[] bytes = new byte[size]; if (size > 0) Marshal.Copy(value.Data, bytes, 0, size); return Encoding.UTF8.GetString(bytes); }

    public string Get(string key) {
      lock (gate) {
        IntPtr txn; Check(LmdbNative.mdb_txn_begin(env, IntPtr.Zero, ReadOnly, out txn), "read_begin");
        try {
          byte[] kb = Bytes(key); GCHandle kp; MdbVal kval = Pin(kb, out kp);
          try { MdbVal data; int rc = LmdbNative.mdb_get(txn, dbi, ref kval, out data); if (rc == NotFound) return null; Check(rc, "get"); return CopyString(data); }
          finally { kp.Free(); }
        } finally { LmdbNative.mdb_txn_abort(txn); }
      }
    }

    public void PutBatch(IDictionary<string,string> puts, ICollection<string> deletes) {
      lock (gate) {
        IntPtr txn; Check(LmdbNative.mdb_txn_begin(env, IntPtr.Zero, 0, out txn), "write_begin");
        try {
          foreach (KeyValuePair<string,string> item in puts) {
            byte[] kb=Bytes(item.Key), vb=Bytes(item.Value); GCHandle kp, vp; MdbVal key=Pin(kb,out kp), value=Pin(vb,out vp);
            try { Check(LmdbNative.mdb_put(txn,dbi,ref key,ref value,0),"put"); } finally { vp.Free(); kp.Free(); }
          }
          foreach (string item in deletes) {
            byte[] kb=Bytes(item); GCHandle kp; MdbVal key=Pin(kb,out kp);
            try { int rc=LmdbNative.mdb_del(txn,dbi,ref key,IntPtr.Zero); if(rc!=0 && rc!=NotFound) Check(rc,"delete"); } finally { kp.Free(); }
          }
          Check(LmdbNative.mdb_txn_commit(txn),"write_commit"); txn=IntPtr.Zero;
        } finally { if(txn!=IntPtr.Zero) LmdbNative.mdb_txn_abort(txn); }
      }
    }

    public Dictionary<string,string> Scan(string prefix) {
      lock (gate) {
        Dictionary<string,string> rows=new Dictionary<string,string>(StringComparer.Ordinal);
        IntPtr txn; Check(LmdbNative.mdb_txn_begin(env,IntPtr.Zero,ReadOnly,out txn),"scan_begin");
        IntPtr cursor=IntPtr.Zero;
        try {
          Check(LmdbNative.mdb_cursor_open(txn,dbi,out cursor),"cursor_open");
          MdbVal key=new MdbVal(), data=new MdbVal(); int rc;
          if (prefix.Length == 0) { rc=LmdbNative.mdb_cursor_get(cursor,ref key,ref data,0); }
          else {
            byte[] pb=Bytes(prefix); GCHandle pp; key=Pin(pb,out pp);
            try { rc=LmdbNative.mdb_cursor_get(cursor,ref key,ref data,17); } finally { pp.Free(); }
          }
          while(rc==0) {
            string found=CopyString(key); if(!found.StartsWith(prefix,StringComparison.Ordinal)) break;
            rows[found]=CopyString(data); rc=LmdbNative.mdb_cursor_get(cursor,ref key,ref data,8);
          }
          if(rc!=0 && rc!=NotFound) Check(rc,"cursor_get");
          return rows;
        } finally { if(cursor!=IntPtr.Zero)LmdbNative.mdb_cursor_close(cursor); LmdbNative.mdb_txn_abort(txn); }
      }
    }

    public void Sync() { lock(gate) { Check(LmdbNative.mdb_env_sync(env,1),"sync"); } }
    public void Dispose() { lock(gate) { if(env!=IntPtr.Zero) { LmdbNative.mdb_env_close(env); env=IntPtr.Zero; } } }
  }
}
'@
    $escapedLibrary = $library.Replace('\','\\')
    Add-Type -TypeDefinition ($interopSource.Replace('__WAA_LMDB_LIBRARY__',$escapedLibrary))
}

function Initialize-WaaLiveStore {
    param([Parameter(Mandatory=$true)][string]$Root,[Parameter(Mandatory=$true)][string]$DataRoot)
    Initialize-WaaLmdbInterop -Root $Root
    if ($null -ne $script:WaaLiveStore) { $script:WaaLiveStore.Dispose() }
    $path = Join-Path $DataRoot 'live'
    [IO.Directory]::CreateDirectory($path) | Out-Null
    $script:WaaLiveStore = [Waa.Native.LmdbStore]::new($path, 536870912)
    $script:WaaLiveDataRoot = $DataRoot
    $script:WaaLiveLastCheckpoint = Get-Date
    $script:WaaLiveMutationCount = 0
}

function Close-WaaLiveStore {
    if ($null -eq $script:WaaLiveStore) { return }
    $script:WaaLiveStore.Sync()
    $script:WaaLiveStore.Dispose()
    $script:WaaLiveStore = $null
}

function Test-WaaLiveStoreOnline { return $null -ne $script:WaaLiveStore }

function Get-WaaLiveRaw { param([string]$Key) return $script:WaaLiveStore.Get($Key) }
function Get-WaaLivePrefix { param([string]$Prefix) return $script:WaaLiveStore.Scan($Prefix) }

function Set-WaaLiveRawBatch {
    param([hashtable]$Puts=@{},[string[]]$Deletes=@())
    $values = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($key in $Puts.Keys) { $values[[string]$key] = [string]$Puts[$key] }
    $removed = [Collections.Generic.List[string]]::new()
    foreach ($key in $Deletes) { [void]$removed.Add([string]$key) }
    $script:WaaLiveStore.PutBatch($values,$removed)
}

function ConvertFrom-WaaLiveJson { param([AllowNull()][string]$Json) if ([string]::IsNullOrWhiteSpace($Json)) { return $null }; return ConvertFrom-Json -InputObject $Json }
function ConvertTo-WaaLiveJson { param($Value) return ConvertTo-Json -InputObject $Value -Compress -Depth 12 }

function ConvertTo-WaaLiveRecord {
    param($Row,[long]$Revision=0,[switch]$Deleted)
    $record = [ordered]@{}
    if ($null -ne $Row) { foreach ($property in $Row.PSObject.Properties) { $record[$property.Name] = $property.Value } }
    $record['_revision'] = $Revision
    $record['_deleted'] = [bool]$Deleted
    return $record
}

function Get-WaaLiveCallKey {
    param([int]$DriverId,[string]$CycleKey)
    $bytes = [Text.Encoding]::UTF8.GetBytes($CycleKey)
    $encoded = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
    return "call:$DriverId`:$encoded"
}

function Get-WaaLiveWork {
    param([int]$DriverId)
    $record = ConvertFrom-WaaLiveJson (Get-WaaLiveRaw "work:$DriverId")
    if ($null -ne $record -and -not [bool]$record._deleted) { return $record }
    return [pscustomobject]@{
        driver_id=$DriverId;cycle_key=$null;home_checked=0;expected_work='Unknown';home_status='Unknown';home_reason=$null
        ontime_status='Unknown';ontime_reason=$null;ontime_checked_at=$null;preplan_reviewed=0;preplan_response='Unknown'
        preplan_note=$null;routing_checked=0;routing_status='Unknown';routing_note=$null;safety_note_id=$null
        safety_mentioned_at=$null;include_transition=0;transition_note=$null;updated_at=$null;_revision=0;_deleted=$false
    }
}

function Get-WaaLiveCall {
    param([int]$DriverId,[string]$CycleKey)
    return ConvertFrom-WaaLiveJson (Get-WaaLiveRaw (Get-WaaLiveCallKey $DriverId $CycleKey))
}

function Get-WaaLiveFollowups {
    param([int]$DriverId)
    $notes = @((Get-WaaLivePrefix "note:$DriverId`:").Values | ForEach-Object { ConvertFrom-WaaLiveJson $_ } | Where-Object { -not [bool]$_._deleted } | Sort-Object created_at -Descending)
    $reminders = @((Get-WaaLivePrefix "reminder:$DriverId`:").Values | ForEach-Object { ConvertFrom-WaaLiveJson $_ } | Where-Object { -not [bool]$_._deleted } | Sort-Object @{Expression={ [bool]$_.completed_at }},due_at)
    $timers = @((Get-WaaLivePrefix "timer:$DriverId`:").Values | ForEach-Object { ConvertFrom-WaaLiveJson $_ } | Where-Object { -not [bool]$_._deleted } | Sort-Object @{Expression={ [bool]$_.completed_at }},target_at)
    return @{notes=$notes;reminders=$reminders;timers=$timers}
}

function Get-WaaLiveTransition {
    $draft = ConvertFrom-WaaLiveJson (Get-WaaLiveRaw 'transition:1')
    if ($null -ne $draft) { return $draft }
    return [pscustomobject]@{id=1;body="No Open ACE/ACI's";is_manual=0;updated_at=$null;_revision=0;_deleted=$false}
}

function Get-WaaLiveRevision {
    $raw = Get-WaaLiveRaw 'meta:revision'
    if ([string]::IsNullOrWhiteSpace($raw)) { return [long]0 }
    return [long]$raw
}

function Set-WaaLiveEntity {
    param(
        [Parameter(Mandatory=$true)][string]$EntityKey,
        [Parameter(Mandatory=$true)]$Record,
        [AllowNull()][string]$Action,
        [string]$EntityType='driver',
        [AllowNull()]$EntityId,
        [AllowNull()]$Detail,
        [hashtable]$ExtraPuts=@{}
    )
    $revision = (Get-WaaLiveRevision) + 1
    if ($Record -is [Collections.IDictionary]) { $Record['_revision']=$revision }
    else { $Record._revision=$revision }
    $puts = @{'meta:revision' = [string]$revision}
    $puts[$EntityKey] = ConvertTo-WaaLiveJson $Record
    $puts["dirty:$EntityKey"] = [string]$revision
    foreach ($key in $ExtraPuts.Keys) { $puts[$key] = $ExtraPuts[$key] }
    if (-not [string]::IsNullOrWhiteSpace($Action)) {
        $event = @{revision=$revision;occurred_at=(Get-Date).ToUniversalTime().ToString('s');action=$Action;entity_type=$EntityType;entity_id=$EntityId;detail_json=(ConvertTo-Json $Detail -Compress -Depth 8)}
        $puts[('event:' + $revision.ToString('D20'))] = ConvertTo-WaaLiveJson $event
    }
    Set-WaaLiveRawBatch -Puts $puts
    $script:WaaLiveMutationCount++
    return $revision
}

function Get-WaaLiveNextId {
    param([ValidateSet('note','reminder','timer')][string]$Type)
    $key = "seq:$Type"
    $raw = Get-WaaLiveRaw $key
    $next = if ([string]::IsNullOrWhiteSpace($raw)) { [long]1 } else { [long]$raw + 1 }
    Set-WaaLiveRawBatch -Puts @{$key=[string]$next}
    return $next
}

function Set-WaaLiveWorkField {
    param([int]$DriverId,[string]$Field,$Value)
    $work = Get-WaaLiveWork $DriverId
    $work.$Field = $Value
    if ($Field -eq 'ontime_status') { $work.ontime_checked_at = (Get-Date).ToUniversalTime().ToString('s') }
    $work.updated_at = (Get-Date).ToUniversalTime().ToString('s')
    [void](Set-WaaLiveEntity -EntityKey "work:$DriverId" -Record $work -Action $Field -EntityId $DriverId -Detail @{action=$Field;value=$Value})
    return $work
}

function Set-WaaLiveCallField {
    param(
        [int]$DriverId,
        [string]$CycleKey,
        [string]$Field,
        $Value,
        [switch]$NoAudit,
        [switch]$CaptureIdleSnapshot,
        [AllowNull()]$IdlePercentSnapshot,
        [AllowNull()][string]$IdlePeriodEndSnapshot
    )
    $key = Get-WaaLiveCallKey $DriverId $CycleKey
    $call = Get-WaaLiveCall $DriverId $CycleKey
    if ($null -eq $call) {
        $now=(Get-Date).ToUniversalTime().ToString('s')
        $call=[pscustomobject]@{id=$null;driver_id=$DriverId;cycle_key=$CycleKey;opened_at=$now;updated_at=$now;fuel_status='Unknown';fuel_note=$null;driver_eta=$null;eta_status='Unknown';eta_note=$null;idle_plan=$null;idle_percent_snapshot=$null;idle_period_end_snapshot=$null;load_help_status='Unknown';load_help_note=$null;conversation_wrap=$null;completed_at=$null;_revision=0;_deleted=$false}
    }
    foreach($property in @('idle_percent_snapshot','idle_period_end_snapshot')){
        if($null-eq$call.PSObject.Properties[$property]){$call|Add-Member -NotePropertyName $property -NotePropertyValue $null}
    }
    if ($Field -eq 'completed_at') { $call.completed_at = if ([bool]$Value) { (Get-Date).ToUniversalTime().ToString('s') } else { $null } }
    else { $call.$Field = $Value }
    if($Field-eq'idle_plan'-and$CaptureIdleSnapshot){
        $call.idle_percent_snapshot=$IdlePercentSnapshot
        $call.idle_period_end_snapshot=$IdlePeriodEndSnapshot
    }
    $call.updated_at=(Get-Date).ToUniversalTime().ToString('s')
    $auditAction=if($NoAudit){$null}else{'call_flow_update'}
    [void](Set-WaaLiveEntity -EntityKey $key -Record $call -Action $auditAction -EntityId $DriverId -Detail @{field=$Field;cycle_key=$CycleKey;value=$Value})
    return $call
}

function Add-WaaLiveFollowup {
    param([int]$DriverId,[ValidateSet('note','reminder','timer')][string]$Type,[hashtable]$Values)
    $id = Get-WaaLiveNextId $Type
    $now=(Get-Date).ToUniversalTime().ToString('s')
    $record=[ordered]@{id=$id;driver_id=$DriverId;created_at=$now;_revision=0;_deleted=$false}
    foreach($key in $Values.Keys){$record[$key]=$Values[$key]}
    $detail=@{action=$Type}
    foreach($key in $Values.Keys){$detail[$key]=$Values[$key]}
    [void](Set-WaaLiveEntity -EntityKey "$Type`:$DriverId`:$id" -Record $record -Action $Type -EntityId $DriverId -Detail $detail)
    return $record
}

function Update-WaaLiveFollowup {
    param([int]$DriverId,[ValidateSet('note','reminder','timer')][string]$Type,[long]$Id,[ValidateSet('delete','toggle','snooze')][string]$Operation)
    $key="$Type`:$DriverId`:$Id"
    $record=ConvertFrom-WaaLiveJson (Get-WaaLiveRaw $key)
    if($null-eq$record-or[bool]$record._deleted){throw "Driver $Type not found"}
    if($Operation-eq'delete'){$record._deleted=$true}
    elseif($Operation-eq'toggle'){$record.completed_at=if($record.completed_at){$null}else{(Get-Date).ToUniversalTime().ToString('s')}}
    elseif($Operation-eq'snooze'){$record.due_at=([datetime]$record.due_at).AddDays(1).ToString('s');$record.completed_at=$null}
    $action = if($Operation-eq'delete'){"delete_$Type"}elseif($Operation-eq'snooze'){'snooze_reminder'}else{"complete_$Type"}
    [void](Set-WaaLiveEntity -EntityKey $key -Record $record -Action $action -EntityId $DriverId -Detail @{action=$action;item_id=$Id})
    if($Operation-eq'delete'){Invoke-WaaLiveCheckpoint -Force|Out-Null}
    return $record
}

function Set-WaaLiveTransition {
    param([string]$Body,[bool]$IsManual,[string]$Action='transition_saved')
    $draft=Get-WaaLiveTransition
    $draft.body=$Body;$draft.is_manual=if($IsManual){1}else{0};$draft.updated_at=(Get-Date).ToUniversalTime().ToString('s')
    [void](Set-WaaLiveEntity -EntityKey 'transition:1' -Record $draft -Action $Action -EntityType 'transition' -EntityId 1 -Detail @{})
    return $draft
}

function Initialize-WaaLiveDomain {
    $meta = Get-WaaLiveRaw 'meta:schema'
    if (-not [string]::IsNullOrWhiteSpace($meta)) {
        Invoke-WaaLiveCheckpoint -Force | Out-Null
        return @{hydrated=$false;recovered=$true}
    }

    $puts=@{'meta:schema'='1';'meta:revision'='0'}
    foreach($row in @(Invoke-Sql 'SELECT * FROM driver_work_items;' -Json)){$puts["work:$($row.driver_id)"]=ConvertTo-WaaLiveJson (ConvertTo-WaaLiveRecord $row)}
    foreach($row in @(Invoke-Sql 'SELECT * FROM driver_call_sessions;' -Json)){$puts[(Get-WaaLiveCallKey ([int]$row.driver_id) ([string]$row.cycle_key))]=ConvertTo-WaaLiveJson (ConvertTo-WaaLiveRecord $row)}
    foreach($type in @('note','reminder','timer')){
        $table=@{note='driver_notes';reminder='reminders';timer='timers'}[$type]
        $rows=@(Invoke-Sql "SELECT * FROM $table;" -Json)
        foreach($row in $rows){$puts["$type`:$($row.driver_id)`:$($row.id)"]=ConvertTo-WaaLiveJson (ConvertTo-WaaLiveRecord $row)}
        $max=if($rows.Count){[long](($rows|Measure-Object id -Maximum).Maximum)}else{0};$puts["seq:$type"]=[string]$max
    }
    $drafts=@(Invoke-Sql 'SELECT * FROM transition_drafts WHERE id=1;' -Json)
    if($drafts.Count){$puts['transition:1']=ConvertTo-WaaLiveJson (ConvertTo-WaaLiveRecord $drafts[0])}
    $checkpoint=[string](Invoke-Sql "SELECT value FROM settings WHERE key='hybrid_checkpoint_revision';")
    if(-not[string]::IsNullOrWhiteSpace($checkpoint)){$puts['meta:revision']=$checkpoint}
    Set-WaaLiveRawBatch -Puts $puts
    $script:WaaLiveStore.Sync()
    return @{hydrated=$true;recovered=$false;entities=$puts.Count}
}

function Get-WaaLiveSqlValue {
    param($Record,[string]$Name)
    $property=$Record.PSObject.Properties[$Name]
    $value=if($null-eq$property){$null}else{$property.Value}
    if($value-is[datetime]){$value=$value.ToUniversalTime().ToString('s')}
    return ConvertTo-SqlLiteral $value
}

function Add-WaaLiveCheckpointSql {
    param([Text.StringBuilder]$Sql,[string]$EntityKey,$Record,[long]$Revision)
    if($EntityKey.StartsWith('work:')){
        $columns=@('driver_id','cycle_key','home_checked','expected_work','home_status','home_reason','ontime_status','ontime_reason','ontime_checked_at','preplan_reviewed','preplan_response','preplan_note','routing_checked','routing_status','routing_note','safety_note_id','safety_mentioned_at','include_transition','transition_note','updated_at')
        $values=@($columns|ForEach-Object{Get-WaaLiveSqlValue $Record $_})
        $updates=@($columns|Where-Object{$_-ne'driver_id'}|ForEach-Object{"$_=excluded.$_"})
        [void]$Sql.AppendLine("INSERT INTO driver_work_items($($columns-join',')) VALUES($($values-join',')) ON CONFLICT(driver_id) DO UPDATE SET $($updates-join',');")
    }
    elseif($EntityKey.StartsWith('call:')){
        $columns=@('driver_id','cycle_key','opened_at','updated_at','fuel_status','fuel_note','driver_eta','eta_status','eta_note','idle_plan','idle_percent_snapshot','idle_period_end_snapshot','load_help_status','load_help_note','conversation_wrap','completed_at')
        $values=@($columns|ForEach-Object{Get-WaaLiveSqlValue $Record $_})
        $updates=@($columns|Where-Object{$_-notin@('driver_id','cycle_key','opened_at')}|ForEach-Object{"$_=excluded.$_"})
        [void]$Sql.AppendLine("INSERT INTO driver_call_sessions($($columns-join',')) VALUES($($values-join',')) ON CONFLICT(driver_id,cycle_key) DO UPDATE SET $($updates-join',');")
    }
    elseif($EntityKey.StartsWith('note:')){
        $id=Get-WaaLiveSqlValue $Record 'id'
        if([bool]$Record._deleted){[void]$Sql.AppendLine("DELETE FROM driver_notes WHERE id=$id;")}
        else{$columns=@('id','driver_id','note','created_at');$values=@($columns|ForEach-Object{Get-WaaLiveSqlValue $Record $_});[void]$Sql.AppendLine("INSERT INTO driver_notes($($columns-join',')) VALUES($($values-join',')) ON CONFLICT(id) DO UPDATE SET driver_id=excluded.driver_id,note=excluded.note,created_at=excluded.created_at;")}
    }
    elseif($EntityKey.StartsWith('reminder:')){
        $id=Get-WaaLiveSqlValue $Record 'id'
        if([bool]$Record._deleted){[void]$Sql.AppendLine("DELETE FROM reminders WHERE id=$id;")}
        else{$columns=@('id','driver_id','text','due_at','completed_at','created_at');$values=@($columns|ForEach-Object{Get-WaaLiveSqlValue $Record $_});[void]$Sql.AppendLine("INSERT INTO reminders($($columns-join',')) VALUES($($values-join',')) ON CONFLICT(id) DO UPDATE SET driver_id=excluded.driver_id,text=excluded.text,due_at=excluded.due_at,completed_at=excluded.completed_at,created_at=excluded.created_at;")}
    }
    elseif($EntityKey.StartsWith('timer:')){
        $id=Get-WaaLiveSqlValue $Record 'id'
        if([bool]$Record._deleted){[void]$Sql.AppendLine("DELETE FROM timers WHERE id=$id;")}
        else{$columns=@('id','driver_id','label','target_at','completed_at','created_at');$values=@($columns|ForEach-Object{Get-WaaLiveSqlValue $Record $_});[void]$Sql.AppendLine("INSERT INTO timers($($columns-join',')) VALUES($($values-join',')) ON CONFLICT(id) DO UPDATE SET driver_id=excluded.driver_id,label=excluded.label,target_at=excluded.target_at,completed_at=excluded.completed_at,created_at=excluded.created_at;")}
    }
    elseif($EntityKey-eq'transition:1'){
        $body=Get-WaaLiveSqlValue $Record 'body';$manual=Get-WaaLiveSqlValue $Record 'is_manual';$updated=Get-WaaLiveSqlValue $Record 'updated_at'
        [void]$Sql.AppendLine("INSERT INTO transition_drafts(id,body,is_manual,updated_at) VALUES(1,$body,$manual,$updated) ON CONFLICT(id) DO UPDATE SET body=excluded.body,is_manual=excluded.is_manual,updated_at=excluded.updated_at;")
    }
    $keySql=ConvertTo-SqlLiteral $EntityKey
    [void]$Sql.AppendLine("INSERT INTO live_checkpoint_state(entity_key,revision,checkpointed_at) VALUES($keySql,$Revision,CURRENT_TIMESTAMP) ON CONFLICT(entity_key) DO UPDATE SET revision=excluded.revision,checkpointed_at=CURRENT_TIMESTAMP;")
}

function Invoke-WaaLiveCheckpoint {
    param([switch]$Force)
    if($null-eq$script:WaaLiveStore){return @{ok=$true;checkpointed=0;revision=0}}
    $dirty=Get-WaaLivePrefix 'dirty:'
    $events=Get-WaaLivePrefix 'event:'
    if(-not$dirty.Count-and-not$events.Count){$script:WaaLiveLastCheckpoint=Get-Date;$script:WaaLiveMutationCount=0;return @{ok=$true;checkpointed=0;revision=(Get-WaaLiveRevision)}}
    $sql=[Text.StringBuilder]::new([Math]::Max(4096,($dirty.Count+$events.Count)*700));[void]$sql.AppendLine('BEGIN IMMEDIATE;')
    $maxRevision=[long]0;$ackDirty=[Collections.Generic.List[string]]::new();$ackEvents=[Collections.Generic.List[string]]::new()
    foreach($entry in $dirty.GetEnumerator()){
        $entityKey=$entry.Key.Substring(6);$revision=[long]$entry.Value;$current=Get-WaaLiveRaw $entityKey
        if([string]::IsNullOrWhiteSpace($current)){continue}
        $record=ConvertFrom-WaaLiveJson $current
        Add-WaaLiveCheckpointSql -Sql $sql -EntityKey $entityKey -Record $record -Revision $revision
        if($revision-gt$maxRevision){$maxRevision=$revision};[void]$ackDirty.Add($entry.Key)
    }
    foreach($entry in $events.GetEnumerator()){
        $event=ConvertFrom-WaaLiveJson $entry.Value;$revision=[long]$event.revision
        $action=ConvertTo-SqlLiteral $event.action;$type=ConvertTo-SqlLiteral $event.entity_type;$id=ConvertTo-SqlLiteral $event.entity_id;$detail=ConvertTo-SqlLiteral $event.detail_json
        $occurredValue=if($event.occurred_at-is[datetime]){$event.occurred_at.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss',[Globalization.CultureInfo]::InvariantCulture)}else{([string]$event.occurred_at).Replace('T',' ')}
        $occurred=ConvertTo-SqlLiteral $occurredValue
        [void]$sql.AppendLine("INSERT INTO audit_history(occurred_at,action,entity_type,entity_id,detail_json) SELECT $occurred,$action,$type,$id,$detail WHERE NOT EXISTS(SELECT 1 FROM live_audit_events WHERE revision=$revision);")
        [void]$sql.AppendLine("INSERT OR IGNORE INTO live_audit_events(revision,audit_history_id) VALUES($revision,last_insert_rowid());")
        if($revision-gt$maxRevision){$maxRevision=$revision};[void]$ackEvents.Add($entry.Key)
    }
    $revisionSql=ConvertTo-SqlLiteral ([string]$maxRevision)
    [void]$sql.AppendLine("INSERT INTO settings(key,value,updated_at) VALUES('hybrid_checkpoint_revision',$revisionSql,CURRENT_TIMESTAMP) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=CURRENT_TIMESTAMP;")
    [void]$sql.AppendLine('COMMIT;')
    Invoke-Sql $sql.ToString() -AllowWrite|Out-Null
    $deletes=[Collections.Generic.List[string]]::new()
    foreach($key in $ackDirty){$snapshot=[string]$dirty[$key];if((Get-WaaLiveRaw $key)-eq$snapshot){[void]$deletes.Add($key)}}
    foreach($key in $ackEvents){[void]$deletes.Add($key)}
    Set-WaaLiveRawBatch -Deletes $deletes.ToArray();$script:WaaLiveStore.Sync()
    $script:WaaLiveLastCheckpoint=Get-Date;$script:WaaLiveMutationCount=0
    return @{ok=$true;checkpointed=$ackDirty.Count;events=$ackEvents.Count;revision=$maxRevision}
}

function Invoke-WaaLiveCheckpointIfDue {
    if($null-eq$script:WaaLiveStore){return}
    if($script:WaaLiveMutationCount-ge25-or((Get-Date)-$script:WaaLiveLastCheckpoint).TotalSeconds-ge2){Invoke-WaaLiveCheckpoint|Out-Null}
}

function Reset-WaaLiveDomainFromSqlite {
    $all=Get-WaaLivePrefix ''
    if($all.Count){Set-WaaLiveRawBatch -Deletes @($all.Keys)}
    $script:WaaLiveMutationCount=0
    return Initialize-WaaLiveDomain
}

function Get-WaaLiveHealth {
    $dirty=if($null-eq$script:WaaLiveStore){0}else{(Get-WaaLivePrefix 'dirty:').Count}
    $revision=if($null-eq$script:WaaLiveStore){0}else{Get-WaaLiveRevision}
    return @{engine='LMDB';online=($null-ne$script:WaaLiveStore);dirty=$dirty;revision=$revision;last_checkpoint=$script:WaaLiveLastCheckpoint.ToUniversalTime().ToString('s')}
}
