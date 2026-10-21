"""Presentation acceptance: concise effects without changing rules or source evidence."""
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from presentation_copy import COPY


def read(path):
    return json.loads(path.read_text())


class PresentationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.table = read(ROOT / 'mod/yixian.sect/content/cardtable.json')
        cls.cards = {c['key']: c for c in cls.table['cards']}
        cls.by_name = {c['name']: c for c in cls.table['cards']}

    def test_complete_concise_copy(self):
        self.assertEqual(set(COPY['cardSummaries']), set(self.cards))
        forbidden = ('原牌依据', '原作1级：', '适配说明：', '本Mod换算', '来源差异：', 'RNG', 'SDK', 'hit列表', 'MitigateNextDamage')
        for card in self.cards.values():
            with self.subTest(card=card['name']):
                text = card['description']
                self.assertTrue(card['summary'])
                self.assertLessEqual(len(card['summary']), 80)
                self.assertNotIn('\n', card['summary'])
                self.assertLessEqual(len(text), 300)
                self.assertFalse(any(word in text for word in forbidden))
                self.assertEqual(text.splitlines()[-1], f'消耗：{4 * card["rules"].get("cost", 0)}斗气。')
                self.assertEqual(text.count('消耗：'), 1)

    def test_rules_and_provenance_remain_authored(self):
        for folder, name in [('sword', 'cardtable'), ('five-elements', 'school'), ('seven-stars', 'school')]:
            source = read(ROOT / 'data' / folder / (name + '.json'))
            for card in source['cards']:
                generated = self.cards[card['key']]
                for field in ('rules', 'area', 'sourceId', 'sourceUrl', 'originalText', 'adaptation', 'rank', 'name'):
                    self.assertEqual(generated.get(field), card.get(field), (card['key'], field))
            packs = {p['key']: p for p in self.table['packs']}
            for pack in source['packs']:
                for field in ('cards', 'rank', 'index', 'price'):
                    self.assertEqual(packs[pack['key']][field], pack[field])

    def test_simple_cards_and_missing_condition(self):
        self.assertEqual(self.by_name['水灵印']['description'], '恢复斗气〔8＋14%×能量＋6%×反应〕。\n激活水灵，本场持续生效。\n属性加成取牌面公式与主属性×总系数×70%的较大值；主属性开战锁定。\n消耗：0斗气。')
        self.assertEqual(self.by_name['星弈·虎']['description'], '造成物理伤害〔3＋40%×反应〕 ×3段。\n星位：对方获得1层虚弱；虚弱每个宿主回合末减1层。\n消耗：0斗气。')
        blade = self.by_name['金灵·回锋刃']['description']
        self.assertIn('金灵条件：每次攻击消耗锋锐后返还60%，向上取整，可用于下一段。', blade)
        self.assertIn('锋锐', self.by_name['金灵·回锋刃']['summary'])
        for keyword in ('护盾', '生命', '后招'):
            self.assertIn(keyword, self.by_name['金蝉脱壳']['summary'])

    def test_summaries_do_not_invent_effects(self):
        cases = {'金灵印': ('攻击并激活金灵。', '锋锐'),
                 '木灵印': ('回气并恢复生命，激活木灵。', '加攻'),
                 '木灵·芽': ('双段攻击；木灵条件恢复生命。', '加攻'),
                 '水灵·涛': ('攻击；水灵条件获得水势。', '回气'),
                 '金灵阵': ('获得锋锐；持续使用金灵牌后获得锋锐。', '攻击'),
                 '木灵·疏影': ('双段攻击；木灵条件获得加攻。', '摸牌')}
        for name, (expected, forbidden) in cases.items():
            self.assertEqual(self.by_name[name]['summary'], expected)
            self.assertNotIn(forbidden, self.by_name[name]['summary'])

    def test_unique_restrictions_survive(self):
        cases = {'水灵·波澜': ('水灵条件：本牌不消耗斗气', '消耗：8斗气'),
                 '火灵·燃烬灭': ('火灵条件', '土灵条件', '金灵条件', '三项独立判断'),
                 '木灵·散枝叶': ('之后3次', '不含本张'),
                 '星弈·尖': ('奇数格', '偶数格', '满层仍消耗星力'),
                 '梅开二度': ('结算两次', '只支付一次', '第二次可触发后招'),
                 '万法归灵剑': ('耗尽剩余斗气', '每完整消耗 4 斗气'),
                 '云剑·月华': ('总治疗最多 300',),
                 '连环剑阵': ('不重复触发额外摸牌',)}
        for name, phrases in cases.items():
            for phrase in phrases:
                self.assertIn(phrase, self.by_name[name]['description'], (name, phrase))

    def test_pack_details_and_shared_rules(self):
        for pack in self.table['packs']:
            text = pack['description']
            self.assertNotIn('逐牌对照来源', text)
            self.assertNotIn('图文版差', text)
            for key in pack['cards']:
                self.assertIn('【' + self.cards[key]['name'] + '】\n' + self.cards[key]['description'], text)
        for sect in self.table['sects']:
            self.assertIn('4斗气＝1灵气', sect['rulesDescription'])
            for rule in ('首回合起手3张', '自动摸2张', '满10张'):
                self.assertIn(rule, sect['rulesDescription'])
            self.assertNotIn('攻击溅射', sect['rulesDescription'])
            self.assertNotIn('回合护盾', sect['rulesDescription'])
        self.assertIn('金→水→木→火→土→金', next(s for s in self.table['sects'] if s['statePrefix'] == 'five')['rulesDescription'])
        self.assertIn('默认第3、6格', next(s for s in self.table['sects'] if s['statePrefix'] == 'star')['rulesDescription'])
        for fate in self.table['fates']:
            self.assertNotIn('原作', fate['description'])
            self.assertNotIn('适配', fate['description'])
            self.assertNotIn('不占手牌', fate['description'])  # Added once at the UI/content boundary.

    def test_four_sword_area_descriptions_and_declarations(self):
        cases = {
            '云剑·飞刺': ('连云', '后方至多1名', '50%'),
            '震雷剑': ('主目标击伤', '1格邻近至多2名', '40%', '不再触发击伤'),
            '云剑·无锋': ('未连云时不攻击', '同阵位', '50%'),
            '镜花剑阵': ('护盾', '1格邻接', '30%', '独立判定命中'),
        }
        abilities = {a['fields']['displayName']: a for a in read(ROOT / 'mod/yixian.sect/content/abilities.json')['operations']}
        self.assertEqual({c['name'] for c in self.cards.values() if 'area' in c}, set(cases))
        for name, phrases in cases.items():
            card = self.by_name[name]
            for phrase in phrases:
                self.assertIn(phrase, card['description'])
            self.assertEqual(abilities[name]['fields']['flavour'], card['description'])
            self.assertEqual(abilities[name]['fields']['ranks'][0]['operation']['targetKind'], 50)


if __name__ == '__main__':
    unittest.main()
