"""Build the Windows archive and keep the 0.2.12 update contract executable."""
from pathlib import Path
import argparse
import json
import shutil
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ALIASES = {'App.ico': 'Assets/App.ico', 'Update-App.ps1': 'Scripts/Update-App.ps1',
           **{name: 'Generated/' + name for name in ('Core.cs','App.cs','IpaMetadata.cs','WindowTheme.cs','GuiUpdater.cs')},
           **{name: 'Tests/' + name for name in ('Verify.ps1','Verify-IpaMetadata.ps1','Verify-Updater.ps1')}}

def create(publish, destination=None):
    version = ET.parse(ROOT / 'IPA-Desktop.csproj').findtext('.//Version')
    archive = Path(destination or ROOT / f'IPA-Desktop-{version}-Windows.zip')
    manifest = json.loads((ROOT / 'package-files.json').read_text())
    legacy = json.loads((ROOT / 'Tests/Fixtures/legacy-package-files.json').read_text())
    with tempfile.TemporaryDirectory(prefix='ipa-package-') as folder:
        stage = Path(folder)
        for name in manifest:
            source = Path(publish) / name if name == 'IPA-Desktop.exe' else ROOT / name
            target = stage / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
        # These non-runtime entries let old update helpers copy the new apphost.
        # The Avalonia executable loads its compiled resources, never these markers.
        for name in legacy:
            target = stage / name
            if target.exists():
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text('Legacy update compatibility entry. Build from the current repository with .NET 10.\n', encoding='utf-8')
        for name, source in ALIASES.items():
            shutil.copy2(stage / source, stage / name)
        if (stage / 'IPA-Desktop.exe').stat().st_size > 32 * 1024 * 1024:
            raise ValueError('EXE exceeds the 0.2.12 updater per-file limit (32 MiB)')
        temporary = archive.with_suffix('.tmp')
        try:
            with zipfile.ZipFile(temporary,'w',zipfile.ZIP_DEFLATED) as out:
                for file in sorted(stage.rglob('*')):
                    if file.is_file():
                        out.write(file,file.relative_to(stage).as_posix())
            if temporary.stat().st_size > 64 * 1024 * 1024:
                raise ValueError('ZIP exceeds the legacy updater limit (64 MiB)')
            temporary.replace(archive)
        finally:
            temporary.unlink(missing_ok=True)
    return archive

if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('publish',type=Path)
    parser.add_argument('--output',type=Path)
    args=parser.parse_args()
    print(create(args.publish,args.output))
