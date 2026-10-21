import json
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
class WaterPilotContentTests(unittest.TestCase):
 def test_single_purchase_catalogue_preserves_authored_cards_and_cost_envelope(self):
  table=json.loads((ROOT/'mod/yixian.sect/content/cardtable.json').read_text())
  cards=[c for c in table['cards'] if c.get('sect')=='yixian.sect.lineage.five']
  ops=json.loads((ROOT/'mod/yixian.sect/content/abilities.json').read_text())['operations']
  singles={o['id']:o['fields'] for o in ops if o['id'].startswith('yixian.sect.selection.')}
  self.assertEqual(len(singles),48)
  paid=[(c,singles['yixian.sect.selection.'+c['key']]) for c in cards if c['element']==2 and c['rank']>1]
  self.assertEqual(len(paid),8)
  self.assertEqual(sum(f['purchasePrice'] for c,f in paid),170)
  self.assertEqual([sum(f['purchasePrice'] for c,f in paid if c['rank']<=r) for r in range(1,6)],[0,15,45,95,170])
  for c in cards:
   f=singles['yixian.sect.selection.'+c['key']]
   self.assertEqual(f['acquisition'],10 if c['element']==2 and c['rank']>1 else 20)
   self.assertEqual(f['ranks'][0]['rank'],'DCBAS'[c['rank']-1])
   self.assertEqual(f['iconKey'],'icon.yixian.sect.card.'+c['key'])
 def test_build_copies_exact_pilot_configuration(self):
  self.assertEqual(json.loads((ROOT/'data/five-water-pilot.json').read_text()),json.loads((ROOT/'mod/yixian.sect/content/five-water-pilot.json').read_text()))
