"""Content-addressed native bundle cache. Restore verified release binaries only for identical inputs/image."""
from pathlib import Path
import argparse,hashlib,json,os,shutil,subprocess,sys,tempfile,zipfile
ROOT=Path(__file__).resolve().parents[2];BASE=ROOT/'artifacts/unix';SCHEMA=1
INPUTS=json.loads((ROOT/'Tools/Portable/native-inputs.json').read_text())
COMMANDS=['ipatool','ipa-pty','idevice_id','ideviceinfo','ideviceinstaller','idevicepair']

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def inputs():
 h=hashlib.sha256()
 for name in INPUTS:h.update(name.encode()+b'\0'+(ROOT/name).read_bytes()+b'\0')
 return h.hexdigest()
def image():
 value=os.getenv('ImageVersion')
 if not value and os.getenv('GITHUB_ACTIONS')=='true':raise RuntimeError('Runner ImageVersion is required for the native cache')
 return value or 'local' 
def files():return sorted([*(BASE/'native').glob('*'),BASE/'native-sources.zip'],key=lambda p:p.as_posix())
def record(rid):
 return {'schema':SCHEMA,'rid':rid,'image':image(),'inputs':inputs(),'files':{p.relative_to(BASE).as_posix():digest(p) for p in files() if p.is_file()}}
def complete():
 if not (BASE/'native-sources.zip').is_file() or any(not (BASE/'native'/name).is_file() for name in COMMANDS):raise RuntimeError('Incomplete native build')
def write_record(rid):
 complete()
 (BASE/'cache.json').write_text(json.dumps(record(rid),indent=2)+'\n')
def validate(rid):
 complete()
 previous=json.loads((BASE/'cache.json').read_text())
 if previous!=record(rid):raise RuntimeError('Native cache input/image/checksum mismatch')
 for name in COMMANDS:(BASE/'native'/name).chmod(0o755)

def seed(rid):
 baseline=json.loads((ROOT/'Tools/Portable/native-seed.json').read_text())
 platform=baseline['platforms'].get(rid)
 if not platform or baseline['inputs']!=inputs() or platform['image']!=image():return False
 print('Reusing the matching, verified 1.0.0 native release and its corresponding sources.')
 with tempfile.TemporaryDirectory(prefix='ipa-native-seed-') as folder:
  folder=Path(folder)
  for name,expected in platform['assets'].items():
   subprocess.run(['gh','release','download',baseline['tag'],'--repo','iamdarkls/IPA-Desktop','--pattern',name,'--dir',str(folder)],check=True)
   if digest(folder/name)!=expected:raise RuntimeError('Verified release seed SHA-256 mismatch')
  native=BASE/'native';native.mkdir(parents=True,exist_ok=True)
  archive=next(folder.glob('*-'+platform['suffix']+'.zip'))
  prefix='IPA-Desktop.app/Contents/MacOS/native/' if rid.startswith('osx') else 'IPA-Desktop/native/'
  with zipfile.ZipFile(archive) as z:
   for entry in z.infolist():
    if entry.filename.startswith(prefix):
     name=entry.filename[len(prefix):]
     if not name:continue
     if '/' in name or '\\' in name or name in ('.','..'):raise RuntimeError('Unexpected native seed path')
     path=native/name;path.write_bytes(z.read(entry));path.chmod((entry.external_attr>>16)&0o777 or 0o644)
  shutil.copy2(next(folder.glob('*-native-sources.zip')),BASE/'native-sources.zip')
 write_record(rid);return True

def prepare(rid):
 ready=False
 try:
  if (BASE/'cache.json').exists():validate(rid);ready=True
 except (ValueError,RuntimeError,OSError) as error:print(f'Ignoring invalid native cache: {error}')
 if not ready:
  shutil.rmtree(BASE/'native',ignore_errors=True)
  for name in ['cache.json','native-sources.zip']:(BASE/name).unlink(missing_ok=True)
  try:ready=seed(rid)
  except (RuntimeError,OSError,KeyError,zipfile.BadZipFile,subprocess.CalledProcessError) as error:
   print('Verified release seed unavailable; compiling pinned sources:',type(error).__name__)
   shutil.rmtree(BASE/'native',ignore_errors=True)
   for name in ['cache.json','native-sources.zip']:(BASE/name).unlink(missing_ok=True)
 print('Native cache ready:',ready)
 if os.getenv('GITHUB_OUTPUT'):
  with open(os.environ['GITHUB_OUTPUT'],'a') as f:f.write(f'ready={str(ready).lower()}\n')

def check(rid):
 validate(rid)
 for name in COMMANDS[2:]:subprocess.run([str(BASE/'native'/name),'--version'],check=True,timeout=15)
 help=subprocess.check_output([str(BASE/'native/ideviceinstaller'),'--help'],text=True,timeout=15)
 if '--placeholders' not in help:raise RuntimeError('Missing placeholder support')
 profile=subprocess.check_output([str(BASE/'native/ipatool'),'account-safety-info'],text=True,timeout=15).strip()
 if profile!='IPA-DESKTOP-AUTH-SAFETY/1':raise RuntimeError('Wrong account safety profile')
 # Re-run actual patched C++ auth control flow against the fake transport on cache hits too.
 with tempfile.TemporaryDirectory(prefix='ipa-cached-auth-') as folder:
  folder=Path(folder)
  with zipfile.ZipFile(BASE/'native-sources.zip') as z:
   for name in ['main.cpp','appstore.cpp','http_client.cpp','account_safety.h']:(folder/name).write_bytes(z.read('ipatool/'+name))
  subprocess.run([sys.executable,'Tools/AccountSafety/test_auth.py',str(folder)],check=True)
 print('PASS: native cache identity, checksums, CLI, placeholders and authentication safeguards')

if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('command',choices=['key','prepare','record','check']);parser.add_argument('--rid',required=True);args=parser.parse_args();os.chdir(ROOT)
 if args.command=='key':
  key=f'native-v{SCHEMA}-{args.rid}-{image()}-{inputs()}';print(key)
  if os.getenv('GITHUB_OUTPUT'):
   with open(os.environ['GITHUB_OUTPUT'],'a') as f:f.write('key='+key+'\n')
 else:{'prepare':prepare,'record':write_record,'check':check}[args.command](args.rid)
