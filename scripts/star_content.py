"""Seven-star actual effects only; common keywords live in the school introduction."""
from group_attack_copy import describe_group
HEAL_TARGET = '随机一名存活己方角色（优先未满血）'

def heal_text(value, selected=False):
    target = '指定的一名存活己方角色' if selected else HEAL_TARGET
    return f'为{target}恢复〔{value}〕生命。'

def describe_effects(card):
    r = card['rules']; lines = []
    if r.get('maxHealth'): lines.append(f'自身本场生命上限+{r["maxHealth"]*3}，此效果不恢复生命。')
    if r.get('starGain'): lines.append(f'获得{r["starGain"]}星力。')
    if r.get('omenGain'): lines.append(f'获得{r["omenGain"]}卦象。')
    if r.get('randomMax'):
        n = r.get('randomRepeats', 1)
        lines.append(f'随机攻击〔（{r["randomMin"]*3}～{r["randomMax"]*3}）＋' + ('40%×反应' if n > 1 else '100%×能量') + f'〕，独立结算{n}次；卦象逐次生效。')
    if r.get('markNext'): lines.append(f'之后第{r["markNext"]}格成为星位，本场生效；已是星位则恢复4斗气。')
    for k, label, mul in [('positionAttack', '攻击', 3), ('positionDefense', '护盾', 3), ('positionQi', '斗气', 4), ('postAttack', '攻击', 3), ('postHeal', '生命', 3), ('postGuard', '护体', 1)]:
        if r.get(k):
            mode = '星位' if k.startswith('position') else '后招'
            value = str(r[k] * mul)
            if k == 'postHeal': value += '＋50%×体质'
            if k == 'postAttack' and not r.get('damage'): value += '＋100%×能量'
            if k == 'postHeal': lines.append(f'{mode}：{heal_text(value, r.get("selectedAllyHeal", False))}')
            elif label == '攻击': lines.append(f'{mode}：追加一次〔{value}〕攻击。')
            else: lines.append(f'{mode}：' + ('恢复' if label in ('生命', '斗气') else '获得') + f'〔{value}〕{label}。')
    if r.get('positionWeak'): lines.append(f'星位：对方获得{r["positionWeak"]}层虚弱；虚弱每个宿主回合末减1层。')
    if r.get('weakChance'): lines.append(f'{r["weakChance"]}%概率使对方获得1层虚弱；虚弱每个宿主回合末减1层。')
    if r.get('flawChance'): lines.append(f'攻击前，{r["flawChance"]}%概率施加{r["flaw"]}层破绽；破绽每个宿主回合末减1层。')
    if r.get('enemyQi'): lines.append(f'对方恢复{r["enemyQi"]*4}斗气。')
    if r.get('shieldPerOmen'): lines.append(f'全体存活己方角色各获得〔{r["shieldPerOmen"]*3}×当前卦象〕护盾，不消耗卦象。' if r.get('groupOmenSupport') else f'每点当前卦象获得{r["shieldPerOmen"]*3}护盾，不消耗卦象。')
    if r.get('healPerOmen'): lines.append(f'全体存活己方角色各恢复〔{r["healPerOmen"]*3}×当前卦象〕生命，不消耗卦象；满血照常计入治疗量。')
    if r.get('rankRefundOmen'): lines.append('本牌攻击后获得卦象：金丹1／元婴2／化神3；零卦起手也生效，重施逐次返还。')
    if r.get('hexFormation'): lines.append(f'持续：每实际获得1卦象，造成{r["hexFormation"]*3}非攻击伤害，可被护盾抵消。')
    if r.get('enemyMaxHealth') == r.get('enemyLoseHealth') and r.get('enemyMaxHealth'):
        lines.append(f'对方生命及本场上限−{r["enemyMaxHealth"]*3}。')
    else:
        if r.get('enemyMaxHealth'): lines.append(f'对方本场生命上限−{r["enemyMaxHealth"]*3}。')
        if r.get('enemyLoseHealth'): lines.append(f'对方直接失去{r["enemyLoseHealth"]*3}生命。')
    if r.get('consumeAllOmen'): lines.append(f'耗尽卦象，每点转为1星力、恢复4斗气；{heal_text("3×消耗卦象")}')
    if r.get('qiOnOmen'): lines.append(f'本牌每次随机由卦象取最高值时，恢复{r["qiOnOmen"]*4}斗气。')
    if r.get('positionMaxHeal'): lines.append(f'星位：自身本场生命上限增加〔{r["positionMaxHeal"]*3}＋{r["maxHealPerStar"]*3}×当前星力〕，随后为{HEAL_TARGET}恢复等量生命。')
    if r.get('astralLinks'): lines.append(f'之后{r["astralLinks"]}次使用星弈牌：当前格是星位则+1星力，否则变为星位，并为{HEAL_TARGET}恢复15生命。')
    if r.get('againChance'): lines.append(f'{r["againChance"]}%概率额外摸1张。')
    if r.get('chanceRepeats'): lines.append(f'独立判定{r["chanceRepeats"]}次：每次{r["attackChance"]}%概率造成〔{r["chanceAttack"]*3}＋40%×反应〕攻击；卦象逐次生效。')
    if r.get('againAtOmen'): lines.append(f'本牌获得卦象后，卦象≥{r["againAtOmen"]}时额外摸1张。')
    if r.get('again'): lines.append('额外摸1张。')
    if r.get('postAgain'): lines.append('后招：额外摸1张。')
    if r.get('positionAgain'): lines.append('星位：额外摸1张。')
    if r.get('repeatNext'): lines.append('下一张七星阁牌结算两次，只支付一次、不重复推进牌序；第二次可触发后招，其他牌不消耗此效果。')
    if r.get('positionSkip'): lines.append('星位：对方跳过下一次行动，受控制免疫限制。')
    if r.get('allPositions'): lines.append(f'持续：全部8格成为星位；在第1格使用时，额外获得{r["firstSlotStar"]}星力。')
    if r.get('highestGain'): lines.append(f'比较斗气÷4（向下取整）、卦象、星力：最高项+{r["highestGain"]}，斗气增量×4；同值优先斗气，其次卦象。')
    if r.get('compareQiFlaw'): lines.append(f'本牌回气后，斗气高于对方时施加{r["compareQiFlaw"]}层破绽；破绽每个宿主回合末减1层。')
    if r.get('positionAgainStars'): lines.append(f'星位且星力≥{r["positionAgainStars"]}：额外摸1张。')
    if r.get('bonusChance'):
        if r['bonusHits'] == 1: lines.append(f'{r["bonusChance"]}%概率追加一次〔{r["bonusAttack"]*3}〕攻击。')
        else: lines.append(f'{r["bonusChance"]}%概率追加{r["bonusAttack"]*3}攻击×{r["bonusHits"]}段。')
    if r.get('decreaseChance'): lines.append(f'{r["decreaseChance"]}%概率施加1层减攻，每层攻击−3。')
    if r.get('oddStar'): lines.append(f'奇数格：获得{r["oddStar"]}星力。')
    if r.get('evenStarGuard'): lines.append(f'偶数格：将最多{r["evenStarGuard"]}星力转为等量护体，满层仍消耗星力。')
    if r.get('persistent'): lines.append('本场限用一次。')
    if r.get('calmPerQi'): lines.append(f'持续：本门牌每实际累计恢复4斗气，为{HEAL_TARGET}恢复{r["calmPerQi"]*3}生命；不足4的余数跨牌保留，满斗气不触发。')
    if r.get('randomHealMax'): lines.append(f'为' + ('指定的一名存活己方角色' if r.get('selectedAllyHeal') else HEAL_TARGET) + f'随机恢复〔（{r["randomHealMin"]*3}～{r["randomHealMax"]*3}）＋50%×体质〕生命；卦象可确保治疗量上限。')
    if r.get('healingStep'): lines.append(f'本场累计治疗量每满{r["healingStep"]}，本牌伤害+{r["damagePerHealing"]}；包含溢出，不消耗累计量。')
    lines.extend(describe_group(card))
    return lines
