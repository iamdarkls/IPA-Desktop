import hashlib
from pathlib import Path
import tempfile
import unittest
from device_tools.dependencies import DependencyCollector, sha256_file


class PackageChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.prefix, self.mingw, self.system = [self.root / n for n in ('prefix', 'mingw', 'system')]
        for folder in (self.prefix, self.mingw, self.system):
            folder.mkdir()
        self.calls = []
        self.imports = {}

    def tearDown(self):
        self.temp.cleanup()

    def add(self, folder, name):
        path = folder / name
        path.write_bytes(name.encode())
        return path

    def inspect(self, path):
        self.calls.append(path)
        return self.imports.get(path.name, [])

    def collector(self):
        return DependencyCollector(self.prefix, self.mingw, self.system, self.inspect)

    def test_transitive_case_insensitive_cycles_and_precedence(self):
        app = self.add(self.prefix, 'tool.exe')
        dll = self.add(self.prefix, 'A.dll')
        self.add(self.mingw, 'a.dll')
        other = self.add(self.mingw, 'b.dll')
        self.add(self.system, 'KERNEL32.dll')
        self.imports = {'tool.exe': ['a.DLL', 'KERNEL32.DLL', 'api-ms-win-core-test.dll'], 'A.dll': ['b.dll'], 'b.dll': ['A.DLL']}
        collector = self.collector()
        # No dependency resolution re-enumerates these directories.
        from unittest.mock import patch
        with patch.object(Path, 'iterdir', side_effect=AssertionError('repeated enumeration')):
            self.assertEqual(collector.collect([app, app]), [app, dll, other])
        self.assertEqual(len(self.calls), 3)

    def test_unresolved_dependency_fails(self):
        app = self.add(self.prefix, 'tool.exe')
        self.imports = {'tool.exe': ['missing.dll']}
        with self.assertRaisesRegex(RuntimeError, 'Unresolved dependency'):
            self.collector().collect([app])

    def test_missing_root_and_msys_runtime_fail(self):
        with self.assertRaisesRegex(RuntimeError, 'Missing binary'):
            self.collector().collect([self.prefix / 'missing.exe'])
        app = self.add(self.prefix, 'tool.exe')
        self.add(self.mingw, 'msys-2.0.dll')
        self.imports = {'tool.exe': ['msys-2.0.dll']}
        with self.assertRaisesRegex(RuntimeError, 'MSYS runtime'):
            self.collector().collect([app])

    def test_streaming_hash(self):
        path = self.root / 'large.dll'
        data = b'01234567' * 400000
        path.write_bytes(data)
        self.assertEqual(sha256_file(path), hashlib.sha256(data).hexdigest())


if __name__ == '__main__':
    unittest.main()
