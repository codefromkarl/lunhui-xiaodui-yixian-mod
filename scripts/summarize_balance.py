#!/usr/bin/env python3
"""Summarize real-command balance probes without treating sandbag output as win rate."""
import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path
from statistics import mean


def summarize(report):
    groups=defaultdict(list)
    errors=[]
    for row in report['rows']:
        if row['status']!='completed':
            errors.append(row);continue
        key=tuple(row[k] for k in ['school','rank','profile','policy','fates'])
        groups[key].append(row)
    aggregates=[]
    for key,rows in sorted(groups.items()):
        school,rank,profile,policy,fates=key
        turns=[t for row in rows for t in row['turns']]
        stops=Counter(t['stop'] for t in turns)
        samples=[step for t in turns for step in t['trace'] if 'card' in step]
        total_damage=[r['player_phase_health_damage']+sum(t.get('between_round_health_damage',0) for t in r['turns']) for r in rows]
        aggregates.append(dict(school=school,rank=rank,profile=profile,policy=policy,fates=fates,
            samples=len(rows),turn_samples=len(turns),mean_plays=mean(r['plays'] for r in rows),
            mean_rounds_observed=mean(len(r['turns']) for r in rows),
            terminal_battles=sum(bool(r.get('battle_complete')) for r in rows),
            downed_actors=sum(bool(r.get('actor_downed')) for r in rows),
            mean_manual_draws=mean(r['manual_draws'] for r in rows),mean_free_draws=mean(r['free_draws'] for r in rows),
            mean_health_damage=mean(total_damage),mean_player_phase_damage=mean(r['player_phase_health_damage'] for r in rows),
            mean_pack_spend=mean(r['pack_spend'] for r in rows),mean_fate_spend=mean(r['fate_spend'] for r in rows),
            mean_quoted_card_cost=mean(s['quoted_cost'] for s in samples) if samples else 0,
            net_qi_from_card_effects=sum(s['qi_after']-s['qi_before']+s['quoted_cost'] for s in samples),
            turns_at_qi_cap=sum(t['qi_end']==r['qi_cap'] for r in rows for t in r['turns']),
            zero_play_openings=sum(r['turns'] and r['turns'][0]['plays']==0 for r in rows),
            round_stops=dict(stops),minimum_plays=min(r['plays'] for r in rows),maximum_plays=max(r['plays'] for r in rows)))
    regressions=[]
    lookup={(r['school'],r['rank'],r['profile'],r['policy'],r['fates']):r for r in aggregates}
    for r in aggregates:
        prior=lookup.get((r['school'],r['rank']-1,r['profile'],r['policy'],r['fates']))
        if not prior:continue
        for metric in ['mean_plays','mean_health_damage']:
            if prior[metric]>0 and r[metric]<0.75*prior[metric]:
                regressions.append(dict(school=r['school'],rank=r['rank'],profile=r['profile'],policy=r['policy'],fates=r['fates'],metric=metric,before=prior[metric],after=r[metric]))
    return dict(scope='small common-seed, bounded real-command probes; health loss includes interval observations, not attribution to a card; no win-rate claim; output depends on policy and actual spend',
                matches=len(report['rows']),errors=errors,aggregates=aggregates,regression_flags=regressions)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('report',type=Path)
    parser.add_argument('--out',type=Path,default=Path('logs/balance/runtime-summary.json'))
    args=parser.parse_args()
    result=summarize(json.loads(args.report.read_text()))
    args.out.parent.mkdir(parents=True,exist_ok=True)
    args.out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print('Matches:',result['matches'],'errors:',len(result['errors']),'groups:',len(result['aggregates']))
    for row in result['aggregates']:
        if row['policy']=='setup_resource' and not row['fates']:
            print(row['school'],row['rank'],row['profile'],'plays',round(row['mean_plays'],2),'damage',round(row['mean_health_damage'],2),'spend',row['mean_pack_spend'],'stops',row['round_stops'])
    print('Regression flags:',len(result['regression_flags']))
    print('Report:',args.out)


if __name__=='__main__': main()
