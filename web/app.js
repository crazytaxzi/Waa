const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];

const api = async (path, options = {}) => {
  const response = await fetch(path, {
    headers: { 'Content-Type': 'application/json' },
    ...options
  });
  const body = await response.json();
  if (!response.ok) throw new Error(body.error || response.statusText);
  return body ?? [];
};

const state = {
  cache: new Map(),
  routeController: null,
  cardEventsController: null,
  cardLoadController: null,
  cardQueue: [],
  cardStep: 1,
  pendingSaves: new Set(),
  saveFailed: false,
  activityRowsByDriver: new Map(),
  idleRowsByDriver: new Map()
};
const cachedApi = async (path, maxAge = 20000) => {
  const hit = state.cache.get(path);
  if (hit && Date.now() - hit.time < maxAge) return hit.value;
  const value = await api(path, { signal: state.routeController?.signal });
  state.cache.set(path, { time: Date.now(), value });
  return value;
};
const invalidate = (...prefixes) => {
  for (const key of state.cache.keys()) if (prefixes.some(prefix => key.startsWith(prefix))) state.cache.delete(key);
};
const debounce = (fn, delay = 120) => {
  let timer;
  return (...args) => { clearTimeout(timer); timer = setTimeout(() => fn(...args), delay); };
};
const setCardQueue = ids => {
  state.cardQueue = [...new Set(ids.map(Number).filter(Boolean))];
};
const trackSave = promise => {
  state.pendingSaves.add(promise);
  promise.then(
    () => state.pendingSaves.delete(promise),
    () => {
      state.pendingSaves.delete(promise);
      state.saveFailed = true;
    }
  );
  return promise;
};
const awaitPendingSaves = async () => {
  while (state.pendingSaves.size) await Promise.allSettled([...state.pendingSaves]);
  const successful = !state.saveFailed;
  state.saveFailed = false;
  return successful;
};

const esc = value => String(value ?? 'Unknown').replace(/[&<>"]/g, char => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;'
}[char]));

const fmtPercent = value => value == null || !Number.isFinite(Number(value))
  ? 'No Data'
  : `${Number(value).toFixed(1)}%`;

const fmtHours = value => Number.isFinite(Number(value)) ? `${Number(value).toFixed(1)} h` : '—';
const hasTruck = driver => !!String(driver?.truck ?? '').trim();
const displayDate = value => {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
};

const toast = message => {
  const node = $('#toast');
  node.textContent = message;
  node.classList.add('show');
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => node.classList.remove('show'), 2200);
};

let drivers = [];
let cardId = null;
let chartSequence = 0;

function pageHead(title, subtitle, kicker = 'WAA // Live Operations') {
  return `
    <div class="page-head">
      <div>
        <p class="page-kicker">${esc(kicker)}</p>
        <h2>${esc(title)}</h2>
        <p>${esc(subtitle)}</p>
      </div>
      <div class="head-signal" aria-hidden="true"><span></span><span></span><span></span><span></span></div>
    </div>`;
}

function priority(driver) {
  if (!driver.actionable) return 'na';
  const date = new Date(driver.pta_at);
  if (date.getHours() === 23 && date.getMinutes() === 57) return 'pinned';
  const hours = (date - Date.now()) / 36e5;
  if (hours < 0) return 'overdue';
  if (hours < 6) return 'immediate';
  return 'future';
}

function relative(value) {
  if (!value) return 'N/A';
  const ms = new Date(value) - Date.now();
  const hours = Math.max(0, Math.round(Math.abs(ms) / 36e5));
  if (ms < 0) return `${hours}h overdue`;
  if (hours < 6) return `in ${hours}h · immediate`;
  return `in ${hours}h`;
}

function chart(rows, options = {}) {
  const {
    field = 'p7',
    dateField = 'period_end',
    tone = 'green',
    title = 'Idle history',
    compact = false,
    emptyMessage = 'No valid history yet',
    emptyDetail = 'The graph will populate as reports accumulate.'
  } = options;

  const points = (rows || [])
    .map(row => ({ date: row[dateField], value: Number(row[field] ?? row.percent) }))
    .filter(point => Number.isFinite(point.value));

  if (!points.length) {
    return `<div class="chart-empty"><span>${esc(emptyMessage)}</span><small>${esc(emptyDetail)}</small></div>`;
  }

  const id = `chart-${++chartSequence}`;
  const width = Math.max(compact ? 620 : 780, points.length * (compact ? 72 : 92));
  const height = compact ? 220 : 300;
  const left = compact ? 46 : 58;
  const right = 28;
  const top = 30;
  const bottom = compact ? 42 : 54;
  const maxValue = Math.max(60, Math.ceil(Math.max(...points.map(point => point.value)) / 10) * 10);
  const plotWidth = width - left - right;
  const plotHeight = height - top - bottom;
  const step = points.length > 1 ? plotWidth / (points.length - 1) : 0;
  const coords = points.map((point, index) => ({
    ...point,
    x: points.length > 1 ? left + index * step : left + plotWidth / 2,
    y: top + plotHeight - (point.value / maxValue) * plotHeight
  }));

  const linePath = coords.map((point, index) => `${index ? 'L' : 'M'} ${point.x.toFixed(1)} ${point.y.toFixed(1)}`).join(' ');
  const areaPath = `${linePath} L ${coords[coords.length - 1].x.toFixed(1)} ${(top + plotHeight).toFixed(1)} L ${coords[0].x.toFixed(1)} ${(top + plotHeight).toFixed(1)} Z`;
  const grid = [0, .25, .5, .75, 1].map(ratio => {
    const y = top + plotHeight - plotHeight * ratio;
    const label = Math.round(maxValue * ratio);
    return `<g class="chart-grid"><line x1="${left}" y1="${y}" x2="${width - right}" y2="${y}"/><text x="${left - 10}" y="${y + 4}" text-anchor="end">${label}%</text></g>`;
  }).join('');
  const dateLabels = coords.map((point, index) => {
    const show = points.length <= 8 || index === 0 || index === points.length - 1 || index % Math.ceil(points.length / 6) === 0;
    if (!show) return '';
    const label = point.date ? new Date(point.date).toLocaleDateString([], { month: 'short', day: 'numeric' }) : '';
    return `<text class="chart-date" x="${point.x}" y="${height - 14}" text-anchor="middle">${esc(label)}</text>`;
  }).join('');
  const hits = coords.map((point, index) => {
    const hitWidth = points.length > 1 ? Math.max(32, step) : plotWidth;
    const hitX = points.length > 1 ? point.x - hitWidth / 2 : left;
    return `
      <rect class="chart-hit" data-index="${index}" data-x="${point.x}" data-y="${point.y}" data-date="${esc(point.date)}" data-value="${point.value}" x="${hitX}" y="${top}" width="${hitWidth}" height="${plotHeight}" tabindex="0" role="button" aria-label="${esc(displayDate(point.date))}: ${fmtPercent(point.value)}"></rect>
      <circle class="chart-point" cx="${point.x}" cy="${point.y}" r="4.5"></circle>`;
  }).join('');

  return `
    <div class="chart-shell ${tone} ${compact ? 'compact' : ''}" id="${id}">
      <div class="chart-topline">
        <span>${esc(title)}</span>
        <small>${points.length} snapshot${points.length === 1 ? '' : 's'} · hover/focus for detail · horizontal scroll when history grows</small>
      </div>
      <div class="chart-scroll">
        <div class="chart-stage" style="width:${width}px">
          <svg class="chart" viewBox="0 0 ${width} ${height}" style="width:${width}px" role="img" aria-label="${esc(title)}">
            <defs>
              <linearGradient id="${id}-fill" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stop-color="currentColor" stop-opacity=".30"></stop>
                <stop offset="100%" stop-color="currentColor" stop-opacity="0"></stop>
              </linearGradient>
            </defs>
            ${grid}
            <path class="chart-area" d="${areaPath}" fill="url(#${id}-fill)"></path>
            <path class="chart-line" d="${linePath}"></path>
            <line class="chart-crosshair" x1="0" y1="${top}" x2="0" y2="${top + plotHeight}" hidden></line>
            ${hits}
            ${dateLabels}
          </svg>
          <div class="chart-tooltip" hidden></div>
        </div>
      </div>
    </div>`;
}

function bindCharts(root = document) {
  $$('.chart-shell', root).forEach(shell => {
    const stage = $('.chart-stage', shell);
    const tip = $('.chart-tooltip', shell);
    const crosshair = $('.chart-crosshair', shell);
    const scroll = $('.chart-scroll', shell);
    const show = hit => {
      const x = Number(hit.dataset.x);
      const y = Number(hit.dataset.y);
      tip.innerHTML = `<strong>${fmtPercent(hit.dataset.value)}</strong><span>${esc(displayDate(hit.dataset.date))}</span>`;
      tip.hidden = false;
      const left = Math.min(Math.max(x - 70, 8), stage.clientWidth - 150);
      tip.style.left = `${left}px`;
      tip.style.top = `${Math.max(8, y - 74)}px`;
      crosshair.hidden = false;
      crosshair.setAttribute('x1', x);
      crosshair.setAttribute('x2', x);
      $$('.chart-point', shell).forEach(point => point.classList.remove('active'));
      const points = $$('.chart-point', shell);
      const index = Number(hit.dataset.index);
      if (points[index]) points[index].classList.add('active');
    };
    const hide = () => {
      tip.hidden = true;
      crosshair.hidden = true;
      $$('.chart-point', shell).forEach(point => point.classList.remove('active'));
    };
    shell.addEventListener('pointerover', event => { const hit = event.target.closest('.chart-hit'); if (hit) show(hit); });
    shell.addEventListener('pointerout', event => { if (event.target.closest('.chart-hit')) hide(); });
    shell.addEventListener('focusin', event => { const hit = event.target.closest('.chart-hit'); if (hit) show(hit); });
    shell.addEventListener('focusout', event => { if (event.target.closest('.chart-hit')) hide(); });
    scroll.addEventListener('wheel', event => {
      if (event.shiftKey || Math.abs(event.deltaX) > Math.abs(event.deltaY)) {
        scroll.scrollLeft += event.deltaY || event.deltaX;
        event.preventDefault();
      }
    }, { passive: false });
    if (scroll.scrollWidth > scroll.clientWidth) scroll.scrollLeft = scroll.scrollWidth;
  });
}

