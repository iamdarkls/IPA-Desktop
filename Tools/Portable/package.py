"""Create self-contained Linux and macOS packages, including the reviewed native tools."""
import argparse
from pathlib import Path
import plistlib
import shutil
import struct
import subprocess
import xml.etree.ElementTree as ET
import zipfile

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


def prepare_bundle(bundle, version):
    contents = bundle / 'Contents'
    icon_name = 'App.icns'
    write_macos_icon(Path('Assets/App.ico'), contents / 'Resources' / icon_name)
    info = {
        'CFBundleExecutable': 'IPA-Desktop',
        'CFBundleIdentifier': 'ru.darkls.ipa-desktop',
        'CFBundleName': 'IPA Desktop',
        'CFBundleDisplayName': 'IPA Desktop',
        'CFBundleVersion': version,
        'CFBundleShortVersionString': version,
        'CFBundlePackageType': 'APPL',
        'CFBundleIconFile': icon_name,
        'LSMinimumSystemVersion': '15.0',
        'NSHighResolutionCapable': True,
    }
    (contents / 'Info.plist').write_bytes(plistlib.dumps(info))
    subprocess.run(['codesign', '--force', '--deep', '--sign', '-', bundle], check=True)
    subprocess.run(['codesign', '--verify', '--deep', '--strict', bundle], check=True)


def stage_application(publish, root, macos):
    if root.exists():
        shutil.rmtree(root)
    base = root / 'Contents' / 'MacOS' if macos else root
    shutil.copytree(publish, base)
    shutil.copytree('artifacts/unix/native', base / 'native')
    (base / 'Scripts').mkdir()
    shutil.copy2('Scripts/Update-App.sh', base / 'Scripts' / 'Update-App.sh')
    shutil.copytree('Docs', base / 'Docs')
    for filename in ('README.md', 'LICENSE.txt', 'DEPENDENCIES.md'):
        shutil.copy2(filename, base / filename)


def write_archive(root, archive, macos):
    if macos:
        # Keep bundle metadata readable by Archive Utility and the in-app updater.
        subprocess.run(
            ['/usr/bin/ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', root, archive],
            check=True,
        )
    else:
        with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as zipped:
            for path in sorted(root.rglob('*')):
                if path.is_file():
                    zipped.write(path, Path(root.name) / path.relative_to(root))


def package(rid, publish):
    version = ET.parse('IPA-Desktop.csproj').findtext('.//Version')
    macos = rid.startswith('osx-')
    root = Path('artifacts/portable') / ('IPA-Desktop.app' if macos else 'IPA-Desktop')
    stage_application(publish, root, macos)
    if macos:
        prepare_bundle(root, version)

    platform, architecture = rid.split('-')
    suffix = f"{'macOS' if platform == 'osx' else 'Linux'}-{architecture}"
    destination = Path('artifacts/release')
    destination.mkdir(parents=True, exist_ok=True)
    shutil.copy2(
        'artifacts/unix/native-sources.zip',
        destination / f'IPA-Desktop-{version}-{rid}-native-sources.zip',
    )
    archive = destination / f'IPA-Desktop-{version}-{suffix}.zip'
    write_archive(root, archive, macos)
    return archive


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('rid', choices=('linux-x64', 'osx-arm64', 'osx-x64'))
    parser.add_argument('publish', type=Path)
    args = parser.parse_args()
    print(package(args.rid, args.publish))
