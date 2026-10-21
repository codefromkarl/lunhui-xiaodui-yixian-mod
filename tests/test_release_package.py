import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from build_release import build

class ReleasePackageTests(unittest.TestCase):
    def fixture(self, root):
        source=root/'source';source.mkdir()
        manifest={'id':'yixian.sect','version':'0.7.1','content':['content/abilities.json'],'tables':{'cardtable':'content/cardtable.json'},'assets':{'card':{'file':'icons/card.png'}}}
        (source/'mod.json').write_text(json.dumps(manifest))
        for name in ['README.md','plugins/YixianSect.dll','plugins/YixianSect.pdb','content/abilities.json','content/cardtable.json','icons/card.png','plugins/Reincarnation.Domain.dll','private.secret']:
            p=source/name;p.parent.mkdir(exist_ok=True);p.write_bytes(name.encode())
        return source

    def test_archive_contains_only_project_code_and_configuration(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);source=self.fixture(root);out=root/'update.zip'
            info=build(source,out,'test-commit')
            with zipfile.ZipFile(out) as z:
                self.assertIsNone(z.testzip())
                self.assertNotIn('yixian.sect/icons/card.png',z.namelist())
                self.assertNotIn('yixian.sect/plugins/Reincarnation.Domain.dll',z.namelist())
                self.assertNotIn('yixian.sect/private.secret',z.namelist())
                self.assertEqual(z.read('yixian.sect/plugins/YixianSect.dll'),(source/'plugins/YixianSect.dll').read_bytes())
                self.assertEqual(json.loads(z.read('release-manifest.json')),info)
                self.assertIn('不是首次安装包',z.read('UPDATE.md').decode())
            self.assertEqual(info['requiredLocalAssets'],['icons/card.png'])
            self.assertFalse(info['includesThirdPartyArtwork'])

    def test_incomplete_local_source_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);source=self.fixture(root);(source/'icons/card.png').unlink()
            with self.assertRaisesRegex(ValueError,'complete'):
                build(source,root/'update.zip','test-commit')
