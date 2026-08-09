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

const state = { cache: new Map(), routeController: null };
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
    compact = false
  } = options;

  const points = (rows || [])
    .map(row => ({ date: row[dateField], value: Number(row[field] ?? row.percent) }))
    .filter(point => Number.isFinite(point.value));

  if (!points.length) {
    return `<div class="chart-empty"><span>No valid history yet</span><small>The graph will wake up as reports accumulate.</small></div>`;
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
              <filter id="${id}-glow"><feGaussianBlur stdDeviation="3" result="blur"></feGaussianBlur><feMerge><feMergeNode in="blur"></feMergeNode><feMergeNode in="SourceGraphic"></feMergeNode></feMerge></filter>
            </defs>
            ${grid}
            <path class="chart-area" d="${areaPath}" fill="url(#${id}-fill)"></path>
            <path class="chart-line" d="${linePath}" filter="url(#${id}-glow)"></path>
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

function metricCard(label, value, detail, tone = 'green') {
  return `<div class="metric-card ${tone}"><span>${esc(label)}</span><b>${esc(value)}</b><small>${esc(detail)}</small><i aria-hidden="true"></i></div>`;
}

async function dashboard() {
  const data = await cachedApi('/api/dashboard');
  const heroList = (items, kind) => `
    <section class="rank-panel ${kind}">
      <div class="panel-title"><div><p class="eyebrow">${kind === 'heroes' ? 'Steal the good habits' : 'Coaching queue'}</p><h3>${kind === 'heroes' ? 'Heroes' : 'Heroes in Training'}</h3></div><span>${items.length}</span></div>
      <div class="rank-list">
        ${items.map((item, index) => `
          <button class="rank-row open" data-id="${item.id}">
            <span class="rank-number">0${index + 1}</span>
            <span class="rank-driver"><b>${esc(item.full_name)}</b><small>Truck ${esc(item.truck)} · ${esc(item.pta_code)}</small></span>
            <span class="rank-value"><b>${fmtPercent(item.p7)}</b><small>28D ${fmtPercent(item.p28)} · ${fmtHours(item.engine7)}</small></span>
          </button>`).join('') || '<p class="empty-copy">Waiting for idle history.</p>'}
      </div>
    </section>`;

  $('#app').innerHTML = pageHead('Fleet Pulse', 'A glanceable live board: what is healthy, what needs attention, and who has habits worth copying.') + `
    <section class="metrics-strip">
      ${metricCard('Over 50% idle', String(data.over50), 'Latest valid rolling 7-day', data.over50 ? 'red' : 'green')}
      ${metricCard('Tracked drivers', String(data.drivers.length), 'Drivers with current idle data', 'blue')}
      ${metricCard('Best current idle', data.heroes?.[0] ? fmtPercent(data.heroes[0].p7) : '—', data.heroes?.[0]?.full_name || 'Awaiting data', 'green')}
      ${metricCard('Coaching focus', data.training?.[0] ? fmtPercent(data.training[0].p7) : '—', data.training?.[0]?.full_name || 'Awaiting data', 'purple')}
    </section>
    <section class="dashboard-grid">
      <div class="glass-panel chart-panel green-edge">
        <div class="panel-title"><div><p class="eyebrow">Fleet trend</p><h3>Rolling 7-Day Idle</h3></div><span class="pulse-dot"></span></div>
        ${chart(data.history7, { tone: 'green', title: 'Fleet weighted rolling 7-day idle' })}
      </div>
      <div class="glass-panel chart-panel purple-edge">
        <div class="panel-title"><div><p class="eyebrow">Long view</p><h3>28-Day Coverage</h3></div><span class="pulse-dot purple"></span></div>
        ${chart(data.history28, { tone: 'purple', title: 'Fleet 28-day view' })}
      </div>
    </section>
    <section class="dashboard-grid rank-grid">
      ${heroList(data.heroes || [], 'heroes')}
      ${heroList(data.training || [], 'training')}
    </section>`;

  bindCharts($('#app'));
}

async function loadDrivers() {
  drivers = await cachedApi('/api/drivers') || [];
  drivers.forEach(driver => { driver._search = Object.values(driver).join(' ').toLowerCase(); });
  return drivers;
}

function driverRows(list, showPta) {
  return list.map(driver => `
    <tr class="open" data-id="${driver.id}" data-search="${esc(driver._search)}" data-priority="${priority(driver)}" data-truck-state="${hasTruck(driver) ? 'assigned' : 'unassigned'}">
      ${showPta ? `<td class="pta-cell ${priority(driver)}"><span class="priority-chip">${priority(driver).toUpperCase()}</span><b>${esc(driver.pta_raw || 'N/A')}</b><small>${esc(relative(driver.pta_at))}</small></td>` : ''}
      <td>${hasTruck(driver) ? `<b class="truck-no">${esc(driver.truck)}</b>` : `<form class="quick-truck-form" data-driver-id="${driver.id}"><input name="truck" aria-label="Truck number for ${esc(driver.full_name)}" placeholder="Truck #" maxlength="24" required><button type="submit">Assign</button></form>`}</td>
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
    : 'Open a driver and move through a natural phone conversation instead of a wall of checkboxes.';

  $('#app').innerHTML = pageHead(title, subtitle) + `
    <section class="glass-panel table-panel">
      <div class="table-toolbar">
        <div class="searchbox"><span aria-hidden="true">⌕</span><input id="search" placeholder="Search driver, truck, division, status, location"></div>
        <select id="filter" aria-label="${isPta ? 'Priority' : 'Truck assignment'} filter">
          ${isPta ? '<option value="">All states</option><option value="pinned">Pinned 23:57</option><option value="overdue">Overdue</option><option value="immediate">Immediate</option><option value="future">Future</option>' : '<option value="">All drivers</option><option value="unassigned">Needs Truck</option><option value="assigned">Truck Assigned</option>'}
        </select>
      </div>
      <div class="table-scroll"><table><thead><tr>${isPta ? '<th>PTA / Priority</th>' : ''}<th>Truck</th><th>Driver</th><th>Div</th><th>Operational</th><th>Planning</th><th>Note</th><th>Type</th><th>Location</th></tr></thead><tbody></tbody></table></div>
    </section>`;

  let baseList = [...drivers];
  if (isPta) baseList = baseList.filter(driver => driver.actionable).sort((a, b) => {
    const pa = priority(a) === 'pinned' ? -1 : 0;
    const pb = priority(b) === 'pinned' ? -1 : 0;
    return pa - pb || new Date(a.pta_at) - new Date(b.pta_at);
  });
  $('tbody').innerHTML = driverRows(baseList, isPta);
  const draw = () => {
    const query = $('#search').value.toLowerCase();
    const filter = $('#filter').value;
    $$('tbody tr').forEach(row => { const stateValue = isPta ? row.dataset.priority : row.dataset.truckState; row.hidden = !row.dataset.search.includes(query) || !!filter && stateValue !== filter; });
  };

  $('#search').addEventListener('input', debounce(draw));
  $('#filter').addEventListener('change', draw);
  draw();
}

async function bols() {
  const list = await cachedApi('/api/bols');
  list.forEach(item => { item._search = Object.values(item).join(' ').toLowerCase(); });
  $('#app').innerHTML = pageHead('Missing BOLs', 'A persistent admin queue. During a driver call, these live near the end of the conversation—not at hello.') + `
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
    $$('tbody tr').forEach(row => { row.hidden = !row.dataset.search.includes(query) || !!filter && row.dataset.state !== filter; });
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
  return `<label class="organizer-item reminder ${overdue ? 'late' : ''} ${item.completed_at ? 'complete' : ''}">
    <input data-organizer-complete="${item.id}" data-driver-id="${item.driver_id}" type="checkbox" ${item.completed_at ? 'checked' : ''}>
    <span class="organizer-driver"><b>${esc(item.truck)} · ${esc(item.full_name)}</b><small>${esc(displayDate(item.due_at))}</small></span>
    <span class="organizer-copy">${esc(item.text)}</span>
  </label>`;
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
      <span class="organizer-driver"><b>${esc(item.truck)} · ${esc(item.full_name)}</b><small>${esc(displayDate(item.created_at))}</small></span>
      <p class="organizer-copy">${esc(item.text)}</p>
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
    const driverId = Number($('#organizerDriver').value);
    const type = $('#organizerType').value;
    const text = $('#organizerText').value.trim();
    const due = $('#organizerDue').value;
    if (!driverId || !text || (type === 'reminder' && !due)) { toast('Choose a driver and complete the item'); return; }
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
  pta: 'PTA updated', assign_truck: 'Truck assigned', bol_mentioned: 'Missing BOL status changed', call_flow_update: 'Call flow updated',
  transition_regenerated: 'Transition regenerated', transition_saved: 'Transition saved',
  home_checked: 'Home time reviewed', preplan_reviewed: 'Preplan reviewed', routing_checked: 'Routing reviewed',
  safety_mentioned_at: 'Safety discussed', include_transition: 'Transition selection changed'
};

function localDayRange(value) {
  const start = new Date(`${value}T00:00:00`);
  const end = new Date(start);
  end.setDate(end.getDate() + 1);
  return { start: start.toISOString(), end: end.toISOString() };
}

async function activity() {
  const today = new Date();
  const defaultDay = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
  const selected = activity.selectedDay || defaultDay;
  const range = localDayRange(selected);
  const path = `/api/activity?start=${encodeURIComponent(range.start)}&end=${encodeURIComponent(range.end)}`;
  const rows = await cachedApi(path, 5000);
  const driverRows = rows.filter(row => row.driver_id);
  const render = () => {
    const driverId = $('#activityDriver').value;
    const visible = rows.filter(row => !driverId || String(row.driver_id) === driverId);
    $('#activityList').innerHTML = visible.map(row => {
      let detail = {};
      try { detail = JSON.parse(row.detail_json || '{}'); } catch { detail = {}; }
      const detailText = detail.text || detail.field || detail.action || '';
      return `<article class="activity-row ${row.driver_id ? '' : 'system'}">
        <time>${esc(new Date(`${row.occurred_at.replace(' ', 'T')}Z`).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }))}</time>
        <div><b>${esc(activityLabels[row.action] || row.action.replaceAll('_', ' '))}</b><small>${row.driver_id ? `${esc(row.truck)} · ${esc(row.full_name)}` : 'WAA operations'}</small>${detailText ? `<p>${esc(detailText)}</p>` : ''}</div>
        ${row.driver_id ? `<button class="secondary open" data-id="${row.driver_id}" type="button">Open Driver</button>` : ''}
      </article>`;
    }).join('') || '<p class="empty-copy">No recorded activity for this day.</p>';
    $('#activityCount').textContent = `${visible.length} action${visible.length === 1 ? '' : 's'}`;
  };
  const uniqueDrivers = [...new Map(driverRows.map(row => [row.driver_id, row])).values()];
  $('#app').innerHTML = pageHead('Daily Activity Review', 'Review what you completed today with every action tied back to its driver.', 'WAA // Daily Record') + `
    <section class="glass-panel activity-toolbar">
      <label class="field"><span>Review date</span><input id="activityDate" type="date" value="${selected}"></label>
      <label class="field"><span>Driver</span><select id="activityDriver"><option value="">All activity</option>${uniqueDrivers.map(row => `<option value="${row.driver_id}">${esc(row.truck)} · ${esc(row.full_name)}</option>`).join('')}</select></label>
      <div class="activity-summary"><span id="activityCount"></span><b>${uniqueDrivers.length} drivers touched</b></div>
    </section>
    <section class="glass-panel"><div class="panel-title"><div><p class="eyebrow">Chronological record</p><h3>${esc(new Date(`${selected}T12:00:00`).toLocaleDateString([], { weekday: 'long', month: 'long', day: 'numeric' }))}</h3></div></div><div id="activityList" class="activity-list"></div></section>`;
  $('#activityDate').addEventListener('change', event => { activity.selectedDay = event.target.value; invalidate('/api/activity'); activity(); });
  $('#activityDriver').addEventListener('change', render);
  render();
}

async function transition() {
  const data = await api('/api/transition');
  $('#app').innerHTML = pageHead('Transition Draft', 'A clean handoff, not another system to wrestle. Edit it like normal text and copy when you are done.') + `
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
      <div><p class="eyebrow">Automatic intake</p><h3>${esc(title)}</h3><b>${esc(item?.source_name || 'Waiting for report')}</b><small>${esc(item?.source_modified_utc ? displayDate(item.source_modified_utc) : 'No matching download yet')}</small><p>${esc(item?.detail || 'WAA is watching Downloads for the newest matching report.')}</p></div>
      <span class="status-pill ${item?.status === 'Error' ? 'alert' : item?.status === 'Imported' || item?.status === 'Current' ? 'good' : ''}">${esc(item?.status || 'Waiting')}</span>
    </div>`;

  $('#app').innerHTML = pageHead('Imports / Data Quality', 'Downloads feeds idle and Missing BOL automatically. PTA stays intentional: copy, preview, commit.') + `
    <section class="intake-header glass-panel">
      <div><p class="eyebrow">Downloads watcher</p><h3>${esc(intake.downloads_path)}</h3><p>Only the newest matching report of each type is considered. Originals remain untouched.</p></div>
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
    toast(changed ? 'Newest reports imported' : 'Downloads already current');
    imports();
  });
  $('#preview').addEventListener('click', async () => {
    preview = await api('/api/import/preview', { method: 'POST', body: JSON.stringify({ raw: $('#raw').value, type: 'pta' }) });
    $('#previewOut').textContent = JSON.stringify(preview, null, 2);
    $('#commit').disabled = !!preview.errors.length;
  });
  $('#commit').addEventListener('click', async () => {
    await api('/api/import/commit', { method: 'POST', body: JSON.stringify({ raw: $('#raw').value, type: 'pta' }) });
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

function callStep(number, title, prompt, body, tone = '') {
  return `
    <section class="call-step ${tone}" data-step="${number}">
      <div class="step-number">${String(number).padStart(2, '0')}</div>
      <div class="step-body"><p class="step-prompt">${esc(prompt)}</p><h3>${esc(title)}</h3>${body}<div class="step-save" aria-live="polite">Auto-saves as you move through the call</div></div>
    </section>`;
}

function noteList(notes) {
  return notes.length
    ? notes.slice(0, 8).map(note => `<div class="note-chip"><p>${esc(note.note)}</p><small>${esc(displayDate(note.created_at))}</small></div>`).join('')
    : '<p class="empty-copy">Nothing captured yet. Keep this conversational—only save what will actually help later.</p>';
}

function followupList(reminders) {
  return reminders.length ? reminders.map(reminder => `<label class="follow-row ${!reminder.completed_at && new Date(reminder.due_at) < new Date() ? 'late' : ''}"><input class="item-action" data-action="complete_reminder" data-item="${reminder.id}" type="checkbox" ${reminder.completed_at ? 'checked' : ''}><span>${esc(reminder.text)}<small>${esc(displayDate(reminder.due_at))}</small></span></label>`).join('') : '<p class="empty-copy">No reminders.</p>';
}

async function openCard(id) {
  if (!id) return;
  cardId = id;
  const [card, conversation] = await Promise.all([
    api(`/api/drivers/${id}`),
    api(`/api/drivers/${id}/conversation`)
  ]);
  const driver = card.driver;
  const work = card.work || {};
  const latestIdle = card.idle?.[0];
  const idlePrompt = Number(latestIdle?.percent) > 50
    ? 'What can we change together to pull that idle number down?'
    : 'What is working well that is keeping idle under control?';

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
      <details class="inline-detail"><summary>Adjust imported PTA only if needed</summary><label class="field"><span>Manual PTA observation</span><input data-action="pta" type="datetime-local" value="${esc(driver.pta_at?.slice(0, 16) || '')}"></label></details>`, 'blue-step'),
    callStep(3, 'Idle Coaching', idlePrompt, `
      <div class="idle-coach-head"><div><span>Latest 7D</span><b>${fmtPercent(latestIdle?.percent)}</b></div><div><span>Engine</span><b>${fmtHours(latestIdle?.engine_hours)}</b></div><div><span>Idle</span><b>${fmtHours(latestIdle?.idle_hours)}</b></div></div>
      ${chart([...(card.idle || [])].reverse(), { field: 'percent', tone: 'green', title: 'Driver rolling idle history', compact: true })}
      ${conversationArea('What is their plan / what is working?', 'idle_plan', conversation.idle_plan, 'Keep it natural: “going to shut down during long waits”, “APU issue needs help”, “current routine is working”…')}`, 'purple-step'),
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
      ${conversationArea('Anything else worth remembering?', 'conversation_wrap', conversation.conversation_wrap, 'Not a report. Just the thing Future You will be glad you wrote down.')}
      <div class="wrap-grid">${checkField('Include in Transition', 'include_transition', work.include_transition)}${textField('Transition note', 'transition_note', work.transition_note, 'Only the handoff-worthy part')}</div>`, 'green-step')
  ].join('');

  $('#card').innerHTML = `
    <header class="driver-hero">
      <div class="driver-sigil" aria-hidden="true">${esc((driver.full_name || '?').slice(0, 1).toUpperCase())}</div>
      <div class="driver-identity"><p class="eyebrow">Live driver conversation</p><h2>${esc(driver.truck)} <span>·</span> ${esc(driver.full_name)}</h2><p>${esc(driver.pta_code)} · Div ${esc(driver.division)} · ${esc(driver.operational_status)} / ${esc(driver.planning_status)} · ${esc(driver.driver_type)} · ${esc(driver.location)}</p></div>
      <div class="driver-pta"><span>PTA</span><b>${esc(driver.pta_raw || 'N/A')}</b><small>${esc(relative(driver.pta_at))}</small></div>
    </header>
    <div class="call-progress" aria-label="Call flow">
      ${['Fuel', 'ETA', 'Idle', 'Load', 'Home', 'BOL', 'Wrap'].map((name, index) => `<button type="button" data-jump="${index + 1}"><span>${index + 1}</span>${name}</button>`).join('')}
    </div>
    <div class="call-layout">
      <main class="call-main">${flow}</main>
      <aside class="call-side">
        <section class="side-card snapshot"><p class="eyebrow">Driver snapshot</p><dl><div><dt>Truck</dt><dd>${esc(driver.truck)}</dd></div><div><dt>Location</dt><dd>${esc(driver.location)}</dd></div><div><dt>Status</dt><dd>${esc(driver.operational_status)}</dd></div><div><dt>Planning</dt><dd>${esc(driver.planning_status)}</dd></div><div><dt>Freshness</dt><dd>${esc(displayDate(driver.observed_at))}</dd></div></dl>${hasTruck(driver) ? '' : `<div class="unassigned-truck"><p>No truck association is on record.</p><form class="quick-truck-form" data-driver-id="${driver.id}"><input name="truck" aria-label="Truck number" placeholder="Enter truck #" maxlength="24" required><button type="submit">Assign Truck</button></form></div>`}</section>
        <section class="side-card notes-rail"><div class="side-title"><div><p class="eyebrow">Remember this</p><h3>Call Notes</h3></div><span>Alt+N</span></div><p class="rail-copy">Use this like a scratchpad, not a form. Save the sentence you would tell yourself later.</p><div class="quick-note"><textarea id="quickNote" placeholder="Driver mentioned…"></textarea><button id="saveNote" type="button">Save Note</button></div><div id="noteList">${noteList(card.notes || [])}</div></section>
        <section class="side-card followups"><p class="eyebrow">After the call</p><h3>Follow-ups</h3><div class="follow-add"><input id="remtext" placeholder="Reminder"><input id="remdue" type="datetime-local"><button id="addReminder" type="button">Add</button></div><div id="followupList">${followupList(card.reminders || [])}</div></section>
      </aside>
    </div>`;

  $('#modal').classList.remove('hidden');
  document.body.classList.add('modal-open');
  bindCharts($('#card'));
  bindCardEvents(card);
  requestAnimationFrame(() => $('.call-main [data-conversation], .call-main [data-action]')?.focus());
}

function bindCardEvents(card) {
  const root = $('#card');
  const setSaved = element => {
    const step = element.closest('.call-step');
    if (!step) return;
    const status = $('.step-save', step);
    status.textContent = 'Saved';
    step.classList.add('saved');
    setTimeout(() => {
      status.textContent = 'Auto-saves as you move through the call';
      step.classList.remove('saved');
    }, 1400);
  };

  const saveNote = async () => {
    const box = $('#quickNote');
    const text = box.value.trim();
    if (!text) return;
    const updated = await driverAction('note', null, { text });
    box.value = '';
    $('#noteList').innerHTML = noteList(updated.notes || []);
    toast('Note kept');
  };
  const addReminder = async () => {
    const text = $('#remtext').value.trim();
    const due = $('#remdue').value;
    if (!text || !due) return;
    const updated = await driverAction('reminder', null, { text, due_at: due });
    $('#remtext').value = '';
    $('#remdue').value = '';
    $('#followupList').innerHTML = followupList(updated.reminders || []);
    toast('Reminder added');
  };
  const newSafetyNote = async () => {
    const note = await api('/api/safety/random');
    $('#safety').textContent = note.note;
    await driverAction('safety_note_id', note.id);
  };

  root.addEventListener('change', async event => {
    const element = event.target;
    if (element.matches('[data-conversation]')) {
      await api(`/api/drivers/${cardId}/conversation`, {
        method: 'POST', body: JSON.stringify({ field: element.dataset.conversation, value: element.value })
      });
      invalidate('/api/activity', `/api/drivers/${cardId}`);
      setSaved(element);
    } else if (element.matches('.item-action')) {
      await driverAction(element.dataset.action, element.checked, { item_id: Number(element.dataset.item) });
      toast('Saved');
    } else if (element.matches('[data-action]')) {
      const value = element.type === 'checkbox' ? element.checked : element.value;
      await driverAction(element.dataset.action, value);
      setSaved(element);
    }
  });
  root.addEventListener('click', event => {
    const button = event.target.closest('button');
    if (!button) return;
    if (button.id === 'saveNote') saveNote();
    else if (button.id === 'addReminder') addReminder();
    else if (button.id === 'random') newSafetyNote();
    else if (button.dataset.jump) $(`.call-step[data-step="${button.dataset.jump}"]`, root)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  });
  root.addEventListener('keydown', event => {
    if (event.target.id === 'quickNote' && event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      saveNote();
    }
  });

  root.addEventListener('focusin', event => {
    const step = event.target.closest('.call-step');
    if (!step) return;
    const number = step.dataset.step;
    $$('[data-jump]', root).forEach(button => button.classList.toggle('active', button.dataset.jump === number));
    $$('.call-step', root).forEach(node => node.classList.toggle('active', node === step));
  });
}

async function driverAction(action, value, extra = {}) {
  const result = await api(`/api/drivers/${cardId}/action`, {
    method: 'POST',
    body: JSON.stringify({ action, value, ...extra })
  });
  invalidate('/api/drivers', '/api/dashboard', '/api/bols', '/api/organizer', '/api/activity', `/api/drivers/${cardId}`);
  return result;
}

function closeCard() {
  $('#modal').classList.add('hidden');
  document.body.classList.remove('modal-open');
  cardId = null;
}

$('.close').addEventListener('click', closeCard);
$('#modal').addEventListener('click', event => {
  if (event.target === $('#modal')) closeCard();
});
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && !$('#modal').classList.contains('hidden')) closeCard();
  if (event.altKey && event.key.toLowerCase() === 'n' && !$('#modal').classList.contains('hidden')) {
    event.preventDefault();
    $('#quickNote')?.focus();
  }
});
document.addEventListener('click', event => {
  const opener = event.target.closest('.open[data-id]');
  if (opener && !event.target.closest('button,input,select,textarea,a')) openCard(Number(opener.dataset.id));
});
document.addEventListener('submit', async event => {
  const form = event.target.closest('.quick-truck-form');
  if (!form) return;
  event.preventDefault();
  const driverId = Number(form.dataset.driverId);
  const truck = new FormData(form).get('truck')?.trim();
  if (!driverId || !truck) return;
  const button = $('button', form);
  button.disabled = true;
  try {
    await api(`/api/drivers/${driverId}/action`, {
      method: 'POST', body: JSON.stringify({ action: 'assign_truck', value: truck })
    });
    invalidate('/api/drivers', '/api/dashboard', '/api/organizer', '/api/activity', `/api/drivers/${driverId}`);
    toast(`Truck ${truck.toUpperCase()} assigned`);
    if (cardId === driverId) await openCard(driverId);
    else if ((location.hash.slice(1) || 'dashboard') === 'workflow') await queue(false);
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
api('/api/health')
  .then(health => { $('#health').textContent = health.integrity === 'ok' ? 'LOOPBACK · SECURE' : 'RESTORE REQUIRED'; })
  .catch(() => { $('#health').textContent = 'OFFLINE'; });
route();
