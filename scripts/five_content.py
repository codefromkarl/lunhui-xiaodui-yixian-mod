"""Five Elements player copy, generated from actual rules rather than source citations."""
from card_costs import printed_cost
from group_attack_copy import describe_group

ELEMENTS = {1: '金', 2: '水', 3: '木', 4: '火', 5: '土'}


def describe_effects(card):
    r = card['rules']; lines = []
    condition = ELEMENTS.get(card.get('element'), '元素') + ('灵条件：' if card.get('element') else '条件：')
    if r.get('activate'): lines.append('激活' + ELEMENTS[r['activate']] + '灵，本场持续生效。')
    for key, title in [('sharp', '锋锐'), ('attack', '加攻'), ('water', '水势')]:
        if r.get(key): lines.append(f'获得{r[key]}层{title}。')
        conditional = 'conditional' + key.capitalize()
        if r.get(conditional): lines.append(condition + f'获得{r[conditional]}层{title}。')
    if r.get('conditionalHeal'):
        base = str(r['conditionalHeal'])
        if r.get('conditionalHealBp'): base += f'＋{r["conditionalHealBp"] / 100:g}%×体质'
        if r.get('healPerAttack'): base += f'＋{r["healPerAttack"] * 3}×加攻'
        lines.append(condition + f'恢复〔{base}〕生命。')
    if r.get('freeWhenElement'): lines.append(condition + '本牌不消耗斗气。')
    if r.get('conditionalPierceNext'): lines.append(condition + '下一次本派攻击首段无视护盾与物理防御，保留至触发。')
    if r.get('array'):
        title = {1: '锋锐', 2: '斗气', 3: '生命', 4: '对方生命及本场上限', 5: '护盾'}[r['array']]
        amount = r['arrayGain'] * (1 if r['array'] == 1 else 4 if r['array'] == 2 else 3)
        effect = f'对方生命及本场上限−{amount}' if r['array'] == 4 else f'{title}+{amount}'
        lines.append(f'持续：之后每次使用{ELEMENTS[r["array"]]}灵牌后，{effect}；本张不触发新建灵阵。')
    if r.get('array') or r.get('activationArray'): lines.append('本场限用一次，用后不再抽到；本场持续牌最多使用2张。')
    if r.get('activationArray'): lines.append(f'持续：每次主动激活五行，对方受到{r["activationArray"] * 3}非攻击伤害；重复激活也触发。')
    if r.get('directDamage'): lines.append(f'造成{r["directDamage"]}非攻击物理伤害，可被护盾抵消。')
    if r.get('lifeLoss'): lines.append(f'对方直接失去{r["lifeLoss"] * 3}生命及本场上限。')
    if r.get('conditionalLifeLoss'):
        amount = str(r['conditionalLifeLoss'] * 3)
        if r.get('lifeLossPerAttack'): amount += f'＋{r["lifeLossPerAttack"] * 3}×加攻'
        lines.append(condition + f'对方直接失去〔{amount}〕生命及本场上限。')
    if r.get('enemyMaxLoss'): lines.append(f'对方本场生命上限−{r["enemyMaxLoss"] * 3}。')
    if r.get('conditionalMaxHealth'): lines.append(condition + f'自身生命及本场上限增加〔{r["conditionalMaxHealth"] * 3}＋{r["maxHealthPerWater"] * 3}×水势〕。')
    if r.get('conditionalSharpPerShield'): lines.append(condition + f'结算时每有{r["conditionalSharpPerShield"] * 3}护盾，额外获得1锋锐。')
    if r.get('sharpPerHealth'): lines.append(condition + f'每次攻击每击伤{r["sharpPerHealth"] * 3}生命，获得1锋锐。')
    if r.get('healPerHealth'): lines.append(condition + f'每次攻击每击伤{r["healPerHealth"] * 3}生命，恢复3生命。')
    if r.get('conditionalNextWaterPerHealth'): lines.append(condition + f'下一次本派攻击首段每击伤{r["conditionalNextWaterPerHealth"] * 3}生命，获得1水势。')
    if r.get('conditionalWaterPerQi'): lines.append(condition + f'本牌回气后，每有{r["conditionalWaterPerQi"] * 4}斗气，获得1水势。')
    if r.get('conditionalAttackPerQi'): lines.append(condition + f'本牌回气后，每有{r["conditionalAttackPerQi"] * 4}斗气，获得1加攻。')
    if r.get('damagePerLostShield'): lines.append(condition + f'本场每因攻击损失{r["damagePerLostShield"] * 3}护盾，本牌伤害+3。')
    if r.get('shieldPerLostShield'): lines.append(condition + f'本场每因攻击损失{r["shieldPerLostShield"] * 3}护盾，本牌护盾+3。')
    if r.get('breakShield'): lines.append('每段击中护盾后，额外扣减等量剩余护盾，不溢出扣生命。')
    if r.get('noSharp'): lines.append('本牌不触发、不消耗锋锐。')
    if r.get('conditionalAgain'): lines.append(condition + '额外摸1张。')
    if r.get('conditionalAgainIfHealed'): lines.append(condition + '本场已恢复过生命时，额外摸1张。')
    if r.get('conditionalAgainEither'): lines.append('出牌前满足金灵或水灵条件：额外摸1张。')
    if r.get('conditionalReductionBp'): lines.append(condition + f'物理／法术伤害减少{r["conditionalReductionBp"] / 100:g}%，最低减至1，持续本回合及下回合。')
    if r.get('conditionalReductionFlat'): lines.append(condition + f'受到伤害−{r["conditionalReductionFlat"] * 3}，最低减至1，持续本回合及下回合。')
    if r.get('maxLossPerHealth'): lines.append(condition + f'每次攻击每击伤{r["maxLossPerHealth"] * 3}生命，对方本场上限−{r["maxLossMultiplier"] * 3}。')
    if r.get('damagePerDeckElement'): lines.append(f'开战启用卡包中每有一种不同五行，本牌伤害+{r["damagePerDeckElement"] * 3}。')
    if r.get('doubleHealth'): lines.append(condition + '每次攻击实际扣生命加倍，包含锋锐收益。')
    if r.get('conditionalShieldReturn'): lines.append(condition + f'之后{r["conditionalShieldReturn"]}次因攻击损失护盾时，返还损失值。')
    if r.get('capAtHealthMinus'): lines.append(condition + f'攻击后将对方本场上限降至其当时生命−{r["capAtHealthMinus"] * 3}，仅降低不提高。')
    if r.get('conditionalSpring'): lines.append(condition + f'自身每次攻击击中后，生命及本场上限+{r["conditionalSpring"] * 3}，持续{r["duration"]}回合；本张也触发。')
    if r.get('sharpReturnBp'): lines.append(condition + f'每次攻击消耗锋锐后返还{r["sharpReturnBp"] / 100:g}%，向上取整，可用于下一段。')
    if r.get('conditionalAgilityPerResource'): lines.append(condition + f'每1水势或4斗气获得{r["conditionalAgilityPerResource"]}身法，本牌最多{r["agilityCap"]}点。')
    if r.get('conditionalDrainPlays'): lines.append(condition + f'之后{r["conditionalDrainPlays"]}次使用任意卡牌后，吸取〔{r["drainBase"] * 3}＋{r["drainPerAttack"] * 3}×加攻〕生命；不含本张。')
    if r.get('conditionalLifeLossPerAttack'): lines.append(f'火灵条件：每层加攻多减{r["conditionalLifeLossPerAttack"] * 3}生命及上限。\n土灵条件：每{r["earthLossPerShield"] * 3}护盾多减3。\n金灵条件：每{r["metalLossPerSharp"]}锋锐多减3。三项独立判断。')
    if r.get('waterAttack'): lines.append(f'水灵条件：追加〔{r["waterAttack"] * 3}＋{r["waterDamagePerWater"] * 3}×水势〕攻击。\n木灵条件：追加〔{r["woodAttack"] * 3}＋{r["woodDamagePerQi"] * 3}×灵气〕攻击。两项独立判断，均受加攻。')
    return lines


def describe_card(card, formula):
    r = card['rules']; lines = []
    for key, label in [('damage', '造成物理伤害'), ('shield', '获得护盾'), ('heal', '恢复生命'), ('qi', '恢复斗气')]:
        if key == 'heal' and r.get('maxHealth'):
            lines.append(f'自身本场生命上限+{r["maxHealth"] * 3}。')
        if r.get(key) or r.get(key + 'Bp'):
            lines.append(f'{label}〔{formula(r, key)}〕' + (f' ×{r["hits"]}段。' if key == 'damage' and r.get('hits', 1) > 1 else '。'))
    lines.extend(describe_effects(card))
    lines.extend(describe_group(card))
    if r.get('specialDamageBp'): lines.append(f'每次条件追加攻击另加{r["specialDamageBp"] / 100:g}%×反应。')
    lines.append(f'消耗：{printed_cost(r)}斗气。')
    return '\n'.join(lines)