function metricCard(label, value, detail, tone = 'green', driverId = null) {
  const tag = driverId ? 'button' : 'div';
  const open = driverId ? ` open" data-id="${Number(driverId)}" type="button` : '';
  return `<${tag} class="metric-card ${tone}${open}"><span>${esc(label)}</span><b>${esc(value)}</b><small>${esc(detail)}</small><i aria-hidden="true"></i></${tag}>`;
}

async function dashboard() {
  const data = await cachedApi('/api/dashboard');
  const heroList = (items, kind) => `
    <section class="rank-panel ${kind}">
      <div class="panel-title"><div><p class="eyebrow">${kind === 'heroes' ? 'Lowest current idle' : 'Highest current idle'}</p><h3>${kind === 'heroes' ? 'Strong Performers' : 'Coaching Priority'}</h3></div><span>${items.length}</span></div>
      <div class="rank-list">
        ${items.map((item, index) => `
          <button class="rank-row open" data-id="${item.id}">
            <span class="rank-number">0${index + 1}</span>
            <span class="rank-driver"><b>${esc(item.full_name)}</b><small>Truck ${esc(item.truck)} · ${esc(item.pta_code)}</small></span>
            <span class="rank-value"><b>${fmtPercent(item.p7)}</b><small>${item.p28 == null ? `28D ${esc(item.coverage28_detail)}` : `28D ${fmtPercent(item.p28)}`} · ${fmtHours(item.engine7)}</small></span>
          </button>`).join('') || '<p class="empty-copy">Waiting for idle history.</p>'}
      </div>
    </section>`;

  $('#app').innerHTML = pageHead('Fleet Pulse', 'Current idle performance, coaching priorities, and 28-day data coverage.') + `
    <section class="metrics-strip">
      ${metricCard('Over 50% idle', String(data.over50), 'Latest valid rolling 7-day', data.over50 ? 'red' : 'green')}
      ${metricCard('Tracked drivers', String(data.drivers.length), 'Drivers with current idle data', 'blue')}
      ${metricCard('28-day ready', `${data.coverage28?.complete_drivers || 0}/${data.coverage28?.tracked_drivers || 0}`, `Fleet history ${data.coverage28?.fleet_weeks || 0}/4 weeks`, data.coverage28?.fleet_ready ? 'green' : 'purple')}
      ${metricCard('Best current idle', data.heroes?.[0] ? fmtPercent(data.heroes[0].p7) : '—', data.heroes?.[0]?.full_name || 'Awaiting data', 'green', data.heroes?.[0]?.id)}
      ${metricCard('Above 50% coached', data.coaching?.percent == null ? '—' : fmtPercent(data.coaching.percent), `${data.coaching?.coached || 0} of ${data.coaching?.eligible || 0} drivers coached`, data.coaching?.eligible && data.coaching.coached === data.coaching.eligible ? 'green' : 'purple')}
    </section>
    <section class="dashboard-grid">
      <div class="glass-panel chart-panel green-edge">
        <div class="panel-title"><div><p class="eyebrow">Fleet trend</p><h3>Rolling 7-Day Idle</h3></div><span class="pulse-dot"></span></div>
        ${chart(data.history7, { tone: 'green', title: 'Fleet weighted rolling 7-day idle' })}
      </div>
      <div class="glass-panel chart-panel purple-edge">
        <div class="panel-title"><div><p class="eyebrow">Long view</p><h3>28-Day Coverage</h3></div><span class="pulse-dot purple"></span></div>
        ${chart(data.history28, { field: 'p28', tone: 'purple', title: 'Fleet weighted 28-day idle', emptyMessage: `Building 28-day history: ${data.coverage28?.fleet_weeks || 0}/4 weeks`, emptyDetail: 'WAA backfills up to eight recent weekly reports from Downloads. Four consecutive seven-day reports are required.' })}
      </div>
    </section>
    <section class="dashboard-grid rank-grid">
      ${heroList(data.heroes || [], 'heroes')}
      ${heroList(data.training || [], 'training')}
    </section>`;

  bindCharts($('#app'));
  setCardQueue([...(data.training || []), ...(data.heroes || [])].map(driver => driver.id));
}

async function loadDrivers() {
  drivers = await cachedApi('/api/drivers') || [];
  drivers.forEach(driver => { driver._search = Object.values(driver).join(' ').toLowerCase(); });
  return drivers;
}

function driverRows(list, showPta) {
  return list.map(driver => `
    <tr class="open" data-id="${driver.id}" data-search="${esc(driver._search)}" data-priority="${priority(driver)}" data-truck-state="${hasTruck(driver) ? 'assigned' : 'unassigned'}" data-call-state="${driver.call_completed ? 'completed' : 'pending'}">
      ${showPta ? `<td class="pta-cell ${priority(driver)}"><span class="priority-chip">${priority(driver).toUpperCase()}</span><b>${esc(driver.pta_raw || 'N/A')}</b><small>${esc(relative(driver.pta_at))}</small></td>` : ''}
      ${showPta ? '' : `<td>${driver.call_completed ? '<span class="status-pill good">Completed</span>' : '<span class="status-pill alert">Pending</span>'}</td>`}
      <td>${hasTruck(driver) ? `<details class="truck-change"><summary><b class="truck-no">${esc(driver.truck)}</b><small>Change</small></summary><form class="quick-truck-form" data-driver-id="${driver.id}" data-current-truck="${esc(driver.truck)}"><input name="truck" aria-label="New truck number for ${esc(driver.full_name)}" placeholder="New truck #" maxlength="24" required><button type="submit">Assign</button></form></details>` : `<form class="quick-truck-form" data-driver-id="${driver.id}"><input name="truck" aria-label="Truck number for ${esc(driver.full_name)}" placeholder="Truck #" maxlength="24" required><button type="submit">Assign</button></form>`}</td>
      <td><b>${esc(driver.full_name)}</b><small class="subline">${esc(driver.pta_code)}</small></td>
      <td>${esc(driver.division)}</td>
      <td>${esc(driver.operational_status)}</td>
      <td>${esc(driver.planning_status)}</td>
      <td>${esc(driver.operational_note)}</td>
      <td>${esc(driver.driver_type)}</td>
      <td>${esc(driver.location)}</td>
    </tr>`).join('');
}

async function queue(isPta) {
  await loadDrivers();
  const title = isPta ? 'PTA Attention Queue' : 'Driver Workflow';
  const subtitle = isPta
    ? 'The call list is ordered by attention. 23:57 pins first; equipment sentinels stay out of the driver queue.'
    : 'Open a driver to review the current assignment and complete the call workflow.';

  $('#app').innerHTML = pageHead(title, subtitle) + `
    <section class="glass-panel table-panel">
      <div class="table-toolbar">
        <div class="searchbox"><span aria-hidden="true">⌕</span><input id="search" placeholder="Search driver, truck, division, status, location"></div>
        <select id="filter" aria-label="${isPta ? 'Priority' : 'Workflow'} filter">
          ${isPta ? '<option value="">All states</option><option value="pinned">Pinned 23:57</option><option value="overdue">Overdue</option><option value="immediate">Immediate</option><option value="future">Future</option>' : '<option value="pending">Pending calls</option><option value="completed">Completed calls</option><option value="unassigned">Needs Truck</option><option value="assigned">Truck Assigned</option><option value="">All drivers</option>'}
        </select>
        <button id="startQueue" type="button">${isPta ? 'Open First' : 'Start Queue'}</button>
        <span id="queueCount" class="queue-count" aria-live="polite"></span>
      </div>
      <div class="table-scroll"><table><thead><tr>${isPta ? '<th>PTA / Priority</th>' : '<th>Call</th>'}<th>Truck</th><th>Driver</th><th>Div</th><th>Operational</th><th>Planning</th><th>Note</th><th>Type</th><th>Location</th></tr></thead><tbody></tbody></table></div>
    </section>`;

  let baseList = [...drivers];
  if (isPta) baseList = baseList.filter(driver => driver.actionable).sort((a, b) => {
    const pa = priority(a) === 'pinned' ? -1 : 0;
    const pb = priority(b) === 'pinned' ? -1 : 0;
    return pa - pb || new Date(a.pta_at) - new Date(b.pta_at);
  }); else baseList.sort((a, b) => Number(a.call_completed) - Number(b.call_completed) || String(a.full_name).localeCompare(String(b.full_name)));
  $('tbody').innerHTML = driverRows(baseList, isPta);
  const draw = () => {
    const query = $('#search').value.toLowerCase();
    const filter = $('#filter').value;
    const visible = [];
    $$('tbody tr').forEach(row => {
      const stateValue = isPta ? row.dataset.priority : ['pending', 'completed'].includes(filter) ? row.dataset.callState : row.dataset.truckState;
      row.hidden = !row.dataset.search.includes(query) || !!filter && stateValue !== filter;
      if (!row.hidden) visible.push(Number(row.dataset.id));
    });
    setCardQueue(visible);
    $('#queueCount').textContent = `${visible.length} driver${visible.length === 1 ? '' : 's'} in queue`;
    $('#startQueue').disabled = !visible.length;
  };

  $('#search').addEventListener('input', debounce(draw));
  $('#filter').addEventListener('change', draw);
  $('#startQueue').addEventListener('click', () => openCard(state.cardQueue[0]));
  draw();
}

