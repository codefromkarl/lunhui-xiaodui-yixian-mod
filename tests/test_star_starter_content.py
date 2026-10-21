"""Starter replacement: real level-1 cards, four-card packs and exact UI/payment data."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return json.loads((ROOT / path).read_text())


class StarStarterContentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.school = read('data/seven-stars/school.json')
        cls.table = read('mod/yixian.sect/content/cardtable.json')
        cls.cards = {c['sourceId']: c for c in cls.school['cards']}

    def test_starter_pool_is_eight_distinct_cards_with_seven_attacks(self):
        packs = [p for p in self.school['packs'] if p['rank'] == 1]
        keys = [key for p in packs for key in p['cards']]
        cards = {c['key']: c for c in self.school['cards']}
        self.assertEqual(len(keys), 8)
        self.assertEqual(len(set(keys)), 8)
        self.assertEqual(sum(bool(cards[k]['rules'].get('damage') or cards[k]['rules'].get('randomMax')) for k in keys), 7)
        self.assertEqual(sum(cards[k]['rules'].get('omenGain', 0) for k in keys), 1)
        self.assertTrue(all(len(p['cards']) == 4 and p['price'] == 0 for p in packs))
        self.assertEqual(packs[0]['name'], '炼气·初阶招式')
        self.assertEqual(packs[0]['key'], 'yixian_star_chart')
        self.assertEqual(packs[0]['index'], 70)
        self.assertEqual([cards[k]['name'] for k in packs[0]['cards']], ['震卦', '掌心雷', '藕断丝连', '揽雀尾'])

    def test_new_cards_keep_real_source_and_explicit_rules(self):
        catalog = {c['id']: c for c in read('data/sources/original-correction/heptastar-pavilion-cards.json')}
        expected = {
            '4000013': dict(damage=18, postHeal=2, cost=0, originalRules=True, damageAttr=40, damageBp=10000),
            '4000061': dict(damage=27, bonusChance=10, bonusAttack=5, bonusHits=1, cost=1, originalRules=True, damageAttr=40, damageBp=10000),
        }
        for sid, rules in expected.items():
            c = self.cards[sid]
            self.assertEqual(c['rules'], rules)
            self.assertEqual(c['originalText'], catalog[sid]['original'])
            self.assertEqual(c['sourceUrl'], catalog[sid]['url'])
            self.assertEqual((c['rank'], c['realm'], c['sourceLevel']), (1, '炼气', 1))
            self.assertTrue(c['targetEnemy'])
        self.assertEqual(self.cards['4000003']['rules']['damage'], 12)
        self.assertEqual(self.cards['4000004']['rules']['randomMin'], 2)
        self.assertEqual(self.cards['4000004']['rules']['randomMax'], 10)

    def test_removed_cards_are_not_shipped_or_reused(self):
        removed = {'yixian_star_gather', 'yixian_star_divine'}
        self.assertTrue(removed.isdisjoint(c['key'] for c in self.table['cards']))
        manifest = read('mod/yixian.sect/mod.json')
        abilities = read('mod/yixian.sect/content/abilities.json')['operations']
        for key in removed:
            self.assertNotIn('icon.yixian.sect.card.' + key, manifest['assets'])
            self.assertNotIn('yixian.sect.card.' + key, {a['id'] for a in abilities})
        selection = read('data/sources/original-correction/star-selection.json')
        self.assertEqual({c['sourceId'] for c in selection['selection']}, set(self.cards))
        excluded = {c['id'] for c in selection['excluded']}
        self.assertTrue({'4000001', '4000002'} <= excluded)
        self.assertTrue({'4000013', '4000061'}.isdisjoint(excluded))

    def test_generated_details_assets_and_costs_agree(self):
        cards = {c['sourceId']: c for c in self.table['cards'] if c.get('sourceId')}
        expected = {
            '4000013': '造成物理伤害〔18＋100%×能量〕。\n后招：为随机一名存活己方角色（优先未满血）恢复〔6＋50%×体质〕生命。\n消耗：0斗气。',
            '4000061': '造成物理伤害〔27＋100%×能量〕。\n10%概率追加一次〔15〕攻击。\n消耗：4斗气。',
        }
        manifest = read('mod/yixian.sect/mod.json')
        for sid, description in expected.items():
            card = cards[sid]
            self.assertEqual(card['description'], description)
            icon = manifest['assets']['icon.yixian.sect.card.' + card['key']]
            self.assertEqual(icon['file'], 'icons/cards/' + card['key'] + '.png')
            self.assertTrue((ROOT / 'mod/yixian.sect' / icon['file']).is_file())
        costs = read('data/sources/original-correction/original-costs.json')
        self.assertEqual(costs['cards']['4000013']['cost'], 0)
        self.assertEqual(costs['cards']['4000061']['cost'], 1)
        self.assertTrue({'4000001', '4000002'} <= set(costs['retiredCards']))


if __name__ == '__main__':
    unittest.main()
