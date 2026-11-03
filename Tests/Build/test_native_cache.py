"""Only cache invariants: foreign platforms, tampered files, and changed compilation inputs."""
from pathlib import Path
import importlib.util,json,tempfile,unittest
from unittest.mock import patch
REPO=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('native_cache',REPO/'Tools/Portable/native-cache.py');cache=importlib.util.module_from_spec(spec);spec.loader.exec_module(cache)

class NativeCacheTests(unittest.TestCase):
 def setUp(self):
  self.folder=tempfile.TemporaryDirectory();self.root=Path(self.folder.name);self.base=self.root/'artifacts/unix';(self.base/'native').mkdir(parents=True)
  (self.root/'input.txt').write_text('reviewed source')
  for name in cache.COMMANDS:(self.base/'native'/name).write_text('compiled fixture')
  (self.base/'native-sources.zip').write_bytes(b'corresponding source archive')
  self.overrides=patch.multiple(cache,ROOT=self.root,BASE=self.base,INPUTS=['input.txt']);self.overrides.start();cache.write_record('linux-x64')
 def tearDown(self):self.overrides.stop();self.folder.cleanup()
 def test_rejects_foreign_platform_and_image(self):
  cache.validate('linux-x64')
  with self.assertRaises(RuntimeError):cache.validate('osx-x64')
  with patch.dict('os.environ',{'ImageVersion':'another image'}):
   with self.assertRaises(RuntimeError):cache.validate('linux-x64')
 def test_rejects_changed_binary_and_source_archive(self):
  for path in [self.base/'native/ipatool',self.base/'native-sources.zip']:
   original=path.read_bytes();path.write_bytes(b'tampered')
   with self.assertRaises(RuntimeError):cache.validate('linux-x64')
   path.write_bytes(original)
 def test_rejects_changed_compilation_input(self):
  (self.root/'input.txt').write_text('new source')
  with self.assertRaises(RuntimeError):cache.validate('linux-x64')

if __name__=='__main__':unittest.main()
