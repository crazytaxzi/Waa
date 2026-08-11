from pathlib import Path


def read(path):
    return Path(path).read_text(encoding='utf-8').replace('\r\n', '\n')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8', newline='\n')


def replace_once(text, old, new, label):
    if text.count(old) != 1:
        raise SystemExit(f'{label}: expected exactly one match, found {text.count(old)}')
    return text.replace(old, new, 1)


def replace_between(text, start_marker, end_marker, replacement, label):
    start = text.find(start_marker)
    if start < 0:
        raise SystemExit(f'{label}: start marker not found')
    end = text.find(end_marker, start)
    if end < 0:
        raise SystemExit(f'{label}: end marker not found')
    return text[:start] + replacement + text[end:]


# ---------------------------------------------------------------------------
# SQLite schema + historical coaching queries
# ---------------------------------------------------------------------------
p = 'src/Waa.psm1'
s = read(p)
s = replace_once(
    s,
    "idle_plan TEXT, load_help_status TEXT NOT NULL DEFAULT 'Unknown'",
    "idle_plan TEXT, idle_percent_snapshot REAL, idle_period_end_snapshot TEXT, load_help_status TEXT NOT NULL DEFAULT 'Unknown'",
    'fresh call-session schema snapshot columns'
)

migration_anchor = """    if (@($callSessionColumns | Where-Object { $_.name -eq 'completed_at' }).Count -eq 0) {
        Invoke-Sql 'ALTER TABLE driver_call_sessions ADD COLUMN completed_at TEXT;' -AllowWrite | Out-Null
    }
"""
migration_new = migration_anchor + """    if (@($callSessionColumns | Where-Object { $_.name -eq 'idle_percent_snapshot' }).Count -eq 0) {
        Invoke-Sql 'ALTER TABLE driver_call_sessions ADD COLUMN idle_percent_snapshot REAL;' -AllowWrite | Out-Null
    }
    if (@($callSessionColumns | Where-Object { $_.name -eq 'idle_period_end_snapshot' }).Count -eq 0) {
        Invoke-Sql 'ALTER TABLE driver_call_sessions ADD COLUMN idle_period_end_snapshot TEXT;' -AllowWrite | Out-Null
    }
"""
s = replace_once(s, migration_anchor, migration_new, 'call-session migration')
s = replace_once(
    s,
    "    Invoke-Sql 'INSERT OR IGNORE INTO schema_version(version) VALUES(3);' -AllowWrite | Out-Null\n",
    "    Invoke-Sql 'INSERT OR IGNORE INTO schema_version(version) VALUES(3);' -AllowWrite | Out-Null\n    Invoke-Sql 'INSERT OR IGNORE INTO schema_version(version) VALUES(4);' -AllowWrite | Out-Null\n",
    'schema version 4'
)

card_start = "  'idle_coaching',json(coalesce((SELECT json_group_array(json_object('cycle_key',cycle_key,'talked_at',talked_at,'idle_plan',idle_plan,"
card_end = "  'bols',json(coalesce("
card_replacement = r"""  'idle_coaching',json(coalesce((SELECT json_group_array(json_object('cycle_key',cycle_key,'talked_at',talked_at,'idle_plan',idle_plan,
    'idle_percent',idle_percent,'period_end',period_end,'snapshot_captured',snapshot_captured)) FROM (
      SELECT c.cycle_key,coalesce(c.completed_at,c.updated_at,c.opened_at) talked_at,c.idle_plan,
        c.idle_percent_snapshot idle_percent,c.idle_period_end_snapshot period_end,
        CASE WHEN c.idle_percent_snapshot IS NOT NULL OR c.idle_period_end_snapshot IS NOT NULL THEN 1 ELSE 0 END snapshot_captured
      FROM driver_call_sessions c
      WHERE c.driver_id=$Id AND trim(coalesce(c.idle_plan,''))<>''
      ORDER BY coalesce(c.completed_at,c.updated_at,c.opened_at) DESC,c.id DESC LIMIT 12
    )),'[]')),
"""
s = replace_between(s, card_start, card_end, card_replacement, 'driver card idle coaching query')

