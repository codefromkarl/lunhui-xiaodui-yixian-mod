import itertools
import json
from pathlib import Path
import sys
import unittest
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'scripts'))
from balance_audit import acquisition_cost, audit, legal_pools, miss_probability


class BalanceAuditTests(unittest.TestCase):
    def test_with_replacement_probability_matches_enumeration(self):
        draws = list(itertools.product(range(8), repeat=3))
        measured = sum(all(card >= 2 for card in hand) for hand in draws)/len(draws)
        self.assertEqual(miss_probability(8, 2), measured)
        self.assertEqual(miss_probability(8, 0), 1)
        self.assertEqual(miss_probability(8, 8), 0)

    def test_native_minimum_and_eight_pack_limit(self):
        pools=list(legal_pools(list(range(12))))
        self.assertEqual(len(pools),3718)
        self.assertEqual({len(p) for p in pools},set(range(3,9)))
        self.assertEqual(list(legal_pools([1,2])),[(1,2)])

    def test_only_actual_foundation_choice_is_free(self):
        packs=[dict(key='a',rank=1,price=0),dict(key='b',rank=2,price=40),dict(key='c',rank=2,price=45)]
        self.assertEqual(acquisition_cost(packs,'b'),45)
        self.assertEqual(acquisition_cost(packs,'c'),40)

    def test_catalogue_matrix_covers_all_schools_and_realms(self):
        result=audit()
        self.assertEqual(len(result['rows']),10)
        self.assertEqual(len(result['routes']),30)
        self.assertEqual(result['total_legal_pools'],5455)
        self.assertEqual({r['rank'] for r in result['rows']},set(range(1,6)))
        self.assertEqual([r['candidate_cards'] for r in result['five_fixed_deck']],[8,8,8,12,12])
        self.assertTrue(all(r['deck_size']==8 and r['card_purchase_cost']==0 for r in result['five_fixed_deck']))
        self.assertEqual([r['unlocked_cards'] for r in result['five_fixed_deck']],[8,10,12,14,16])
        self.assertEqual([r['deck_choices'] for r in result['five_fixed_deck']],[1,45,495,3003,12870])
        self.assertFalse(any(r['school']=='five' for r in result['routes']))

    def test_route_history_includes_retired_packs_without_rebuying_them(self):
        routes=audit()['routes']
        for row in routes:
            self.assertGreaterEqual(set(row['historical_route_packs']),set(row['packs']))
            self.assertTrue(all(h>=c for h,c in zip(row['historical_route_cost_by_foundation'],row['purchase_cost_by_foundation'])))
        cloud=next(r for r in routes if r['school']=='sword' and r['rank']==5 and r['route']=='cloud')
        self.assertEqual(cloud['purchase_cost_by_foundation'],[325,325])
        self.assertEqual(cloud['historical_route_cost_by_foundation'],[325,365])

    def test_gift_aware_selection_is_free_for_both_actual_foundation_choices(self):
        root=Path(__file__).resolve().parents[1]
        old_config=json.loads((root/'data/balance-adjusted-scenarios.json').read_text())
        old_config['routes']['star']=json.loads((root/'data/balance-star-routes-scenarios.json').read_text())['routes']['star']
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'old-five-current-star.json';path.write_text(json.dumps(old_config))
            old=audit(config_path=path,legacy_five=True)['routes']
        current=audit(legacy_five=True)['routes']
        for profile in ['water_wood','earth_cycle']:
            row=next(r for r in current if r['school']=='five' and r['rank']==2 and r['route']==profile)
            self.assertEqual(row['purchase_cost_by_foundation'],[0,0])
            self.assertTrue(row['selection_varies_by_foundation'])
            self.assertIn('yixian_five_forge',row['packs_by_foundation'][0])
            self.assertIn('yixian_five_nurture',row['packs_by_foundation'][1])
            for rank in range(2,6):
                a=next(r for r in old if r['school']=='five' and r['rank']==rank and r['route']==profile)
                b=next(r for r in current if r['school']=='five' and r['rank']==rank and r['route']==profile)
                self.assertTrue(all(n<=p for n,p in zip(b['historical_route_cost_by_foundation'],a['historical_route_cost_by_foundation'])))


if __name__=='__main__': unittest.main()
