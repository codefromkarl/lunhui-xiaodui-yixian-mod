"""Player-facing copy; source evidence and gameplay rules remain in their authored data."""
import json
from pathlib import Path
from sword_areas import area_description

COPY = json.loads((Path(__file__).resolve().parents[1] / 'data/presentation.json').read_text())
COMMON_RULES = '首回合起手3张，之后每回合开始自动摸2张，保留原手牌，满10张不再摸牌。主动摸牌消耗1行动力，出牌不耗行动力；最多启用8包。\n费用以斗气显示；涉及灵气的效果按4斗气＝1灵气计算。恢复不超过上限，属性倍率向下取整。再次行动＝额外摸1张。'


def rules_for(school):
    common = COMMON_RULES.replace('最多启用8包。', '五行固定一个八张卡包；突破解锁两张新牌，战斗中从已解锁牌库免费调整八张。') if school == 'five' else COMMON_RULES
    return common + '\n' + COPY['schoolRules'][school]


def card_summary(card):
    return COPY['cardSummaries'][card['key']]


def pack_detail(pack, by_key):
    return pack['role'] + '\n' + pack['acquisition'] + '\n\n' + '\n\n'.join(
        '【' + by_key[key]['name'] + '】\n' + by_key[key]['description'] for key in pack['cards'])


def compact_sword(table, content):
    """Remove only known shared clauses and duplicate adaptation notes, before emission."""
    for card in table['cards']:
        lines = []
        for line in card['description'].splitlines():
            if line.startswith(('剑意整张消费一次、', '属性采用当前有效属性，', '适配：', '消耗：')):
                continue
            if line.startswith('本牌不消耗、不享受剑意；本场加攻每段'):
                continue  # The preceding card-specific clause already states this exception.
            line = line.replace('，不超过气池上限', '')
            line = line.replace(' 同种持续不叠加；持续数值取施放时属性。', '')
            line = line.replace('再次行动：额外摸 1 张，不耗或返还行动力；手牌最多 10 张。', '额外摸1张。')
            line = line.replace('；手牌最多 10 张', '')
            lines.append(line)
        area = area_description(card)
        if area:
            lines.append(area)
        lines.append(f'消耗：{card["rules"].get("cost", 0) * 4}斗气。')
        card['description'] = '\n'.join(lines)
        card['summary'] = card_summary(card)
    by_key = {card['key']: card for card in table['cards']}
    for ability in content['abilities']['operations']:
        card = by_key.get(ability['id'].rsplit('.', 1)[-1])
        if card and card.get('area'):
            ability['fields']['flavour'] = card['description']
    for pack in table['packs']:
        pack['description'] = pack_detail(pack, by_key)
    for fate in content['fates']['operations']:
        fate['fields']['flavour'] = COPY['fateDescriptions']['sword'][fate['id'].rsplit('.', 1)[-1]] + '\n独立被动，本局限购一次，不占手牌。'
    apply_status_copy('sword', content['statuses']['operations'])
    content['lineages']['operations'][0]['fields']['flavour'] = '云剑连云、剑意爆发、灵剑回气。\n每人最多一个弈仙门派，可搭配原生血统。\n' + rules_for('sword')


def apply_status_copy(school, operations):
    for row in operations:
        key = row['id'].rsplit('.', 1)[-1]
        if key in COPY['statusDescriptions'][school]:
            row['fields']['flavour'] = COPY['statusDescriptions'][school][key]