log_start = 'function Get-IdleCoachingLog {'
log_end = 'function Get-Organizer {'
log_replacement = r'''function Get-IdleCoachingLog {
    if(Test-WaaLiveStoreOnline){Invoke-WaaLiveCheckpoint -Force|Out-Null}
    $sql=@'
WITH coaching AS (
  SELECT c.id,c.driver_id,c.cycle_key,
         coalesce(c.completed_at,c.updated_at,c.opened_at) talked_at,
         trim(c.idle_plan) idle_plan,
         c.idle_percent_snapshot idle_percent,
         c.idle_period_end_snapshot period_end,
         CASE WHEN c.idle_percent_snapshot IS NOT NULL OR c.idle_period_end_snapshot IS NOT NULL THEN 1 ELSE 0 END snapshot_captured,
         CASE WHEN instr(c.cycle_key,'|')>0 THEN substr(c.cycle_key,1,instr(c.cycle_key,'|')-1) ELSE '' END cycle_truck
  FROM driver_call_sessions c
  WHERE trim(coalesce(c.idle_plan,''))<>''
)
SELECT c.id,c.driver_id,d.full_name,d.pta_code,
       CASE WHEN trim(coalesce(c.cycle_truck,''))='' OR c.cycle_truck='NO-TRUCK' THEN
         coalesce((SELECT th.truck FROM truck_history th
                   WHERE th.driver_id=c.driver_id AND th.observed_at<=c.talked_at
                   ORDER BY th.observed_at DESC,th.id DESC LIMIT 1),'')
       ELSE c.cycle_truck END truck,
       c.talked_at,c.idle_plan,c.idle_percent,c.period_end,c.snapshot_captured
FROM coaching c
JOIN drivers d ON d.id=c.driver_id
ORDER BY c.talked_at DESC,c.id DESC;
'@
    return Invoke-Sql $sql -Json
}

'''
s = replace_between(s, log_start, log_end, log_replacement, 'Idle Coaching log query')
write(p, s)


# ---------------------------------------------------------------------------
# LMDB live call records + SQLite checkpoints
# ---------------------------------------------------------------------------
p = 'src/LiveStore.ps1'
s = read(p)
call_start = 'function Set-WaaLiveCallField {'
call_end = 'function Add-WaaLiveFollowup {'
call_replacement = r'''function Set-WaaLiveCallField {
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

'''
s = replace_between(s, call_start, call_end, call_replacement, 'live call setter')
s = replace_once(
    s,
    "$columns=@('driver_id','cycle_key','opened_at','updated_at','fuel_status','fuel_note','driver_eta','eta_status','eta_note','idle_plan','load_help_status','load_help_note','conversation_wrap','completed_at')",
    "$columns=@('driver_id','cycle_key','opened_at','updated_at','fuel_status','fuel_note','driver_eta','eta_status','eta_note','idle_plan','idle_percent_snapshot','idle_period_end_snapshot','load_help_status','load_help_note','conversation_wrap','completed_at')",
    'live checkpoint call columns'
)
write(p, s)


# ---------------------------------------------------------------------------
# Conversation domain captures the current idle snapshot exactly when idle is saved
# ---------------------------------------------------------------------------
p = 'src/Conversation.ps1'
s = read(p)
save_marker = 'function Save-WaaConversation {'
helper = r'''function Get-WaaIdleSnapshot {
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

'''
if helper not in s:
    if save_marker not in s:
        raise SystemExit('conversation save insertion point not found')
    s = s.replace(save_marker, helper + save_marker, 1)

