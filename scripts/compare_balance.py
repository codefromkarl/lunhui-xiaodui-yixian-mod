#!/usr/bin/env python3
"""Pair bounded balance runs; reject mismatched inputs and preserve unchanged-pool controls."""
import argparse
from collections import defaultdict
import json
from pathlib import Path
from statistics import mean
from summarize_balance import summarize

KEYS=('school','rank','profile','policy','seed','foundation_choice','fates')
GROUP=('school','rank','profile','policy','fates')
CONDITIONS=('seeds','rounds','maxPlaysPerRound','baseAttribute','policies','fateSensitivityRanks','schools','ranks','foundationChoices')
OUTCOME=('opening_hand','turns','plays','manual_draws','free_draws','player_phase_health_damage',
         'player_phase_shield_damage','effective_attributes','opening_qi','qi_cap','opening_ap')


def indexed(report):
    rows={}
    for row in report['rows']:
        key=tuple(row[k] for k in KEYS)
        if key in rows:
            raise ValueError('duplicate pair key: '+str(key))
        if row['status']!='completed' or len(row['turns'])!=report['config']['rounds'] or row.get('actor_downed') or row.get('battle_complete'):
            raise ValueError('incomplete/terminal row: '+str(key))
        rows[key]=row
    return rows


def metrics(row):
    turns=row['turns']
    return dict(plays=row['plays'],health_loss=row['player_phase_health_damage']+sum(t.get('between_round_health_damage',0) for t in turns),
                pack_spend=row['pack_spend'],qi_blocked=sum(t['stop']=='qi_blocked' for t in turns),
                play_caps=sum(t['stop']=='play_cap' for t in turns),
                activated=int(any(t.get('mechanics',{}).get('active',0)>0 for t in turns)),
                final_active_elements=int(turns[-1].get('mechanics',{}).get('active',0)).bit_count())


def foundation_growth_flags(report):
    groups=defaultdict(list)
    fields=GROUP+('foundation_choice',)
    for row in report['rows']:
        groups[tuple(row[k] for k in fields)].append(metrics(row)['health_loss'])
    values={key:mean(rows) for key,rows in groups.items()}
    flags=[]
    for key,damage in sorted(values.items()):
        prior=(key[0],key[1]-1)+key[2:]
        if prior in values and damage<.75*values[prior]:
            flags.append(dict(zip(fields,key),metric='mean_health_damage',before=values[prior],after=damage))
    return flags


def refinement_checks(aggregates,flags_before,flags_after):
    targets=[r for r in aggregates if r['school']=='five' and
             (r['rank']==5 and r['profile']=='water_wood' or
              r['rank']==2 and r['profile'] in ('water_wood','earth_cycle'))]
    def improved(rows):
        return all(r['after']['health_loss']>r['before']['health_loss'] for r in rows) if rows else None
    water=[r for r in targets if r['rank']==5]
    foundation=[r for r in targets if r['rank']==2]
    flag_key=lambda r:tuple(r[k] for k in GROUP)+ (r['metric'],)
    old={flag_key(r) for r in flags_before}
    new=[r for r in flags_after if flag_key(r) not in old]
    return dict(water_wood_rank5_output_improved_all_observed_policies_and_fates=improved(water),
                five_rank2_output_improved_all_observed_target_routes_and_policies=improved(foundation),
                no_new_growth_flags=not new,new_growth_flags=new,
                target_rank2_aggregate_output_cliffs_removed=None if not foundation else not any(r['school']=='five' and r['rank']==2 and
                    r['profile'] in ('water_wood','earth_cycle') and r['metric']=='mean_health_damage' for r in flags_after))


