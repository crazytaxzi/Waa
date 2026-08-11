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


# Dashboard: a coaching note counts as current only for the same Rolling 7-Day period.
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


# Frontend: system-generated timestamps are UTC in storage, but display in workstation local time.
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

for old, new in [
    ("displayDate(item.source_modified_utc)", "displayUtcDate(item.source_modified_utc)"),
    ("displayDate(item.imported_at)", "displayUtcDate(item.imported_at)"),
    ("displayDate(row.talked_at)", "displayUtcDate(row.talked_at)"),
    ("displayDate(lastTalk.talked_at)", "displayUtcDate(lastTalk.talked_at)"),
    ("displayDate(latest.talked_at)", "displayUtcDate(latest.talked_at)"),
    ("displayDate(latestConversation.talked_at)", "displayUtcDate(latestConversation.talked_at)"),
    ("displayDate(item.talked_at)", "displayUtcDate(item.talked_at)"),
    ("displayDate(item.occurred_at)", "displayUtcDate(item.occurred_at)"),
    ("displayDate(note.created_at)", "displayUtcDate(note.created_at)"),
    ("displayDate(driver.observed_at)", "displayUtcDate(driver.observed_at)"),
    ("displayDate(data.updated_at)", "displayUtcDate(data.updated_at)"),
]:
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
s = replace_once(s, old_attention, new_attention, 'current-period idle attention history')
write(p, s)


# Force the updated frontend to load after replacing the app folder.
p = 'web/index.html'
s = read(p)
s = replace_once(s, 'styles.css?v=20260811.17', 'styles.css?v=20260811.18', 'CSS asset version')
s = replace_once(s, 'app.js?v=20260811.17', 'app.js?v=20260811.18', 'JS asset version')
write(p, s)


# Regression: the existing test already saves an exact 7D snapshot through Save-WaaConversation.
# After the next 7D period is added, it must remain historical but stop counting as current coaching.
p = 'tests/Run-Tests.ps1'
s = read(p)
old_after_report = "Assert ([double]$afterNewReport.idle_percent-eq80-and[string]$afterNewReport.period_end-eq'2026-08-09') 'stored idle coaching percentage does not drift when a newer idle report arrives';"
new_after_report = old_after_report + "$periodDash=Get-Dashboard;$periodDriver=@($periodDash.drivers|Where-Object{$_.id-eq$coachedId})[0];Assert ([int]$periodDriver.coached-eq0-and[string]$periodDriver.period_end7-eq'2026-08-16') 'above-50 coaching falls out when a newer Rolling 7-Day period becomes current';"
s = replace_once(s, old_after_report, new_after_report, 'current-period coaching expiry regression')

old_static = "Assert ($serverSource.Contains('/api/idle-coaching')-and$appSource.Contains('idleCoachingLog')) 'Idle Coaching tab is integrated through the normal server and client routing';"
new_static = old_static + "Assert ($appSource.Contains('displayUtcDate')-and$appSource.Contains('currentHistory')-and$appSource.Contains('period_end7')) 'system report timestamps display in workstation local time and idle attention is scoped to the current 7-day period';"
s = replace_once(s, old_static, new_static, 'time and coaching UI regression')
write(p, s)

print('Applied report-period coaching freshness and local timestamp display.')
