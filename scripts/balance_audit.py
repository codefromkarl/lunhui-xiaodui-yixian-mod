#!/usr/bin/env python3
"""Audit catalogue economics and legal pools; does not simulate combat or install a Mod."""
import argparse
import itertools
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REALMS = ['炼气', '筑基', '金丹', '元婴', '化神']


def miss_probability(total, useful, draws=3):
    """Native pool draws are uniform with replacement, not a finite deck."""
    if total <= 0 or not 0 <= useful <= total or draws < 0:
        raise ValueError('invalid pool or draw count')
    return ((total - useful) / total) ** draws


def legal_pools(packs):
    minimum = min(3, len(packs))
    for size in range(minimum, min(8, len(packs)) + 1):
        yield from itertools.combinations(packs, size)


def acquisition_cost(selected, foundation):
    return sum(p['price'] for p in selected if p['rank'] > 1 and p['key'] != foundation)


def selected_route(packs, priority):
    lookup = {p['key']: p for p in packs}
    return [lookup[k] for k in priority if k in lookup][:3]


def configured_route(config,school,route,rank,packs,foundation):
    priority=config['routes'][school][route]
    first=config.get('rankPriorities',{}).get(school,{}).get(route,{}).get(str(rank),[])
    assert len(first)==len(set(first)) and set(first)<=set(priority), 'invalid rank priority'
    priority=list(dict.fromkeys(first+priority))
    if rank==2 and route in config.get('reuseFoundationGiftRoutes',{}).get(school,[]):
        owned={p['key'] for p in packs if p['rank']==1 or p['key']==foundation}
        priority=[k for k in priority if k in owned]+[k for k in priority if k not in owned]
    return selected_route([p for p in packs if p['rank']<=rank],priority)


def pool_metrics(packs, cards):
    pool = [cards[k] for p in packs for k in p['cards']]
    free = sum(c['rules'].get('cost', 0) == 0 for c in pool)
    seals = {c['rules'].get('activate') for c in pool if c['rules'].get('activate') in range(1, 6)}
    elements = {c.get('element') for c in pool if c.get('element') in range(1, 6)}
    return dict(packs=[p['key'] for p in packs], cards=len(pool), printed_free_cards=free,
                mean_printed_qi=sum(4*c['rules'].get('cost', 0) for c in pool)/len(pool),
                no_printed_free_in_opening=miss_probability(len(pool), free),
                seal_elements=sorted(seals), represented_elements=sorted(elements))


