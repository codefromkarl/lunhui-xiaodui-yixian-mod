"""Independent manual corner audit, including zero-fee and negative controls."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from check_original_sources import check_face_costs

PAID = {
    '700': {7000004: 1, 7000007: 2, 7000017: 1, 7000019: 2, 7000028: 1,
            7000032: 1, 7000020: 1, 7000027: 1, 7000066: 1, 7000043: 1,
            7000056: 2, 7000057: 2, 7000115: 1},
    '400': {4000010: 1, 4000020: 1, 4000030: 1, 4000029: 1, 4000036: 1,
            4000038: 1, 4000041: 1, 4000044: 1, 4000046: 1, 4000060: 2, 4000107: 1},
}


class FiveStarOriginalCostsTests(unittest.TestCase):
    def setUp(self):
        self.costs = json.loads((ROOT / 'data/sources/original-correction/original-costs.json').read_text())['cards']
        self.cards = json.loads((ROOT / 'mod/yixian.sect/content/cardtable.json').read_text())['cards']

    def test_every_original_face_matches_independent_transcription(self):
        for prefix, paid in PAID.items():
            cards = check_face_costs(self.cards, self.costs, prefix)
            for c in cards:
                sid = str(c['sourceId'])
                with self.subTest(card=c['name']):
                    self.assertEqual(self.costs[sid]['cost'], paid.get(int(sid), 0))
                    self.assertEqual(len(self.costs[sid]['imagePixelSha256']), 64)
            self.assertEqual(sum(c['rules']['cost'] > 0 for c in cards), len(paid))

    def test_zero_fee_missing_fee_and_spurious_fee_are_rejected(self):
        for sid, bad in [('7000007', 0), ('4000060', 0), ('7000001', 1), ('4000001', 1), ('7000004', None), ('4000010', None)]:
            with self.subTest(source=sid):
                cards = copy.deepcopy(self.cards)
                card = next(c for c in cards if str(c.get('sourceId')) == sid)
                if bad is None:
                    card['rules'].pop('cost')
                else:
                    card['rules']['cost'] = bad
                with self.assertRaisesRegex(AssertionError, card['name']):
                    check_face_costs(cards, self.costs, sid[:3])
