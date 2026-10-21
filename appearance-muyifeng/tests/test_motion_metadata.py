import copy
import importlib.util
import json
from pathlib import Path
import unittest
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
MOD=ROOT/'mod/appearance.muyifeng'
spec=importlib.util.spec_from_file_location('motion_metadata',ROOT/'scripts/build_motion_metadata.py')
build=importlib.util.module_from_spec(spec);spec.loader.exec_module(build)


class MotionMetadataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.roster=json.loads((ROOT/'characters.json').read_text())

    def atlas(self,c):
        p=MOD/('art/muyifeng-sprites.json' if c['slug']=='muyifeng' else f"art/characters/{c['slug']}/atlas.json")
        return json.loads(p.read_text())

    def test_profiles_cover_distinct_poses_without_changing_identity(self):
        profiles={c['slug']:self.atlas(c)['rig']['presentation']['kind'] for c in self.roster}
        self.assertEqual(set(profiles.values()),{'sword','dual','cast','heavy'})
        self.assertEqual(profiles['yanxue'],'dual')
        self.assertEqual(profiles['muhu'],'heavy')
        self.assertEqual(profiles['qiwangyou'],'cast')
        self.assertEqual(self.roster[0]['token'],[255,77,89,70,1,0,0])

    def test_all_18_have_separate_aligned_injured_and_closed_eye_sprites(self):
        for c in self.roster:
            a=self.atlas(c)
            for state in ('damaged','downed'):
                with self.subTest(character=c['slug'],state=state):
                    p=a['parts']['Face/'+state];x,y,w,h=p['rect'];nx,ny=p['landmarks']['neck']
                    self.assertTrue(p['mirrorX'],'render reference must be returned to the canonical source frame')
                    self.assertGreater(p['pixelsPerUnit'],100)
                    with Image.open(MOD/a['sheets'][p['sheet']]) as im:
                        self.assertEqual(im.mode,'RGBA');self.assertEqual(im.getchannel('A').getextrema(),(0,255))
                        self.assertLessEqual(x+w,im.width);self.assertLessEqual(y+h,im.height)
                        self.assertGreaterEqual(im.getpixel((int(x+nx),int(y+ny)))[3],160)
                    self.assertNotEqual(p['sheet'],a['parts']['Body/Head']['sheet'])

    def test_lantern_and_veil_are_used_instead_of_overwriting_originals(self):
        lookup={c['slug']:c for c in self.roster}
        a=self.atlas(lookup['dulingyuan']);p=a['parts']['Weapon/Sword']
        self.assertEqual(a['sheets'][p['sheet']],'art/characters/dulingyuan/lantern-v2.png')
        self.assertLess(a['rig']['sword']['grip'][1],p['rect'][3]*.15)
        a=self.atlas(lookup['huaqinrui']);p=a['parts']['Body/Head']
        self.assertEqual(a['sheets'][p['sheet']],'art/characters/huaqinrui/veil-v2.png')
        self.assertTrue((MOD/'art/characters/huaqinrui/atlas-v1.png').is_file())
        self.assertTrue((MOD/'art/characters/dulingyuan/atlas-v1.png').is_file())

    def test_all_props_have_an_in_crop_effect_emitter(self):
        for c in self.roster:
            a=self.atlas(c);w,h=a['parts']['Weapon/Sword']['rect'][2:];x,y=a['rig']['sword']['emitter']
            self.assertTrue(0<=x<w and 0<=y<h,c['slug'])
            part=a['parts']['Weapon/Sword'];rx,ry=part['rect'][:2]
            with Image.open(MOD/a['sheets'][part['sheet']]) as im:
                self.assertGreaterEqual(im.getpixel((int(rx+x),int(ry+y)))[3],160,c['slug'])

    def test_metadata_rebuild_is_idempotent(self):
        for c in self.roster:
            a=self.atlas(c)
            self.assertEqual(build.enrich(copy.deepcopy(a),c),a,c['slug'])


if __name__=='__main__':unittest.main()
