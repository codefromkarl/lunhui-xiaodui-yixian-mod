#!/usr/bin/env python3
"""Compare the water pilot, separating selection changes, mastery and legacy controls."""
import argparse
import json
from pathlib import Path
from compare_balance import compare,metrics
from summarize_balance import summarize


def read(path):return json.loads(Path(path).read_text())

def analyze(before,plain,after):
    combined=compare(before,after)
    mastery=compare(plain,after)
    rows=[r for r in after['rows'] if r['profile']=='water_pilot']
    resource=[]
    for group in summarize(after)['aggregates']:
        if group['profile']!='water_pilot':continue
        sample=[r for r in rows if all(r[k]==group[k] for k in ['rank','policy','fates'])]
        steps=[s for r in sample for t in r['turns'] for s in t['trace'] if 'card' in s]
        resource.append({k:group[k] for k in ['rank','policy','fates','mean_health_damage','mean_plays','mean_free_draws','turns_at_qi_cap','round_stops']}|
            dict(direct_qi_restore=sum(s.get('authored_direct_qi_restore',0) for s in steps),direct_qi_overflow=sum(s.get('authored_direct_qi_overflow',0) for s in steps)))
    costs=[]
    for rank in range(1,6):
        for gift in [0,1]:
            a=[r for r in before['rows'] if r['rank']==rank and r['profile']=='water_pilot' and r['foundation_choice']==gift and not r['fates']]
            b=[r for r in rows if r['rank']==rank and r['foundation_choice']==gift and not r['fates']]
            if not a or not b:raise ValueError('missing rank/foundation cost cohort')
            costs.append(dict(rank=rank,foundation_choice=gift,before=sorted(set(r['pack_spend'] for r in a)),after=sorted(set(r['pack_spend'] for r in b))))
    growth=[f for f in summarize(after)['regression_flags'] if f['profile']=='water_pilot']
    deaths=any(r.get('battle_complete') or r.get('actor_downed') for r in rows)
    capped=any(t['stop']=='play_cap' for r in rows for t in r['turns'])
    losses=[p for p in combined['pairs'] if p['key']['profile']=='water_pilot' and p['delta']['health_loss']<0]
    seed_values={(r['rank'],r['policy'],r['seed'],r['foundation_choice'],r['fates']):metrics(r)['health_loss'] for r in rows}
    seed_cliffs=[]
    for key,damage in sorted(seed_values.items()):
        prior=(key[0]-1,)+key[1:]
        if prior in seed_values and damage<.75*seed_values[prior]:seed_cliffs.append(dict(rank=key[0],policy=key[1],seed=key[2],foundation_choice=key[3],fates=key[4],before=seed_values[prior],after=damage))
    acceptance=dict(pairing_and_unchanged_controls=combined['status']=='passed' and mastery['status']=='passed',
        pure_water_pool_counts=all(len(r['draw_pool'])==[2,3,5,7,10][r['rank']-1] for r in rows),
        no_cost_increase_each_gift_and_rank=all(max(c['after'])<=min(c['before']) for c in costs),
        no_aggregate_output_growth_cliffs_over_25_percent=not growth,no_play_cap_or_terminal=not(capped or deaths))
    return dict(scope='same four seeds, both gifts fully crossed; mastery was added after observing plain pilot; no independent holdout or win-rate claim',
        acceptance=acceptance,costs=costs,resources=resource,growth_flags=growth,seed_growth_cliffs=seed_cliffs,balance_status="needs_follow_up" if seed_cliffs or losses else "candidate_passed",
        per_seed_output_losses=losses,combined=combined,mastery_only=mastery)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    for name in ['before','plain','after','out']:p.add_argument('--'+name,type=Path,required=True)
    args=p.parse_args();result=analyze(read(args.before),read(args.plain),read(args.after))
    args.out.parent.mkdir(parents=True,exist_ok=True);args.out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(result['acceptance'],ensure_ascii=False));print('combined',result['combined']['matches'],'pairs; unchanged',result['combined']['unchanged_pool_pairs'])
    for row in result['resources']:
        if row['policy']=='setup_resource' and not row['fates']:print(row)
    return 0 if all(result['acceptance'].values()) else 1
if __name__=='__main__':raise SystemExit(main())
