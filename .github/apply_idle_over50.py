from pathlib import Path


def read(path):
    return Path(path).read_text(encoding='utf-8').replace('\r\n', '\n')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8', newline='\n')


p = 'web/app.js'
s = read(p)
start = s.find('async function idleCoachingLog() {')
end = s.find('\nasync function organizer() {', start)
if start < 0 or end < 0:
    raise SystemExit('Idle Coaching page boundaries not found')

replacement = r'''async function idleCoachingLog() {
  const [rows, dashboardData] = await Promise.all([
    cachedApi('/api/idle-coaching', 5000),
    cachedApi('/api/dashboard', 5000)
  ]);
  const groups = new Map();
  rows.forEach(row => {
    const id = Number(row.driver_id);
    if (!groups.has(id)) groups.set(id, []);
    groups.get(id).push(row);
  });
  state.idleRowsByDriver = groups;
  const currentOver50 = (dashboardData.drivers || [])
    .filter(driver => Number.isFinite(Number(driver.p7)) && Number(driver.p7) > 50)
    .sort((a, b) => Number(b.p7) - Number(a.p7) || String(a.full_name).localeCompare(String(b.full_name)));
  const groupSearch = new Map([...groups].map(([id, conversations]) => [
    id,
    conversations.map(row => `${row.full_name} ${row.truck} ${row.pta_code} ${row.idle_plan}`).join(' ').toLowerCase()
  ]));
  const latest = rows[0];

  $('#app').innerHTML = pageHead('Idle Coaching', 'Current drivers above 50% first, followed by one coaching-history card per driver.', 'WAA // Coaching Record') + `
    <section class="idle-log-summary">
      <div class="idle-log-metric"><span>Current over 50%</span><b>${currentOver50.length}</b><small>Latest Rolling 7-Day report</small></div>
      <div class="idle-log-metric"><span>Conversations</span><b>${rows.length}</b><small>Idle discussions recorded</small></div>
      <div class="idle-log-metric"><span>Drivers coached</span><b>${groups.size}</b><small>Drivers with idle history</small></div>
      <div class="idle-log-metric"><span>Most recent coaching</span><b>${latest ? esc(displayDate(latest.talked_at)) : '—'}</b><small>${latest ? esc(latest.full_name) : 'No coaching recorded yet'}</small></div>
    </section>
    <section class="glass-panel idle-attention-panel">
      <div class="panel-title"><div><p class="eyebrow">Current Rolling 7-Day attention</p><h3>Drivers Above 50%</h3></div><span>${currentOver50.length}</span></div>
      <p class="idle-attention-copy">Highest idle percentage first. Previous and Next in the Work Card will stay inside this current over-50 list.</p>
      <div class="idle-attention-list">${currentOver50.length ? currentOver50.map((driver, index) => {
        const history = groups.get(Number(driver.id)) || [];
        const lastTalk = history[0];
        const historyCopy = history.length
          ? `${history.length} prior conversation${history.length === 1 ? '' : 's'} · last ${displayDate(lastTalk.talked_at)}`
          : 'No idle coaching history yet';
        return `<button class="idle-attention-row" data-idle-attention-driver="${driver.id}" type="button">
          <span class="idle-attention-rank">${index + 1}</span>
          <span class="idle-attention-driver"><b>${esc(driver.truck || 'No truck')} · ${esc(driver.full_name)}</b><small>${esc(driver.pta_code || 'No PTA code')}</small></span>
          <span class="idle-attention-history ${history.length ? 'coached' : 'new'}"><b>${history.length ? 'History on file' : 'Needs first idle talk'}</b><small>${esc(historyCopy)}</small></span>
          <span class="idle-attention-score"><b>${fmtPercent(driver.p7)}</b><small>${driver.p28 == null ? '28D No Data' : `${fmtPercent(driver.p28)} 28D`}</small></span>
          <strong>Open Work Card →</strong>
        </button>`;
      }).join('') : '<p class="empty-copy idle-attention-empty">Nobody is above 50% on the latest Rolling 7-Day report.</p>'}</div>
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
    $('#idleLogList').innerHTML = visible.length ? visible.map(([id, conversations]) => {
      const latestConversation = conversations[0];
      const score = Number(latestConversation.idle_percent);
      const tone = Number.isFinite(score) && score > 50 ? 'hot' : 'good';
      return `<button class="activity-driver-card idle-driver-card" data-review-idle-driver="${id}" type="button">
        <span class="activity-driver-count">${conversations.length}</span>
        <span class="idle-driver-identity"><b>${esc(latestConversation.truck || 'No truck')} · ${esc(latestConversation.full_name)}</b><small>${esc(latestConversation.pta_code || 'No PTA code')} · Last coached ${esc(displayDate(latestConversation.talked_at))}</small></span>
        <span class="idle-driver-preview"><small>Latest idle conversation</small><b>${esc(latestConversation.idle_plan)}</b></span>
        <span class="idle-driver-score ${tone}"><b>${fmtPercent(latestConversation.idle_percent)}</b><small>latest 7D at talk</small></span>
        <strong>Review Coaching →</strong>
      </button>`;
    }).join('') : '<p class="empty-copy idle-log-empty">No driver idle coaching records match this search.</p>';
  };

  $('#idleLogSearch').addEventListener('input', debounce(draw));
  draw();
}
'''
s = s[:start] + replacement + s[end:]

