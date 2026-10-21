"""The deletion is exact; remaining cards, fees and pack identities stay intact."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from sword_retirement import RETIRED_KEYS, RETIRED_SOURCES, PACK_SIZES, REPLACEMENT_KEYS, REPLACEMENT_BY_RETIRED
from check_original_sources import check_sword_costs


def read(path):
    return json.loads((ROOT / path).read_text())


class SwordRetirementTests(unittest.TestCase):
    def test_active_catalog_has_exact_three_removals(self):
        old = read('tests/fixtures/sword-v032/content/cardtable.json')
        source = read('data/sword/cardtable.json')
        self.assertEqual(RETIRED_KEYS, {'yixian_sword_22', 'yixian_sword_102', 'yixian_sword_35'})
        self.assertEqual({c['key'] for c in source['cards']}, ({c['key'] for c in old['cards']} - RETIRED_KEYS) | REPLACEMENT_KEYS)
        self.assertEqual(len(source['cards']), 48)
        self.assertEqual(len(source['packs']), 12)
        before = {p['key']: p for p in old['packs']}
        for pack in source['packs']:
            self.assertEqual(pack['cards'], [REPLACEMENT_BY_RETIRED.get(k, k) for k in before[pack['key']]['cards']])
            for field in ('rank', 'index'):
                self.assertEqual(pack[field], before[pack['key']][field])
            self.assertEqual(pack['price'], read('tests/fixtures/reward-prices.json')['packByRank'][pack['rank'] - 1])
            self.assertEqual(len(pack['cards']), PACK_SIZES.get(pack['key'], 4))
        self.assertEqual([len(p['cards']) for p in source['packs'] if p['rank'] == 1], [4, 4])

    def test_generated_content_and_source_have_no_retired_declarations(self):
        for folder in ('data/sword', 'mod/yixian.sect/content'):
            abilities = read(folder + '/abilities.json')['operations']
            self.assertFalse({o['id'] for o in abilities} & {'yixian.sect.card.' + key for key in RETIRED_KEYS})
        table = read('mod/yixian.sect/content/cardtable.json')
        self.assertEqual(len(table['cards']), 136)
        self.assertFalse(any('狂剑' in c['name'] or 'mad' in c.get('tags', []) or any(k.startswith('mad') for k in c['rules']) for c in table['cards']))
        self.assertFalse(any('狂剑' in p['description'] for p in table['packs']))
        assets = read('mod/yixian.sect/mod.json')['assets']
        self.assertFalse(any(a['file'] in {'icons/cards/' + k + '.png' for k in RETIRED_KEYS} for a in assets.values()))

    def test_archived_cost_evidence_is_preserved_but_reintroduced_card_rejected(self):
        ledger = read('data/sources/original-correction/original-costs.json')['cards']
        self.assertTrue(RETIRED_SOURCES <= set(ledger))
        self.assertEqual(len([sid for sid in ledger if sid.startswith('100')]), 51)
        cards = read('data/sword/cardtable.json')['cards']
        check_sword_costs(cards, ledger)
        deleted = next(c for c in read('tests/fixtures/sword-v032/content/cardtable.json')['cards'] if c['key'] in RETIRED_KEYS)
        with self.assertRaises(AssertionError):
            check_sword_costs(cards + [copy.deepcopy(deleted)], ledger)
        with self.assertRaises(AssertionError):
            check_sword_costs(cards[1:], ledger)


if __name__ == '__main__':
    unittest.main()