old_return = """    $cycle = Get-WaaConversationCycle -DriverId $DriverId
    return Set-WaaLiveCallField -DriverId $DriverId -CycleKey $cycle -Field $field -Value $Body.value
}
"""
new_return = """    $cycle = Get-WaaConversationCycle -DriverId $DriverId
    if ($field -eq 'idle_plan') {
        if ([string]::IsNullOrWhiteSpace([string]$Body.value)) {
            return Set-WaaLiveCallField -DriverId $DriverId -CycleKey $cycle -Field $field -Value $Body.value -CaptureIdleSnapshot -IdlePercentSnapshot $null -IdlePeriodEndSnapshot $null
        }
        $snapshot = Get-WaaIdleSnapshot -DriverId $DriverId
        return Set-WaaLiveCallField -DriverId $DriverId -CycleKey $cycle -Field $field -Value $Body.value -CaptureIdleSnapshot -IdlePercentSnapshot $snapshot.percent -IdlePeriodEndSnapshot $snapshot.period_end
    }
    return Set-WaaLiveCallField -DriverId $DriverId -CycleKey $cycle -Field $field -Value $Body.value
}
"""
s = replace_once(s, old_return, new_return, 'idle snapshot capture on conversation save')
write(p, s)


# ---------------------------------------------------------------------------
# Driver Work Card: fewer visible entry boxes, preserve optional detail on demand
# ---------------------------------------------------------------------------
p = 'web/app.js'
s = read(p)
area_helper = """function conversationArea(label, field, value, placeholder = '') {
  return `<label class=\"field\"><span>${esc(label)}</span><textarea data-conversation=\"${esc(field)}\" placeholder=\"${esc(placeholder)}\">${esc(value ?? '')}</textarea></label>`;
}

"""
optional_helper = area_helper + """function optionalDetail(summary, body, open = false) {
  return `<details class=\"inline-detail optional-detail\" ${open ? 'open' : ''}><summary>${esc(summary)}</summary><div class=\"optional-detail-body\">${body}</div></details>`;
}

"""
s = replace_once(s, area_helper, optional_helper, 'optional detail UI helper')

steps_start = 'function completedCallSteps(card, conversation) {'
steps_end = 'function showCardStep(number, focus = true) {'
steps_replacement = r'''function completedCallSteps(card, conversation) {
  const work = card.work || {};
  return new Set([
    (conversation.fuel_status && conversation.fuel_status !== 'Unknown') || conversation.fuel_note ? 1 : 0,
    conversation.driver_eta || (conversation.eta_status && conversation.eta_status !== 'Unknown') ? 2 : 0,
    conversation.idle_plan ? 3 : 0,
    (conversation.load_help_status && conversation.load_help_status !== 'Unknown') ||
      (work.preplan_response && work.preplan_response !== 'Unknown') ||
      (work.routing_status && work.routing_status !== 'Unknown') ? 4 : 0,
    (work.expected_work && work.expected_work !== 'Unknown') ||
      (work.home_status && work.home_status !== 'Unknown') || work.home_reason ? 5 : 0,
    !card.bols?.length || card.bols.every(item => item.mentioned_at) ? 6 : 0,
    conversation.conversation_wrap || work.safety_mentioned_at || work.include_transition ? 7 : 0
  ].filter(Boolean));
}

'''
s = replace_between(s, steps_start, steps_end, steps_replacement, 'simplified call step completion')

step1 = r'''    callStep(1, 'Fuel & Immediate Needs', 'Start with the driver, not the paperwork.', `
      <div class="field-grid">
        ${conversationSelect('Fuel looks…', 'fuel_status', conversation.fuel_status, ['Unknown', 'Good', 'Needs Fuel', 'Concern'])}
      </div>
      ${optionalDetail('Add fuel detail only if something needs attention', conversationText('Fuel detail', 'fuel_note', conversation.fuel_note, 'Fuel stop, card issue, mechanical concern…'), !!conversation.fuel_note)}`, 'green-step'),
'''
s = replace_between(s, "    callStep(1, 'Fuel & Immediate Needs'", "    callStep(2, 'ETA & Timing'", step1, 'Fuel step simplification')

