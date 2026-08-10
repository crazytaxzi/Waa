from pathlib import Path


def read(path):
    return Path(path).read_text(encoding='utf-8').replace('\r\n', '\n')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8', newline='\n')


# Group the existing flat idle-only history into one driver review card, using the
# same review modal surface already owned by Daily Review.
p = 'web/app.js'
s = read(p)

state_old = "  activityRowsByDriver: new Map()\n};"
state_new = "  activityRowsByDriver: new Map(),\n  idleRowsByDriver: new Map()\n};"
if state_old not in s:
    raise SystemExit('state insertion point not found')
s = s.replace(state_old, state_new, 1)

start = s.find('async function idleCoachingLog() {')
end = s.find('async function organizer() {', start)
if start < 0 or end < 0:
    raise SystemExit('Idle Coaching page boundaries not found')

replacement = r'''function openIdleCoachingSummary(driverId) {
  const rows = state.idleRowsByDriver.get(Number(driverId)) || [];
  if (!rows.length) return;
  const driver = rows[0];
  const scores = rows.map(row => Number(row.idle_percent)).filter(Number.isFinite);
  const latestScore = Number(driver.idle_percent);
  const highScore = scores.length ? Math.max(...scores) : null;
  const over50 = scores.filter(score => score > 50).length;
  $('#activityModalBody').innerHTML = `
    <header class="activity-modal-head"><div><p class="eyebrow">Idle coaching history</p><h2 id="activityModalTitle">${esc(driver.truck || 'No truck')} · ${esc(driver.full_name)}</h2><p>${rows.length} idle conversation${rows.length === 1 ? '' : 's'} recorded for this driver</p></div><button class="open" data-id="${driver.driver_id}" type="button">Open Driver Work Card</button></header>
    <div class="activity-summary-chips">
      <span><b>${rows.length}</b> conversations</span>
      <span><b>${fmtPercent(Number.isFinite(latestScore) ? latestScore : null)}</b> latest 7D</span>
      <span><b>${fmtPercent(highScore)}</b> highest 7D</span>
      <span><b>${over50}</b> over 50%</span>
    </div>
    <div class="activity-popup-list idle-popup-list">${rows.map(row => {
      const score = Number(row.idle_percent);
      const tone = Number.isFinite(score) && score > 50 ? 'hot' : 'good';
      const period = row.period_end ? new Date(row.period_end).toLocaleDateString([], { month: 'short', day: 'numeric', year: 'numeric' }) : null;
      return `<article class="activity-row idle-summary-row">
        <time>${esc(displayDate(row.talked_at))}</time>
        <div class="idle-summary-score ${tone}"><b>${fmtPercent(row.idle_percent)}</b><small>${period ? `7D ending ${esc(period)}` : 'No idle snapshot'}</small></div>
        <div><b>${esc(row.idle_plan)}</b><small>${esc(row.truck || 'No truck')} · ${esc(row.pta_code || 'No PTA code')}</small></div>
      </article>`;
    }).join('')}</div>`;
  $('#activityModal').classList.remove('hidden');
  document.body.classList.add('modal-open');
}

async function idleCoachingLog() {
  const rows = await cachedApi('/api/idle-coaching', 5000);
  const groups = new Map();
  rows.forEach(row => {
    const id = Number(row.driver_id);
    if (!groups.has(id)) groups.set(id, []);
    groups.get(id).push(row);
  });
  state.idleRowsByDriver = groups;
  const groupSearch = new Map([...groups].map(([id, conversations]) => [
    id,
    conversations.map(row => `${row.full_name} ${row.truck} ${row.pta_code} ${row.idle_plan}`).join(' ').toLowerCase()
  ]));
  const above50 = rows.filter(row => Number.isFinite(Number(row.idle_percent)) && Number(row.idle_percent) > 50).length;
  const latest = rows[0];

  $('#app').innerHTML = pageHead('Idle Coaching', 'One driver card for every driver you coached on idle. Open a card to review every idle conversation with that driver.', 'WAA // Coaching Record') + `
    <section class="idle-log-summary">
      <div class="idle-log-metric"><span>Conversations</span><b>${rows.length}</b><small>Idle discussions recorded</small></div>
      <div class="idle-log-metric"><span>Drivers</span><b>${groups.size}</b><small>Driver coaching records</small></div>
      <div class="idle-log-metric"><span>Over 50% at talk</span><b>${above50}</b><small>Conversations above target</small></div>
      <div class="idle-log-metric"><span>Most recent</span><b>${latest ? esc(displayDate(latest.talked_at)) : '—'}</b><small>${latest ? esc(latest.full_name) : 'No coaching recorded yet'}</small></div>
    </section>
    <section class="glass-panel idle-log-panel">
      <div class="table-toolbar">
        <div class="searchbox"><span aria-hidden="true">⌕</span><input id="idleLogSearch" placeholder="Search driver, truck, or anything discussed about idle"></div>
        <span id="idleLogCount" class="queue-count"></span>
      </div>
      <div class="panel-title idle-log-title"><div><p class="eyebrow">One coaching record per driver</p><h3>Driver Idle Conversations</h3></div></div>
      <div id="idleLogList" class="activity-list idle-log-list"></div>
    </section>`;

  const draw = () => {
    const query = $('#idleLogSearch').value.trim().toLowerCase();
    const visible = [...groups].filter(([id]) => !query || groupSearch.get(id).includes(query));
    const conversationCount = visible.reduce((total, [, conversations]) => total + conversations.length, 0);
    $('#idleLogCount').textContent = `${visible.length} driver${visible.length === 1 ? '' : 's'} · ${conversationCount} conversation${conversationCount === 1 ? '' : 's'}`;
    setCardQueue(visible.map(([id]) => id));
    $('#idleLogList').innerHTML = visible.length ? visible.map(([id, conversations]) => {
      const latestConversation = conversations[0];
      const score = Number(latestConversation.idle_percent);
      const tone = Number.isFinite(score) && score > 50 ? 'hot' : 'good';
      return `<button class="activity-driver-card idle-driver-card" data-review-idle-driver="${id}" type="button">
        <span class="activity-driver-count">${conversations.length}</span>
        <span class="idle-driver-identity"><b>${esc(latestConversation.truck || 'No truck')} · ${esc(latestConversation.full_name)}</b><small>${esc(latestConversation.pta_code || 'No PTA code')} · Last coached ${esc(displayDate(latestConversation.talked_at))}</small></span>
        <span class="idle-driver-preview"><small>Latest idle conversation</small><b>${esc(latestConversation.idle_plan)}</b></span>
        <span class="idle-driver-score ${tone}"><b>${fmtPercent(latestConversation.idle_percent)}</b><small>latest 7D</small></span>
        <strong>Review Coaching →</strong>
      </button>`;
    }).join('') : '<p class="empty-copy idle-log-empty">No driver idle coaching records match this search.</p>';
  };

  $('#idleLogSearch').addEventListener('input', debounce(draw));
  draw();
}

'''
s = s[:start] + replacement + s[end:]

