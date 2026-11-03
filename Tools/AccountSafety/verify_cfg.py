"""Fail closed if the reviewed Unicorn fix is absent; run its real JIT with Windows CFG."""
from pathlib import Path
import hashlib
import subprocess
import sys
import tempfile

build, stage = map(Path, sys.argv[1:])
header = build/'_deps/unicorn_src-src/qemu/include/sysemu/os-win32.h'
content = header.read_text()
for expected in ['#define setjmp(env) __builtin_setjmp(env)', '#define longjmp(env, val) __builtin_longjmp((env), 1)']:
    if expected not in content:
        raise RuntimeError('Unicorn CFG patch was not applied: '+expected)
fixture = build/'ipa_cfg_smoke.exe'
with tempfile.TemporaryDirectory(prefix='ipa-cfg-') as directory:
    script = Path(directory)/'verify.ps1'
    script.write_text('param([string]$Fixture)\n$env:PATH="$env:SystemRoot\\System32;$env:SystemRoot"\n& $Fixture\nexit $LASTEXITCODE\n')
    output = subprocess.check_output(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(script.resolve()),str(fixture.resolve())],text=True,timeout=45).strip()
if not output.startswith('PASS: CFG enabled; Unicorn '):
    raise RuntimeError('Missing CFG runtime verification: '+output)
report=output+'\nPatched os-win32.h SHA256: '+hashlib.sha256(header.read_bytes()).hexdigest()+'\n'
(stage/'cfg-verification.txt').write_text(report)
print(report)
