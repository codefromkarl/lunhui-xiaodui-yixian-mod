"""Only the explicit attribute references change; spirit conversion is a real debit."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
def read(path): return json.loads((ROOT / path).read_text())

class SwordScalingTests(unittest.TestCase):
    def test_exact_source_and_generated_attribute_contract(self):
        fixture = read('tests/fixtures/sword-scaling.json')
        source = read('data/sword/cardtable.json')['cards']
        generated = {c['key']: c for c in read('mod/yixian.sect/content/cardtable.json')['cards']}
        for card in source:
            for key, value in card['rules'].items():
                self.assertEqual(generated[card['key']]['rules'][key], value)
            for key, change in fixture['attributeChanges'].get(card['key'], {}).items():
                self.assertEqual(card['rules'][key], change['after'])
        self.assertEqual(len(fixture['attributeChanges']), 33)
        self.assertEqual(sum(map(len, fixture['attributeChanges'].values())), 45)

    def test_nine_spirit_cards_use_energy_including_old_reaction_cards(self):
        spirit = set(read('tests/fixtures/sword-scaling.json')['spiritCards'])
        self.assertEqual(spirit, {'yixian_sword_'+str(i) for i in (29,36,59,65,103,43,45,94,107)})
        for c in read('data/sword/cardtable.json')['cards']:
            for key, value in c['rules'].items():
                if key.endswith('Attr'):
                    if c['key'] in spirit: self.assertEqual(value, 40, (c['name'],key))
                    else: self.assertIn(value, (10,20,30), (c['name'],key))
        cards = {c['key']:c for c in read('data/sword/cardtable.json')['cards']}
        self.assertEqual(cards['yixian_sword_1']['rules']['damageAttr'],10)
        self.assertEqual(cards['yixian_sword_91']['rules']['damageAttr'],20)
        self.assertEqual(cards['yixian_sword_43']['rules']['damageAttr'],40)
        self.assertEqual(cards['yixian_sword_94']['rules']['damageAttr'],40)
        self.assertEqual(cards['yixian_sword_8']['rules']['nextShieldAttr'],30)

    def test_training_is_a_twenty_point_native_passive_not_a_card_or_fate(self):
        expected=read('data/sword/training.json')['operations'][0]
        self.assertEqual(expected['id'],'yixian.sect.training.sword.spirit_conversion')
        fields=expected['fields']
        self.assertEqual((fields['mode'],fields['acquisition'],fields['purchasePrice']),(20,10,20))
        self.assertEqual([r['rank'] for r in fields['ranks']],list('DCBAS'))
        self.assertEqual(fields['flavour'], '')  # Native detail already shows the operation description once.
        code=(ROOT/'src/Sects/Sword/SpiritConversion.cs').read_text()
        self.assertIn('各扣除60%',code)
        self.assertIn('实际扣除量合计转为能量',code)
        actual=next(o for o in read('mod/yixian.sect/content/abilities.json')['operations'] if o['id']==expected['id'])
        self.assertEqual(actual,expected)
        self.assertEqual(len(read('mod/yixian.sect/content/fates.json')['operations']),12)
        self.assertEqual(len(read('mod/yixian.sect/content/cardtable.json')['cards']),136)

    def test_source_formula_copy_has_no_stale_attributes(self):
        for c in read('data/sword/cardtable.json')['cards']:
            for key, value in c['rules'].items():
                if key.endswith('Attr'):
                    bp=c['rules'][key[:-4]+'Bp']
                    attr={10:'筋肉',20:'反应',30:'体质',40:'能量'}[value]
                    self.assertIn(f'{bp/100:g}%×{attr}',c['description'],(c['name'],key))

    def test_sword_original_assets_cache_raw_sources_for_reproducible_conversion(self):
        import sys
        sys.path.insert(0,str(ROOT/'scripts'))
        from prepare_assets import source_rows
        sword=[r for r in source_rows() if str(r.get('sourceId','')).startswith('100')]
        self.assertEqual(len(sword),48)
        for row in sword:
            self.assertIn({'path':'data/sources/original-correction/card-images/'+row['sourceId']+'.webp','format':'raw'},row['outputs'])