click_old = "  const reviewDriver = event.target.closest('[data-review-driver]');\n  if (reviewDriver) { openActivitySummary(reviewDriver.dataset.reviewDriver); return; }"
click_new = "  const idleReviewDriver = event.target.closest('[data-review-idle-driver]');\n  if (idleReviewDriver) { openIdleCoachingSummary(idleReviewDriver.dataset.reviewIdleDriver); return; }\n  const reviewDriver = event.target.closest('[data-review-driver]');\n  if (reviewDriver) { openActivitySummary(reviewDriver.dataset.reviewDriver); return; }"
if click_old not in s:
    raise SystemExit('review click insertion point not found')
s = s.replace(click_old, click_new, 1)
write(p, s)


# Replace the old conversation-per-row styling; reuse Daily Review's card/modal
# primitives and keep only Idle Coaching-specific presentation here.
p = 'web/styles.css'
s = read(p)
marker = '\n.idle-log-summary{'
idx = s.find(marker)
if idx < 0:
    raise SystemExit('Idle Coaching CSS block not found')
s = s[:idx].rstrip() + r'''

.idle-log-summary{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;margin-bottom:16px}.idle-log-metric{position:relative;min-height:112px;padding:16px 18px;border:1px solid #344154;background:linear-gradient(145deg,#17202cdd,#0c121bdd);border-radius:14px;box-shadow:var(--shadow);overflow:hidden}.idle-log-metric::after{content:"";position:absolute;left:0;right:0;bottom:0;height:2px;background:linear-gradient(90deg,var(--green),var(--blue),var(--purple));box-shadow:0 0 16px #36bfff55}.idle-log-metric span{display:block;color:var(--muted);font-size:10px;font-weight:800;letter-spacing:.12em;text-transform:uppercase}.idle-log-metric b{display:block;margin:7px 0 3px;color:var(--white);font:800 clamp(22px,2.3vw,34px) Bahnschrift,"Segoe UI",sans-serif;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.idle-log-metric small{color:var(--muted);font-size:11px}.idle-log-panel{padding:0}.idle-log-title{padding:16px 16px 0;margin-bottom:8px}.idle-log-list{padding:0 16px 16px}.idle-driver-card{grid-template-columns:48px minmax(220px,.85fr) minmax(280px,1.3fr) 110px auto}.idle-driver-identity b,.idle-driver-identity small,.idle-driver-preview b,.idle-driver-preview small,.idle-driver-score b,.idle-driver-score small{display:block}.idle-driver-identity small,.idle-driver-preview small,.idle-driver-score small{margin-top:4px;color:var(--muted);font-size:10px}.idle-driver-preview{min-width:0}.idle-driver-preview b{margin-top:4px;color:#e5ebf2;font-size:11px;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.idle-driver-score{text-align:center}.idle-driver-score b{font:800 24px Bahnschrift,"Segoe UI",sans-serif}.idle-driver-score.good b,.idle-summary-score.good b{color:var(--green);text-shadow:0 0 14px #7cff3a38}.idle-driver-score.hot b,.idle-summary-score.hot b{color:var(--red);text-shadow:0 0 14px #ff405d38}.idle-log-empty{padding:24px!important;margin:0!important}.idle-popup-list .idle-summary-row{grid-template-columns:160px 120px minmax(0,1fr);align-items:start}.idle-summary-row time{white-space:normal;line-height:1.35}.idle-summary-score b,.idle-summary-score small{display:block}.idle-summary-score b{font:800 22px Bahnschrift,"Segoe UI",sans-serif}.idle-summary-score small{margin-top:3px;color:var(--muted);font-size:9px}.idle-summary-row>div:last-child>b{display:block;color:#edf2f7;font-size:12px;line-height:1.5}.idle-summary-row>div:last-child>small{display:block;margin-top:5px;color:var(--muted);font-size:10px}
@media(max-width:1200px){.idle-log-summary{grid-template-columns:repeat(2,minmax(0,1fr))}.idle-driver-card{grid-template-columns:48px minmax(210px,1fr) 100px auto}.idle-driver-preview{grid-column:2/5}.idle-driver-preview b{white-space:normal}.idle-popup-list .idle-summary-row{grid-template-columns:140px 110px 1fr}}
@media(max-width:700px){.idle-log-summary{grid-template-columns:1fr 1fr}.idle-log-panel .table-toolbar{align-items:stretch;flex-direction:column}.idle-log-panel .searchbox{min-width:0}.idle-log-panel .queue-count{text-align:left}.idle-driver-card{grid-template-columns:44px 1fr auto;gap:10px}.idle-driver-preview{grid-column:1/-1}.idle-driver-score{grid-column:3;grid-row:1/3}.idle-driver-card>strong{display:none}.idle-popup-list .idle-summary-row{grid-template-columns:1fr}.idle-summary-row time{color:var(--blue)}}
'''
write(p, s)


