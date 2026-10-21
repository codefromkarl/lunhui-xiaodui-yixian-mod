import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]

class StarSelectedHealingTests(unittest.TestCase):
    def test_only_two_cards_declare_manual_healing_and_native_ally_target(self):
        school=json.loads((ROOT/'data/seven-stars/school.json').read_text())
        chosen=[c for c in school['cards'] if c.get('targetAlly')]
        self.assertEqual({c['name'] for c in chosen},{'气吞山河','气疗术'})
        abilities={r['id']:r for r in json.loads((ROOT/'mod/yixian.sect/content/abilities.json').read_text())['operations']}
        for c in chosen:
            self.assertTrue(c['rules']['selectedAllyHeal'])
            self.assertFalse(c['targetEnemy'])
            op=abilities['yixian.sect.card.'+c['key']]['fields']['ranks'][0]['operation']
            self.assertEqual(op['targetKind'],20)
            self.assertEqual(next(v['intValue'] for v in op['values'] if v['key']=='energyCost'),4*c['rules']['cost'])

    def test_copy_separates_caster_maximum_from_selected_healing(self):
        table=json.loads((ROOT/'mod/yixian.sect/content/cardtable.json').read_text())
        for c in table['cards']:
            if c['name'] not in {'气吞山河','气疗术'}:continue
            self.assertIn('自身本场生命上限',c['description'])
            self.assertIn('指定的一名存活己方角色',c['description'])
            self.assertIn('指定',c['summary'])
            self.assertEqual(c['rules']['maxHealth'],12 if c['name']=='气吞山河' else 8)
            if c['name']=='气吞山河':
                self.assertEqual(c['rules']['postHeal'],24)
                self.assertIn('后招',c['description'])
