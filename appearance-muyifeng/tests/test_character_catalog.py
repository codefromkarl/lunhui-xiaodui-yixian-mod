from collections import Counter
import json
from pathlib import Path
import unittest
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / 'mod/appearance.muyifeng'


class CharacterCatalogTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.roster = json.loads((ROOT / 'characters.json').read_text())
        cls.config = json.loads((MOD / 'appearance.json').read_text())

    def test_complete_three_sect_roster_and_persistent_identity(self):
        self.assertEqual(len(self.roster), 18)
        self.assertEqual(Counter(c['group'] for c in self.roster), {'云灵剑宗': 6, '七星阁': 6, '五行道盟': 6})
        self.assertEqual(len({c['id'] for c in self.roster}), 18)
        self.assertEqual(len({tuple(c['token']) for c in self.roster}), 18)
        self.assertEqual(self.roster[0]['id'], 'appearance.muyifeng')
        self.assertEqual(self.roster[0]['token'], [255, 77, 89, 70, 1, 0, 0])
        self.assertTrue(all(len(c['token']) == 7 and c['token'][0] == 255 and c['token'][-1] == 0 for c in self.roster))

    def test_every_new_character_is_configured_with_its_own_art(self):
        rows = self.config['characters']
        self.assertEqual(len(rows), 17)
        self.assertEqual({r['id'] for r in rows}, {r['id'] for r in self.roster[1:]})
        self.assertEqual(len({r['atlasSource'] for r in rows}), 17)
        self.assertEqual(len({r['spritePrefix'] for r in rows}), 17)
        expected = {c['id']: c for c in self.roster}
        for row in rows:
            for key in ('name', 'group', 'sourceId', 'token'):
                self.assertEqual(row[key], expected[row['id']][key])

    def test_transparent_sources_crops_and_joint_markers_are_complete(self):
        required = {'Body/Head', 'HairBack', 'Cloth/Body', 'Cloth/Left', 'Cloth/LeftLower',
                    'Cloth/Right', 'Cloth/RightLower', 'Hand/Grip_L', 'Hand/Grip_R',
                    'Hand/Open_L', 'Hand/Open_R', 'Pant/Left', 'Pant/Right',
                    'Body/Foot_L', 'Body/Foot_R', 'Weapon/Sword'}
        for row in self.config['characters']:
            with self.subTest(character=row['name']):
                atlas = json.loads((MOD / row['atlasSource']).read_text())
                self.assertTrue(required.issubset(atlas['parts']))
                for source in atlas['sheets'].values():
                    with Image.open(MOD / source) as image:
                        self.assertEqual(image.mode, 'RGBA')
                        self.assertEqual(image.getchannel('A').getextrema(), (0, 255))
                        self.assertGreater(sum(image.getchannel('A').histogram()[:24]), image.width*image.height*.25)
                for key, part in atlas['parts'].items():
                    with Image.open(MOD / atlas['sheets'][part['sheet']]) as image:
                        x, y, w, h = part['rect']
                        self.assertGreater(w, 0); self.assertGreater(h, 0)
                        self.assertGreaterEqual(x, 0); self.assertGreaterEqual(y, 0)
                        self.assertLessEqual(x+w, image.width); self.assertLessEqual(y+h, image.height)
                        self.assertIsNotNone(image.getchannel('A').crop((x,y,x+w,y+h)).getbbox())
                    self.assertGreater(part['pixelsPerUnit'], 0)
                    for mark in part.get('landmarks', {}).values():
                        self.assertTrue(0 <= mark[0] < w and 0 <= mark[1] < h, (row['name'], key, mark))
                self.assertEqual('offhand' in atlas['rig'], row['name'] == '炎雪')

    def test_new_art_has_retrievable_reference_and_generation_prompts(self):
        for c in self.roster[1:]:
            directory = MOD / 'art/characters' / c['slug']
            provenance = json.loads((directory / 'provenance.json').read_text())
            self.assertEqual(provenance['sourceId'], c['sourceId'])
            self.assertEqual(provenance['referenceUrl'], c['sourceUrl'])
            self.assertFalse(provenance['pixelEdits'])
            self.assertEqual(provenance['generationTool'], 'built-in image_gen')
            self.assertGreater(len((directory / provenance['prompt']).read_text()), 1000)


if __name__ == '__main__':
    unittest.main()