# Bump static assets so the work PC cannot keep the previous flat-list client.
p = 'web/index.html'
s = read(p)
s = s.replace('styles.css?v=20260810.15', 'styles.css?v=20260810.16')
s = s.replace('app.js?v=20260810.15', 'app.js?v=20260810.16')
write(p, s)


# Regression expectations: the Idle Coaching route remains canonical and the UI
# now groups the flat idle-only data into one review card per driver.
p = 'tests/Run-Tests.ps1'
s = read(p)
old = "Assert ($serverSource.Contains('/api/idle-coaching')-and$appSource.Contains('idleCoachingLog')-and$appSource.Contains('idle-log-note')) 'Idle Coaching tab is integrated through the normal server and client routing';"
new = "Assert ($serverSource.Contains('/api/idle-coaching')-and$appSource.Contains('idleCoachingLog')) 'Idle Coaching tab is integrated through the normal server and client routing';Assert ($appSource.Contains('idleRowsByDriver: new Map()')-and$appSource.Contains('openIdleCoachingSummary')-and$appSource.Contains('data-review-idle-driver')-and$appSource.Contains('One coaching record per driver')-and-not$appSource.Contains('idle-log-entry open')) 'Idle Coaching groups every driver into one review card with all idle conversations';"
if old not in s:
    raise SystemExit('Idle Coaching frontend regression assertion not found')
s = s.replace(old, new, 1)
write(p, s)