click_marker = "  const idleReviewDriver = event.target.closest('[data-review-idle-driver]');\n"
if click_marker not in s:
    raise SystemExit('Idle Coaching click insertion point not found')
click_block = "  const idleAttentionDriver = event.target.closest('[data-idle-attention-driver]');\n  if (idleAttentionDriver) {\n    const queue = $$('[data-idle-attention-driver]').map(node => Number(node.dataset.idleAttentionDriver)).filter(Boolean);\n    setCardQueue(queue);\n    openCard(Number(idleAttentionDriver.dataset.idleAttentionDriver));\n    return;\n  }\n"
s = s.replace(click_marker, click_block + click_marker, 1)
write(p, s)

p = 'web/styles.css'
s = read(p)
marker = '\n/* Idle Coaching current over-50 queue */\n'
if marker not in s:
    s = s.rstrip() + r'''

/* Idle Coaching current over-50 queue */
.idle-attention-panel{margin-bottom:18px;padding:20px}.idle-attention-copy{margin:-5px 0 15px;color:var(--muted);font-size:12px}.idle-attention-list{display:grid;gap:8px}.idle-attention-row{display:grid;grid-template-columns:46px minmax(220px,.9fr) minmax(260px,1.15fr) 125px auto;align-items:center;gap:14px;width:100%;padding:14px 15px;border:1px solid #3a4658;background:linear-gradient(90deg,#ff405d08,#ffffff03 35%,transparent);color:var(--white);text-align:left;border-radius:11px;cursor:pointer;transition:.15s ease}.idle-attention-row:hover,.idle-attention-row:focus-visible{border-color:#ff405daa;background:linear-gradient(90deg,#ff405d10,#36bfff07 58%,#b34cff08);box-shadow:inset 3px 0 var(--red);transform:translateX(3px);outline:none}.idle-attention-rank{font:800 22px Consolas,monospace;color:var(--red);text-align:center}.idle-attention-driver b,.idle-attention-history b{display:block;color:var(--white)}.idle-attention-driver small,.idle-attention-history small,.idle-attention-score small{display:block;margin-top:3px;color:var(--muted);font-size:10px}.idle-attention-history.coached b{color:var(--green)}.idle-attention-history.new b{color:var(--amber)}.idle-attention-score{text-align:right}.idle-attention-score b{display:block;color:var(--red);font:800 25px Bahnschrift,"Segoe UI",sans-serif;text-shadow:0 0 14px #ff405d38}.idle-attention-row>strong{color:var(--blue);font-size:10px;letter-spacing:.07em;text-transform:uppercase;white-space:nowrap}.idle-attention-empty{padding:20px!important;margin:0!important}
@media(max-width:1050px){.idle-attention-row{grid-template-columns:42px 1fr 110px auto}.idle-attention-history{grid-column:2/5;grid-row:2}.idle-attention-score{grid-column:3;grid-row:1}}@media(max-width:700px){.idle-attention-row{grid-template-columns:38px 1fr auto;gap:10px}.idle-attention-score{grid-column:3}.idle-attention-row>strong{grid-column:2/4}.idle-attention-history{grid-column:2/4}}
'''
write(p, s)
