export async function renderDotTracking(context) {
  const { api, esc, pageHead, toast, invalidate } = context;
  const data = await api('/api/dot');
  const rows = data.rows || [];
  let sortKey = 'days_overdue';
  let sortDir = 'desc';

  const options = (values, label) => {
    const clean = [...new Set(values.filter(Boolean).map(String))].sort((a, b) => a.localeCompare(b));
    return `<option value="">${esc(label)}</option>${clean.map(value => `<option value="${esc(value)}">${esc(value)}</option>`).join('')}`;
  };
  const distanceCell = row => {
    const miles = row.miles_from_83501 == null ? null : Number(row.miles_from_83501);
    const summary = Number.isFinite(miles)
      ? `<b>${miles.toFixed(miles < 10 ? 1 : 0)} mi</b><small>${esc(row.location_label || 'Stored customer distance')}</small>`
      : '<b>Unknown</b><small>Set once per customer</small>';
    return `<details class="dot-distance-editor"><summary>${summary}</summary>
      <form data-dot-location="${esc(row.customer_key)}">
        <label>Location label<input name="location" value="${esc(row.location_label || '')}" placeholder="City, site, or ZIP"></label>
        <label>Miles from ${esc(data.origin_zip)}<input name="miles" inputmode="decimal" value="${Number.isFinite(miles) ? miles : ''}" placeholder="0" required></label>
        <input type="hidden" name="customer" value="${esc(row.customer || '')}">
        <button type="submit">Save distance</button>
      </form>
    </details>`;
  };
  const header = (key, label) => `<button type="button" class="sheet-sort" data-sort="${key}">${esc(label)}<span data-sort-mark="${key}"></span></button>`;
  document.querySelector('#app').innerHTML = pageHead(
    'DOT Trailers',
    `One row per trailer. Last DOT is the inspection date; due is 365 days later, and overdue is days past that due point. Distance is stored by customer from ZIP ${data.origin_zip}.`,
    'WAA // DOT Accountability'
  ) + `
    <section class="sheet-panel">
      <div class="sheet-toolbar">
        <div class="searchbox"><span aria-hidden="true">⌕</span><input id="dotSearch" aria-label="Search DOT trailers" placeholder="Search trailer, customer, CSR, status, KMA"></div>
        <select id="dotStatus" aria-label="Filter DOT trailers by status">${options(rows.map(row => row.status), 'All statuses')}</select>
        <select id="dotKma" aria-label="Filter DOT trailers by KMA">${options(rows.map(row => row.kma), 'All KMAs')}</select>
        <label class="sheet-toggle"><input id="dotShowHidden" type="checkbox"> Show hidden (<span id="dotHiddenCount">${Number(data.hidden_count || 0)}</span>)</label>
        <span id="dotCount" class="queue-count" aria-live="polite"></span>
      </div>
      <div class="sheet-summary">
        <span><b>${rows.length}</b> trailers</span>
        <span><b>${rows.filter(row => row.age_days != null).length}</b> dated</span>
        <span><b id="dotUnresolvedCount">${Number(data.unresolved_locations || 0)}</b> distances unresolved</span>
        <span>Source: ${esc(data.filename || 'Waiting for DOT report')}</span>
      </div>
      <div class="table-scroll dot-sheet-scroll"><table class="dot-sheet">
        <thead><tr>
          <th>${header('trailer','Trailer')}</th><th>${header('days_overdue','Due Status')}</th><th>${header('last_dot_date','Inspection')}</th><th>${header('due_date','Due')}</th>
          <th>${header('miles_from_83501',`Miles from ${data.origin_zip}`)}</th><th>${header('status','Status')}</th><th>${header('customer','Customer')}</th>
          <th>${header('kma','KMA')}</th><th>${header('responsible_csr','CSR')}</th><th>${header('responsible_csr_supervisor','Supervisor')}</th><th>Visibility</th>
        </tr></thead><tbody id="dotBody"></tbody>
      </table></div>
    </section>`;
  const valueOf = (row, key) => {
    const value = row[key];
    if (key === 'days_overdue' || key === 'miles_from_83501') return value == null || value === '' ? null : Number(value);
    return String(value || '').toLowerCase();
  };
  const compare = (a, b) => {
    const av = valueOf(a, sortKey), bv = valueOf(b, sortKey);
    if (av == null && bv == null) return String(a.trailer).localeCompare(String(b.trailer));
    if (av == null) return 1;
    if (bv == null) return -1;
    const result = typeof av === 'number' ? av - bv : av.localeCompare(bv);
    return sortDir === 'asc' ? result : -result;
  };
  const draw = () => {
    const query = document.querySelector('#dotSearch').value.trim().toLowerCase();
    const status = document.querySelector('#dotStatus').value;
    const kma = document.querySelector('#dotKma').value;
    const showHidden = document.querySelector('#dotShowHidden').checked;
    const visible = rows.filter(row => {
      const haystack = Object.values(row).join(' ').toLowerCase();
      return (showHidden || !Number(row.hidden)) && (!query || haystack.includes(query)) && (!status || row.status === status) && (!kma || row.kma === kma);
    }).sort(compare);
    document.querySelector('#dotBody').innerHTML = visible.map(row => `
      <tr class="${Number(row.hidden) ? 'dot-hidden-row' : ''}">
        <td><b class="truck-no">${esc(row.trailer)}</b></td>
        <td class="dot-age"><b>${row.days_overdue == null ? 'Unknown' : Number(row.days_overdue) > 0 ? `${Number(row.days_overdue).toLocaleString()}d overdue` : Number(row.days_overdue) === 0 ? 'Due today' : `${Math.abs(Number(row.days_overdue)).toLocaleString()}d left`}</b></td>
        <td>${esc(row.last_dot_date || 'Unknown')}</td><td>${esc(row.due_date || 'Unknown')}</td>
        <td>${distanceCell(row)}</td><td>${esc(row.status || 'Unknown')}</td><td>${esc(row.customer || 'Unknown')}</td>
        <td>${esc(row.kma || 'Unknown')}</td><td>${esc(row.responsible_csr || 'Unknown')}</td><td>${esc(row.responsible_csr_supervisor || 'Unknown')}</td>
        <td><button class="compact ${Number(row.hidden) ? '' : 'secondary'}" type="button" data-dot-hide="${esc(row.trailer)}" data-hidden="${Number(row.hidden) ? 'false' : 'true'}">${Number(row.hidden) ? 'Unhide' : 'Hide'}</button></td>
      </tr>`).join('') || '<tr><td colspan="11" class="sheet-empty">No trailers match this view.</td></tr>';
    document.querySelector('#dotCount').textContent = `${visible.length} shown`;
    document.querySelector('#dotHiddenCount').textContent = rows.filter(row => Number(row.hidden)).length;
    document.querySelector('#dotUnresolvedCount').textContent = rows.filter(row => row.miles_from_83501 == null || row.miles_from_83501 === '').length;
    document.querySelectorAll('[data-sort-mark]').forEach(node => { node.textContent = node.dataset.sortMark === sortKey ? (sortDir === 'asc' ? ' ▲' : ' ▼') : ''; });
  };
  document.querySelector('#dotSearch').addEventListener('input', draw);
  document.querySelector('#dotStatus').addEventListener('change', draw);
  document.querySelector('#dotKma').addEventListener('change', draw);
  document.querySelector('#dotShowHidden').addEventListener('change', draw);
  document.querySelectorAll('[data-sort]').forEach(button => button.addEventListener('click', () => {
    const key = button.dataset.sort;
    if (sortKey === key) sortDir = sortDir === 'asc' ? 'desc' : 'asc';
    else { sortKey = key; sortDir = key === 'days_overdue' ? 'desc' : 'asc'; }
    draw();
  }));
  document.querySelector('#dotBody').addEventListener('click', async event => {
    const button = event.target.closest('[data-dot-hide]');
    if (!button) return;
    button.disabled = true;
    try {
      const hidden = button.dataset.hidden === 'true';
      await api('/api/dot/visibility', { method: 'POST', body: JSON.stringify({ trailer: button.dataset.dotHide, hidden }) });
      const row = rows.find(item => String(item.trailer) === String(button.dataset.dotHide));
      if (row) row.hidden = hidden ? 1 : 0;
      invalidate('/api/dot');
      toast(hidden ? 'Trailer hidden' : 'Trailer restored');
      draw();
    } catch (error) { button.disabled = false; toast(error.message); }
  });
  document.querySelector('#dotBody').addEventListener('submit', async event => {
    const form = event.target.closest('[data-dot-location]');
    if (!form) return;
    event.preventDefault();
    const button = form.querySelector('button');
    button.disabled = true;
    const fields = new FormData(form);
    try {
      const payload = {
        customer_key: form.dataset.dotLocation,
        customer: fields.get('customer'),
        location_label: fields.get('location'),
        miles_from_83501: fields.get('miles')
      };
      const saved = await api('/api/dot/location', { method: 'POST', body: JSON.stringify(payload) });
      rows.filter(row => row.customer_key === payload.customer_key).forEach(row => {
        row.location_label = saved.location_label;
        row.miles_from_83501 = saved.miles_from_83501;
      });
      invalidate('/api/dot');
      toast(`Distance saved for ${payload.customer}`);
      draw();
    } catch (error) { button.disabled = false; toast(error.message); }
  });
  draw();
}