async function bols() {
  const list = await cachedApi('/api/bols');
  list.forEach(item => { item._search = Object.values(item).join(' ').toLowerCase(); });
  $('#app').innerHTML = pageHead('Missing BOLs', 'Current report only. Historical Missing BOL evidence stays preserved without cluttering today’s call work.') + `
    <section class="glass-panel table-panel">
      <div class="table-toolbar">
        <div class="searchbox"><span aria-hidden="true">⌕</span><input id="search" placeholder="Search driver, truck, order, lane"></div>
        <select id="filter"><option value="">All</option><option value="open">Not mentioned</option><option value="done">Mentioned</option></select>
      </div>
      <div class="table-scroll"><table><thead><tr><th>Driver</th><th>Truck</th><th>Order</th><th>Empty Call</th><th>Origin</th><th>Destination</th><th>Age</th><th>Type</th><th>Mentioned</th></tr></thead><tbody></tbody></table></div>
    </section>`;

  const draw = () => {
    const query = $('#search').value.toLowerCase();
    const filter = $('#filter').value;
    const visible = [];
    $$('tbody tr').forEach(row => {
      row.hidden = !row.dataset.search.includes(query) || !!filter && row.dataset.state !== filter;
      if (!row.hidden) visible.push(Number(row.dataset.id));
    });
    setCardQueue(visible);
  };
  $('tbody').innerHTML = list.map(item => `
      <tr class="open" data-id="${item.driver_id}" data-search="${esc(item._search)}" data-state="${item.mentioned_at ? 'done' : 'open'}">
        <td><b>${esc(item.full_name)}</b></td><td><b class="truck-no">${esc(item.truck)}</b></td><td>${esc(item.order_number)}</td>
        <td>${esc(item.empty_call_date)}</td><td>${esc(item.origin)}</td><td>${esc(item.destination)}</td>
        <td>${item.empty_call_date ? `${Math.max(0, Math.floor((Date.now() - new Date(item.empty_call_date)) / 864e5))}d` : 'Unknown'}</td>
        <td>${esc(item.bol_type)}</td><td>${item.mentioned_at ? `<span class="status-pill good">Mentioned</span>` : '<span class="status-pill alert">Pending</span>'}</td>
      </tr>`).join('');
  $('#search').addEventListener('input', debounce(draw));
  $('#filter').addEventListener('change', draw);
  draw();
}

function driverOptions(list, selected = '') {
  return `<option value="">Select a driver…</option>${list.map(driver =>
    `<option value="${driver.id}" ${String(driver.id) === String(selected) ? 'selected' : ''}>${esc(driver.truck)} · ${esc(driver.full_name)}</option>`
  ).join('')}`;
}

function reminderRow(item) {
  const overdue = !item.completed_at && item.due_at && new Date(item.due_at) < new Date();
  return `<div class="organizer-item reminder ${overdue ? 'late' : ''} ${item.completed_at ? 'complete' : ''}">
    <input data-organizer-complete="${item.id}" data-driver-id="${item.driver_id}" type="checkbox" ${item.completed_at ? 'checked' : ''}>
    <button class="organizer-driver open" data-id="${item.driver_id}" type="button"><b>${esc(item.truck)} · ${esc(item.full_name)}</b><small>${esc(displayDate(item.due_at))}</small></button>
    <span class="organizer-copy">${esc(item.text)}</span>
    <button class="danger compact" data-organizer-delete="reminder" data-item="${item.id}" data-driver-id="${item.driver_id}" type="button">Delete</button>
  </div>`;
}

