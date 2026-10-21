"""Permanent unlocked library and a separate active eight-card deck."""
import json
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]

class FiveDeckContentTests(unittest.TestCase):
 def test_one_deck_no_card_or_pack_shop(self):
  ops=json.loads((ROOT/'mod/yixian.sect/content/abilities.json').read_text())['operations']
  five=[o for o in ops if o['fields']['sourceContentId']=='yixian.sect.lineage.five']
  self.assertTrue(all(o['fields']['acquisition']==20 and o['fields']['purchasePrice']==0 for o in five))
  tokens=[o for o in five if o['id'].startswith('yixian.sect.deckcard.')]
  self.assertEqual(len(tokens),48)
  unlocked=[o for o in five if o['id'].startswith('yixian.sect.unlockedcard.')]
  self.assertEqual(len(unlocked),48)
  self.assertEqual({o['id'].removeprefix('yixian.sect.unlockedcard.') for o in unlocked},{o['id'].removeprefix('yixian.sect.deckcard.') for o in tokens})
  self.assertEqual(len([o for o in five if o['id']=='yixian.sect.pack.five_deck']),1)
  self.assertFalse(any(o['id'].startswith('yixian.sect.selection.') for o in ops))
  table=json.loads((ROOT/'mod/yixian.sect/content/cardtable.json').read_text())
  cards=[c for c in table['cards'] if c.get('sect')=='yixian.sect.lineage.five']
  self.assertEqual([sum(c['rank']==r for c in cards) for r in range(1,6)],[8,8,8,12,12])
  self.assertEqual({t['id'].removeprefix('yixian.sect.deckcard.') for t in tokens},{c['key'] for c in cards})
  for c in cards:
   old=json.loads((ROOT/'data/five-elements/school.json').read_text())
   authored=next(x for x in old['cards'] if x['key']==c['key'])
   self.assertEqual(c['rules'],authored['rules'])
 def test_retired_mastery_not_shipped(self):
  self.assertFalse((ROOT/'mod/yixian.sect/content/five-water-pilot.json').exists())
  rules=(ROOT/'src/Sects/FiveElements/FiveElementRules.cs').read_text()
  self.assertNotIn('waterMastery',rules)
  text=(ROOT/'src/Sects/FiveElements/FiveDeckRules.cs').read_text()
  self.assertIn('public const int Size = 8;',text)
  self.assertIn('public const int UnlocksPerRank = 2;',text)
