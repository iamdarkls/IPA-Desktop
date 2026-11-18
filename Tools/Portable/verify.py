"""Run the packaged GUI and native commands on its actual host OS without Apple accounts."""

import argparse
import json
import os
from pathlib import Path
import plistlib
import subprocess
import sys
import tempfile
import zipfile


def extract_package(archive, stage, macos):
    stage.mkdir()
    if macos:
        subprocess.run(['/usr/bin/ditto', '-x', '-k', archive, stage], check=True, timeout=60)
        return stage / 'IPA-Desktop.app'

    with zipfile.ZipFile(archive) as zipped:
        zipped.extractall(stage)
        for entry in zipped.infolist():
            path = stage / entry.filename
            if path.is_file():
                path.chmod((entry.external_attr >> 16) & 0o777 or 0o644)
    return stage / 'IPA-Desktop'


def verify_macos_bundle(package):
    contents = package / 'Contents'
    with (contents / 'Info.plist').open('rb') as info:
        icon_name = plistlib.load(info)['CFBundleIconFile']
    icon = contents / 'Resources' / icon_name
    subprocess.run(['/usr/bin/sips', '-g', 'pixelWidth', '-g', 'pixelHeight', icon],
                   check=True, timeout=15)
    subprocess.run(['/usr/bin/codesign', '--verify', '--deep', '--strict', package],
                   check=True, timeout=15)


def verify_native_tools(native):
    for tool in ('idevice_id', 'ideviceinfo', 'ideviceinstaller', 'idevicepair', 'ipa-device-events'):
        subprocess.run([native / tool, '--version'], check=True, timeout=15)

    installer_help = subprocess.check_output(
        [native / 'ideviceinstaller', '--help'], text=True, timeout=15)
    if '--placeholders' not in installer_help:
        raise RuntimeError('Packaged ideviceinstaller lacks placeholder support.')

    safety_marker = subprocess.check_output(
        [native / 'ipatool', 'account-safety-info'], text=True, timeout=15).strip()
    if safety_marker != 'IPA-DESKTOP-AUTH-SAFETY/1':
        raise RuntimeError('Packaged ipatool lacks the required account safety profile.')

    engine_help = subprocess.check_output([native / 'ipatool', 'help'], text=True, timeout=15)
    if '--ipa-desktop-cache-gib' not in engine_help:
        raise RuntimeError('Packaged ipatool lacks the IPA cache option.')

    manifest = json.loads((native / 'device-tools.json').read_text())
    if manifest['schema'] != 1 or manifest['eventProtocol'] != 1 or 'libplist' not in manifest['components']:
        raise RuntimeError('Packaged device-tools manifest is incompatible.')


def verify_gui(base, root):
    data = root / 'data'
    data.mkdir()
    (data / 'state.json').write_text(json.dumps({
        'Current': 'legacy',
        'DeviceCurrent': 'devices',
        'AutoUpdate': False,
        'SoundsEnabled': False,
        'CheckGuiUpdates': False,
        'AutoUpdateDeviceTools': False,
    }))
    (data / 'AppSets.json').write_text('[{"Name":"Legacy set","Apps":[{"ID":"123","Name":"Fixture"}]}]')
    env = dict(os.environ, DOTNET_ROOT=str(root / 'absent-runtime'), AVALONIA_TELEMETRY_OPTOUT='1')
    result = subprocess.run([base / 'IPA-Desktop', '--smoke-test', data], cwd=base, env=env, timeout=60)
    error = data / 'smoke-error.txt'
    if error.is_file():
        print(error.read_text())
    result.check_returncode()
    marker = data / 'smoke-ok.txt'
    if not marker.is_file():
        raise RuntimeError('The packaged GUI exited without completing the smoke test.')
    print(marker.read_text())


def verify(archive):
    macos = sys.platform == 'darwin'
    with tempfile.TemporaryDirectory(prefix='ipa-platform-') as folder:
        root = Path(folder)
        package = extract_package(archive, root / 'package', macos)
        if macos:
            verify_macos_bundle(package)
        base = package / 'Contents' / 'MacOS' if macos else package
        verify_native_tools(base / 'native')
        verify_gui(base, root)
    print(f'PASS: native binaries, protected engine, placeholders, self-contained GUI and migration on {sys.platform}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    args = parser.parse_args()
    verify(args.archive.resolve())