step2 = r'''    callStep(2, 'ETA & Timing', 'Get their ETA and whether the timing looks healthy. Extra explanation is optional.', `
      <div class="context-ribbon"><span>Current PTA</span><b>${esc(driver.pta_raw || 'N/A')}</b><small>${esc(relative(driver.pta_at))}</small></div>
      <div class="field-grid">
        ${conversationText('Driver says ETA is…', 'driver_eta', conversation.driver_eta, 'Example: 14:30, 2 hours out, after fuel')}
        ${conversationSelect('Timing looks…', 'eta_status', conversation.eta_status, ['Unknown', 'On Track', 'Tight', 'Late'])}
      </div>
      ${optionalDetail('Add ETA detail only if it matters', conversationText('What is affecting it?', 'eta_note', conversation.eta_note, 'Traffic, shipper delay, weather…'), !!conversation.eta_note)}
      <details class="inline-detail"><summary>Adjust imported PTA only if needed</summary><label class="field"><span>Manual PTA observation</span><input data-action="pta" type="datetime-local" value="${esc(driver.pta_at?.slice(0, 16) || '')}"></label></details>`, 'blue-step'),
'''
s = replace_between(s, "    callStep(2, 'ETA & Timing'", "    callStep(3, 'Idle Coaching'", step2, 'ETA step simplification')

step4 = r'''    callStep(4, 'Help on the Load', 'Keep the main answers quick. Open a detail only when there is actually something to explain.', `
      <div class="field-grid">
        ${conversationSelect('Do they need help?', 'load_help_status', conversation.load_help_status, ['Unknown', 'No Help Needed', 'Needs Help', 'Follow Up'])}
      </div>
      ${optionalDetail('Add load-help detail', conversationText('What do they need from us?', 'load_help_note', conversation.load_help_note, 'Appointment, routing, customer, parking, equipment…'), !!conversation.load_help_note)}
      <div class="mini-workflow">
        <div><p class="eyebrow">Preplan</p><b>Source: ${esc(driver.planning_status)}</b>${selectField('Driver response', 'preplan_response', work.preplan_response, ['Unknown', 'Accepted', 'Denied'])}${optionalDetail('Add preplan note', textField('Anything to remember', 'preplan_note', work.preplan_note, 'Only if something matters'), !!work.preplan_note)}</div>
        <div><p class="eyebrow">Routing</p>${selectField('Routing looks…', 'routing_status', work.routing_status, ['Unknown', 'Accurate', 'Needs Correction'])}${optionalDetail('Add routing note', textField('What changed / what is needed', 'routing_note', work.routing_note, 'Only if something matters'), !!work.routing_note)}</div>
      </div>`, 'green-step'),
'''
s = replace_between(s, "    callStep(4, 'Help on the Load'", "    callStep(5, 'Home Time & Schedule'", step4, 'load step simplification')

step5 = r'''    callStep(5, 'Home Time & Schedule', 'A couple of quick choices are enough unless something needs follow-up.', `
      <div class="field-grid">
        ${selectField('Expected to work?', 'expected_work', work.expected_work, ['Unknown', 'Yes', 'No'])}
        ${selectField('Home-time picture', 'home_status', work.home_status, ['Unknown', 'OK', 'Concern'])}
      </div>
      ${optionalDetail('Add home-time detail', textField('Anything that needs action', 'home_reason', work.home_reason, 'Only capture what someone needs to know or do'), !!work.home_reason)}`, 'blue-step'),
'''
s = replace_between(s, "    callStep(5, 'Home Time & Schedule'", "    callStep(6, 'Quick Admin Close-Out'", step5, 'home step simplification')

step7 = r'''    callStep(7, 'Safety & Wrap', 'Finish like a human conversation: one useful reminder, then capture only what genuinely needs to survive the call.', `
      <div class="safety-box"><div><p class="eyebrow">Safety touch</p><p id="safety">Pick one useful note if it fits the conversation.</p></div><button id="random" type="button">New Safety Note</button></div>
      ${checkField('Safety note mentioned', 'safety_mentioned_at', work.safety_mentioned_at)}
      ${optionalDetail('Add a wrap-up note', conversationArea('Anything else worth remembering?', 'conversation_wrap', conversation.conversation_wrap, 'Only information that will help with the next follow-up.'), !!conversation.conversation_wrap)}
      <div class="wrap-grid">${checkField('Send to Transition', 'include_transition', work.include_transition)}${textField('Transition note', 'transition_note', work.transition_note, 'One concise handoff line')}</div>`, 'green-step')
'''
s = replace_between(s, "    callStep(7, 'Safety & Wrap'", "  ].join('');", step7, 'wrap step simplification')