def compare(before,after,acceptance_scheme='initial'):
    if before['content_sha256']!=after['content_sha256']:
        raise ValueError('card content changed: this experiment permits loadout changes only')
    for name in CONDITIONS:
        if before['config'].get(name)!=after['config'].get(name):
            raise ValueError('condition mismatch: '+name)
    old,new=indexed(before),indexed(after)
    if not old or set(old)!=set(new):
        raise ValueError('missing or extra pair keys')
    groups=defaultdict(list);pairs=[];unexpected=[];unchanged=0
    for key in sorted(old):
        a,b=old[key],new[key];am,bm=metrics(a),metrics(b)
        packs_changed=a['packs']!=b['packs']
        mastery_changed=before.get('water_mastery_multiplier',1)!=after.get('water_mastery_multiplier',1)
        rule_intervention=mastery_changed and b['school']=='five' and b['rank']>=2 and b['profile'] in after['config'].get('pureWaterProfiles',[])
        inputs_changed=packs_changed or rule_intervention
        if not inputs_changed:
            unchanged+=1
            differences=[name for name in OUTCOME if a.get(name)!=b.get(name)]
            if differences:unexpected.append(dict(key=dict(zip(KEYS,key)),fields=differences))
        row=dict(key=dict(zip(KEYS,key)),packs_before=a['packs'],packs_after=b['packs'],inputs_changed=inputs_changed,packs_changed=packs_changed,rule_intervention=rule_intervention,
                 before=am,after=bm,delta={name:bm[name]-am[name] for name in am})
        pairs.append(row);groups[tuple(b[k] for k in GROUP)].append(row)
    aggregates=[]
    for key,rows in sorted(groups.items()):
        values={side:{name:mean(r[side][name] for r in rows) for name in rows[0][side]} for side in ['before','after']}
        aggregates.append(dict(zip(GROUP,key),samples=len(rows),changed_pairs=sum(r['inputs_changed'] for r in rows),**values,
                               qi_blocked_before=sum(r['before']['qi_blocked'] for r in rows),qi_blocked_after=sum(r['after']['qi_blocked'] for r in rows)))
    lookup={tuple(r[k] for k in GROUP):r for r in aggregates}
    spirit=lookup.get(('sword',5,'spirit','setup_resource',False))
    growth=[]
    for profile,previous,current in [('earth_cycle',2,3),('water_wood',3,4)]:
        for policy in before['config']['policies']:
            a=lookup.get(('five',previous,profile,policy,False));b=lookup.get(('five',current,profile,policy,False))
            if a and b:growth.append(b['after']['health_loss']>=.75*a['after']['health_loss'])
    acceptance=dict(sword_spirit_qi_blocks_halved_and_output_retained=None if not spirit else
                    spirit['qi_blocked_after']<=.5*spirit['qi_blocked_before'] and spirit['after']['health_loss']>=.75*spirit['before']['health_loss'],
                    five_reported_rank3_rank4_cliffs_removed=all(growth) if growth else None,
                    all_growth_flags_eliminated=not summarize(after)['regression_flags'])
    tradeoffs=[{k:r[k] for k in GROUP}|dict(before=r['before']['health_loss'],after=r['after']['health_loss'])
               for r in aggregates if r['changed_pairs'] and r['after']['health_loss']<.75*r['before']['health_loss']]
    flags_before=summarize(before)['regression_flags'];flags_after=summarize(after)['regression_flags']
    gift_flags_before=foundation_growth_flags(before);gift_flags_after=foundation_growth_flags(after)
    if acceptance_scheme=='refinement':
        acceptance=refinement_checks(aggregates,flags_before,flags_after)
        acceptance['target_rank2_foundation_paths_all_cliffs_removed']=None if acceptance['target_rank2_aggregate_output_cliffs_removed'] is None else not any(
            r['school']=='five' and r['rank']==2 and r['profile'] in ('water_wood','earth_cycle') for r in gift_flags_after)
    elif acceptance_scheme!='initial':raise ValueError('unknown acceptance scheme: '+acceptance_scheme)
    return dict(water_mastery_before=before.get('water_mastery_multiplier',1),water_mastery_after=after.get('water_mastery_multiplier',1),scope='paired loadout / explicit pure-water mastery intervention; status checks pairing/control integrity, not overall balance acceptance; prices explicit; not win rate',
                pairs=pairs,aggregates=aggregates,matches=len(pairs),unchanged_pool_pairs=unchanged,
                unexpected_control_changes=unexpected,
                acceptance_scheme=acceptance_scheme,acceptance=acceptance,same_rank_output_losses_over_25_percent=tradeoffs,
                growth_flags_before=flags_before,growth_flags_after=flags_after,
                foundation_path_growth_flags_before=gift_flags_before,foundation_path_growth_flags_after=gift_flags_after,
                status='passed' if not unexpected and all(r['after']['play_caps']==0 for r in pairs) else 'failed')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before',type=Path,required=True);parser.add_argument('--after',type=Path,required=True)
    parser.add_argument('--out',type=Path,required=True)
    parser.add_argument('--acceptance',choices=['initial','refinement'],default='initial');args=parser.parse_args()
    result=compare(json.loads(args.before.read_text()),json.loads(args.after.read_text()),args.acceptance)
    args.out.parent.mkdir(parents=True,exist_ok=True)
    args.out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print(result['status'],result['matches'],'pairs;',result['unchanged_pool_pairs'],'unchanged-pool controls;',len(result['unexpected_control_changes']),'unexpected changes')
    print('Growth flags:',len(result['growth_flags_before']),'->',len(result['growth_flags_after']))
    for r in result['aggregates']:
        if r['changed_pairs'] and r['policy']=='setup_resource' and not r['fates']:
            print(r['school'],r['rank'],r['profile'],'health',r['before']['health_loss'],'->',r['after']['health_loss'],
                  'qi blocks',r['qi_blocked_before'],'->',r['qi_blocked_after'],'spend',r['before']['pack_spend'],'->',r['after']['pack_spend'])
    return 0 if result['status']=='passed' else 1


if __name__=='__main__':raise SystemExit(main())
