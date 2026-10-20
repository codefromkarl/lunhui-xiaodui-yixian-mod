"""Source-faithful Five Elements clauses; values derive from the actual authored rules."""

def describe_effects(card):
    r = card['rules']
    lines = ['原作1级：' + card['originalText'].replace('\n', '')]
    element_names = {1: '金', 2: '水', 3: '木', 4: '火', 5: '土'}
    if card.get('element'):
        lines.append('元素条件：该行已被灵印／仙命永久激活，或上一张牌同属性／按金→水→木→火→土→金相生时，触发本牌元素效果。普通五行牌不自动永久激活；中性卡打断衔接，不清激活。')
    if r.get('activate'):
        lines.append('主动激活' + element_names[r['activate']] + '灵；本场保持，其后该行效果无条件触发。')
    for key, title in [('sharp', '锋锐'), ('attack', '加攻'), ('water', '水势')]:
        if r.get(key): lines.append(f'获得{r[key]}层{title}。')
        condition = 'conditional' + key.capitalize()
        if r.get(condition): lines.append(f'元素条件：获得{r[condition]}层{title}。')
    if any(r.get(k) for k in ['sharp', 'conditionalSharp', 'conditionalSharpPerShield', 'sharpPerHealth', 'sharpReturnBp']):
        lines.append('锋锐：本派攻击真正击伤时触发并清空，每层额外扣3生命；未击伤不触发也不消费，不是每段加攻。')
    if any(r.get(k) for k in ['attack', 'conditionalAttack', 'conditionalAttackPerQi']):
        lines.append('加攻：每层使本派每次攻击多3攻，本场保持；多段逐段生效。')
    if any(r.get(k) for k in ['water', 'conditionalWater', 'conditionalWaterPerQi', 'conditionalNextWaterPerHealth']):
        lines.append('水势：自身回合结束时每层向对方造成3点非攻击伤害，可被盾抵消；不消费层数、不叠加攻。')
    if r.get('conditionalHeal'):
        base = str(r['conditionalHeal'])
        if r.get('conditionalHealBp'): base += f'＋{r["conditionalHealBp"] / 100:g}%×体质'
        lines.append(f'元素条件：恢复生命〔{base}〕' + (f'，每层加攻另恢复{r["healPerAttack"] * 3}生命。' if r.get('healPerAttack') else '。'))
    if r.get('freeWhenElement'): lines.append('元素条件满足时，本牌免付全部原灵气费用（0斗气），否则按原卡费用支付。')
    if r.get('conditionalPierceNext'): lines.append('元素条件：下一次本派攻击首段无视护盾与物理防御；未出攻击前保留。')
    if r.get('array'):
        title = {1: '锋锐', 2: '灵气', 3: '生命', 4: '对方生命及上限', 5: '护盾'}[r['array']]
        amount = r['arrayGain'] * (1 if r['array'] == 1 else 4 if r['array'] == 2 else 3)
        lines.append(f'持续：此后每次使用{element_names[r["array"]]}灵牌后，' + ('使对方减少' if r['array'] == 4 else '获得') + f'{amount}{title}；多个灵阵不互相冒充，每张新阵不追溯触发自身。')
    if r.get('array') or r.get('activationArray'): lines.append('持续牌：使用后本场不再抽到；本场持续／消耗牌合计最多2张，不能重放已使用的同张持续牌。')
    if r.get('activationArray'): lines.append(f'持续：每次主动激活任意五行时向对方造成{r["activationArray"] * 3}非攻击伤害；重复主动激活亦计次。')
    if r.get('directDamage'): lines.append(f'向对方造成{r["directDamage"]}非攻击物理伤害；可被护盾抵消，不叠加攻。')
    if r.get('lifeLoss'): lines.append(f'对方直接减{r["lifeLoss"] * 3}生命及本场上限，不以普通攻击代替。')
    if r.get('conditionalLifeLoss'): lines.append(f'元素条件：对方直接减{r["conditionalLifeLoss"] * 3}生命及本场上限' + (f'，每层加攻多减{r["lifeLossPerAttack"] * 3}。' if r.get('lifeLossPerAttack') else '。'))
    if r.get('enemyMaxLoss'): lines.append(f'对方本场生命上限减少{r["enemyMaxLoss"] * 3}；不是额外普通伤害。')
    if r.get('maxHealth'): lines.append(f'自身本场生命上限增加{r["maxHealth"] * 3}。')
    if r.get('conditionalMaxHealth'): lines.append(f'元素条件：自身生命及本场上限增加〔{r["conditionalMaxHealth"] * 3}＋{r["maxHealthPerWater"] * 3}×水势〕。')
    if r.get('conditionalSharpPerShield'): lines.append(f'元素条件：每有{r["conditionalSharpPerShield"] * 3}护盾，额外获得1层锋锐。')
    if r.get('sharpPerHealth'): lines.append(f'元素条件：本牌每次攻击每击伤{r["sharpPerHealth"] * 3}生命，获得1层锋锐。')
    if r.get('healPerHealth'): lines.append(f'元素条件：本牌每次攻击每击伤{r["healPerHealth"] * 3}生命，恢复3生命。')
    if r.get('conditionalNextWaterPerHealth'): lines.append(f'元素条件：下一次本派攻击每击伤{r["conditionalNextWaterPerHealth"] * 3}生命，获得1层水势；不是整张多段反复免费触发。')
    if r.get('conditionalWaterPerQi'): lines.append(f'元素条件：每有{r["conditionalWaterPerQi"]}灵气（{r["conditionalWaterPerQi"] * 4}斗气），获得1层水势；包括本牌回气。')
    if r.get('conditionalAttackPerQi'): lines.append(f'元素条件：每有{r["conditionalAttackPerQi"]}灵气（{r["conditionalAttackPerQi"] * 4}斗气），获得1层加攻；包括本牌回气。')
    if r.get('damagePerLostShield'): lines.append(f'元素条件：本场每因实际攻击减少过{r["damagePerLostShield"] * 3}护盾，本牌多3攻。')
    if r.get('shieldPerLostShield'): lines.append(f'元素条件：本场每因实际攻击减少过{r["shieldPerLostShield"] * 3}护盾，本牌多获3盾。')
    if r.get('breakShield'): lines.append('碎防适配：本牌每次真实击中护盾后，追加扣减等量剩余盾；不会预扣未命中的盾，也不把额外减盾转为扣生命。')
    if r.get('noSharp'): lines.append('本牌不触发也不消费锋锐。')
    if r.get('conditionalAgain'): lines.append('元素条件：再次行动，适配为额外摸1张。')
    if r.get('conditionalAgainIfHealed'): lines.append('元素条件且本场已加过生命：再次行动，适配为额外摸1张。')
    if r.get('conditionalAgainEither'): lines.append('金灵或水灵任一条件满足：再次行动，适配为额外摸1张；以打出本牌前的衔接／激活状态判断。')
    if r.get('conditionalReductionBp'): lines.append(f'元素条件：受到的非真实物理／法术伤害减少{r["conditionalReductionBp"] / 100:g}%，持续本回合及下回合；扩大了原作只减攻击的判定范围。')
    if r.get('conditionalReductionFlat'): lines.append(f'元素条件：受到的伤害减少{r["conditionalReductionFlat"] * 3}，最低减至1，持续本回合及下回合。')
    if r.get('maxLossPerHealth'): lines.append(f'元素条件：本牌每次攻击每击伤{r["maxLossPerHealth"] * 3}生命，对方本场上限再减少{r["maxLossMultiplier"] * 3}。')
    if r.get('damagePerDeckElement'): lines.append(f'开战启用卡包中每有一种不同五行，本牌多{r["damagePerDeckElement"] * 3}攻；不是已激活种数。')
    if r.get('doubleHealth'): lines.append('元素条件：本牌每次攻击击伤后追加扣除等量生命，使该次实际扣生命加倍；锋锐计入本次收益。')
    if r.get('conditionalShieldReturn'): lines.append(f'元素条件：下{r["conditionalShieldReturn"]}次受到攻击而减少护盾时，返还减少值；不覆盖自然衰减／支付。')
    if r.get('capAtHealthMinus'): lines.append(f'元素条件：攻击结束后，将对方本场上限降至其当时生命减{r["capAtHealthMinus"] * 3}，不会反而增加上限。')
    if r.get('conditionalSpring'): lines.append(f'元素条件：自身每次实际攻击击中后，加{r["conditionalSpring"] * 3}生命及本场上限，持续{r["duration"]}回合；本张攻击也可触发，未命中不触发。')
    if r.get('sharpReturnBp'): lines.append(f'元素条件：本牌每次攻击消费锋锐后返还{r["sharpReturnBp"] / 100:g}%（向上取整），可用于下一段；无击伤不消费。')
    if r.get('conditionalAgilityPerResource'): lines.append(f'元素条件：每有1水势或1灵气（4斗气），获得{r["conditionalAgilityPerResource"]}身法，本牌最多{r["agilityCap"]}；每累计10身法额外摸1张，余数本场保留。')
    if r.get('conditionalDrainPlays'): lines.append(f'元素条件：下{r["conditionalDrainPlays"]}次使用卡牌后，吸取对方〔{r["drainBase"] * 3}＋{r["drainPerAttack"] * 3}×加攻〕生命；本张不提前消费次数，任何卡牌均可触发。')
    if r.get('conditionalLifeLossPerAttack'): lines.append(f'火灵条件：每层加攻令对方多减{r["conditionalLifeLossPerAttack"] * 3}生命及上限；土灵条件：每{r["earthLossPerShield"] * 3}自身护盾多减3；金灵条件：每{r["metalLossPerSharp"]}锋锐多减3。各元素条件独立判断。')
    if r.get('waterAttack'): lines.append(f'水灵条件：额外〔{r["waterAttack"] * 3}＋{r["waterDamagePerWater"] * 3}×水势〕攻；木灵条件：额外〔{r["woodAttack"] * 3}＋{r["woodDamagePerQi"] * 3}×灵气〕攻，两项可分别触发并受加攻。')
    lines.append('适配说明：' + card['adaptation'])
    return lines

def describe_card(card, formula):
    """Shared compiler's source-backed five-element entry point."""
    r = card['rules']
    lines = []
    for key, label in [('damage', '造成物理攻击伤害'), ('shield', '获得护盾'), ('heal', '恢复生命'), ('qi', '恢复斗气')]:
        if r.get(key) or r.get(key + 'Bp'):
            lines.append(f'{label}〔{formula(r, key)}〕' + (f' ×{r["hits"]}段。' if key == 'damage' and r.get('hits', 1) > 1 else '。'))
    lines.extend(describe_effects(card))
    if r.get('specialDamageBp'):
        lines.append(f'本牌每次条件追加攻击另加{r["specialDamageBp"] / 100:g}%×反应；倍率向下取整。')
    lines.append(f'消耗：0行动力，{r.get("cost", 0) * 4}斗气（原图灵气费用{r.get("cost", 0)}×4；无基础费）；条件免除另见牌面。回气／治疗不超过上限。')
    return '\n'.join(lines)