s = replace_once(
    s,
    "<div class=\"idle-history-stat\"><b>${fmtPercent(item.idle_percent)}</b><small>${item.period_end ? `7D ending ${esc(new Date(item.period_end).toLocaleDateString([], { month: 'short', day: 'numeric' }))}` : 'Idle snapshot unavailable'}</small></div>",
    "<div class=\"idle-history-stat\"><b>${fmtPercent(item.idle_percent)}</b><small>${item.snapshot_captured ? (item.period_end ? `7D ending ${esc(new Date(item.period_end).toLocaleDateString([], { month: 'short', day: 'numeric' }))}` : 'Snapshot captured · No Data') : 'Legacy · idle % was not stored'}</small></div>",
    'driver card legacy idle snapshot label'
)

s = replace_once(
    s,
    "  const scores = rows.map(row => Number(row.idle_percent)).filter(Number.isFinite);\n  const latestScore = Number(driver.idle_percent);",
    "  const scores = rows.map(row => row.idle_percent == null ? null : Number(row.idle_percent)).filter(Number.isFinite);\n  const latestScore = driver.idle_percent == null ? null : Number(driver.idle_percent);",
    'coaching modal null-safe scores'
)
s = replace_once(
    s,
    "      const score = Number(row.idle_percent);\n      const tone = Number.isFinite(score) && score > 50 ? 'hot' : 'good';\n      const period = row.period_end ? new Date(row.period_end).toLocaleDateString([], { month: 'short', day: 'numeric', year: 'numeric' }) : null;",
    "      const score = row.idle_percent == null ? null : Number(row.idle_percent);\n      const tone = Number.isFinite(score) && score > 50 ? 'hot' : 'good';\n      const period = row.period_end ? new Date(row.period_end).toLocaleDateString([], { month: 'short', day: 'numeric', year: 'numeric' }) : null;",
    'coaching modal row score'
)
s = replace_once(
    s,
    "<div class=\"idle-summary-score ${tone}\"><b>${fmtPercent(row.idle_percent)}</b><small>${period ? `7D ending ${esc(period)}` : 'No idle snapshot'}</small></div>",
    "<div class=\"idle-summary-score ${tone}\"><b>${fmtPercent(row.idle_percent)}</b><small>${row.snapshot_captured ? (period ? `7D ending ${esc(period)}` : 'Snapshot captured · No Data') : 'Legacy · idle % was not stored'}</small></div>",
    'coaching modal legacy snapshot label'
)
s = replace_once(
    s,
    "      const score = Number(latestConversation.idle_percent);",
    "      const score = latestConversation.idle_percent == null ? null : Number(latestConversation.idle_percent);",
    'coaching driver card null-safe score'
)
write(p, s)


# ---------------------------------------------------------------------------
# Regression coverage: exact snapshot survives future reports; legacy stays honest
# ---------------------------------------------------------------------------
p = 'tests/Run-Tests.ps1'
s = read(p)
schema_assert = "Assert ((Invoke-Sql \"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='driver_call_sessions';\").Trim()-eq'1') 'call workflow state is owned by the core database schema';"
schema_new = schema_assert + "$callCols=@(Invoke-Sql 'PRAGMA table_info(driver_call_sessions);' -Json);Assert (@($callCols|Where-Object{$_.name-eq'idle_percent_snapshot'}).Count-eq1-and@($callCols|Where-Object{$_.name-eq'idle_period_end_snapshot'}).Count-eq1) 'call sessions persist exact idle coaching snapshots';"
s = replace_once(s, schema_assert, schema_new, 'snapshot schema regression')

