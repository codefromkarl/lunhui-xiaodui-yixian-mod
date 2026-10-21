import copy
from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from compare_balance import compare, refinement_checks


class ComparisonTests(unittest.TestCase):
    def report(self):
        turn=dict(plays=3,stop='empty_hand',qi_end=8,mechanics=dict(active=0),trace=[])
        row=dict(school='five',rank=3,profile='earth_cycle',policy='setup_resource',seed=91001,foundation_choice=0,fates=False,
                 status='completed',turns=[copy.deepcopy(turn) for _ in range(4)],packs=['a','b','c'],plays=12,manual_draws=12,
                 free_draws=0,player_phase_health_damage=100,pack_spend=80,fate_spend=0,qi_cap=16)
        return dict(config=dict(seeds=[91001],rounds=4,policies=['setup_resource']),content_sha256='same-content',rows=[row])

    def test_loadout_intervention_keeps_cost_and_per_seed_delta(self):
        before=self.report();after=copy.deepcopy(before);after['rows'][0].update(packs=['a','b','d'],pack_spend=120,player_phase_health_damage=160)
        result=compare(before,after)
        self.assertEqual(result['status'],'passed')
        self.assertEqual(result['pairs'][0]['delta']['health_loss'],60)
        self.assertEqual(result['pairs'][0]['delta']['pack_spend'],40)

    def test_unchanged_pool_outcome_drift_is_a_failure(self):
        before=self.report();after=copy.deepcopy(before);after['rows'][0]['plays']=13
        result=compare(before,after)
        self.assertEqual(result['status'],'failed')
        self.assertIn('plays',result['unexpected_control_changes'][0]['fields'])

    def test_refinement_detects_a_cliff_moved_to_the_next_rank(self):
        flag=dict(school='five',rank=3,profile='water_wood',policy='setup_resource',fates=False,metric='mean_health_damage')
        result=refinement_checks([],[],[flag])
        self.assertFalse(result['no_new_growth_flags'])
        self.assertEqual(result['new_growth_flags'],[flag])
        self.assertIsNone(result['water_wood_rank5_output_improved_all_observed_policies_and_fates'])
        self.assertIsNone(result['target_rank2_aggregate_output_cliffs_removed'])

    def test_gift_path_cliff_is_reported_even_when_aggregate_improves(self):
        before=self.report();before['rows']=[]
        for rank in [1,2]:
            for gift in [0,1]:
                row=self.report()['rows'][0]
                row.update(rank=rank,profile='water_wood',seed=91001+gift,foundation_choice=gift,
                           player_phase_health_damage=100 if rank==1 else [150,60][gift])
                before['rows'].append(row)
        result=compare(before,copy.deepcopy(before),'refinement')
        self.assertTrue(result['acceptance']['target_rank2_aggregate_output_cliffs_removed'])
        self.assertFalse(result['acceptance']['target_rank2_foundation_paths_all_cliffs_removed'])
        self.assertEqual(result['foundation_path_growth_flags_after'][0]['foundation_choice'],1)

    def test_refinement_does_not_hide_a_policy_regression_in_the_mean(self):
        rows=[dict(school='five',rank=5,profile='water_wood',policy=policy,fates=False,
                   before=dict(health_loss=100),after=dict(health_loss=damage))
              for policy,damage in [('hand_order',90),('setup_resource',200)]]
        self.assertFalse(refinement_checks(rows,[],[])['water_wood_rank5_output_improved_all_observed_policies_and_fates'])

    def test_missing_duplicate_terminal_and_changed_conditions_are_rejected(self):
        before=self.report()
        for mutate in [lambda r:r['rows'].clear(),lambda r:r['rows'].append(copy.deepcopy(r['rows'][0])),
                       lambda r:r['rows'][0]['turns'].pop(),lambda r:r['config'].update(rounds=3),
                       lambda r:r.update(content_sha256='changed')]:
            after=copy.deepcopy(before);mutate(after)
            with self.assertRaises(ValueError):compare(before,after)

    def test_explicit_water_mastery_change_is_not_an_unchanged_pool_control(self):
        before=self.report();after=copy.deepcopy(before)
        for r in [before,after]:
            r['rows'][0]['profile']='water_pilot'
            r['config']['pureWaterProfiles']=['water_pilot']
        before['water_mastery_multiplier']=1;after['water_mastery_multiplier']=2
        after['rows'][0]['player_phase_health_damage']=160
        result=compare(before,after)
        self.assertEqual(result['status'],'passed')
        self.assertTrue(result['pairs'][0]['rule_intervention'])
        self.assertFalse(result['pairs'][0]['packs_changed'])
        self.assertEqual(result['unchanged_pool_pairs'],0)

    def test_water_mastery_does_not_excuse_drift_in_a_mixed_control(self):
        before=self.report();after=copy.deepcopy(before)
        before['water_mastery_multiplier']=1;after['water_mastery_multiplier']=2
        after['config']['pureWaterProfiles']=['water_pilot']
        after['rows'][0]['player_phase_health_damage']=160
        self.assertEqual(compare(before,after)['status'],'failed')
