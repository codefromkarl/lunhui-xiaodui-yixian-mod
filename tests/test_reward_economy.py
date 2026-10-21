"""Lineage prices match native bloodlines; discounted packs/fates and battle fees stay separate."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return json.loads((ROOT / path).read_text())


class RewardEconomyTests(unittest.TestCase):
    def setUp(self):
        self.policy = read('tests/fixtures/reward-prices.json')
        self.table = read('mod/yixian.sect/content/cardtable.json')
        self.lineages = read('mod/yixian.sect/content/lineages.json')['operations']
        self.abilities = read('mod/yixian.sect/content/abilities.json')['operations']
        self.fates = read('mod/yixian.sect/content/fates.json')['operations']

    def test_all_three_progressions_share_native_prices_and_unchanged_gates(self):
        self.assertEqual(self.policy['lineageSteps'], [15, 55, 130, 240, 380])
        self.assertEqual(sum(self.policy['lineageSteps']), 820)
        self.assertEqual(sum(self.policy['oldLineageSteps']), 820)
        self.assertEqual({o['id'] for o in self.lineages}, {s['id'] for s in self.table['sects']})
        for row in self.lineages:
            self.assertEqual([s['price'] for s in row['fields']['steps']], self.policy['lineageSteps'])
            self.assertEqual([s['rank'] for s in row['fields']['steps']], ['D', 'C', 'B', 'A', 'S'])

    def test_pack_declarations_and_native_shelves_have_identical_prices(self):
        by_id = {o['id']: o['fields'] for o in self.abilities}
        for p in self.table['packs']:
            five = p.get('sect') == 'yixian.sect.lineage.five'
            expected = 0 if five else self.policy['packByRank'][p['rank'] - 1]
            self.assertEqual(p['price'], expected, p['key'])
            fields = by_id['yixian.sect.pack.' + p['key']]
            self.assertEqual(fields['purchasePrice'], expected)
            self.assertEqual(fields['acquisition'], 20 if five or p['rank'] == 1 else 10)
        deck = by_id['yixian.sect.pack.five_deck']
        self.assertEqual((deck['purchasePrice'], deck['acquisition']), (0, 20))

    def test_twelve_fates_are_twenty_and_keep_unlock_ranks(self):
        self.assertEqual(len(self.fates), 12)
        for row in self.fates:
            f = row['fields']
            self.assertEqual(f['purchasePrice'], 20)
            self.assertEqual(f['acquisition'], 10)
            self.assertIn(f['ranks'][0]['rank'], ['C', 'B'])
        for name in ('data/sword/fates.json', 'data/sword/lineages.json', 'data/sword/manifest.json'):
            self.assertNotIn('40点', (ROOT / name).read_text())

    def test_card_actions_and_persisted_records_do_not_acquire_reward_fees(self):
        for row in self.abilities:
            if row['id'].startswith(('yixian.sect.card.', 'yixian.sect.deckcard.', 'yixian.sect.unlockedcard.', 'yixian.sect.practice.')):
                self.assertEqual(row['fields']['purchasePrice'], 0)
        self.assertEqual(self.table['playBaseCost'], 0)
        for c in self.table['cards']:
            action = next(o['fields'] for o in self.abilities if o['id'] == 'yixian.sect.card.' + c['key'])
            v = {x['key']: x.get('intValue') for x in action['ranks'][0]['operation']['values']}
            self.assertEqual((v['apCost'], v['energyCost']), (0, c['rules']['cost'] * 4))

    def test_full_collection_budgets_include_free_foundation_gift(self):
        expected = {'sword': 1365, 'star': 1240, 'five': 900}
        for school, cost in expected.items():
            packs = [p for p in self.table['packs'] if p.get('sect', 'yixian.sect.lineage.sword') == 'yixian.sect.lineage.' + school]
            pack_cost = 0 if school == 'five' else sum(p['price'] for p in packs) - 20
            self.assertEqual(sum(self.policy['lineageSteps']) + pack_cost + 4 * self.policy['fatePrice'], cost)
