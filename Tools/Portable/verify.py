"""Run the packaged GUI and native commands on its actual host OS without Apple accounts."""
from pathlib import Path
import json,subprocess,tempfile,sys,os,zipfile,stat
archive=Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix='ipa-platform-') as folder:
 root=Path(folder);stage=root/'package';stage.mkdir()
 with zipfile.ZipFile(archive) as z:
  z.extractall(stage)
  for entry in z.infolist():
   p=stage/entry.filename
   if p.is_file():p.chmod((entry.external_attr>>16)&0o777 or 0o644)
 package=stage/('IPA-Desktop.app' if sys.platform=='darwin' else 'IPA-Desktop');base=package/'Contents/MacOS' if sys.platform=='darwin' else package
 native=base/'native'
 for tool in ['idevice_id','ideviceinfo','ideviceinstaller','idevicepair','ipa-device-events']:
  subprocess.run([str(native/tool),'--version'],check=True,timeout=15)
 assert '--placeholders' in subprocess.check_output([str(native/'ideviceinstaller'),'--help'],text=True)
 assert subprocess.check_output([str(native/'ipatool'),'account-safety-info'],text=True).strip()=='IPA-DESKTOP-AUTH-SAFETY/1'
 manifest=json.loads((native/'device-tools.json').read_text());assert manifest['schema']==1 and manifest['eventProtocol']==1 and 'libplist' in manifest['components']
 data=root/'data';data.mkdir()
 (data/'state.json').write_text(json.dumps({'Current':'legacy','DeviceCurrent':'devices','AutoUpdate':False,'SoundsEnabled':False,'CheckGuiUpdates':False,'AutoUpdateDeviceTools':False}))
 (data/'AppSets.json').write_text('[{"Name":"Legacy set","Apps":[{"ID":"123","Name":"Fixture"}]}]')
 env=dict(os.environ,DOTNET_ROOT=str(root/'absent-runtime'),AVALONIA_TELEMETRY_OPTOUT='1')
 result=subprocess.run([str(base/'IPA-Desktop'),'--smoke-test',str(data)],cwd=base,env=env,timeout=60)
 if (data/'smoke-error.txt').exists():print((data/'smoke-error.txt').read_text())
 assert result.returncode==0
 assert (data/'smoke-ok.txt').is_file()
 print((data/'smoke-ok.txt').read_text())
 print('PASS: native binaries, protected engine, placeholders, self-contained GUI and migration on '+sys.platform)
