"""Player-facing scope for authored full-damage group attacks."""


def describe_group(card):
    group = card.get('groupAttack')
    if not group:
        return []
    scope = '主目标同阵位的所有敌人' if group['shape'] == 'row' else '全体敌人'
    if group.get('waterOnly'):
        return [f'水灵条件攻击覆盖{scope}，每个目标承受完整伤害；木灵条件攻击仍仅主目标。',
                '治疗、增加上限与获得水势不按敌人数增加；次目标不触发锋锐、泉涌或春风收益。']
    condition = '星位时：' if group.get('position') else {2: '水灵条件：', 4: '火灵条件：', 5: '土灵条件：'}.get(group.get('element'), '')
    lines = [f'{condition}本牌每段攻击覆盖{scope}，每个目标承受完整伤害，不分摊；' +
             ('条件未满足时仍仅攻击主目标。' if condition else '不减少主目标原有攻击段数。')]
    if card.get('school') == 'star' or str(card.get('sourceId', '')).startswith('400'):
        if card['rules'].get('randomMax') or card['rules'].get('chanceRepeats'):
            lines.append('每轮伤害／概率只判定一次并供全部目标共享，卦象按轮消耗，回气不按目标数增加。')
    else:
        lines.append('次目标正常结算命中、防御和护盾，不触发锋锐、泉涌或春风收益；削上限仅主目标。')
    return lines
