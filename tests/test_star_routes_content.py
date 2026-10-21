import json
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
class StarRoutesContentTests(unittest.TestCase):
 def test_exact_three_route_inventory_and_no_injury(self):
  school=json.loads((ROOT/'data/seven-stars/school.json').read_text())
  self.assertEqual((len(school['cards']),len(school['packs'])),(40,10))
  self.assertEqual([sum(p['rank']==r for p in school['packs']) for r in range(1,6)],[2]*5)
  keys={c['key'] for c in school['cards']};members=[k for p in school['packs'] for k in p['cards']]
  self.assertEqual(len(members),40);self.assertEqual(set(members),keys)
  forbidden={'internal','triggerInternal','soulCharges','injuryRounds','oddInternal','evenExternal'}
  for c in school['cards']:self.assertTrue(forbidden.isdisjoint(c['rules']))
  self.assertTrue({'internal','external','decrease'}.isdisjoint(c['key'] for c in school['combatStatuses']))
  self.assertTrue({'静气心法','气疗术','枯木逢春'} <= {c['name'] for c in school['cards']})
 def test_group_refund_and_healing_growth_are_explicit(self):
  school=json.loads((ROOT/'data/seven-stars/school.json').read_text());cards={c['name']:c for c in school['cards']}
  self.assertTrue(cards['两仪阵']['rules']['groupOmenSupport'])
  self.assertEqual((cards['两仪阵']['rules']['shieldPerOmen'],cards['两仪阵']['rules']['healPerOmen']),(1,1))
  self.assertTrue(cards['御雷卦诀']['rules']['rankRefundOmen']);self.assertNotIn('refundOmen',cards['御雷卦诀']['rules'])
  self.assertEqual((cards['枯木逢春']['rules']['damage'],cards['枯木逢春']['rules']['healingStep'],cards['枯木逢春']['rules']['damagePerHealing']),(30,15,3))
  self.assertEqual(cards['静气心法']['rules']['calmPerQi'],2)
 def test_removed_cards_and_packs_not_shipped(self):
  table=json.loads((ROOT/'mod/yixian.sect/content/cardtable.json').read_text())
  self.assertTrue({'落花有意','杯弓蛇影','伤魂咒阵','灵蛇绕柱','折枝点穴','魂咒卦诀'}.isdisjoint(c['name'] for c in table['cards']))
  self.assertTrue({'yixian_star_pivot','yixian_star_zenith'}.isdisjoint(p['key'] for p in table['packs']))
  manifest=json.loads((ROOT/'mod/yixian.sect/mod.json').read_text())
  self.assertIn('136',manifest['description'])
