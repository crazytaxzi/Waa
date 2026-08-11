from pathlib import Path

p = Path('tests/Run-Tests.ps1')
s = p.read_text(encoding='utf-8').replace('\r\n','\n')
old = "Assert ($appSource.Contains('data-delete-timer')-and$appSource.Contains('data-snooze-reminder')-and$appSource.Contains(\"'ontime_status'\")) 'timers snoozing and on-time fields are connected to the shared card';"
new = "Assert ($appSource.Contains('data-delete-timer')-and$appSource.Contains('data-snooze-reminder')-and$appSource.Contains(\"optionalDetail('Add ETA detail\")-and-not$appSource.Contains(\"selectField('On-time status'\")-and-not$appSource.Contains(\"checkField('Home time checked'\")-and-not$appSource.Contains(\"checkField('Routing checked'\")) 'timers stay connected while redundant call inputs are removed or made optional';"
if s.count(old) != 1:
    raise SystemExit(f'expected old shared-card assertion once, found {s.count(old)}')
p.write_text(s.replace(old,new,1), encoding='utf-8', newline='\n')
print('Updated regression expectation for simplified call inputs.')
