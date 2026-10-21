"""Replacement cards retain original cost/art evidence and level-one gameplay."""
import json
from pathlib import Path
import sys
import unittest
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from sword_retirement import REPLACEMENTS


def read(path):
    return json.loads((ROOT / path).read_text())


class SwordReplacementTests(unittest.TestCase):
    def test_three_original_cards_have_exact_new_rules_and_slots(self):
        fixture = read('tests/fixtures/sword-replacements.json')
        source = read('data/sword/cardtable.json')
        actual = {c['key']: c for c in source['cards']}
        self.assertEqual([c['name'] for c in fixture['cards']], ['形意剑', '反身剑', '流云乱剑'])
        for c, replacement in zip(fixture['cards'], REPLACEMENTS):
            self.assertEqual(actual[c['key']], c)
            pack = next(p for p in source['packs'] if p['key'] == replacement['packKey'])
            self.assertEqual(pack['cards'][replacement['slot']], c['key'])
            self.assertEqual(len(pack['cards']), 4)
            self.assertEqual(c['rules']['cost'], 1)
            self.assertNotIn('mad', c['tags'])
        abilities = {o['id']: o for o in read('data/sword/abilities.json')['operations']}
        for o in fixture['abilities']:
            self.assertEqual(abilities[o['id']], o)

    def test_original_names_realms_text_fees_and_original_size_art(self):
        original = {c['id']: c for c in read('data/sources/sword-original-cards.json')}
        costs = read('data/sources/original-correction/original-costs.json')['cards']
        for c in read('tests/fixtures/sword-replacements.json')['cards']:
            sid = c['sourceId']; row = original[sid]
            self.assertEqual((c['name'], c['realm'], c['originalText']), (row['name'], row['realm'], row['original']))
            self.assertEqual(costs[sid]['cost'], 1)
            self.assertEqual(costs[sid]['imageSize'], [308, 508])
            with Image.open(ROOT / 'data/sources/original-correction/card-images' / (sid + '.webp')) as before, Image.open(ROOT / 'mod/yixian.sect/icons/cards' / (c['key'] + '.png')) as after:
                self.assertEqual(after.size, before.size)
                self.assertEqual(before.convert('RGBA').tobytes(), after.convert('RGBA').tobytes())
