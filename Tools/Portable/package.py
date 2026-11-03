"""Create self-contained Linux and macOS packages, including the reviewed native tools."""
from pathlib import Path
import json,plistlib,shutil,subprocess,sys,zipfile,xml.etree.ElementTree as ET
rid=sys.argv[1];publish=Path(sys.argv[2]);version=ET.parse('IPA-Desktop.csproj').findtext('.//Version')
mac=rid.startswith('osx-');name='IPA-Desktop.app' if mac else 'IPA-Desktop';root=Path('artifacts/portable')/name
if root.exists():shutil.rmtree(root)
base=root/'Contents/MacOS' if mac else root
shutil.copytree(publish,base);shutil.copytree('artifacts/unix/native',base/'native')
(base/'Scripts').mkdir();shutil.copy2('Scripts/Update-App.sh',base/'Scripts/Update-App.sh')
shutil.copytree('Docs',base/'Docs')
for file in ['README.md','LICENSE.txt','DEPENDENCIES.md']:shutil.copy2(file,base/file)
if mac:
 info={'CFBundleExecutable':'IPA-Desktop','CFBundleIdentifier':'ru.darkls.ipa-desktop','CFBundleName':'IPA Desktop','CFBundleDisplayName':'IPA Desktop','CFBundleVersion':version,'CFBundleShortVersionString':version,'CFBundlePackageType':'APPL','LSMinimumSystemVersion':'15.0','NSHighResolutionCapable':True}
 (root/'Contents/Info.plist').write_bytes(plistlib.dumps(info))
 subprocess.run(['codesign','--force','--deep','--sign','-',str(root)],check=True)
 subprocess.run(['codesign','--verify','--deep','--strict',str(root)],check=True)
suffix=('macOS' if mac else 'Linux')+'-'+rid.split('-')[1]
destination=Path('artifacts/release');destination.mkdir(parents=True,exist_ok=True)
shutil.copy2('artifacts/unix/native-sources.zip',destination/f'IPA-Desktop-{version}-{rid}-native-sources.zip')
archive=destination/f'IPA-Desktop-{version}-{suffix}.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
 for p in sorted(root.rglob('*')):
  if p.is_file():z.write(p,Path(name)/p.relative_to(root))
print(archive)
