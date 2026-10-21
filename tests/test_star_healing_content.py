"""All seven-star healing paths expose the same injured-ally target contract."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class StarHealingContentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.table = json.loads((ROOT / 'mod/yixian.sect/content/cardtable.json').read_text())

    def test_every_healing_card_names_random_injured_ally(self):
        expected = {'藕断丝连', '气贯长虹', '两仪阵', '螳螂捕蝉', '金蝉脱壳',
                    '星轨推衍', '星弈·长', '星弈·连', '梅开二度', '气吞山河', '静气心法', '气疗术'}
        healing = [c for c in self.table['cards'] if c.get('sect') == 'yixian.sect.lineage.star'
                   and any(c['rules'].get(k) for k in ('heal', 'postHeal', 'healPerOmen',
                                                      'consumeAllOmen', 'positionMaxHeal', 'astralLinks', 'calmPerQi', 'randomHealMax'))]
        self.assertEqual({c['name'] for c in healing}, expected)
        for card in healing:
            target = '全体存活己方角色' if card['name']=='两仪阵' else '指定的一名存活己方角色' if card.get('targetAlly') else '随机一名存活己方角色（优先未满血）'
            self.assertIn(target, card['description'], card['name'])
            self.assertTrue(any('治疗' in s for s in card['adaptation']))
            self.assertIn('治疗' if card['name'] in ('两仪阵','静气心法','气疗术') else '存活队友', card['summary'])

    def test_common_rules_and_fates_explain_target_and_self_maximum(self):
        rules = next(s for s in self.table['sects'] if s['id'] == 'yixian.sect.lineage.star')['rulesDescription']
        for text in ('未满血存活己方角色', '包含自身', '全部满血仍随机治疗', '选人不消耗卦象', '包含溢出'):
            self.assertIn(text, rules)
        fates = {f['name']: f for f in self.table['fates'] if f['sect'] == 'yixian.sect.lineage.star'}
        self.assertIn('随机一名存活己方角色（优先未满血）', fates['后发制人']['description'])
        self.assertIn('自身', fates['后发制人']['description'])
        self.assertIn('自身本场生命上限', fates['妙手回春']['description'])


if __name__ == '__main__':
    unittest.main()
