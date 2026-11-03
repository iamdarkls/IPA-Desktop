"""One local/CI entry point: metadata, test, package, publish. Run from the repository root."""
from pathlib import Path
import argparse,json,os,subprocess,sys,xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parent.parent
PLATFORMS=json.loads((ROOT/'Tools/Portable/platforms.json').read_text())

def run(*args,**kwargs):return subprocess.run([str(x) for x in args],check=True,**kwargs)
def version():return ET.parse(ROOT/'IPA-Desktop.csproj').findtext('.//Version')
def output(**values):
 for key,value in values.items():
  value=json.dumps(value,separators=(',',':')) if not isinstance(value,str) else value
  print(f'{key}={value}')
  if os.getenv('GITHUB_OUTPUT'):
   with open(os.environ['GITHUB_OUTPUT'],'a') as f:f.write(f'{key}={value}\n')
def published(tag):
 r=subprocess.run(['gh','release','view',tag,'--json','tagName'],text=True,capture_output=True)
 if r.returncode==0:return True
 if not any(x in r.stderr.lower() for x in ('not found','404')):raise RuntimeError(r.stderr)
 return False

def metadata():
 current=version()
 if not (ROOT/f'changelogs/v{current}.md').is_file() or f'Version = "{current}"' not in (ROOT/'Source/Domain/Updates/AppInfo.cs').read_text():raise RuntimeError('Version/changelog mismatch')
 released=published('v'+current)
 output(version=current,package=not released or os.getenv('GITHUB_EVENT_NAME')=='workflow_dispatch',publish=not released,
        matrix={'include':[dict(rid=rid,**platform) for rid,platform in PLATFORMS.items()]})

def tests(rid):
 run(sys.executable,'-m','unittest','discover','-s','Tests/Build','-v')
 run('dotnet','build','Tests/Modern/IPA-Desktop.Tests.csproj','-c','Release','-m:1')
 if rid!='win-x64':
  destination=ROOT/'Tests/Modern/bin/Release/net10.0/native';destination.mkdir(parents=True,exist_ok=True)
  run('cc','-O2','-Wall','-Wextra','Tools/Portable/ipa-pty.c','-o',destination/'ipa-pty',*(['-lutil'] if rid.startswith('linux') else []))
 reports=ROOT/'artifacts/tests'/rid;reports.mkdir(parents=True,exist_ok=True)
 # Same xUnit suite, direct entry point avoids a second MSBuild/VSTest startup.
 run('dotnet','Tests/Modern/bin/Release/net10.0/IPA-Desktop.Tests.dll','-xml',reports/'tests.xml')

def package(rid):
 import shutil
 shutil.rmtree(ROOT/'artifacts/publish',ignore_errors=True)
 run('dotnet','publish','IPA-Desktop.csproj','-c','Release','-r',rid,'--self-contained','true','-p:PublishSingleFile=true','-o','artifacts/publish')
 if rid=='win-x64':
  run('python','Scripts/package.py','artifacts/publish')
  archive=f'IPA-Desktop-{version()}-Windows.zip'
  run('powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','Scripts/Verify-Package.ps1','-Archive',archive)
  destination=ROOT/'artifacts/release';destination.mkdir(parents=True,exist_ok=True);shutil.move(archive,destination/archive)
 else:
  run(sys.executable,'Tools/Portable/package.py',rid,'artifacts/publish')
  archive=f'artifacts/release/IPA-Desktop-{version()}-{PLATFORMS[rid]["suffix"]}.zip'
  command=[sys.executable,'Tools/Portable/verify.py',archive]
  if rid.startswith('linux'):command=['xvfb-run','-a',*command]
  run(*command)

def publish():
 current=version();tag='v'+current
 if published(tag):print('Already published');return
 head=subprocess.check_output(['gh','api',f'repos/{os.environ["GITHUB_REPOSITORY"]}/git/ref/heads/main','--jq','.object.sha'],text=True).strip()
 if head!=os.environ['GITHUB_SHA']:print('Main changed; this tested build is not published.');return
 folder=ROOT/'artifacts/release'
 names={f'IPA-Desktop-{current}-{p["suffix"]}.zip' for p in PLATFORMS.values()}
 names.update(f'IPA-Desktop-{current}-{rid}-native-sources.zip' for rid in PLATFORMS if rid!='win-x64')
 actual={p.name for p in folder.glob('*.zip')}
 if actual!=names:raise RuntimeError(f'Unexpected release set: missing={names-actual}, extra={actual-names}')
 run('gh','release','create',tag,*[folder/name for name in sorted(names)],'--target',os.environ['GITHUB_SHA'],'--title',f'IPA Desktop {current} — Windows, macOS, Linux','--notes-file',f'changelogs/{tag}.md','--latest')

if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('command',choices=['metadata','test','package','publish']);parser.add_argument('--rid',choices=PLATFORMS);args=parser.parse_args();os.chdir(ROOT)
 if args.command in ('test','package') and not args.rid:parser.error('--rid is required')
 {'metadata':metadata,'test':lambda:tests(args.rid),'package':lambda:package(args.rid),'publish':publish}[args.command]()
