from pathlib import Path
import json
import tempfile
import unittest
import zipfile
from unittest.mock import patch

from device_tools.dependencies import sha256_file
from device_tools.packaging import PackageBuilder, TOOLS, write_archive


class PackagingChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.build = self.root / 'device-build'
        self.config = self.root / 'Tools/DeviceTools'
        self.prefix = self.build / 'prefix/bin'
        self.mingw = self.root / 'mingw/bin'
        self.system = self.root / 'system'
        for folder in (self.config, self.prefix, self.mingw, self.system,
                       self.build / 'source/test', self.build / 'source-archives',
                       self.mingw.parent / 'share/licenses/pkg'):
            folder.mkdir(parents=True)
        for name in TOOLS:
            (self.prefix / name).write_bytes(b'native fixture')
        (self.config / 'VERSION').write_text('1.0.2\n')
        (self.config / 'pins.json').write_text('{}')
        (self.config / 'README.md').write_text('Device tools')
        (self.build / 'msys2-packages.txt').write_text('fixture 1.0')
        (self.build / 'source/test/LICENSE').write_text('license')
        (self.build / 'source-archives/test.tar').write_bytes(b'source archive')
        (self.mingw.parent / 'share/licenses/pkg/LICENSE').write_text('dependency license')
        self.builder = PackageBuilder(self.root, self.mingw, self.system, lambda p: [])

    def test_rebuild_excludes_stale_files_and_hashes_every_payload(self):
        self.assertEqual(self.builder.create(), 3)
        out = self.build / 'package'
        (out / 'windows_amd64/obsolete.dll').write_bytes(b'stale')
        self.builder.create()
        self.assertFalse((out / 'windows_amd64/obsolete.dll').exists())
        manifest = json.loads((out / 'sha256.json').read_text())
        expected = {p.relative_to(out).as_posix() for p in out.rglob('*') if p.is_file()} - {'sha256.json'}
        self.assertEqual(set(manifest), expected)
        for name, digest in manifest.items():
            self.assertEqual(sha256_file(out / name), digest)
        with zipfile.ZipFile(self.root / 'IPA-DeviceTools-1.0.2-Windows-x64.zip') as archive:
            self.assertEqual(set(archive.namelist()), expected | {'sha256.json'})
        with zipfile.ZipFile(self.root / 'IPA-DeviceTools-1.0.2-Sources.zip') as archive:
            self.assertEqual(archive.read('test.tar'), b'source archive')

    def test_missing_license_preserves_last_assembled_package(self):
        self.builder.create()
        out = self.build / 'package'
        old = (out / 'sha256.json').read_bytes()
        (self.build / 'source/test/LICENSE').unlink()
        with self.assertRaisesRegex(RuntimeError, 'Missing license'):
            self.builder.create()
        self.assertEqual((out / 'sha256.json').read_bytes(), old)
        self.assertFalse(list(self.build.glob('package-*')))

    def test_archive_failure_preserves_previous_archive_and_cleans_temp(self):
        destination = self.root / 'previous.zip'
        destination.write_bytes(b'previous')
        with patch.object(zipfile.ZipFile, 'write', side_effect=OSError('disk full')):
            with self.assertRaises(OSError):
                write_archive(self.prefix, destination)
        self.assertEqual(destination.read_bytes(), b'previous')
        self.assertEqual(list(self.root.glob('*.zip')), [destination])


if __name__ == '__main__':
    unittest.main()
