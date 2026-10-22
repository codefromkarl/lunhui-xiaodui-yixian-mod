"""Five cultivation changes scaling, never source identity, cost or fixed mechanics."""
import json
from pathlib import Path
import unittest
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))


def read(path):
    return json.loads((ROOT / path).read_text())


class FiveCultivationContentTests(unittest.TestCase):
    def test_multi_attribute_budgets_and_fixed_rules(self):
        previous = {c['key']: c for c in read('tests/fixtures/five-scaling-v070.json')['cards']}
        cards = read('data/five-elements/school.json')['cards']
        self.assertEqual(set(previous), {c['key'] for c in cards})
        for card in cards:
            with self.subTest(card=card['name']):
                before, after = previous[card['key']]['rules'], card['rules']
                self.assertEqual(card['sourceId'], previous[card['key']]['sourceId'])
                prefixes = {k[:-2] for k in before if k.endswith('Bp') and k[:-2] + 'Attr' in before}
                fixed = {k: v for k, v in before.items() if not any(k in (p + 'Attr', p + 'Bp') for p in prefixes)}
                self.assertEqual(fixed, {k: v for k, v in after.items() if not k.endswith('Scaling')})
                self.assertEqual({k[:-7] for k in after if k.endswith('Scaling')}, prefixes)
                for key in prefixes:
                    terms = after[key + 'Scaling']
                    self.assertEqual(len(terms), 2)
                    self.assertEqual(len({t['attribute'] for t in terms}), 2)
                    self.assertTrue(all(t['attribute'] in (20, 30, 40) and t['bp'] > 0 for t in terms))
                    self.assertEqual(sum(t['bp'] for t in terms), before[key + 'Bp'])
                    self.assertEqual(terms[0]['attribute'], {0: 40, 1: 20, 2: 40, 3: 20, 4: 40, 5: 30}[card['element']])

    def test_automatic_config_and_retired_save_ids_not_shop_items(self):
        table = read('mod/yixian.sect/content/cardtable.json')
        five = next(s for s in table['sects'] if s['statePrefix'] == 'five')
        self.assertEqual(five['cultivation'], dict(fallbackBp=7000, growth=[8, 18, 44, 84, 132], primaryGrowth=[6, 12, 30, 58, 92]))
        ops = read('mod/yixian.sect/content/abilities.json')['operations']
        profiles = [o for o in ops if o['id'].startswith('yixian.sect.practice.five.')]
        expected = {f'yixian.sect.practice.five.{attribute}.{mask}' for attribute, group in ((40, 1), (20, 2), (30, 4)) for mask in range(1, 8) if mask & group}
        self.assertEqual({o['id'] for o in profiles}, expected)
        for profile in profiles:
            self.assertIn('已停用', profile['fields']['displayName'])
            self.assertEqual(profile['fields']['acquisition'], 20)
            self.assertEqual(profile['fields']['purchasePrice'], 0)
            self.assertFalse(any(v['key'] == 'cardTablePackKey' for v in profile['fields']['ranks'][0]['operation']['values']))
        lineage = next(o for o in read('mod/yixian.sect/content/lineages.json')['operations'] if o['id'] == five['id'])
        self.assertTrue(all(not any(v['key'] in ('energyFlat', 'muscleFlat', 'reactionFlat', 'constitutionFlat') for v in s['passive']['values']) for s in lineage['fields']['steps']))

    def test_five_growth_does_not_inherit_sword_specialization(self):
        from school_content import compile_school
        sword = dict(lineages=read('data/sword/lineages.json'), statuses=read('data/sword/statuses.json'))
        for step in sword['lineages']['operations'][0]['fields']['steps']:
            step['passive']['values'].extend(dict(key=key, intValue=99) for key in ('muscleFlat', 'reactionFlat', 'constitutionFlat'))
        compiled = compile_school(read('data/five-elements/school.json'), sword)
        for step in compiled['lineages']['operations'][0]['fields']['steps']:
            self.assertFalse(any(v['key'] in ('energyFlat', 'muscleFlat', 'reactionFlat', 'constitutionFlat') for v in step['passive']['values']))

    def test_formula_copy_contains_each_actual_attribute(self):
        cards = [c for c in read('mod/yixian.sect/content/cardtable.json')['cards'] if c.get('sect') == 'yixian.sect.lineage.five']
        for card in cards:
            for key, terms in card['rules'].items():
                if key.endswith('Scaling'):
                    for term in terms:
                        label = {20: '反应', 30: '体质', 40: '能量'}[term['attribute']]
                        self.assertIn(f'{term["bp"] / 100:g}%×{label}', card['description'])


if __name__ == '__main__':
    unittest.main()
