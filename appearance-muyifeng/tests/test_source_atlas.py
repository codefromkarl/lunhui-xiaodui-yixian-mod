import json
from pathlib import Path
import unittest
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / 'mod/appearance.muyifeng'


class SourceAtlasTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.config = json.loads((MOD / 'appearance.json').read_text())
        cls.atlas = json.loads((MOD / cls.config['atlasSource']).read_text())

    def test_reference_renderer_is_selected(self):
        self.assertNotIn('pixelSource', self.config)
        self.assertEqual(self.atlas['format'], 'source-atlas-v1')
        self.assertEqual(self.atlas['sheets']['reference'], 'art/muyifeng-atlas.png')
        self.assertEqual(len(self.atlas['parts']), 18)

    def test_selection_is_per_character_not_a_local_override(self):
        self.assertEqual(self.config['target'], 'character-selection')
        manifest = json.loads((MOD / 'mod.json').read_text())
        self.assertEqual(manifest['id'], 'appearance.muyifeng')
        self.assertEqual(manifest['version'], '0.7.3')

    def test_source_crops_are_valid_and_have_visible_pixels(self):
        for name, part in self.atlas['parts'].items():
            with self.subTest(part=name), Image.open(MOD / self.atlas['sheets'][part['sheet']]) as image:
                x, y, w, h = part['rect']
                self.assertGreaterEqual(x, 0)
                self.assertGreaterEqual(y, 0)
                self.assertLessEqual(x + w, image.width)
                self.assertLessEqual(y + h, image.height)
                self.assertGreater(part['pixelsPerUnit'], 0)
                alpha = image.getchannel('A')
                visible = sum(alpha.getpixel((xx, yy)) >= max(1, part.get('alphaThreshold', 0))
                              for yy in range(y, y+h) for xx in range(x, x+w))
                self.assertGreater(visible, 1000)

    def test_face_is_original_unrotated_unquantized_cutout(self):
        head = self.atlas['parts']['Body/Head']
        self.assertEqual(head['sheet'], 'reference')
        self.assertNotIn('rotation', head)
        self.assertNotIn('alphaThreshold', head)
        self.assertNotIn('colorMultiply', head)
        self.assertNotIn('backing', head)
        rx, ry, rw, rh = self.atlas['faceIdentityRegion']
        x, y, w, h = head['rect']
        self.assertGreaterEqual(rx, x)
        self.assertGreaterEqual(ry, y)
        self.assertLessEqual(rx+rw, x+w)
        self.assertLessEqual(ry+rh, y+h)
        self.assertGreaterEqual(rw*rh, 20000)

    def test_clothing_and_hair_use_reference_not_generated_replacements(self):
        for name in ['HairBack', 'Cloth/Body', 'Cloth/Left', 'Cloth/Right', 'Cloth/LeftLower', 'Cloth/RightLower']:
            self.assertEqual(self.atlas['parts'][name]['sheet'], 'reference')
            self.assertNotIn('colorMultiply', self.atlas['parts'][name])
            self.assertNotIn('rotation', self.atlas['parts'][name])
        for name in ['Pant/Left', 'Pant/Right', 'Body/Foot_L', 'Body/Foot_R', 'Weapon/Sword']:
            self.assertEqual(self.atlas['parts'][name]['sheet'], 'support')

    def test_torso_does_not_duplicate_the_embedded_sleeves(self):
        torso = self.atlas['parts']['Cloth/Body']
        self.assertIn('mask', torso)
        # The original torso cell included full arms: retain only the central robe.
        points = torso['mask']
        self.assertTrue(all(120 <= x <= 300 for x, y in points if y < 195))

    def test_sleeves_have_overlapping_elbow_segments(self):
        for side in ['Left', 'Right']:
            upper = self.atlas['parts']['Cloth/' + side]
            lower = self.atlas['parts']['Cloth/' + side + 'Lower']
            self.assertEqual(upper['rect'], lower['rect'])
            self.assertEqual(upper['pivot'], lower['pivot'])
            self.assertEqual(upper['pixelsPerUnit'], lower['pixelsPerUnit'])
            upper_end = max(y for x, y in upper['mask'])
            lower_start = min(y for x, y in lower['mask'])
            elbow_y = self.atlas['rig']['sleeve' + side]['elbow'][1]
            self.assertLess(lower_start, elbow_y)
            self.assertGreater(upper_end, elbow_y)
            self.assertGreaterEqual(upper_end - lower_start, 40)

    def test_hands_and_joint_markers_are_inside_their_sources(self):
        for side in ['L', 'R']:
            for pose in ['Grip_', 'Open_']:
                part = self.atlas['parts']['Hand/' + pose + side]
                self.assertEqual(part['sheet'], 'hands')
                x, y, w, h = part['rect']
                with Image.open(MOD / self.atlas['sheets']['hands']) as image:
                    for marker in part['landmarks'].values():
                        xx, yy = marker
                        self.assertTrue(0 <= xx < w and 0 <= yy < h)
                        self.assertGreater(image.getpixel((x+xx, y+yy))[3], 160)
        self.assertIn('neck', self.atlas['rig']['body'])
        self.assertIn('neck', self.atlas['rig']['head'])
        for side in ['Left', 'Right']:
            self.assertEqual(set(self.atlas['rig']['sleeve'+side]), {'shoulder', 'elbow', 'cuff'})

    def test_support_has_real_transparency_and_straight_sword(self):
        sword = self.atlas['parts']['Weapon/Sword']
        self.assertEqual(sword['rotation'], -25)
        with Image.open(MOD / self.atlas['sheets']['support']) as image:
            self.assertEqual(image.mode, 'RGBA')
            self.assertEqual(image.getpixel((0, 0))[3], 0)
            self.assertGreater(sword['rect'][3], 3*sword['rect'][2])


if __name__ == '__main__':
    unittest.main()
