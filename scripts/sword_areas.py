"""Authored four-card area adaptation; printed costs and original rules stay untouched."""


def area_description(card):
    area = card.get('area')
    if not area:
        return None
    bp = area['secondaryBp']
    if type(bp) is not int or not 0 < bp <= 10000:
        raise ValueError('次目标比例无效：' + card['key'])
    percent = bp // 100
    shape = area['shape']
    if shape == 'line':
        return f'连云：沿施法者朝主目标方向贯穿后方至多{area["maxTargets"]}名敌人，每段造成本段伤害的{percent}%。'
    if shape == 'splash':
        return f'主目标击伤追加时：向其1格邻近至多{area["maxTargets"]}名敌人各造成追加伤害的{percent}%；不再触发击伤。'
    if shape == 'row':
        return f'连云：横扫主目标同阵位其他敌人，每段造成本段伤害的{percent}%。'
    if shape == 'radius':
        return f'范围：主目标1格邻接的其他敌人，各承受本段伤害的{percent}%，独立判定命中。'
    raise ValueError('未知剑宗攻击形状：' + shape)
