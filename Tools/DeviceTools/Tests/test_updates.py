import unittest
from device_tools.updates import PROJECTS,discover,resolve_tag,version,published

class UpdateChecks(unittest.TestCase):
    def setUp(self):
        self.pins={n:'a'*40 for n in PROJECTS}
        self.stable={n:{'tag':'1.0.0','sha':'b'*40} for n in PROJECTS}
    def fetch(self,path):
        if '/releases/latest' in path:return {'tag_name':'1.1.0','draft':False,'prerelease':False}
        if '/git/ref/tags/' in path:return {'object':{'type':'tag','sha':'d'*40}}
        if '/git/tags/' in path:return {'object':{'type':'commit','sha':'c'*40}}
        if '/compare/' in path:return {'status':'ahead'}
        raise AssertionError(path)
    def test_new_stable_resolves_annotated_tags(self):
        pins,stable,changes=discover(self.pins,self.stable,self.fetch)
        self.assertEqual(len(changes),6);self.assertEqual(set(pins.values()),{'c'*40})
        self.assertEqual(stable['libplist']['tag'],'1.1.0');self.assertEqual(self.pins['libplist'],'a'*40)
    def test_old_stable_does_not_downgrade_current_head(self):
        def fetch(path):return {'tag_name':'1.0.0'}
        pins,stable,changes=discover(self.pins,self.stable,fetch)
        self.assertFalse(changes);self.assertEqual(pins,self.pins)
    def test_prerelease_is_ignored(self):
        self.assertFalse(discover(self.pins,self.stable,lambda p:{'tag_name':'2.0.0','prerelease':True})[2])
    def test_divergence_fails_without_mutating_input(self):
        def fetch(path):return {'status':'diverged'} if '/compare/' in path else self.fetch(path)
        with self.assertRaises(ValueError):discover(self.pins,self.stable,fetch)
        self.assertEqual(set(self.pins.values()),{'a'*40})
    def test_transport_error_aborts_entire_candidate(self):
        def fetch(path):
            if 'libtatsu/' in path:raise OSError('network')
            return self.fetch(path)
        with self.assertRaises(OSError):discover(self.pins,self.stable,fetch)
        self.assertEqual(self.stable['libplist']['tag'],'1.0.0')
    def test_draft_or_partial_publication_is_retried(self):
        self.assertFalse(published('device-tools-v1.0.1',lambda p:{'draft':True}))
        self.assertFalse(published('device-tools-v1.0.1',lambda p:{'assets':[]}))
        assets=[{'name':f'IPA-DeviceTools-1.0.1-{suffix}.zip','size':1,'digest':'sha256:'+'c'*64} for suffix in ('Windows-x64','Sources')]
        self.assertTrue(published('device-tools-v1.0.1',lambda p:{'assets':assets}))
        assets[0]['digest']=None
        self.assertFalse(published('device-tools-v1.0.1',lambda p:{'assets':assets}))
    def test_tag_cycle_and_nonstable_versions_fail(self):
        with self.assertRaises(ValueError):resolve_tag('libplist','1.1.0',lambda p:{'object':{'type':'tag','sha':'a'*40}})
        for tag in ('1.0.1-beta','master','1.0','1.0.0/unsafe'):
            with self.assertRaises(ValueError):version(tag)
        self.assertGreater(version('1.10.0'),version('1.9.0'))

if __name__=='__main__':unittest.main()
