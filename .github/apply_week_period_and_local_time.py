from pathlib import Path


def read(path):
    return Path(path).read_text(encoding='utf-8').replace('\r\n', '\n')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8', newline='\n')


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected exactly one match, found {count}')
    return text.replace(old, new, 1)


# ---------------------------------------------------------------------------
# Dashboard coaching freshness belongs to the current Rolling 7-Day period.
# ---------------------------------------------------------------------------
p = 'src/Waa.psm1'
s = read(p)
s = replace_once(
    s,
    "SELECT d.id,d.full_name,d.pta_code,s.truck,s.engine_hours engine7,s.idle_hours idle7,s.p p7,\n",
    "SELECT d.id,d.full_name,d.pta_code,s.truck,s.period_start period_start7,s.period_end period_end7,s.engine_hours engine7,s.idle_hours idle7,s.p p7,\n",
    'dashboard current period fields'
)
s = replace_once(
    s,
    "EXISTS(SELECT 1 FROM driver_call_sessions c WHERE c.driver_id=d.id AND trim(coalesce(c.idle_plan,''))<>'') coached\n",
    "EXISTS(SELECT 1 FROM driver_call_sessions c WHERE c.driver_id=d.id AND trim(coalesce(c.idle_plan,''))<>'' AND c.idle_period_end_snapshot=s.period_end) coached\n",
    'dashboard coaching freshness'
)
write(p, s)


# ---------------------------------------------------------------------------
# Frontend: distinguish UTC system timestamps from local operational dates.
# ---------------------------------------------------------------------------
p = 'web/app.js'
s = read(p)
old_helper = """const displayDate = value => {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
};
"""
new_helper = old_helper + """
const displayUtcDate = value => {
  if (!value) return '—';
  const text = String(value).trim();
  const hasZone = /(?:Z|[+-]\\d{2}:?\\d{2})$/i.test(text);
  const utcText = hasZone ? text : `${text.replace(' ', 'T')}Z`;
  const date = new Date(utcText);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
};
"""
s = replace_once(s, old_helper, new_helper, 'UTC display helper')

# System-generated UTC timestamps: convert to the browser/workstation local zone.
for old, new, label in [
    ("displayDate(item.source_modified_utc)", "displayUtcDate(item.source_modified_utc)", 'report source modified display'),
    ("displayDate(item.imported_at)", "displayUtcDate(item.imported_at)", 'report imported display'),
    ("displayDate(row.talked_at)", "displayUtcDate(row.talked_at)", 'idle popup talked display'),
    ("displayDate(lastTalk.talked_at)", "displayUtcDate(lastTalk.talked_at)", 'idle attention talked display'),
    ("displayDate(latest.talked_at)", "displayUtcDate(latest.talked_at)", 'idle latest talked display'),
    ("displayDate(latestConversation.talked_at)", "displayUtcDate(latestConversation.talked_at)", 'idle card talked display'),
    ("displayDate(item.talked_at)", "displayUtcDate(item.talked_at)", 'driver card idle history talked display'),
    ("displayDate(item.occurred_at)", "displayUtcDate(item.occurred_at)", 'daily activity timestamp display'),
    ("displayDate(note.created_at)", "displayUtcDate(note.created_at)", 'driver note timestamp display'),
    ("displayDate(driver.observed_at)", "displayUtcDate(driver.observed_at)", 'driver snapshot freshness display'),
    ("displayDate(data.updated_at)", "displayUtcDate(data.updated_at)", 'transition updated display'),
]:
    if old in s:
        s = s.replace(old, new)

old_attention = """        const history = groups.get(Number(driver.id)) || [];
        const lastTalk = history[0];
        const historyCopy = history.length
          ? `${history.length} prior conversation${history.length === 1 ? '' : 's'} · last ${displayUtcDate(lastTalk.talked_at)}`
          : 'No idle coaching history yet';
        return `<button class=\"idle-attention-row\" data-idle-attention-driver=\"${driver.id}\" type=\"button\">
          <span class=\"idle-attention-rank\">${index + 1}</span>
          <span class=\"idle-attention-driver\"><b>${esc(driver.truck || 'No truck')} · ${esc(driver.full_name)}</b><small>${esc(driver.pta_code || 'No PTA code')}</small></span>
          <span class=\"idle-attention-history ${history.length ? 'coached' : 'new'}\"><b>${history.length ? 'History on file' : 'Needs first idle talk'}</b><small>${esc(historyCopy)}</small></span>
          <span class=\"idle-attention-score\"><b>${fmtPercent(driver.p7)}</b><small>${driver.p28 == null ? '28D No Data' : `${fmtPercent(driver.p28)} 28D`}</small></span>
          <strong>Open Work Card →</strong>
        </button>`;
"""
new_attention = """        const history = groups.get(Number(driver.id)) || [];
        const currentPeriod = String(driver.period_end7 || '').slice(0, 10);
        const currentHistory = currentPeriod
          ? history.filter(row => String(row.period_end || '').slice(0, 10) === currentPeriod)
          : [];
        const lastTalk = currentHistory[0];
        const periodLabel = currentPeriod
          ? new Date(`${currentPeriod}T00:00:00`).toLocaleDateString([], { month: 'short', day: 'numeric' })
          : null;
        const historyCopy = currentHistory.length
          ? `${currentHistory.length} conversation${currentHistory.length === 1 ? '' : 's'} for 7D ending ${periodLabel} · last ${displayUtcDate(lastTalk.talked_at)}`
          : history.length
            ? `Previous coaching is history · current 7D${periodLabel ? ` ends ${periodLabel}` : ''}`
            : `No coaching yet for current 7D${periodLabel ? ` ending ${periodLabel}` : ''}`;
        return `<button class=\"idle-attention-row\" data-idle-attention-driver=\"${driver.id}\" type=\"button\">
          <span class=\"idle-attention-rank\">${index + 1}</span>
          <span class=\"idle-attention-driver\"><b>${esc(driver.truck || 'No truck')} · ${esc(driver.full_name)}</b><small>${esc(driver.pta_code || 'No PTA code')}</small></span>
          <span class=\"idle-attention-history ${currentHistory.length ? 'coached' : 'new'}\"><b>${currentHistory.length ? 'Coached for current 7D' : 'Needs idle talk this 7D'}</b><small>${esc(historyCopy)}</small></span>
          <span class=\"idle-attention-score\"><b>${fmtPercent(driver.p7)}</b><small>${driver.p28 == null ? '28D No Data' : `${fmtPercent(driver.p28)} 28D`}</small></span>
          <strong>Open Work Card →</strong>
        </button>`;
"""
s = replace_once(s, old_attention, new_attention, 'current-week idle attention history')
write(p, s)