function openIdleCoachingSummary(driverId) {
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

async function organizer() {
  const data = await cachedApi('/api/organizer', 10000);
  const notes = data.items.filter(item => item.item_type === 'note');
  const reminders = data.items.filter(item => item.item_type === 'reminder');
  const renderItems = () => {
    const query = $('#organizerSearch').value.trim().toLowerCase();
    const driverId = $('#organizerDriverFilter').value;
    const matches = item => (!driverId || String(item.driver_id) === driverId) &&
      (!query || `${item.full_name} ${item.truck} ${item.text}`.toLowerCase().includes(query));
    $('#notesList').innerHTML = notes.filter(matches).map(item => `<article class="organizer-item note">
      <button class="organizer-driver open" data-id="${item.driver_id}" type="button"><b>${esc(item.truck)} · ${esc(item.full_name)}</b><small>${esc(displayDate(item.created_at))}</small></button>
      <p class="organizer-copy">${esc(item.text)}</p>
      <button class="danger compact" data-organizer-delete="note" data-item="${item.id}" data-driver-id="${item.driver_id}" type="button">Delete</button>
    </article>`).join('') || '<p class="empty-copy">No matching notes.</p>';
    $('#remindersList').innerHTML = reminders.filter(matches).map(reminderRow).join('') || '<p class="empty-copy">No matching reminders.</p>';
  };

  $('#app').innerHTML = pageHead('Notes & Reminders', 'A separate driver-specific workspace for everything you need to remember or revisit.', 'WAA // Personal Operations') + `
    <section class="glass-panel organizer-compose">
      <div class="panel-title"><div><p class="eyebrow">Capture with context</p><h3>New Driver Item</h3></div><span class="status-pill">Driver required</span></div>
      <div class="organizer-form">
        <label class="field"><span>Driver</span><select id="organizerDriver">${driverOptions(data.drivers)}</select></label>
        <label class="field"><span>Type</span><select id="organizerType"><option value="note">Note</option><option value="reminder">Reminder</option></select></label>
        <label class="field grow"><span>What do you need to remember?</span><input id="organizerText" placeholder="Write a useful, specific note or follow-up"></label>
        <label class="field" id="organizerDueField" hidden><span>Due</span><input id="organizerDue" type="datetime-local"></label>
        <button id="organizerSave" type="button">Save Item</button>
      </div>
    </section>
    <section class="glass-panel organizer-controls">
      <div class="searchbox"><span aria-hidden="true">⌕</span><input id="organizerSearch" placeholder="Search notes, reminders, drivers or trucks"></div>
      <select id="organizerDriverFilter" aria-label="Driver filter"><option value="">All drivers</option>${driverOptions(data.drivers).replace('<option value="">Select a driver…</option>', '')}</select>
    </section>
    <section class="organizer-grid">
      <div class="glass-panel"><div class="panel-title"><div><p class="eyebrow">Reference</p><h3>Driver Notes</h3></div><span>${notes.length}</span></div><div id="notesList" class="organizer-list"></div></div>
      <div class="glass-panel"><div class="panel-title"><div><p class="eyebrow">Follow through</p><h3>Reminders</h3></div><span>${reminders.filter(item => !item.completed_at).length} open</span></div><div id="remindersList" class="organizer-list"></div></div>
    </section>`;

  $('#organizerType').addEventListener('change', event => { $('#organizerDueField').hidden = event.target.value !== 'reminder'; });
  $('#organizerSearch').addEventListener('input', debounce(renderItems));
  $('#organizerDriverFilter').addEventListener('change', renderItems);
  $('#organizerSave').addEventListener('click', async () => {
    const saveButton = $('#organizerSave');
    if (saveButton.disabled) return;
    const driverId = Number($('#organizerDriver').value);
    const type = $('#organizerType').value;
    const text = $('#organizerText').value.trim();
    const due = $('#organizerDue').value;
    if (!driverId || !text || (type === 'reminder' && !due)) { toast('Choose a driver and complete the item'); return; }
    saveButton.disabled = true;
    const body = type === 'note' ? { action: 'note', text } : { action: 'reminder', text, due_at: due };
    await api(`/api/drivers/${driverId}/action`, { method: 'POST', body: JSON.stringify(body) });
    invalidate('/api/organizer', '/api/activity', `/api/drivers/${driverId}`);
    toast(type === 'note' ? 'Driver note saved' : 'Driver reminder saved');
    organizer();
  });
  renderItems();
}

const activityLabels = {
  note: 'Note added', reminder: 'Reminder created', complete_reminder: 'Reminder status changed',
  delete_note: 'Note deleted', delete_reminder: 'Reminder deleted', snooze_reminder: 'Reminder snoozed',
  timer: 'Timer started', complete_timer: 'Timer status changed', delete_timer: 'Timer deleted',
  pta: 'PTA updated', assign_truck: 'Truck assigned', bol_mentioned: 'Missing BOL status changed', call_flow_update: 'Call flow updated',
  transition_regenerated: 'Transition regenerated', transition_saved: 'Transition saved',
  home_checked: 'Home time reviewed', expected_work: 'Work expectation updated', home_status: 'Home-time status updated', home_reason: 'Home-time note updated',
  ontime_status: 'On-time status updated', ontime_reason: 'On-time note updated', preplan_reviewed: 'Preplan reviewed', preplan_response: 'Preplan response updated', preplan_note: 'Preplan note updated',
  routing_checked: 'Routing reviewed', routing_status: 'Routing status updated', routing_note: 'Routing note updated', safety_note_id: 'Safety note selected',
  safety_mentioned_at: 'Safety discussed', include_transition: 'Transition selection changed', transition_note: 'Transition note updated'
};
const callFieldLabels = {
  fuel_status: 'Fuel status updated', fuel_note: 'Fuel note updated', driver_eta: 'Driver ETA updated',
  eta_status: 'ETA status updated', eta_note: 'ETA note updated', idle_plan: 'Idle coaching plan updated',
  load_help_status: 'Load-help status updated', load_help_note: 'Load-help note updated',
  conversation_wrap: 'Call wrap-up updated', completed_at: 'Driver call completed'
};

function localDayRange(value) {
  const start = new Date(`${value}T00:00:00`);
  const end = new Date(start);
  end.setDate(end.getDate() + 1);
  return { start: start.toISOString(), end: end.toISOString() };
}

function activityView(row) {
  let detail = {};
  try { detail = JSON.parse(row.detail_json || '{}'); } catch { detail = {}; }
  const callLabel = row.action === 'call_flow_update' ? callFieldLabels[detail.field] : '';
  return {
    label: callLabel || activityLabels[row.action] || row.action.replaceAll('_', ' '),
    detail: detail.text || detail.action || '',
    time: new Date(`${row.occurred_at.replace(' ', 'T')}Z`).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
  };
}

function closeActivitySummary() {
  $('#activityModal').classList.add('hidden');
  if ($('#modal').classList.contains('hidden')) document.body.classList.remove('modal-open');
}

function openActivitySummary(driverId) {
  const rows = state.activityRowsByDriver.get(Number(driverId)) || [];
  if (!rows.length) return;
  const driver = rows[0];
  const counts = new Map();
  rows.forEach(row => { const label = activityView(row).label; counts.set(label, (counts.get(label) || 0) + 1); });
  $('#activityModalBody').innerHTML = `
    <header class="activity-modal-head"><div><p class="eyebrow">Daily driver summary</p><h2 id="activityModalTitle">${esc(driver.truck)} · ${esc(driver.full_name)}</h2><p>${rows.length} recorded action${rows.length === 1 ? '' : 's'} for ${esc(activity.selectedDay)}</p></div><button class="open" data-id="${driver.driver_id}" type="button">Open Driver Work Card</button></header>
    <div class="activity-summary-chips">${[...counts].map(([label, count]) => `<span><b>${count}</b>${esc(label)}</span>`).join('')}</div>
    <div class="activity-popup-list">${rows.map(row => { const view = activityView(row); return `<article class="activity-row"><time>${esc(view.time)}</time><div><b>${esc(view.label)}</b>${view.detail ? `<p>${esc(view.detail)}</p>` : ''}</div><button class="danger compact" data-delete-activity="${row.id}" type="button">Delete</button></article>`; }).join('')}</div>`;
  $('#activityModal').classList.remove('hidden');
  document.body.classList.add('modal-open');
}

async function activity() {
  const today = new Date();
  const defaultDay = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
  const selected = activity.selectedDay || defaultDay;
  activity.selectedDay = selected;
  const range = localDayRange(selected);
  const path = `/api/activity?start=${encodeURIComponent(range.start)}&end=${encodeURIComponent(range.end)}`;
  const rows = await cachedApi(path, 5000);
  const driverRows = rows.filter(row => row.driver_id);
  const render = () => {
    const driverId = $('#activityDriver').value;
    const visible = rows.filter(row => !driverId || String(row.driver_id) === driverId);
    const groups = new Map();
    visible.forEach(row => { const id = Number(row.driver_id); if (!groups.has(id)) groups.set(id, []); groups.get(id).push(row); });
    state.activityRowsByDriver = groups;
    $('#activityList').innerHTML = [...groups].map(([id, actions]) => {
      const driver = actions[0];
      const labels = [...new Set(actions.map(row => activityView(row).label))];
      return `<button class="activity-driver-card" data-review-driver="${id}" type="button"><span class="activity-driver-count">${actions.length}</span><span><b>${esc(driver.truck)} · ${esc(driver.full_name)}</b><small>${esc(labels.slice(0, 3).join(' · '))}${labels.length > 3 ? ` · +${labels.length - 3} more` : ''}</small></span><strong>Review Summary →</strong></button>`;
    }).join('') || '<p class="empty-copy">No recorded activity for this day.</p>';
    $('#activityCount').textContent = `${groups.size} driver${groups.size === 1 ? '' : 's'} · ${visible.length} actions`;
    setCardQueue([...groups.keys()]);
  };
  const uniqueDrivers = [...new Map(driverRows.map(row => [row.driver_id, row])).values()];
  $('#app').innerHTML = pageHead('Daily Activity Review', 'Review what you completed today with every action tied back to its driver.', 'WAA // Daily Record') + `
    <section class="glass-panel activity-toolbar">
      <label class="field"><span>Review date</span><input id="activityDate" type="date" value="${selected}"></label>
      <label class="field"><span>Driver</span><select id="activityDriver"><option value="">All activity</option>${uniqueDrivers.map(row => `<option value="${row.driver_id}">${esc(row.truck)} · ${esc(row.full_name)}</option>`).join('')}</select></label>
      <div class="activity-summary"><span id="activityCount"></span><b>${uniqueDrivers.length} drivers with activity</b><button id="cleanupActivity" class="danger compact" type="button">Clean Up Review</button><small>Removes automated identity noise and exact duplicate records only.</small></div>
    </section>
    <section class="glass-panel"><div class="panel-title"><div><p class="eyebrow">One summary per driver</p><h3>${esc(new Date(`${selected}T12:00:00`).toLocaleDateString([], { weekday: 'long', month: 'long', day: 'numeric' }))}</h3></div></div><div id="activityList" class="activity-list"></div></section>`;
  $('#activityDate').addEventListener('change', event => { activity.selectedDay = event.target.value; invalidate('/api/activity'); activity(); });
  $('#activityDriver').addEventListener('change', render);
  $('#cleanupActivity').addEventListener('click', async event => {
    if (!confirm('Clean every Daily Review date? This permanently removes automated identity evidence and exact duplicate audit records. Notes, reminders, driver work, imports, and unique actions are not changed.')) return;
    const button = event.currentTarget;
    button.disabled = true;
    try {
      const result = await api('/api/activity/cleanup', { method: 'POST', body: '{}' });
      invalidate('/api/activity');
      toast(result.removed ? `Removed ${result.removed} noisy or duplicate records` : 'Daily Review is already clean');
      await activity();
    } catch (error) {
      button.disabled = false;
      toast(error.message);
    }
  });
  render();
}

async function transition() {
  const data = await api('/api/transition');
  $('#app').innerHTML = pageHead('Transition Draft', 'Edit the handoff as plain text, then copy it when complete.') + `
    <section class="glass-panel transition-panel">
      <div class="panel-title"><div><p class="eyebrow">Current handoff</p><h3>${data.is_manual ? 'Manual Draft' : 'Generated Draft'}</h3></div><small>${esc(displayDate(data.updated_at))}</small></div>
      <textarea id="draft" class="transition-editor" spellcheck="true">${esc(data.body)}</textarea>
      <div class="action-row"><button id="regen">Regenerate</button><button class="secondary" id="copy">Copy All</button></div>
    </section>`;

  let timer;
  $('#draft').addEventListener('input', () => {
    clearTimeout(timer);
    timer = setTimeout(async () => {
      await api('/api/transition', { method: 'POST', body: JSON.stringify({ body: $('#draft').value }) });
      toast('Draft saved');
    }, 500);
  });
  $('#regen').addEventListener('click', async () => {
    if ($('#draft').value !== data.body && !confirm('Replace the active manual draft?')) return;
    const next = await api('/api/transition/regenerate', { method: 'POST', body: '{}' });
    $('#draft').value = next.body;
    toast('Transition regenerated');
  });
  $('#copy').addEventListener('click', async () => {
    await navigator.clipboard.writeText($('#draft').value);
    toast('Copied');
  });
}

async function imports() {
  const [quality, intake] = await Promise.all([api('/api/data-quality'), api('/api/report-intake')]);
  const reportCard = (title, item, tone) => `
    <div class="intake-card ${tone}">
      <div class="intake-icon" aria-hidden="true"></div>
      <div><p class="eyebrow">Automatic intake</p><h3>${esc(title)}</h3><b>${esc(item?.source_name || 'Waiting for report')}</b><small>${esc(item?.source_modified_utc ? displayDate(item.source_modified_utc) : 'No matching download yet')}</small><p>${esc(item?.detail || 'WAA is watching Downloads for matching reports.')}</p></div>
      <span class="status-pill ${item?.status === 'Error' ? 'alert' : item?.status === 'Imported' || item?.status === 'Current' ? 'good' : ''}">${esc(item?.status || 'Waiting')}</span>
    </div>`;

  $('#app').innerHTML = pageHead('Imports / Data Quality', 'Downloads feeds idle and Missing BOL automatically. PTA stays intentional: copy, preview, commit.') + `
    <section class="intake-header glass-panel">
      <div><p class="eyebrow">Downloads watcher</p><h3>${esc(intake.downloads_path)}</h3><p>Idle intake checks up to eight recent weekly reports for 28-day history. Missing BOL uses the newest report. Originals remain untouched.</p></div>
      <button id="scan">Scan Downloads Now</button>
    </section>
    <section class="intake-grid">${reportCard('Rolling 7-Day Idle', intake.idle, 'green')}${reportCard('Missing BOL', intake.bol, 'purple')}</section>
    <section class="glass-panel pta-paste">
      <div class="panel-title"><div><p class="eyebrow">Intentional input</p><h3>PTA / Fleet State · Paste Only</h3></div><span class="status-pill">11 columns</span></div>
      <textarea id="raw" placeholder="Paste PTA / fleet-state table here..."></textarea>
      <div class="action-row"><button id="preview">Preview PTA</button><button id="commit" class="secondary" disabled>Commit PTA Snapshot</button></div>
      <pre id="previewOut"></pre>
    </section>
    <section class="dashboard-grid">
      <div class="glass-panel"><div class="panel-title"><div><p class="eyebrow">Resolve only when needed</p><h3>Identity Issues</h3></div><span>${quality.issues.length}</span></div>${quality.issues.map(issue => `<div class="notice identity-issue">${esc(issue.issue_type)} · ${esc(issue.alias_type)}: <b>${esc(issue.alias_value)}</b></div>`).join('') || '<p class="empty-copy">No open identity issues.</p>'}</div>
      <div class="glass-panel"><div class="panel-title"><div><p class="eyebrow">Local database</p><h3>Integrity & Backups</h3></div><span class="status-pill good">${esc(quality.integrity)}</span></div><button id="backup">Backup Now</button><div class="backup-list">${quality.backups.slice(0, 6).map(item => `<button class="backup-row" data-backup="${esc(item.name)}"><span>${esc(item.name)}</span><small>${Math.round(item.size / 1024)} KB</small></button>`).join('')}</div></div>
    </section>
    <section class="glass-panel table-panel"><div class="panel-title"><div><p class="eyebrow">Evidence trail</p><h3>Import History</h3></div></div><div class="table-scroll"><table><thead><tr><th>Time</th><th>Type</th><th>File</th><th>Rows</th><th>Warnings</th><th>Hash</th></tr></thead><tbody>${quality.imports.map(item => `<tr><td>${esc(displayDate(item.imported_at))}</td><td>${esc(item.import_type)}</td><td>${esc(item.filename)}</td><td>${item.row_count}</td><td>${item.warning_count}</td><td>${esc(item.source_hash).slice(0, 12)}…</td></tr>`).join('')}</tbody></table></div></section>`;

  let preview = null;
  $('#scan').addEventListener('click', async () => {
    const result = await api('/api/report-intake/scan', { method: 'POST', body: '{}' });
    const changed = result.results?.idle?.imported || result.results?.bol?.imported;
    if (changed) invalidate('/api/dashboard', '/api/drivers', '/api/bols', '/api/data-quality');
    toast(changed ? 'Report history imported' : 'Downloads already current');
    imports();
  });
  $('#preview').addEventListener('click', async () => {
    preview = await api('/api/import/preview', { method: 'POST', body: JSON.stringify({ raw: $('#raw').value, type: 'pta' }) });
    $('#previewOut').textContent = JSON.stringify(preview, null, 2);
    $('#commit').disabled = !!preview.errors.length;
  });
  $('#commit').addEventListener('click', async () => {
    await api('/api/import/commit', { method: 'POST', body: JSON.stringify({ raw: $('#raw').value, type: 'pta' }) });
    invalidate('/api/dashboard', '/api/drivers', '/api/data-quality');
    toast('PTA snapshot committed');
    imports();
  });
  $('#backup').addEventListener('click', async () => {
    const backup = await api('/api/backup', { method: 'POST', body: '{}' });
    toast(`Backup created: ${backup.name}`);
    imports();
  });
  $$('.identity-issue').forEach(node => node.addEventListener('dblclick', async () => {
    const issue = quality.issues.find(item => node.textContent.includes(item.alias_value));
    if (!issue) return;
    const id = prompt(`Canonical driver ID for ${issue.alias_value}:`);
    if (!id) return;
    await api('/api/identity/resolve', { method: 'POST', body: JSON.stringify({ issue_id: issue.id, driver_id: +id }) });
    toast('Identity linked');
    imports();
  }));
  $$('.backup-row').forEach(button => button.addEventListener('dblclick', async () => {
    const name = button.dataset.backup;
    if (!confirm(`Restore ${name}? A pre-restore backup will be created.`)) return;
    await api('/api/restore', { method: 'POST', body: JSON.stringify({ name }) });
    toast('Backup restored');
    imports();
  }));
}

function selectField(label, action, value, options, attrs = '') {
  return `<label class="field"><span>${esc(label)}</span><select data-action="${esc(action)}" ${attrs}>${options.map(option => `<option ${option === value ? 'selected' : ''}>${esc(option)}</option>`).join('')}</select></label>`;
}

function textField(label, action, value, placeholder = '', attrs = '') {
  return `<label class="field"><span>${esc(label)}</span><input data-action="${esc(action)}" value="${esc(value ?? '')}" placeholder="${esc(placeholder)}" ${attrs}></label>`;
}

function conversationSelect(label, field, value, options) {
  return `<label class="field"><span>${esc(label)}</span><select data-conversation="${esc(field)}">${options.map(option => `<option ${option === value ? 'selected' : ''}>${esc(option)}</option>`).join('')}</select></label>`;
}

function conversationText(label, field, value, placeholder = '') {
  return `<label class="field"><span>${esc(label)}</span><input data-conversation="${esc(field)}" value="${esc(value ?? '')}" placeholder="${esc(placeholder)}"></label>`;
}

function conversationArea(label, field, value, placeholder = '') {
  return `<label class="field"><span>${esc(label)}</span><textarea data-conversation="${esc(field)}" placeholder="${esc(placeholder)}">${esc(value ?? '')}</textarea></label>`;
}

function checkField(label, action, checked) {
  return `<label class="toggle-row"><input data-action="${esc(action)}" type="checkbox" ${checked ? 'checked' : ''}><span class="toggle-ui"></span><b>${esc(label)}</b></label>`;
}

function completedCallSteps(card, conversation) {
  const work = card.work || {};
  return new Set([
    (conversation.fuel_status && conversation.fuel_status !== 'Unknown') || conversation.fuel_note ? 1 : 0,
    conversation.driver_eta || (conversation.eta_status && conversation.eta_status !== 'Unknown') || (work.ontime_status && work.ontime_status !== 'Unknown') ? 2 : 0,
    conversation.idle_plan ? 3 : 0,
    (conversation.load_help_status && conversation.load_help_status !== 'Unknown') || work.preplan_reviewed || work.routing_checked ? 4 : 0,
    work.home_checked ? 5 : 0,
    !card.bols?.length || card.bols.every(item => item.mentioned_at) ? 6 : 0,
    conversation.conversation_wrap || work.safety_mentioned_at || work.include_transition ? 7 : 0
  ].filter(Boolean));
}

function showCardStep(number, focus = true) {
  const root = $('#card');
  if (!root) return;
  state.cardStep = Math.min(7, Math.max(1, Number(number) || 1));
  $$('.call-step', root).forEach(step => {
    const active = Number(step.dataset.step) === state.cardStep;
    step.hidden = !active;
    step.classList.toggle('active', active);
  });
  $$('[data-jump]', root).forEach(button => {
    const active = Number(button.dataset.jump) === state.cardStep;
    button.classList.toggle('active', active);
    button.setAttribute('aria-current', active ? 'step' : 'false');
  });
  const counter = $('#stepCounter', root);
  if (counter) counter.textContent = `Step ${state.cardStep} of 7`;
  if (focus) requestAnimationFrame(() => $(`.call-step[data-step="${state.cardStep}"] [data-conversation], .call-step[data-step="${state.cardStep}"] [data-action]`, root)?.focus());
}

function callStep(number, title, prompt, body, tone = '') {
  const next = number < 7
    ? `<button type="button" data-step-next>Next Step <span aria-hidden="true">→</span></button>`
    : '<button type="button" data-finish-call>Finish Call &amp; Next Driver</button>';
  return `
    <section class="call-step ${tone}" data-step="${number}">
      <div class="step-number">${String(number).padStart(2, '0')}</div>
      <div class="step-body"><p class="step-prompt">${esc(prompt)}</p><h3>${esc(title)}</h3>${body}<div class="step-footer"><div class="step-save" aria-live="polite">Changes save automatically</div><div class="step-actions">${number > 1 ? '<button class="secondary" type="button" data-step-back><span aria-hidden="true">←</span> Back</button>' : ''}${next}</div></div></div>
    </section>`;
}

function noteList(notes) {
  return notes.length
    ? notes.slice(0, 8).map(note => `<div class="note-chip"><p>${esc(note.note)}</p><small>${esc(displayDate(note.created_at))}</small><button class="danger compact" data-delete-note="${note.id}" type="button">Delete</button></div>`).join('')
    : '<p class="empty-copy">Nothing captured yet. Keep this conversational—only save what will actually help later.</p>';
}

function followupList(reminders) {
  return reminders.length ? reminders.map(reminder => `<div class="follow-row ${!reminder.completed_at && new Date(reminder.due_at) < new Date() ? 'late' : ''}"><input class="item-action" data-action="complete_reminder" data-item="${reminder.id}" type="checkbox" ${reminder.completed_at ? 'checked' : ''}><span>${esc(reminder.text)}<small>${esc(displayDate(reminder.due_at))}</small></span><button class="compact" data-snooze-reminder="${reminder.id}" type="button">+1 Day</button><button class="danger compact" data-delete-reminder="${reminder.id}" type="button">Delete</button></div>`).join('') : '<p class="empty-copy">No reminders.</p>';
}

function timerList(timers) {
  return timers.length ? timers.map(timer => `<div class="follow-row ${!timer.completed_at && new Date(timer.target_at) < new Date() ? 'late' : ''}"><input class="item-action" data-action="complete_timer" data-item="${timer.id}" type="checkbox" ${timer.completed_at ? 'checked' : ''}><span>${esc(timer.label)}<small>${esc(displayDate(timer.target_at))}</small></span><button class="danger compact" data-delete-timer="${timer.id}" type="button">Delete</button></div>`).join('') : '<p class="empty-copy">No timers.</p>';
}

function idleCoachingHistory(items, currentCycle) {
  const rows = (items || []).filter(item => item.idle_plan && item.cycle_key !== currentCycle);
  return `
    <section class="idle-history">
      <div class="idle-history-title"><div><p class="eyebrow">Idle only</p><h4>Previous Idle Coaching</h4></div><span>${rows.length}</span></div>
      ${rows.length ? rows.map(item => `
        <article class="idle-history-row">
          <div class="idle-history-stat"><b>${fmtPercent(item.idle_percent)}</b><small>${item.period_end ? `7D ending ${esc(new Date(item.period_end).toLocaleDateString([], { month: 'short', day: 'numeric' }))}` : 'Idle snapshot unavailable'}</small></div>
          <div class="idle-history-copy"><time>${esc(displayDate(item.talked_at))}</time><p>${esc(item.idle_plan)}</p></div>
        </article>`).join('') : '<p class="empty-copy">No previous idle coaching recorded for this driver.</p>'}
    </section>`;
}

async function openCard(id) {
  if (!id) return;
  const requestedId = Number(id);
  if (!state.cardQueue.includes(requestedId)) setCardQueue([requestedId]);
  state.cardLoadController?.abort();
  const controller = new AbortController();
  state.cardLoadController = controller;
  const shell = $('.modal-shell');
  shell.classList.add('card-loading');
  shell.setAttribute('aria-busy', 'true');
  let context;
  try {
    context = await api(`/api/drivers/${requestedId}/context`, { signal: controller.signal });
  } catch (error) {
    if (error.name === 'AbortError') return;
    toast(error.message);
    return;
  } finally {
    if (state.cardLoadController === controller) {
      shell.classList.remove('card-loading');
      shell.removeAttribute('aria-busy');
    }
  }
  if (controller.signal.aborted) return;
  cardId = requestedId;
  const { card, conversation } = context;
  const driver = card.driver;
  const work = card.work || {};
  const latestIdle = card.idle?.[0];
  const idlePrompt = Number(latestIdle?.percent) > 50
    ? 'What can we change together to pull that idle number down?'
    : 'What is working well that is keeping idle under control?';
  const completedSteps = completedCallSteps(card, conversation);
  const initialStep = conversation.completed_at ? 7 : ([1, 2, 3, 4, 5, 6, 7].find(step => !completedSteps.has(step)) || 7);
  const queueIndex = state.cardQueue.indexOf(requestedId);

  const bolRows = card.bols.length
    ? card.bols.map(item => `
      <label class="bol-call-row">
        <input class="item-action" data-action="bol_mentioned" data-item="${item.id}" type="checkbox" ${item.mentioned_at ? 'checked' : ''}>
        <span><b>${esc(item.order_number)}</b><small>${esc(item.origin)} → ${esc(item.destination)} · ${esc(item.empty_call_date)}</small></span>
        <span class="status-pill ${item.mentioned_at ? 'good' : 'alert'}">${item.mentioned_at ? 'Done' : 'Mention'}</span>
      </label>`).join('')
    : '<p class="empty-copy">No Missing BOL items for this driver.</p>';

  const flow = [
    callStep(1, 'Fuel & Immediate Needs', 'Start with the driver, not the paperwork.', `
      <div class="field-grid">
        ${conversationSelect('Fuel looks…', 'fuel_status', conversation.fuel_status, ['Unknown', 'Good', 'Needs Fuel', 'Concern'])}
        ${conversationText('Anything to handle right now?', 'fuel_note', conversation.fuel_note, 'Fuel stop, card issue, mechanical concern…')}
      </div>`, 'green-step'),
    callStep(2, 'ETA & Timing', 'Now get the picture of where they are and when they expect to land.', `
      <div class="context-ribbon"><span>Current PTA</span><b>${esc(driver.pta_raw || 'N/A')}</b><small>${esc(relative(driver.pta_at))}</small></div>
      <div class="field-grid three">
        ${conversationText('Driver says ETA is…', 'driver_eta', conversation.driver_eta, 'Example: 14:30, 2 hours out, after fuel')}
        ${conversationSelect('Timing looks…', 'eta_status', conversation.eta_status, ['Unknown', 'On Track', 'Tight', 'Late'])}
        ${conversationText('What is affecting it?', 'eta_note', conversation.eta_note, 'Traffic, shipper delay, weather…')}
      </div>
      <div class="field-grid">${selectField('On-time status', 'ontime_status', work.ontime_status, ['Unknown', 'On Time', 'At Risk', 'Late'])}${textField('On-time reason / action', 'ontime_reason', work.ontime_reason, 'Why, and what needs to happen next')}</div>
      <details class="inline-detail"><summary>Adjust imported PTA only if needed</summary><label class="field"><span>Manual PTA observation</span><input data-action="pta" type="datetime-local" value="${esc(driver.pta_at?.slice(0, 16) || '')}"></label></details>`, 'blue-step'),
    callStep(3, 'Idle Coaching', idlePrompt, `
      <div class="idle-coach-head"><div><span>Latest 7D</span><b>${fmtPercent(latestIdle?.percent)}</b></div><div><span>Engine</span><b>${fmtHours(latestIdle?.engine_hours)}</b></div><div><span>Idle</span><b>${fmtHours(latestIdle?.idle_hours)}</b></div></div>
      ${chart([...(card.idle || [])].reverse(), { field: 'percent', tone: 'green', title: 'Driver rolling idle history', compact: true })}
      ${conversationArea('What is their plan / what is working?', 'idle_plan', conversation.idle_plan, 'Keep it natural: “going to shut down during long waits”, “APU issue needs help”, “current routine is working”…')}
      ${idleCoachingHistory(card.idle_coaching || [], conversation.cycle_key)}`, 'purple-step'),
    callStep(4, 'Help on the Load', 'Ask what would make the load easier before you start checking boxes.', `
      <div class="field-grid">
        ${conversationSelect('Do they need help?', 'load_help_status', conversation.load_help_status, ['Unknown', 'No Help Needed', 'Needs Help', 'Follow Up'])}
        ${conversationText('What do they need from us?', 'load_help_note', conversation.load_help_note, 'Appointment, routing, customer, parking, equipment…')}
      </div>
      <div class="mini-workflow">
        <div><p class="eyebrow">Preplan</p><b>Source: ${esc(driver.planning_status)}</b>${selectField('Driver response', 'preplan_response', work.preplan_response, ['Unknown', 'Accepted', 'Denied'])}${checkField('Reviewed together', 'preplan_reviewed', work.preplan_reviewed)}${textField('Anything to remember', 'preplan_note', work.preplan_note, 'Keep it short and useful')}</div>
        <div><p class="eyebrow">Routing</p>${selectField('Routing looks…', 'routing_status', work.routing_status, ['Unknown', 'Accurate', 'Needs Correction'])}${checkField('Routing checked', 'routing_checked', work.routing_checked)}${textField('What changed / what is needed', 'routing_note', work.routing_note, 'Only if something matters')}</div>
      </div>`, 'green-step'),
    callStep(5, 'Home Time & Schedule', 'Before the admin close-out, make sure nothing important is hiding behind the load.', `
      <div class="field-grid three">
        ${checkField('Home time checked', 'home_checked', work.home_checked)}
        ${textField('Expected to work', 'expected_work', work.expected_work, 'Unknown / Yes / No')}
        ${selectField('Home-time picture', 'home_status', work.home_status, ['Unknown', 'OK', 'Concern'])}
      </div>
      ${textField('Anything that needs action', 'home_reason', work.home_reason, 'Only capture what someone needs to know or do')}`, 'blue-step'),
    callStep(6, 'Quick Admin Close-Out', 'Now is the right time for Missing BOLs—after the driver conversation has already happened.', `
      <div class="bol-call-list">${bolRows}</div>`, 'purple-step'),
    callStep(7, 'Safety & Wrap', 'Finish like a human conversation: one useful reminder, anything else they need, then you are done.', `
      <div class="safety-box"><div><p class="eyebrow">Safety touch</p><p id="safety">Pick one useful note if it fits the conversation.</p></div><button id="random" type="button">New Safety Note</button></div>
      ${checkField('Safety note mentioned', 'safety_mentioned_at', work.safety_mentioned_at)}
      ${conversationArea('Anything else worth remembering?', 'conversation_wrap', conversation.conversation_wrap, 'Record only information that will help with the next follow-up.')}
      <div class="wrap-grid">${checkField('Send to Transition', 'include_transition', work.include_transition)}${textField('Transition note', 'transition_note', work.transition_note, 'One concise handoff line')}</div>`, 'green-step')
  ].join('');

  $('#card').innerHTML = `
    <header class="driver-hero">
      <div class="driver-sigil" aria-hidden="true">${esc((driver.full_name || '?').slice(0, 1).toUpperCase())}</div>
      <div class="driver-identity"><p class="eyebrow">Live driver conversation</p><h2>${esc(driver.truck)} <span>·</span> ${esc(driver.full_name)}</h2><p>${esc(driver.pta_code)} · Div ${esc(driver.division)} · ${esc(driver.operational_status)} / ${esc(driver.planning_status)} · ${esc(driver.driver_type)} · ${esc(driver.location)}</p></div>
      <div class="driver-pta"><span>PTA</span><b>${esc(driver.pta_raw || 'N/A')}</b><small>${esc(relative(driver.pta_at))}</small></div>
    </header>
    <div class="driver-queue-nav">
      <button class="secondary" type="button" data-driver-prev ${queueIndex <= 0 ? 'disabled' : ''}>← Previous Driver</button>
      <span>${queueIndex >= 0 ? `${queueIndex + 1} of ${state.cardQueue.length}` : 'Single driver'} · <b>${conversation.completed_at ? 'Call completed' : 'Call in progress'}</b></span>
      <button class="secondary" type="button" data-driver-next ${queueIndex < 0 || queueIndex >= state.cardQueue.length - 1 ? 'disabled' : ''}>Next Driver →</button>
    </div>
    <div class="call-progress" aria-label="Call flow">
      ${['Fuel', 'ETA', 'Idle', 'Load', 'Home', 'BOL', 'Wrap'].map((name, index) => `<button type="button" data-jump="${index + 1}" class="${completedSteps.has(index + 1) ? 'done' : ''}"><span>${completedSteps.has(index + 1) ? '✓' : index + 1}</span>${name}</button>`).join('')}
      <strong id="stepCounter">Step ${initialStep} of 7</strong>
    </div>
    <div class="call-layout">
      <main class="call-main">${flow}</main>
      <aside class="call-side">
        <section class="side-card snapshot"><p class="eyebrow">Driver snapshot</p><dl><div><dt>Truck</dt><dd>${esc(driver.truck)}</dd></div><div><dt>Location</dt><dd>${esc(driver.location)}</dd></div><div><dt>Status</dt><dd>${esc(driver.operational_status)}</dd></div><div><dt>Planning</dt><dd>${esc(driver.planning_status)}</dd></div><div><dt>Freshness</dt><dd>${esc(displayDate(driver.observed_at))}</dd></div></dl><div class="unassigned-truck"><p>${hasTruck(driver) ? `Assign a different truck. Current truck ${esc(driver.truck)} remains in history.` : 'No truck association is on record.'}</p><form class="quick-truck-form" data-driver-id="${driver.id}" data-current-truck="${esc(driver.truck || '')}"><input name="truck" aria-label="${hasTruck(driver) ? 'New truck number' : 'Truck number'}" placeholder="${hasTruck(driver) ? 'New truck #' : 'Enter truck #'}" maxlength="24" required><button type="submit">${hasTruck(driver) ? 'Change Truck' : 'Assign Truck'}</button></form></div></section>
        <section class="side-card notes-rail"><div class="side-title"><div><p class="eyebrow">Remember this</p><h3>Call Notes</h3></div><span>Alt+N</span></div><p class="rail-copy">Use this like a scratchpad, not a form. Save the sentence you would tell yourself later.</p><div class="quick-note"><textarea id="quickNote" placeholder="Driver mentioned…"></textarea><button id="saveNote" type="button">Save Note</button></div><div id="noteList">${noteList(card.notes || [])}</div></section>
        <section class="side-card followups"><p class="eyebrow">After the call</p><h3>Reminders</h3><div class="follow-add"><input id="remtext" placeholder="Reminder"><input id="remdue" type="datetime-local"><button id="addReminder" type="button">Add Reminder</button></div><div id="followupList">${followupList(card.reminders || [])}</div><h3 class="follow-heading">Timers</h3><div class="follow-add"><input id="timertext" placeholder="Timer label"><input id="timerdue" type="datetime-local"><button id="addTimer" type="button">Start Timer</button></div><div id="timerList">${timerList(card.timers || [])}</div></section>
      </aside>
    </div>`;

  $('#modal').classList.remove('hidden');
  document.body.classList.add('modal-open');
  bindCharts($('#card'));
  bindCardEvents(card);
  const finishButton = $('[data-finish-call]', $('#card'));
  if (conversation.completed_at && finishButton) {
    finishButton.disabled = true;
    finishButton.textContent = 'Call Completed';
  }
  showCardStep(initialStep);
}

function bindCardEvents(card) {
  const root = $('#card');
  state.cardEventsController?.abort();
  state.cardEventsController = new AbortController();
  const listenerOptions = { signal: state.cardEventsController.signal };
  const trackedAction = (...args) => trackSave(driverAction(...args));
  const setSaving = element => {
    const status = $('.step-save', element.closest('.call-step'));
    if (status) status.textContent = 'Saving…';
  };
  const setSaved = element => {
    const step = element.closest('.call-step');
    if (!step) return;
    const status = $('.step-save', step);
    status.textContent = 'Saved';
    step.classList.add('saved');
    const progress = $(`[data-jump="${step.dataset.step}"]`, root);
    progress?.classList.add('done');
    if (progress) progress.querySelector('span').textContent = '✓';
    setTimeout(() => {
      status.textContent = 'Changes save automatically';
      step.classList.remove('saved');
    }, 1400);
  };
  const moveStep = async delta => {
    document.activeElement?.blur();
    // Changing focus already queues the autosave. Step navigation must remain
    // available while that fast LMDB write finishes in the background.
    showCardStep(state.cardStep + delta);
  };
  const moveDriver = async delta => {
    document.activeElement?.blur();
    if (!await awaitPendingSaves()) return;
    const index = state.cardQueue.indexOf(cardId);
    const nextId = state.cardQueue[index + delta];
    if (nextId) await openCard(nextId);
  };
  const finishCall = async button => {
    if (button.disabled) return;
    button.disabled = true;
    document.activeElement?.blur();
    if (!await awaitPendingSaves()) {
      button.disabled = false;
      return;
    }
    const finishedId = cardId;
    const index = state.cardQueue.indexOf(finishedId);
    const nextId = state.cardQueue[index + 1];
    try {
      await api(`/api/drivers/${finishedId}/conversation`, {
        method: 'POST', body: JSON.stringify({ field: 'completed_at', value: true })
      });
      invalidate('/api/drivers', '/api/dashboard', '/api/activity', '/api/idle-coaching', `/api/drivers/${finishedId}`);
      toast(nextId ? 'Call completed · loading next driver' : 'Call completed');
      if (nextId) await openCard(nextId);
      else {
        closeCard();
        await route();
      }
    } catch (error) {
      button.disabled = false;
      toast(error.message);
    }
  };

  const saveNote = async () => {
    const box = $('#quickNote');
    const button = $('#saveNote');
    if (button.disabled) return;
    const text = box.value.trim();
    if (!text) return;
    button.disabled = true;
    try {
      const updated = await trackedAction('note', null, { text });
      box.value = '';
      $('#noteList').innerHTML = noteList(updated.notes || []);
      toast('Note kept');
    } catch (error) { toast(error.message); }
    finally { button.disabled = false; }
  };
  const addReminder = async () => {
    const button = $('#addReminder');
    if (button.disabled) return;
    const text = $('#remtext').value.trim();
    const due = $('#remdue').value;
    if (!text || !due) return;
    button.disabled = true;
    try {
      const updated = await trackedAction('reminder', null, { text, due_at: due });
      $('#remtext').value = '';
      $('#remdue').value = '';
      $('#followupList').innerHTML = followupList(updated.reminders || []);
      toast('Reminder added');
    } catch (error) { toast(error.message); }
    finally { button.disabled = false; }
  };
  const addTimer = async () => {
    const button = $('#addTimer');
    if (button.disabled) return;
    const label = $('#timertext').value.trim();
    const target = $('#timerdue').value;
    if (!label || !target) return;
    button.disabled = true;
    try {
      const updated = await trackedAction('timer', null, { label, target_at: target });
      $('#timertext').value = '';
      $('#timerdue').value = '';
      $('#timerList').innerHTML = timerList(updated.timers || []);
      toast('Timer started');
    } catch (error) { toast(error.message); }
    finally { button.disabled = false; }
  };
  const newSafetyNote = async () => {
    try {
      const note = await api('/api/safety/random');
      $('#safety').textContent = note.note;
      await trackedAction('safety_note_id', note.id);
    } catch (error) { toast(error.message); }
  };

  root.addEventListener('change', async event => {
    const element = event.target;
    const previousChecked = !element.checked;
    try {
      if (element.matches('[data-conversation]')) {
        setSaving(element);
        const driverId = cardId;
        await trackSave(api(`/api/drivers/${driverId}/conversation`, {
          method: 'POST', body: JSON.stringify({ field: element.dataset.conversation, value: element.value })
        }));
        invalidate('/api/activity', '/api/idle-coaching', `/api/drivers/${driverId}`);
        setSaved(element);
      } else if (element.matches('.item-action')) {
        await trackedAction(element.dataset.action, element.checked, { item_id: Number(element.dataset.item) });
        toast('Saved');
      } else if (element.matches('[data-action]')) {
        setSaving(element);
        const value = element.type === 'checkbox' ? element.checked : element.value;
        await trackedAction(element.dataset.action, value);
        setSaved(element);
      }
    } catch (error) {
      if (element.type === 'checkbox') element.checked = previousChecked;
      const status = $('.step-save', element.closest('.call-step'));
      if (status) status.textContent = 'Save failed · try again';
      toast(error.message);
    }
  }, listenerOptions);
  root.addEventListener('click', event => {
    const button = event.target.closest('button');
    if (!button) return;
    if (button.id === 'saveNote') saveNote();
    else if (button.id === 'addReminder') addReminder();
    else if (button.id === 'addTimer') addTimer();
    else if (button.id === 'random') newSafetyNote();
    else if (button.dataset.stepNext !== undefined) moveStep(1);
    else if (button.dataset.stepBack !== undefined) moveStep(-1);
    else if (button.dataset.finishCall !== undefined) finishCall(button);
    else if (button.dataset.driverNext !== undefined) moveDriver(1);
    else if (button.dataset.driverPrev !== undefined) moveDriver(-1);
    else if (button.dataset.deleteNote && confirm('Delete this driver note?')) {
      button.disabled = true;
      trackedAction('delete_note', null, { item_id: Number(button.dataset.deleteNote) }).then(updated => { $('#noteList').innerHTML = noteList(updated.notes || []); toast('Note deleted'); }).catch(error => { button.disabled = false; toast(error.message); });
    }
    else if (button.dataset.deleteReminder && confirm('Delete this reminder?')) {
      button.disabled = true;
      trackedAction('delete_reminder', null, { item_id: Number(button.dataset.deleteReminder) }).then(updated => { $('#followupList').innerHTML = followupList(updated.reminders || []); toast('Reminder deleted'); }).catch(error => { button.disabled = false; toast(error.message); });
    }
    else if (button.dataset.snoozeReminder) {
      button.disabled = true;
      trackedAction('snooze_reminder', null, { item_id: Number(button.dataset.snoozeReminder) }).then(updated => { $('#followupList').innerHTML = followupList(updated.reminders || []); toast('Reminder moved one day'); }).catch(error => { button.disabled = false; toast(error.message); });
    }
    else if (button.dataset.deleteTimer && confirm('Delete this timer?')) {
      button.disabled = true;
      trackedAction('delete_timer', null, { item_id: Number(button.dataset.deleteTimer) }).then(updated => { $('#timerList').innerHTML = timerList(updated.timers || []); toast('Timer deleted'); }).catch(error => { button.disabled = false; toast(error.message); });
    }
    else if (button.dataset.jump) showCardStep(button.dataset.jump);
  }, listenerOptions);
  root.addEventListener('keydown', event => {
    if (event.target.id === 'quickNote' && event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      saveNote();
    }
  }, listenerOptions);

}

async function driverAction(action, value, extra = {}) {
  const driverId = cardId;
  const returnFollowups = ['note', 'reminder', 'timer', 'delete_note', 'delete_reminder', 'delete_timer', 'complete_reminder', 'snooze_reminder', 'complete_timer'].includes(action);
  const payload = { action, value, ...extra };
  if (returnFollowups) payload.return_followups = true;
  const result = await api(`/api/drivers/${driverId}/action`, {
    method: 'POST',
    body: JSON.stringify(payload)
  });
  invalidate('/api/drivers', '/api/dashboard', '/api/bols', '/api/organizer', '/api/activity', `/api/drivers/${driverId}`);
  return result;
}

function closeCard() {
  state.cardEventsController?.abort();
  state.cardEventsController = null;
  state.cardLoadController?.abort();
  state.cardLoadController = null;
  $('#modal').classList.add('hidden');
  document.body.classList.remove('modal-open');
  cardId = null;
}

$('.close').addEventListener('click', closeCard);
$('#modal').addEventListener('click', event => {
  if (event.target === $('#modal')) closeCard();
});
$('.activity-modal-close').addEventListener('click', closeActivitySummary);
$('#activityModal').addEventListener('click', event => { if (event.target === $('#activityModal')) closeActivitySummary(); });
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && !$('#activityModal').classList.contains('hidden')) closeActivitySummary();
  else if (event.key === 'Escape' && !$('#modal').classList.contains('hidden')) closeCard();
  if (event.altKey && event.key.toLowerCase() === 'n' && !$('#modal').classList.contains('hidden')) {
    event.preventDefault();
    $('#quickNote')?.focus();
  }
  if (event.altKey && !$('#modal').classList.contains('hidden') && ['ArrowLeft', 'ArrowRight'].includes(event.key)) {
    event.preventDefault();
    document.activeElement?.blur();
    awaitPendingSaves().then(successful => {
      if (successful) showCardStep(state.cardStep + (event.key === 'ArrowRight' ? 1 : -1));
    });
  }
});
document.addEventListener('click', async event => {
  const organizerDelete = event.target.closest('[data-organizer-delete]');
  if (organizerDelete) {
    if (!confirm(`Delete this driver ${organizerDelete.dataset.organizerDelete}?`)) return;
    organizerDelete.disabled = true;
    try {
      const driverId = Number(organizerDelete.dataset.driverId);
      await api(`/api/drivers/${driverId}/action`, {
        method: 'POST', body: JSON.stringify({ action: `delete_${organizerDelete.dataset.organizerDelete}`, item_id: Number(organizerDelete.dataset.item) })
      });
      invalidate('/api/organizer', '/api/activity', `/api/drivers/${driverId}`);
      toast('Item deleted');
      await organizer();
    } catch (error) { organizerDelete.disabled = false; toast(error.message); }
    return;
  }
  const activityDelete = event.target.closest('[data-delete-activity]');
  if (activityDelete) {
    if (!confirm('Delete this Daily Review record? This does not reverse the original action.')) return;
    activityDelete.disabled = true;
    try {
      await api(`/api/activity/${activityDelete.dataset.deleteActivity}`, { method: 'DELETE' });
      invalidate('/api/activity');
      toast('Activity record deleted');
      closeActivitySummary();
      await activity();
    } catch (error) { activityDelete.disabled = false; toast(error.message); }
    return;
  }
  const idleReviewDriver = event.target.closest('[data-review-idle-driver]');
  if (idleReviewDriver) { openIdleCoachingSummary(idleReviewDriver.dataset.reviewIdleDriver); return; }
  const reviewDriver = event.target.closest('[data-review-driver]');
  if (reviewDriver) { openActivitySummary(reviewDriver.dataset.reviewDriver); return; }
  const opener = event.target.closest('.open[data-id]');
  const interactive = event.target.closest('button,input,select,textarea,a');
  if (opener && (!interactive || interactive === opener)) { closeActivitySummary(); openCard(Number(opener.dataset.id)); }
});
document.addEventListener('submit', async event => {
  const form = event.target.closest('.quick-truck-form');
  if (!form) return;
  event.preventDefault();
  const driverId = Number(form.dataset.driverId);
  const truck = new FormData(form).get('truck')?.trim();
  if (!driverId || !truck) return;
  const currentTruck = form.dataset.currentTruck?.trim();
  if (currentTruck && !confirm(`Change this driver from truck ${currentTruck} to ${truck.toUpperCase()}? The prior assignment will remain in history.`)) return;
  const button = $('button', form);
  button.disabled = true;
  try {
    await api(`/api/drivers/${driverId}/action`, {
      method: 'POST', body: JSON.stringify({ action: 'assign_truck', value: truck })
    });
    invalidate('/api/drivers', '/api/dashboard', '/api/organizer', '/api/activity', `/api/drivers/${driverId}`);
    toast(`Truck ${truck.toUpperCase()} assigned`);
    if (cardId === driverId) await openCard(driverId);
    else {
      const currentRoute = location.hash.slice(1) || 'dashboard';
      if (currentRoute === 'workflow' || currentRoute === 'pta') await queue(currentRoute === 'pta');
    }
  } catch (error) { toast(error.message); button.disabled = false; }
});
document.addEventListener('change', async event => {
  const checkbox = event.target.closest('[data-organizer-complete]');
  if (!checkbox) return;
  checkbox.disabled = true;
  try {
    await api(`/api/drivers/${checkbox.dataset.driverId}/action`, {
      method: 'POST', body: JSON.stringify({ action: 'complete_reminder', item_id: Number(checkbox.dataset.organizerComplete) })
    });
    invalidate('/api/organizer', '/api/activity', `/api/drivers/${checkbox.dataset.driverId}`);
    toast('Reminder updated');
    organizer();
  } catch (error) { checkbox.checked = !checkbox.checked; checkbox.disabled = false; toast(error.message); }
});

