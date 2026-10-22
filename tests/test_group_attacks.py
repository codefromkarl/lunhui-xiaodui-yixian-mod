"""Authored full-damage group metadata and generated player copy stay aligned."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class GroupContentTests(unittest.TestCase):
    def test_eight_explicit_group_cards_and_generated_copy(self):
        expected = {'落雷术': ('row', 4), '星弈·打': ('all', 4),
                    '轰雷掣电': ('all', 0), '五雷轰顶': ('all', 4),
                    '水灵·涛': ('row', 8), '土灵·流沙': ('row', 0),
                    '火灵·烈燎原': ('all', 8), '水灵·雨纷纷': ('all', 4)}
        source = [c for folder in ('five-elements', 'seven-stars')
                  for c in json.loads((ROOT / 'data' / folder / 'school.json').read_text())['cards']]
        generated = {c['key']: c for c in json.loads((ROOT / 'mod/yixian.sect/content/cardtable.json').read_text())['cards']}
        selected = [c for c in source if c.get('groupAttack')]
        self.assertEqual({c['name'] for c in selected}, set(expected))
        for card in selected:
            with self.subTest(card=card['name']):
                shape, cost = expected[card['name']]
                self.assertEqual(card['groupAttack']['shape'], shape)
                self.assertEqual(card['rules']['cost'] * 4, cost)
                current = generated[card['key']]
                self.assertEqual(current['groupAttack'], card['groupAttack'])
                self.assertEqual(current['rules'], card['rules'])
                self.assertEqual(current['originalText'], card['originalText'])
                self.assertIn('完整伤害', current['description'])
                self.assertNotIn('maxTargets', card['groupAttack'])
                self.assertNotIn('distinctLightning', card['rules'])
                self.assertNotIn('distributedLightning', card['rules'])
                self.assertNotIn('优先不同存活敌人', current['description'])
                self.assertNotIn('随机选择一名存活敌人', current['description'])
        by_name = {c['name']: c for c in selected}
        self.assertTrue(by_name['星弈·打']['groupAttack']['position'])
        self.assertTrue(by_name['水灵·雨纷纷']['groupAttack']['waterOnly'])
        for name, element in [('水灵·涛', 2), ('土灵·流沙', 5), ('火灵·烈燎原', 4), ('水灵·雨纷纷', 2)]:
            self.assertEqual(by_name[name]['groupAttack']['element'], element)


if __name__ == '__main__':
    unittest.main()
