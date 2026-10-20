"""Costs come from independently inspected original card faces, not runtime output."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from check_original_sources import check_sword_costs


class SwordOriginalCostsTests(unittest.TestCase):
    def setUp(self):
        self.costs = json.loads((ROOT / 'data/sources/original-correction/original-costs.json').read_text())['cards']
        self.cards = json.loads((ROOT / 'mod/yixian.sect/content/cardtable.json').read_text())['cards']
        self.valid_cards = copy.deepcopy(self.cards)
        for card in self.valid_cards:
            if str(card.get('sourceId', '')).startswith('100'):
                card['rules']['cost'] = self.costs[card['sourceId']]['cost']

    def test_original_fee_sentinels(self):
        expected = {'1000009': 1, '1000021': 1, '1000029': 2, '1000032': 0,
                    '1000035': 0, '1000036': 3, '1000041': 0, '1000059': 1,
                    '1000103': 2, '1000025': 1, '1000098': 1, '1000107': 1}
        for sid, cost in expected.items():
            with self.subTest(source=sid):
                self.assertEqual(self.costs[sid]['cost'], cost)
                self.assertTrue(self.costs[sid]['imageUrl'].endswith(sid + '_zh.webp'))
                self.assertEqual(len(self.costs[sid]['imagePixelSha256']), 64)

    def test_source_and_generated_cover_all_48_original_costs(self):
        check_sword_costs(self.cards, self.costs)
        check_sword_costs(json.loads((ROOT / 'data/sword/cardtable.json').read_text())['cards'], self.costs)

    def test_flying_fang_zero_fee_regression_is_rejected(self):
        cards = copy.deepcopy(self.valid_cards)
        next(c for c in cards if c.get('sourceId') == '1000021')['rules']['cost'] = 0
        with self.assertRaisesRegex(AssertionError, '飞牙剑'):
            check_sword_costs(cards, self.costs)

    def test_missing_cost_cannot_silently_mean_free(self):
        cards = copy.deepcopy(self.valid_cards)
        next(c for c in cards if c.get('sourceId') == '1000001')['rules'].pop('cost', None)
        with self.assertRaisesRegex(AssertionError, 'explicit'):
            check_sword_costs(cards, self.costs)

    def test_spurious_three_peaks_fee_is_rejected(self):
        cards = copy.deepcopy(self.valid_cards)
        next(c for c in cards if c.get('sourceId') == '1000032')['rules']['cost'] = 1
        with self.assertRaisesRegex(AssertionError, '三峰剑'):
            check_sword_costs(cards, self.costs)
