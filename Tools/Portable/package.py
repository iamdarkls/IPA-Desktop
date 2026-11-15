"""Create self-contained Linux and macOS packages, including the reviewed native tools."""
from pathlib import Path
import plistlib,shutil,struct,subprocess,sys,zipfile,xml.etree.ElementTree as ET

def write_macos_icon(source, destination):
    """Reuse the PNG frames in our Windows icon in the macOS ICNS container."""
    data = source.read_bytes()
    reserved, kind, count = struct.unpack_from('<HHH', data)
    if (reserved, kind) != (0, 1):
        raise ValueError(f'Expected an ICO file: {source}')

    png_frames = {}
    for index in range(count):
        width, height, _, _, _, _, size, offset = struct.unpack_from('<BBBBHHII', data, 6 + 16 * index)
        frame = data[offset:offset + size]
        if width == height and len(frame) == size and frame.startswith(b'\x89PNG\r\n\x1a\n'):
            png_frames[width or 256] = frame

    icon_types = {16: b'icp4', 32: b'icp5', 64: b'icp6', 128: b'ic07', 256: b'ic08'}
    sections = []
    for size, icon_type in icon_types.items():
        if size not in png_frames:
            raise ValueError(f'{source} is missing a {size}x{size} PNG frame')
        frame = png_frames[size]
        sections.append(struct.pack('>4sI', icon_type, 8 + len(frame)) + frame)
    icon = b''.join(sections)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(struct.pack('>4sI', b'icns', 8 + len(icon)) + icon)

rid=sys.argv[1];publish=Path(sys.argv[2]);version=ET.parse('IPA-Desktop.csproj').findtext('.//Version')
mac=rid.startswith('osx-');name='IPA-Desktop.app' if mac else 'IPA-Desktop';root=Path('artifacts/portable')/name
if root.exists():shutil.rmtree(root)
base=root/'Contents/MacOS' if mac else root
shutil.copytree(publish,base);shutil.copytree('artifacts/unix/native',base/'native')
(base/'Scripts').mkdir();shutil.copy2('Scripts/Update-App.sh',base/'Scripts/Update-App.sh')
shutil.copytree('Docs',base/'Docs')
for file in ['README.md','LICENSE.txt','DEPENDENCIES.md']:shutil.copy2(file,base/file)
if mac:
 icon_name = 'App.icns'
 write_macos_icon(Path('Assets/App.ico'), root/'Contents/Resources'/icon_name)
 info={'CFBundleExecutable':'IPA-Desktop','CFBundleIdentifier':'ru.darkls.ipa-desktop','CFBundleName':'IPA Desktop','CFBundleDisplayName':'IPA Desktop','CFBundleVersion':version,'CFBundleShortVersionString':version,'CFBundlePackageType':'APPL','CFBundleIconFile':icon_name,'LSMinimumSystemVersion':'15.0','NSHighResolutionCapable':True}
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
