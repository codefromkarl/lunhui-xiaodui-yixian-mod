import json
from pathlib import Path
import unittest
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / 'mod/appearance.muyifeng'


def alpha_for(atlas, key):
    part = atlas['parts'][key]
    x, y, w, h = part['rect']
    with Image.open(MOD / atlas['sheets'][part['sheet']]) as source:
        alpha = source.getchannel('A').crop((x, y, x+w, y+h))
    if 'mask' in part:
        polygon = Image.new('L', (w, h))
        ImageDraw.Draw(polygon).polygon([tuple(p) for p in part['mask']], fill=255)
        from PIL import ImageChops
        alpha = ImageChops.multiply(alpha, polygon)
    for polygon in part.get('exclude', []):
        ImageDraw.Draw(alpha).polygon([tuple(p) for p in polygon], fill=0)
    return alpha


def visible(alpha, point):
    x, y = map(int, point)
    return 0 <= x < alpha.width and 0 <= y < alpha.height and alpha.getpixel((x, y)) >= 128


class RigCalibrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.roster = json.loads((ROOT / 'characters.json').read_text())[1:]

    def atlas(self, character, name='atlas.json'):
        return json.loads((MOD / 'art/characters' / character['slug'] / name).read_text())

    def test_neck_junctions_are_inside_real_opaque_anatomy(self):
        for character in self.roster:
            atlas = self.atlas(character)
            with self.subTest(character=character['name']):
                self.assertTrue(atlas['rig']['calibrated'])
                self.assertTrue(visible(alpha_for(atlas, 'Body/Head'), atlas['rig']['head']['neck']))
                self.assertTrue(visible(alpha_for(atlas, 'Cloth/Body'), atlas['rig']['body']['neck']))

    def test_visible_neck_check_rejects_original_bounding_box_calibration(self):
        character = next(c for c in self.roster if c['slug'] == 'lichengyun')
        original = self.atlas(character, 'atlas-layout-v1.json')
        self.assertFalse(visible(alpha_for(original, 'Cloth/Body'), original['rig']['body']['neck']))
        fixed = self.atlas(character)
        self.assertTrue(visible(alpha_for(fixed, 'Cloth/Body'), fixed['rig']['body']['neck']))

    def test_both_feet_face_forward_with_opaque_toe_and_heel_points(self):
        for character in self.roster:
            atlas = self.atlas(character)
            for side in ('Left', 'Right'):
                with self.subTest(character=character['name'], side=side):
                    leg = atlas['rig']['leg'+side]
                    key = leg['foot']
                    alpha = alpha_for(atlas, key)
                    self.assertTrue(visible(alpha, leg['toe']))
                    self.assertTrue(visible(alpha, leg['heel']))
                    for point in (leg['toe'], leg['heel']):
                        for dy in range(-2, 3):
                            for dx in range(-2, 3):
                                self.assertTrue(visible(alpha, [point[0]+dx, point[1]+dy]), 'toe/heel cap marker must be inside an opaque patch, not a silhouette edge')
                    direction = leg['toe'][0] - leg['heel'][0]
                    if atlas['parts'][key].get('mirrorX'):
                        direction = -direction
                    self.assertLess(direction, 0, 'canonical source character faces left')

    def test_attached_fist_and_shoe_pixels_are_removed_from_sleeve_and_trouser(self):
        for slug, key, point in [('lujianxin', 'Cloth/LeftLower', [125,240]),
                                 ('yanxue', 'Pant/Left', [90,210]),
                                 ('lichengyun', 'Pant/Right', [60,240])]:
            character = next(c for c in self.roster if c['slug'] == slug)
            before = self.atlas(character, 'atlas-layout-v1.json')
            after = self.atlas(character)
            with self.subTest(character=slug, part=key):
                self.assertTrue(visible(alpha_for(before, key), point))
                self.assertFalse(visible(alpha_for(after, key), point))

    def test_lujianxin_boots_match_each_other_and_the_costume_scale(self):
        character = next(c for c in self.roster if c['slug'] == 'lujianxin')
        atlas = self.atlas(character)
        widths = []
        for side in ('Left', 'Right'):
            key = atlas['rig']['leg'+side]['foot']
            part = atlas['parts'][key]
            self.assertEqual(part['sheet'], atlas['parts']['Cloth/Body']['sheet'])
            width = part['rect'][2] / part['pixelsPerUnit']
            original = self.atlas(character, 'atlas-layout-v1.json')['parts'][key]
            self.assertLess(width, .8 * original['rect'][2] / original['pixelsPerUnit'])
            widths.append(width)
        self.assertAlmostEqual(widths[0], widths[1], delta=.005)

    def test_tiger_mantle_has_explicit_shoulder_attachment(self):
        tiger = next(c for c in self.roster if c['slug'] == 'muhu')
        atlas = self.atlas(tiger)
        self.assertEqual(set(atlas['rig']['back']), {'shoulderLeft', 'shoulderRight'})
        for point in atlas['rig']['back'].values():
            self.assertTrue(visible(alpha_for(atlas, 'HairBack'), point))

    def test_cuffs_and_trouser_ends_land_on_visible_extracted_pixels(self):
        for character in self.roster:
            atlas = self.atlas(character)
            for side in ('Left', 'Right'):
                with self.subTest(character=character['name'], side=side):
                    sleeve = atlas['rig']['sleeve'+side]
                    self.assertTrue(visible(alpha_for(atlas, 'Cloth/'+side+'Lower'), sleeve['cuff']))
                    leg = atlas['rig']['leg'+side]
                    self.assertTrue(visible(alpha_for(atlas, leg['pant']), leg['pantAnkle']))
            for key in ('Hand/Grip_L', 'Hand/Grip_R', 'Hand/Open_L', 'Hand/Open_R'):
                self.assertTrue(visible(alpha_for(atlas, key), atlas['parts'][key]['landmarks']['wrist']))


if __name__ == '__main__':
    unittest.main()
