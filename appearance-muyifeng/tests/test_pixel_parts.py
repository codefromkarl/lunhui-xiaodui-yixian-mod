import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('pixel_parts', ROOT / 'scripts/build_pixel_parts.py')
parts = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(parts)


class PixelPartsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = json.loads((ROOT / 'mod/appearance.muyifeng/art/muyifeng-pixels.json').read_text())

    def test_authoring_reproduces_packaged_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / 'mod/appearance.muyifeng/art/muyifeng-pixels.json'
            target.parent.mkdir(parents=True)
            with patch.object(parts, 'ROOT', root):
                parts.main()
            self.assertEqual(json.loads(target.read_text()), self.source)

    def test_ten_valid_final_resolution_parts(self):
        expected = {'Body/Head': (32, 40), 'HairBack': (24, 40), 'Cloth/Body': (28, 28),
                    'Cloth/Left': (16, 20), 'Cloth/Right': (16, 20), 'Pant/Left': (12, 23),
                    'Pant/Right': (12, 23), 'Body/Foot_L': (10, 14), 'Body/Foot_R': (10, 14),
                    'Weapon/Sword': (48, 48)}
        self.assertEqual(set(expected), set(self.source['parts']))
        for key, (width, height) in expected.items():
            part = self.source['parts'][key]
            self.assertEqual(len(part['rows']), height)
            self.assertTrue(all(len(row) == width for row in part['rows']))
            self.assertTrue(all(color in self.source['palette'] for row in part['rows'] for color in row))
            self.assertEqual(part['pixelsPerUnit'], 32 if key == 'Weapon/Sword' else 56)

    def test_sword_has_native_grip_and_narrow_visible_extent(self):
        sword = self.source['parts']['Weapon/Sword']
        self.assertEqual(sword['pivot'], [.5, .25])  # Native 48px blade pivot (24,12).
        occupied = [(x, y) for y, row in enumerate(sword['rows']) for x, cell in enumerate(row) if cell != '.']
        self.assertLessEqual(max(x for x, _ in occupied) - min(x for x, _ in occupied) + 1, 20)
        top = min(y for _, y in occupied)
        self.assertTrue(all(x > 24 for x, y in occupied if y == top))  # Native upright-right axis, not mirrored body art.
        self.assertLess(top, 20)

    def test_head_has_no_long_skin_neck_or_brown_jaw_band(self):
        rows = self.source['parts']['Body/Head']['rows']
        skin = [(x, y) for y, row in enumerate(rows) for x, cell in enumerate(row) if cell in 'stfF']
        self.assertLessEqual(max(y for _, y in skin), 31)
        self.assertTrue(all('s' not in row for row in rows[26:]))
        # Canonical face looks left: broad near eye on the right, tiny far eye by the left nose.
        near = sum(rows[y][x] in 'pa' for y in range(21, 25) for x in range(12, 18))
        far = sum(rows[y][x] in 'pa' for y in range(21, 25) for x in range(6, 9))
        self.assertGreater(near, far)

    def test_package_and_assembly_version_match_revision(self):
        manifest = json.loads((ROOT / 'mod/appearance.muyifeng/mod.json').read_text())
        self.assertEqual(manifest['version'], '0.7.1')
        self.assertIn('<Version>0.7.1</Version>', (ROOT / 'src/MuyifengAppearance.csproj').read_text())


if __name__ == '__main__':
    unittest.main()
