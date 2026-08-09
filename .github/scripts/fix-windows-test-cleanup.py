from pathlib import Path

run_tests = Path('tests/Run-Tests.ps1')
text = run_tests.read_text(encoding='utf-8')
old = "}finally{if(Test-Path $data){Remove-Item $data -Recurse -Force};Remove-Item Env:WAA_SQLITE_TEST -ErrorAction SilentlyContinue}"
new = "}finally{Remove-Module Waa -Force -ErrorAction SilentlyContinue;if(Test-Path $data){Remove-Item $data -Recurse -Force};Remove-Item Env:WAA_SQLITE_TEST -ErrorAction SilentlyContinue}"
if old not in text:
    raise SystemExit('Run-Tests cleanup marker not found')
run_tests.write_text(text.replace(old, new, 1), encoding='utf-8', newline='\n')

identity = Path('tests/Identity.Tests.ps1')
text = identity.read_text(encoding='utf-8')
old = "finally {\n    if (Test-Path $data) { Remove-Item $data -Recurse -Force }\n    Remove-Item Env:WAA_SQLITE_TEST -ErrorAction SilentlyContinue\n}"
new = "finally {\n    Remove-Module Waa -Force -ErrorAction SilentlyContinue\n    if (Test-Path $data) { Remove-Item $data -Recurse -Force }\n    Remove-Item Env:WAA_SQLITE_TEST -ErrorAction SilentlyContinue\n}"
if old not in text:
    raise SystemExit('Identity.Tests cleanup marker not found')
identity.write_text(text.replace(old, new, 1), encoding='utf-8', newline='\n')
