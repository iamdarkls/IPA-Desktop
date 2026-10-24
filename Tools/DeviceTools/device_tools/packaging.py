"""Assemble isolated native packages without invoking native tools at import time."""
from pathlib import Path
import json
import re
import shutil
import tempfile
import zipfile

from .dependencies import DependencyCollector, sha256_file


TOOLS = ('idevice_id.exe', 'ideviceinfo.exe', 'ideviceinstaller.exe', 'idevicepair.exe')
LICENSE_NAMES = (
    'COPYING', 'COPYING.LESSER', 'COPYING.GPLv3', 'LICENSE',
    'LICENSE.md', 'LICENSE.txt', 'COPYING.LGPLv2.1',
)


def write_archive(folder, destination):
    """Keep the previous archive intact if compression fails midway."""
    with tempfile.NamedTemporaryFile(dir=destination.parent, suffix='.zip', delete=False) as stream:
        temporary = Path(stream.name)
    try:
        with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED) as archive:
            for file in sorted(folder.rglob('*')):
                if file.is_file():
                    archive.write(file, file.relative_to(folder).as_posix())
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)


class PackageBuilder:
    """Inject binary inspection so packaging can be tested outside MSYS2."""

    def __init__(self, root, mingw_bin, system_bin, inspect):
        self.root = Path(root).resolve()
        self.config = self.root / 'Tools/DeviceTools'
        self.build = self.root / 'device-build'
        self.mingw = Path(mingw_bin)
        self.system = Path(system_bin)
        self.inspect = inspect

    def create(self):
        version = (self.config / 'VERSION').read_text(encoding='utf-8').strip()
        if not re.fullmatch(r'\d+\.\d+\.\d+', version):
            raise ValueError('Invalid device tools version')
        collector = DependencyCollector(
            self.build / 'prefix/bin', self.mingw, self.system, self.inspect,
        )
        files = collector.collect(self.build / 'prefix/bin' / name for name in TOOLS)
        # Always assemble from an empty directory: removed DLLs and old manifests
        # must never leak into a subsequent build or its checksum manifest.
        with tempfile.TemporaryDirectory(prefix='package-', dir=self.build) as temporary:
            out = Path(temporary)
            bins = out / 'windows_amd64'
            bins.mkdir()
            for file in files:
                shutil.copy2(file, bins / file.name)
            self._licenses(out / 'LICENSES')
            for source, name in (
                (self.config / 'pins.json', 'sources.json'),
                (self.config / 'README.md', 'README.md'),
                (self.build / 'msys2-packages.txt', 'msys2-packages.txt'),
            ):
                shutil.copy2(source, out / name)
            (out / 'version.txt').write_text(version + '\n', encoding='utf-8')
            manifest = {
                p.relative_to(out).as_posix(): sha256_file(p)
                for p in sorted(out.rglob('*')) if p.is_file()
            }
            (out / 'sha256.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
            # Finish assembly before replacing the last inspectable package.
            target = self.build / 'package'
            if target.exists():
                shutil.rmtree(target)
            shutil.move(str(out), target)
        windows_zip = self.root / f'IPA-DeviceTools-{version}-Windows-x64.zip'
        sources_zip = self.root / f'IPA-DeviceTools-{version}-Sources.zip'
        write_archive(target, windows_zip)
        write_archive(self.build / 'source-archives', sources_zip)
        return len(files)

    def _licenses(self, licenses):
        licenses.mkdir()
        for project in sorted((self.build / 'source').iterdir()):
            if not project.is_dir():
                continue
            target = licenses / project.name
            target.mkdir()
            for name in LICENSE_NAMES:
                if (project / name).is_file():
                    shutil.copy2(project / name, target / name)
            if not any(target.iterdir()):
                raise RuntimeError(f'Missing license for {project.name}')
        shutil.copytree(self.mingw.parent / 'share/licenses', licenses / 'MSYS2')