def audit(root=ROOT, config_path=None, legacy_five=False):
    table = json.loads((root/'mod/yixian.sect/content/cardtable.json').read_text())
    config = json.loads((config_path or root/'data/balance-star-routes-scenarios.json').read_text())
    lineages = {op['id']: op['fields'] for op in json.loads((root/'mod/yixian.sect/content/lineages.json').read_text())['operations']}
    cards = {c['key']: c for c in table['cards']}
    rows, routes, five_deck = [], [], []
    for school in ['sword', 'five', 'star']:
        identity = 'yixian.sect.lineage.' + school
        packs = [p for p in table['packs'] if p.get('sect', 'yixian.sect.lineage.sword') == identity]
        assert len(packs) == (10 if school=='star' else 12) and len({k for p in packs for k in p['cards']}) == (40 if school=='star' else 48)
        if school == 'five' and not legacy_five:
            for rank in range(1, 6):
                count = sum(c.get('sect') == identity and c['rank'] == rank for c in table['cards'])
                steps = lineages[identity]['steps'][:rank]
                five_deck.append(dict(rank=rank, realm=REALMS[rank-1], deck_size=8,
                    candidate_cards=count, unlocked_cards=8+2*(rank-1), new_unlocks=8 if rank == 1 else 2,
                    unlock_choices=1 if rank == 1 else math.comb(count,2), deck_choices=math.comb(8+2*(rank-1),8),
                    rank_step_cost=steps[-1]['price'], rank_total_cost=sum(s['price'] for s in steps), card_purchase_cost=0))
            continue
        for priority in config['routes'][school].values():
            assert set(priority) == {p['key'] for p in packs}, (school, 'route is not a permutation')
        foundation = [p for p in packs if p['rank'] == 2]
        for rank in range(1, 6):
            available = [p for p in packs if p['rank'] <= rank]
            metrics = [pool_metrics(pool, cards) for pool in legal_pools(available)]
            steps = lineages[identity]['steps'][:rank]
            paid = sum(p['price'] for p in available)
            gift_values = [0] if rank == 1 else [p['price'] for p in foundation]
            rows.append(dict(school=school, rank=rank, realm=REALMS[rank-1],
                rank_step_cost=steps[-1]['price'], rank_total_cost=sum(s['price'] for s in steps),
                paid_pack_total=paid, all_packs_cost_after_gift=[paid-g for g in gift_values],
                available_packs=len(available), gifts_owned=2 if rank == 1 else 3,
                legal_pool_count=len(metrics),
                no_printed_free_pool_count=sum(m['printed_free_cards']==0 for m in metrics),
                worst_opening_paid_probability=max(m['no_printed_free_in_opening'] for m in metrics),
                no_seal_pool_count=sum(not m['seal_elements'] for m in metrics) if school=='five' else None,
                no_free_examples=[m for m in metrics if m['printed_free_cards']==0][:3],
                no_seal_examples=[m for m in metrics if not m['seal_elements']][:3] if school=='five' else []))
            for route, priority in config['routes'][school].items():
                choices=[p['key'] for p in foundation] if rank>1 else [None]
                variants=[configured_route(config,school,route,rank,packs,gift) for gift in choices]
                selected = variants[0]
                r = dict(school=school, rank=rank, route=route, **pool_metrics(selected, cards))
                r['pool_metrics_by_foundation']=[pool_metrics(pool,cards) for pool in variants]
                r['packs_by_foundation']=[[p['key'] for p in pool] for pool in variants]
                r['selection_varies_by_foundation']=any(pool!=variants[0] for pool in variants)
                r['purchase_cost_by_foundation'] = [acquisition_cost(pool,gift) for pool,gift in zip(variants,choices)]
                # Fixtures buy a final selection; campaigns also pay for retired
                # lower-rank packs acquired while following the same route.
                histories=[{p['key']:p for tier in range(1,rank+1)
                            for p in configured_route(config,school,route,tier,packs,gift)} for gift in choices]
                r['historical_route_packs'] = list(histories[0])
                r['historical_route_packs_by_foundation']=[list(history) for history in histories]
                r['historical_route_cost_by_foundation'] = [acquisition_cost(history.values(),gift) for history,gift in zip(histories,choices)]
                r['foundation_seals_missing'] = sorted(set(r['represented_elements'])-set(r['seal_elements'])) if school=='five' else []
                routes.append(r)
    return dict(schema=2, five_fixed_deck=five_deck, historical_five_pack_model=legacy_five, scope='catalogue facts; printed fees ignore conditional discounts; no combat or win-rate model',
                draw_model='uniform with replacement; 3 opening cards', rows=rows, routes=routes,
                total_legal_pools=sum(r['legal_pool_count'] for r in rows))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, default=ROOT/'data/balance-star-routes-scenarios.json')
    parser.add_argument('--out', type=Path, default=ROOT/'logs/balance/catalog.json')
    parser.add_argument('--legacy-five', action='store_true', help='historical five-pack catalogue only; not current progression legality')
    args = parser.parse_args()
    result = audit(config_path=args.config, legacy_five=args.legacy_five)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n')
    print(f"Audited {len(result['rows'])} realm/school rows, {result['total_legal_pools']} legal pools, {len(result['routes'])} theme configurations")
    for row in result['rows']:
        print(row['school'], row['realm'], 'rank total', row['rank_total_cost'], 'all packs', row['all_packs_cost_after_gift'],
              'no-free pools', row['no_printed_free_pool_count'], 'no-seal pools', row['no_seal_pool_count'])
    for row in result['five_fixed_deck']:
        print('five', row['realm'], 'deck', row['deck_size'], 'candidates', row['candidate_cards'], 'unlocked', row['unlocked_cards'], 'card cost', row['card_purchase_cost'])
    print('Report:', args.out)


if __name__ == '__main__':
    main()
