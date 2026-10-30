from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys
import zipfile
stage,sources=map(Path,sys.argv[1:])
exe=stage/'ipatool.exe'
# Run outside MSYS2 using PowerShell's process environment; no account or network commands.
script=stage/'verify-engine.ps1'
script.write_text('param([string]$Executable)\n$env:PATH="$env:SystemRoot\\System32;$env:SystemRoot"\n& $Executable account-safety-info\nexit $LASTEXITCODE\n')
try:
    output=subprocess.check_output(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(script.resolve()),str(exe.resolve())],text=True).strip()
finally:
    script.unlink(missing_ok=True)
if output!='IPA-DESKTOP-AUTH-SAFETY/1': raise RuntimeError('Wrong auth safety profile')
imports=subprocess.check_output(['objdump','-p',str(exe)],text=True)
system={'advapi32.dll','bcrypt.dll','crypt32.dll','cryptbase.dll','gdi32.dll','iphlpapi.dll','kernel32.dll','msvcrt.dll','ncrypt.dll','normaliz.dll','ntdll.dll','ole32.dll','oleaut32.dll','secur32.dll','shell32.dll','shlwapi.dll','ucrtbase.dll','user32.dll','version.dll','winmm.dll','ws2_32.dll','wldap32.dll'}
for name in re.findall(r'DLL Name: (\S+)',imports):
    if name.lower() not in system and not name.lower().startswith(('api-ms-win-','ext-ms-win-')):
        raise RuntimeError('Engine depends on non-system DLL: '+name)
pins=json.loads(Path('Tools/AccountSafety/pins.json').read_text())
(stage/'sources.json').write_text(json.dumps({**pins,'profile':output,'sha256':hashlib.sha256(exe.read_bytes()).hexdigest()},indent=2)+'\n')
Path('artifacts').mkdir(exist_ok=True)
version=pins['version']
for folder,name in [(stage,f'IPA-AuthTools-{version}-Windows-x64.zip'),(sources,f'IPA-AuthTools-{version}-Sources.zip')]:
    with zipfile.ZipFile(Path('artifacts')/name,'w',zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(folder.rglob('*')):
            if path.is_file() and "__pycache__" not in path.parts: archive.write(path,path.relative_to(folder).as_posix())
print('PASS: packaged guarded engine starts without MSYS2, has no third-party DLL imports, and contains sources/licenses')