coached_start = "  Invoke-Sql \"INSERT INTO drivers(full_name,pta_code) VALUES('Coached Driver','COACHEDD');"
coached_end = "  $card=Get-DriverCard $did;"
coached_block = r'''  Invoke-Sql "INSERT INTO drivers(full_name,pta_code) VALUES('Coached Driver','COACHEDD');INSERT INTO idle_periods(driver_id,truck,period_start,period_end,engine_hours,idle_hours) VALUES(last_insert_rowid(),'COACHED','2026-08-03','2026-08-09',10,8);" -AllowWrite|Out-Null;$coachedId=[int](Invoke-Sql "SELECT id FROM drivers WHERE pta_code='COACHEDD';");$savedIdle=Save-WaaConversation -DriverId $coachedId -Body @{field='idle_plan';value='Reduce long-wait idling'};Assert ([double]$savedIdle.idle_percent_snapshot-eq80-and[string]$savedIdle.idle_period_end_snapshot-eq'2026-08-09') 'idle coaching save captures the exact current 7-day snapshot';Invoke-WaaLiveCheckpoint -Force|Out-Null;$coachingDash=Get-Dashboard;Assert ($coachingDash.coaching.eligible-eq2-and$coachingDash.coaching.coached-eq1-and[double]$coachingDash.coaching.percent-eq50) 'dashboard reports coached share of drivers currently above 50% weekly idle';Invoke-Sql "INSERT INTO driver_call_sessions(driver_id,cycle_key,fuel_note) VALUES($coachedId,'COACHED|2026-08-10','Fuel only conversation');" -AllowWrite|Out-Null;$coachedCard=Get-DriverCard $coachedId;Assert ($coachedCard.idle_coaching.Count-eq1-and$coachedCard.idle_coaching[0].idle_plan-eq'Reduce long-wait idling'-and[double]$coachedCard.idle_coaching[0].idle_percent-eq80-and[int]$coachedCard.idle_coaching[0].snapshot_captured-eq1) 'driver card idle coaching history contains only idle discussion and its stored rolling snapshot';$idleLog=@(Get-IdleCoachingLog);$coachedLog=@($idleLog|Where-Object{$_.driver_id-eq$coachedId});Assert ($coachedLog.Count-eq1-and$coachedLog[0].idle_plan-eq'Reduce long-wait idling'-and[double]$coachedLog[0].idle_percent-eq80-and[string]$coachedLog[0].period_end-eq'2026-08-09') 'Idle Coaching tab uses the stored idle snapshot';Invoke-Sql "INSERT INTO idle_periods(driver_id,truck,period_start,period_end,engine_hours,idle_hours) VALUES($coachedId,'COACHED','2026-08-10','2026-08-16',10,2);" -AllowWrite|Out-Null;$afterNewReport=@(Get-IdleCoachingLog|Where-Object{$_.driver_id-eq$coachedId-and$_.idle_plan-eq'Reduce long-wait idling'})[0];Assert ([double]$afterNewReport.idle_percent-eq80-and[string]$afterNewReport.period_end-eq'2026-08-09') 'stored idle coaching percentage does not drift when a newer idle report arrives';Invoke-Sql "INSERT INTO driver_call_sessions(driver_id,cycle_key,idle_plan) VALUES($coachedId,'COACHED|LEGACY','Legacy coaching without snapshot');" -AllowWrite|Out-Null;$legacy=@(Get-IdleCoachingLog|Where-Object{$_.driver_id-eq$coachedId-and$_.idle_plan-eq'Legacy coaching without snapshot'})[0];Assert ($null-eq$legacy.idle_percent-and[int]$legacy.snapshot_captured-eq0) 'legacy coaching records do not invent an idle percentage that was never stored'
'''
s = replace_between(s, coached_start, coached_end, coached_block, 'idle coaching regression block')
write(p, s)

print('Applied exact idle snapshots and simplified driver call inputs.')
