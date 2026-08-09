from pathlib import Path

path = Path('src/ReportParsing.ps1')
text = path.read_text(encoding='utf-8')
old = "    conversation_wrap=coalesce(nullif(w.conversation_wrap,''),(SELECT l.conversation_wrap FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key)),\n    updated_at=CURRENT_TIMESTAMP"
new = "    conversation_wrap=coalesce(nullif(w.conversation_wrap,''),(SELECT l.conversation_wrap FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key)),\n    completed_at=coalesce(w.completed_at,(SELECT l.completed_at FROM driver_call_sessions l WHERE l.driver_id=$LoserId AND l.cycle_key=w.cycle_key)),\n    updated_at=CURRENT_TIMESTAMP"
if old not in text:
    raise SystemExit('driver call-session merge marker not found')
path.write_text(text.replace(old, new, 1), encoding='utf-8', newline='\n')
