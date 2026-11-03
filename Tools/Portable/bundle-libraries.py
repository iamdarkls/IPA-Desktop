"""Resolve Linux shared dependencies recursively; keep glibc as the host ABI."""
from pathlib import Path
import re,subprocess,sys,shutil
stage=Path(sys.argv[1]).resolve();pending=list(stage.iterdir());visited=set()
base=('libc.so','libm.so','libpthread.so','librt.so','libdl.so','ld-linux','linux-vdso','libresolv.so')
while pending:
 file=pending.pop()
 if file.name in visited:continue
 visited.add(file.name)
 output=subprocess.run(['ldd',str(file)],text=True,capture_output=True).stdout
 if 'not found' in output:raise RuntimeError(output)
 for path in re.findall(r'=> (/[^\s]+)',output):
  source=Path(path)
  if source.name.startswith(base):continue
  target=stage/source.name
  if not target.exists():shutil.copy2(source.resolve(),target);pending.append(target)
 subprocess.run(['patchelf','--set-rpath','$ORIGIN',str(file)],check=True)