const routes = {
  dashboard,
  pta: () => queue(true),
  workflow: () => queue(false),
  idle: idleCoachingLog,
  bols,
  organizer,
  activity,
  transition,
  imports
};

async function route() {
  const name = location.hash.slice(1) || 'dashboard';
  $$('nav a').forEach(anchor => anchor.classList.toggle('active', anchor.hash === `#${name}`));
  try {
    state.routeController?.abort();
    state.routeController = new AbortController();
    await (routes[name] || dashboard)();
  }
  catch (error) {
    if (error.name === 'AbortError') return;
    $('#app').innerHTML = `<div class="console-fault"><p class="eyebrow">WAA fault</p><h2>Console fault</h2><p>${esc(error.message)}</p></div>`;
  }
}

window.addEventListener('hashchange', route);
route();

let maintenanceWasRunning = false;
async function monitorHealth() {
  try {
    const health = await api('/api/health');
    if (health.integrity !== 'ok') {
      $('#health').textContent = 'RESTORE REQUIRED';
      return;
    }
    if (health.maintenance_status === 'pending' || health.maintenance_status === 'running') {
      maintenanceWasRunning = true;
      $('#health').textContent = 'LOOPBACK · SYNCING';
      setTimeout(monitorHealth, 750);
      return;
    }
    if (health.maintenance_status === 'error') {
      $('#health').textContent = 'SYNC NEEDS ATTENTION';
      toast(health.maintenance_detail || 'Background report scan failed');
      return;
    }
    $('#health').textContent = 'LOOPBACK · SECURE';
    if (maintenanceWasRunning) {
      maintenanceWasRunning = false;
      invalidate('/api/dashboard', '/api/drivers', '/api/bols', '/api/idle-coaching', '/api/report-intake', '/api/data-quality');
      await route();
      toast('Reports and driver identities are current');
    }
  }
  catch {
    $('#health').textContent = 'OFFLINE';
  }
}
monitorHealth();
