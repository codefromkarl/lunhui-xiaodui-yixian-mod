"""Card rules.cost is printed lingqi; only that cost is converted to fighting qi."""
import re
from identity_ids import card_id

QI_PER_LINGQI = 4


def printed_cost(rules):
    cost = rules.get('cost', 0)
    if type(cost) is not int or not 0 <= cost <= 2147483647 // QI_PER_LINGQI:
        raise ValueError('单卡灵气费用必须为非负整数，且换算后不能溢出。')
    return cost * QI_PER_LINGQI


def cost_text(value):
    """Update only the retired base-fee wording; preserve unrelated rules/copy."""
    replacements = {
        '持续：灵剑牌的灵气附加费用减少 4，最低 0；仍付基础 2 斗气。': '持续：灵剑牌的斗气费用减少 4，最低 0。',
        '出牌前有护盾时，免去灵气附加费；仍付基础 2 斗气。': '出牌前有护盾时，本牌不消耗斗气。',
        '连云时免去灵气附加费；仍付基础 2 斗气。': '连云时，本牌不消耗斗气。',
        '连云只免灵气附加费，仍付基础斗气。': '连云时本牌不消耗斗气。',
        '不限次数、不赠未购牌、不免基础2斗气。': '不限次数、不赠未购牌；连云减费可将斗气费用降至0。',
        '连云减费仍保留基础2斗气。': '连云减费可将斗气费用降至0。',
        '基础 2 斗气＋灵气附加费': '仅按单卡灵气费用消耗斗气，无额外基础费',
        '出牌耗基础 2 斗气及灵气附加费用': '出牌仅按单卡灵气费用消耗斗气，无额外基础费',
        '出牌0行动力、基础2斗气': '出牌0行动力；无灵气费用的牌不耗斗气，有费用的牌按1灵气＝4斗气换算',
    }
    if isinstance(value, str):
        for old, new in replacements.items():
            value = value.replace(old, new)
        return value
    if isinstance(value, list):
        return [cost_text(x) for x in value]
    if isinstance(value, dict):
        return {k: cost_text(v) for k, v in value.items()}
    return value


def refresh_sword_costs(table, abilities):
    table['playBaseCost'] = 0
    by_id = {a['id']: a for a in abilities['operations']}
    for card in table['cards']:
        cost = printed_cost(card['rules'])
        old = card['description']
        new, count = re.subn(r'^消耗：[^\n]*$',
                            f'消耗：0 行动力，{cost} 斗气（灵气费用 {card["rules"].get("cost", 0)}×4；无基础费）。',
                            old, flags=re.MULTILINE)
        if count != 1:
            raise ValueError('单卡费用说明缺失或重复：' + card['key'])
        card['description'] = new
        for pack in table['packs']:
            if card['key'] in pack['cards']:
                if old not in pack['description']:
                    raise ValueError('卡包详情与单卡不同步：' + card['key'])
                pack['description'] = pack['description'].replace(old, new)
        aid = card_id(card['key'])
        for rank in by_id[aid]['fields']['ranks']:
            for value in rank['operation']['values']:
                if value['key'] == 'energyCost':
                    value['intValue'] = cost