# ---------------------------------------------------------------------------
# Asset version forces browsers to load the new JS after a source refresh.
# ---------------------------------------------------------------------------
p = 'web/index.html'
s = read(p)
s = s.replace('styles.css?v=20260811.17', 'styles.css?v=20260811.18')
s = s.replace('app.js?v=20260811.17', 'app.js?v=20260811.18')
write(p, s)


# ---------------------------------------------------------------------------
# Regression coverage: current-period coaching expires when the 7D period changes,
# and UI uses explicit local conversion for stored UTC system/report timestamps.
# ---------------------------------------------------------------------------
p = 'tests/Run-Tests.ps1'
s = read(p)
old_insert = "INSERT INTO drivers(full_name,pta_code) VALUES('Coached Driver','COACHEDD');INSERT INTO idle_periods(driver_id,truck,period_start,period_end,engine_hours,idle_hours) VALUES(last_insert_rowid(),'COACHED','2026-08-03','2026-08-09',10,8);INSERT INTO driver_call_sessions(driver_id,cycle_key,idle_plan) VALUES((SELECT id FROM drivers WHERE pta_code='COACHEDD'),'COACHED|2026-08-09','Reduce long-wait idling');"
new_insert = "INSERT INTO drivers(full_name,pta_code) VALUES('Coached Driver','COACHEDD');INSERT INTO idle_periods(driver_id,truck,period_start,period_end,engine_hours,idle_hours) VALUES(last_insert_rowid(),'COACHED','2026-08-03','2026-08-09',10,8);INSERT INTO driver_call_sessions(driver_id,cycle_key,idle_plan,idle_percent_snapshot,idle_period_end_snapshot) VALUES((SELECT id FROM drivers WHERE pta_code='COACHEDD'),'COACHED|2026-08-09','Reduce long-wait idling',80,'2026-08-09');"
s = replace_once(s, old_insert, new_insert, 'coached driver current-period fixture')

old_assert = "Assert ($coachingDash.coaching.eligible-eq2-and$coachingDash.coaching.coached-eq1-and[double]$coachingDash.coaching.percent-eq50) 'dashboard reports coached share of drivers currently above 50% weekly idle';"
new_assert = old_assert + "$expiredCoachId=[int](Invoke-Sql \"SELECT id FROM drivers WHERE pta_code='COACHEDD';\");Invoke-Sql \"UPDATE driver_call_sessions SET idle_period_end_snapshot='2026-08-02' WHERE driver_id=$expiredCoachId AND trim(coalesce(idle_plan,''))<>'';\" -AllowWrite|Out-Null;$expiredDash=Get-Dashboard;$expiredRow=@($expiredDash.drivers|Where-Object{$_.id-eq$expiredCoachId})[0];Assert ([int]$expiredRow.coached-eq0) 'above-50 coaching falls out when its stored 7-day period is no longer current';Invoke-Sql \"UPDATE driver_call_sessions SET idle_period_end_snapshot='2026-08-09' WHERE driver_id=$expiredCoachId AND trim(coalesce(idle_plan,''))<>'';\" -AllowWrite|Out-Null;"
s = replace_once(s, old_assert, new_assert, 'current-period coaching expiry regression')

old_static = "Assert ($serverSource.Contains('/api/idle-coaching')-and$appSource.Contains('idleCoachingLog')) 'Idle Coaching tab is integrated through the normal server and client routing';"
new_static = old_static + "Assert ($appSource.Contains('displayUtcDate')-and$appSource.Contains('currentHistory')-and$appSource.Contains('period_end7')) 'system report timestamps display in workstation local time and idle attention is scoped to the current 7-day period';"
s = replace_once(s, old_static, new_static, 'time and coaching UI regression')
write(p, s)

print('Applied report-period coaching freshness and local timestamp display.')
