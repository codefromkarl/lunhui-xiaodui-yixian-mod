import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from build_full_release import build, component, PLUGINS

class FullReleaseTests(unittest.TestCase):
    def fixture(self, root, slug):
        p=root/slug;p.mkdir()
        m={'id':slug,'version':'0.7.1','code':{'assembly':PLUGINS[slug]},'content':['content/test.json'],'assets':{'test':{'file':'icons/card.png'}}}
        (p/'mod.json').write_text(json.dumps(m))
        for name in [PLUGINS[slug],'content/test.json','icons/card.png','TestResults/private.json','plugins/debug.pdb']:
            f=p/name;f.parent.mkdir(parents=True,exist_ok=True);f.write_bytes(b'test')
        return p

    def test_full_bundle_contains_both_complete_components_and_no_user_data(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=Path(tmp);a=self.fixture(r,'yixian.sect');b=self.fixture(r,'appearance.muyifeng');out=r/'full.zip'
            info=build(a,b,out,'sect-commit','published-appearance-commit')
            with zipfile.ZipFile(out) as z:
                self.assertIsNone(z.testzip())
                self.assertIn('Mods/yixian.sect/icons/card.png',z.namelist())
                self.assertIn('Mods/appearance.muyifeng/plugins/MuyifengAppearance.dll',z.namelist())
                self.assertFalse(any('TestResults' in n or n.endswith('.pdb') for n in z.namelist()))
                self.assertEqual(json.loads(z.read('BUNDLE.json')),info)
            self.assertEqual(info['components'][1]['sourceCommit'],'published-appearance-commit')

    def test_unknown_game_binary_and_missing_resources_are_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=Path(tmp);p=self.fixture(r,'yixian.sect');bad=p/'plugins/Reincarnation.Domain.dll';bad.touch()
            with self.assertRaisesRegex(ValueError,'unknown executable'):component(p,'yixian.sect')
            bad.unlink();(p/'icons/card.png').unlink()
            with self.assertRaisesRegex(ValueError,'Missing'):component(p,'yixian.sect')
