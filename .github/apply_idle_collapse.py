from pathlib import Path


def read(path):
    return Path(path).read_text(encoding='utf-8').replace('\r\n', '\n')


def write(path, text):
    Path(path).write_text(text, encoding='utf-8', newline='\n')


p = 'web/app.js'
s = read(p)
old = '''    <section class="glass-panel idle-attention-panel">\n      <div class="panel-title"><div><p class="eyebrow">Current Rolling 7-Day attention</p><h3>Drivers Above 50%</h3></div><span>${currentOver50.length}</span></div>\n      <p class="idle-attention-copy">Highest idle percentage first. Previous and Next in the Work Card will stay inside this current over-50 list.</p>\n      <div class="idle-attention-list">'''
new = '''    <details class="glass-panel idle-attention-panel idle-attention-details" open>\n      <summary class="idle-attention-toggle">\n        <span class="idle-attention-toggle-title"><span class="eyebrow">Current Rolling 7-Day attention</span><b>Drivers Above 50%</b></span>\n        <span class="idle-attention-toggle-meta"><strong>${currentOver50.length}</strong><em aria-hidden="true"></em></span>\n      </summary>\n      <div class="idle-attention-body">\n        <p class="idle-attention-copy">Highest idle percentage first. Previous and Next in the Work Card will stay inside this current over-50 list.</p>\n        <div class="idle-attention-list">'''
if old not in s:
    raise SystemExit('Idle attention opening block not found')
s = s.replace(old, new, 1)

old = '''      }).join('') : '<p class="empty-copy idle-attention-empty">Nobody is above 50% on the latest Rolling 7-Day report.</p>'}</div>\n    </section>\n    <section class="glass-panel idle-log-panel">'''
new = '''      }).join('') : '<p class="empty-copy idle-attention-empty">Nobody is above 50% on the latest Rolling 7-Day report.</p>'}</div>\n      </div>\n    </details>\n    <section class="glass-panel idle-log-panel">'''
if old not in s:
    raise SystemExit('Idle attention closing block not found')
s = s.replace(old, new, 1)
write(p, s)

p = 'web/styles.css'
s = read(p)
marker = '\n/* Idle Coaching collapsible attention queue */\n'
if marker not in s:
    s = s.rstrip() + '''\n\n/* Idle Coaching collapsible attention queue */\n.idle-attention-details{padding:0}.idle-attention-toggle{display:flex;align-items:center;justify-content:space-between;gap:18px;padding:18px 20px;cursor:pointer;list-style:none;user-select:none;border-bottom:1px solid transparent;transition:.15s ease}.idle-attention-toggle::-webkit-details-marker{display:none}.idle-attention-toggle::marker{content:""}.idle-attention-toggle:hover,.idle-attention-toggle:focus-visible{background:#ffffff06;outline:none}.idle-attention-details[open]>.idle-attention-toggle{border-bottom-color:var(--edge-soft)}.idle-attention-toggle-title{display:block}.idle-attention-toggle-title .eyebrow{display:block;margin-bottom:5px}.idle-attention-toggle-title>b{font-family:Bahnschrift,"Segoe UI",sans-serif;text-transform:uppercase;letter-spacing:.08em;font-size:17px}.idle-attention-toggle-meta{display:flex;align-items:center;gap:12px}.idle-attention-toggle-meta>strong{display:grid;place-items:center;min-width:34px;height:30px;padding:0 9px;border:1px solid #3b495e;color:var(--muted);font:11px Consolas,monospace}.idle-attention-toggle-meta>em{display:flex;align-items:center;gap:7px;color:var(--blue);font-style:normal;font-size:10px;font-weight:800;text-transform:uppercase;letter-spacing:.08em}.idle-attention-toggle-meta>em::before{content:"Collapse"}.idle-attention-toggle-meta>em::after{content:"⌃";display:inline-block;font-size:16px;line-height:1;transition:transform .15s ease}.idle-attention-details:not([open]) .idle-attention-toggle-meta>em::before{content:"Expand"}.idle-attention-details:not([open]) .idle-attention-toggle-meta>em::after{content:"⌄"}.idle-attention-body{padding:16px 20px 20px}.idle-attention-body .idle-attention-copy{margin:0 0 15px}@media(max-width:640px){.idle-attention-toggle{padding:15px}.idle-attention-body{padding:14px 15px 16px}.idle-attention-toggle-meta>em::before{display:none}}\n'''
write(p, s)
