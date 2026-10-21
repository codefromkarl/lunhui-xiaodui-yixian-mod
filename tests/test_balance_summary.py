from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from summarize_balance import summarize


class BalanceSummaryTests(unittest.TestCase):
    def sample(self,rank,plays,damage,fates=False):
        return dict(status='completed',school='five',rank=rank,profile='water',policy='setup_resource',fates=fates,
            seed=1,plays=plays,manual_draws=2,free_draws=1,pack_spend=45,fate_spend=0,qi_cap=16,
            player_phase_health_damage=damage,turns=[dict(plays=plays,stop='empty_hand',qi_end=16,
            between_round_health_damage=9,trace=[dict(card='test',quoted_cost=4,qi_before=8,qi_after=12)])])

    def test_counts_round_end_damage_and_separates_fates(self):
        result=summarize({'rows':[self.sample(1,3,21),self.sample(1,5,30,True)]})
        self.assertEqual(len(result['aggregates']),2)
        plain=next(r for r in result['aggregates'] if not r['fates'])
        self.assertEqual(plain['mean_health_damage'],30)
        self.assertEqual(plain['mean_player_phase_damage'],21)
        self.assertEqual(plain['net_qi_from_card_effects'],8)

    def test_errors_are_not_silently_averaged_as_zero(self):
        result=summarize({'rows':[dict(status='error',error='command rejected'),self.sample(1,3,21)]})
        self.assertEqual(len(result['errors']),1)
        self.assertEqual(result['aggregates'][0]['samples'],1)

    def test_regression_flag_uses_predeclared_threshold(self):
        result=summarize({'rows':[self.sample(1,8,100),self.sample(2,5,30)]})
        self.assertEqual({r['metric'] for r in result['regression_flags']},{'mean_plays','mean_health_damage'})


if __name__=='__main__':unittest.main()
