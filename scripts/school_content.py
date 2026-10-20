"""Compile authored prototype rules into native abilities, descriptions and school content."""
import copy
import importlib
from card_costs import printed_cost
from identity_ids import card_id, pack_id, fate_id

ATTRS = {10: '筋肉', 20: '反应', 30: '体质', 40: '能量'}

def formula(rules, key):
    value = str(rules.get(key, 0))
    if rules.get(key + 'Bp'):
        value += f'＋{rules[key + "Bp"] / 100:g}%×{ATTRS[rules.get(key + "Attr", 40)]}'
    return value

def describe(card):
    if card.get('school') == 'five' and card.get('sourceId'):
        return importlib.import_module('five_content').describe_card(card, formula)
    r = card['rules']; lines = []
    if r.get('originalRules'):
        for key, label in [('damage','造成物理伤害'),('shield','获得护盾'),('heal','恢复生命'),('qi','恢复斗气')]:
            if r.get(key) or r.get(key+'Bp'):
                lines.append(f'{label}〔{formula(r,key)}〕'+(f' ×{r["hits"]}段。' if key=='damage' and r.get('hits',1)>1 else '。'))
        lines.extend(importlib.import_module('star_content').describe_effects(card))
        lines.append(f'消耗：0行动力，{printed_cost(r)}斗气（原卡灵气费用{r.get("cost",0)}×4，无基础费）。')
        return '\n'.join(lines)
    for key, label in [('damage', '造成物理伤害'), ('shield', '获得护盾'), ('heal', '恢复生命'), ('qi', '恢复斗气')]:
        if r.get(key) or r.get(key + 'Bp'):
            lines.append(f'{label}〔{formula(r, key)}〕' + (f' ×{r["hits"]}段。' if key == 'damage' and r.get('hits', 1) > 1 else '。'))
    for key, label in [('linkedDamage', '每段追加物理伤害'), ('linkedShield', '获得护盾'), ('linkedQi', '恢复斗气')]:
        if r.get(key) or r.get(key + 'Bp'):
            lines.append(f'相生：{label}〔{formula(r, key)}〕。')
    if r.get('wild'):
        lines.append('下一张五行牌无视前置元素触发相生；生效后消耗。')
    if r.get('converge'):
        lines.append(f'每种已激活五行额外造成{r["damagePerElement"]}伤害、恢复{r["qiPerElement"]}斗气，随后清空激活。')
    if card.get('element'):
        lines.append('激活本牌五行。上一张牌同属性或按金→水→木→火→土→金相生，均触发相生效果；其他牌打断衔接，抽牌／移动／普攻不打断。状态栏分别显示已激活的五行；激活保持到本场结束或归元消耗，同属性不重复计数。')
    if r.get('starGain'):
        lines.append(f'获得{r["starGain"]}星力；全场保留，上限{r.get("starCap", 20)}。')
    if r.get('omenGain'):
        lines.append(f'获得{r["omenGain"]}卦象；全场保留，上限9。')
    if r.get('starSpend'):
        lines.append(f'最多消耗{r["starSpend"]}星力' + (f'，每点每段增加{r["damagePerStar"]}伤害。' if r.get('damagePerStar') else '。'))
    if r.get('randomMax'):
        lines.append(f'本牌随机追加{r["randomMin"]}～{r["randomMax"]}物理伤害；有卦象则消耗1点，直接取上限且不抽取随机数。')
    if r.get('positionShield'):
        lines.append(f'星位：额外获得{r["positionShield"]}护盾。')
    if r.get('positionDraw'):
        lines.append('星位：额外摸1张，手牌最多10张。')
    if r.get('draw'):
        lines.append('额外摸1张，手牌最多10张。')
    if card.get('school') == 'star':
        lines.append('星位＝本回合成功打出的第3／6／9张牌；所有卡牌计数，抽牌／移动／普攻不计，回合开始归零。')
    module = importlib.import_module('five_content' if card.get('school') == 'five' else 'star_content')
    lines.extend(module.describe_effects(card))
    lines.append(f'消耗：0行动力，{printed_cost(r)}斗气（灵气费用{r.get("cost", 0)}×4；无基础费）；回气／治疗不超过上限。属性取当前有效值，倍率部分向下取整。')
    return '\n'.join(lines)

def compile_school(school, sword):
    result = copy.deepcopy(school); d = result['definition']; cards = result['cards']
    by_key = {c['key']: c for c in cards}
    fate_definitions = result.get('fates', [])
    result['fateDefinitions'] = fate_definitions
    result['abilities'] = {'operations': []}; result['statuses'] = {'operations': []}; result['fates'] = {'operations': []}
    result['assets'] = {f'icon.lineage.{d["code"]}': dict(kind='icon', file=f'icons/lineage.{d["code"]}.png')}
    for card in cards:
        card['school'] = d['statePrefix']; card['description'] = describe(card)
        aid = card_id(card['key']); icon = 'icon.' + aid; r = card['rules']
        enemy = card.get('targetEnemy', bool(r.get('damage') or r.get('damageBp')))
        values = [dict(key=k, intValue=v) for k, v in dict(apCost=0, energyCost=printed_cost(r), energyPool=40, range=5).items()]
        fields = dict(displayName=card['name'], iconKey=icon, sourceContentId=d['id'], mode=10, acquisition=20, purchasePrice=0, flavour='', referencedStatusIds=[], ranks=[dict(rank='DCBAS'[card['rank']-1], tier=1, operation=dict(handlerKey='effect.script', targetKind=50 if enemy else 10, values=values))])
        result['abilities']['operations'].append(dict(op='add', kind='abilities', id=aid, fields=fields))
        result['assets'][icon] = dict(kind='icon', file='icons/cards/' + card['key'] + '.png')
    for pack in result['packs']:
        rank = pack['rank']
        pack['acquisition'] = (f'获得{d["name"]}时自动赠送并启用。' if rank == 1 else '突破筑基时从两个筑基包中免费选一；另一包可购买。' if rank == 2 else '达到' + ['炼气','筑基','金丹','元婴','化神'][rank-1] + '后购买，不赠送；低阶未拥有包持续出售。')
        pack['description'] = pack['role'] + '\n' + pack['acquisition'] + '\n每包4张；启用后整包加入随机抽牌池。\n'+'原作1级卡名、境界、原卡图及核心效果；数值／环境适配与图文版差见单卡详情。'+'\n\n' + '\n\n'.join('【' + by_key[k]['name'] + '】\n' + by_key[k]['description'] for k in pack['cards'])
        aid = pack_id(pack['key']); icon = 'icon.' + aid
        operation = dict(handlerKey='passive.lineage', targetKind=110, values=[dict(key='lineageCode', stringValue=d['code']), dict(key='step', intValue=rank-1), dict(key='cardTablePackKey', stringValue=pack['key'])])
        fields = dict(displayName=pack['name'], iconKey=icon, sourceContentId=d['id'], mode=20, acquisition=20 if rank == 1 else 10, purchasePrice=pack['price'], flavour=pack['acquisition'], referencedStatusIds=[], ranks=[dict(rank='DCBAS'[rank-1], tier=1, operation=operation)])
        result['abilities']['operations'].append(dict(op='add', kind='abilities', id=aid, fields=fields))
        result['assets'][icon] = result['assets']['icon.' + card_id(pack['cards'][0])]
    lineage = copy.deepcopy(sword['lineages']['operations'][0]); lineage['id'] = d['id']; f = lineage['fields']
    f.update(displayName=d['name'], iconKey=f'icon.lineage.{d["code"]}', lineageCode=d['code'], rankTitles=['炼气','筑基','金丹','元婴','化神'], flavour=f'{d["summary"]}\n五境界48张牌、12个四张包；入宗双赠包，筑基二选一，之后购包。四个仙命独立购买，不占手牌。每人最多一个弈仙门派，可搭配原生血统。\n首回合3张；主动摸牌1行动力；出牌0行动力，仅有灵气费用的牌消耗斗气，1灵气＝4斗气，无基础费。'+'采用原作牌表，数值和环境适配逐牌注明；旧原创槽位仅保留为内部身份。')
    for step in f['steps']:
        for value in step['passive']['values']:
            if value['key'] == 'lineageCode': value['stringValue'] = d['code']
    result['lineages'] = dict(operations=[lineage])
    for fate in fate_definitions:
        aid = fate_id(d['id'], fate['key']); icon = 'icon.' + aid
        description = fate['description'] + '\n本局购买一次，不占手牌，无需主动释放；效果是否随境界变化以本条说明为准。'
        ranks = []
        for rank in range(fate['rank'], 6):
            operation = dict(handlerKey='passive.lineage', targetKind=110, values=[dict(key='lineageCode', stringValue=d['code']), dict(key='step', intValue=rank-1)])
            ranks.append(dict(rank='DCBAS'[rank-1], tier=1, operation=operation))
        fields = dict(displayName=fate['name'], iconKey=icon, sourceContentId=d['id'], mode=20, acquisition=10, purchasePrice=fate['price'], flavour=description, referencedStatusIds=[], ranks=ranks)
        result['fates']['operations'].append(dict(op='add', kind='abilities', id=aid, fields=fields))
        result['assets'][icon] = dict(kind='icon', file=f'icons/fates/{d["code"]}_{fate["key"]}.png')
    for name, title, detail in result['views']:
        status = copy.deepcopy(sword['statuses']['operations'][0]); sid = 'yixian.sect.status.' + d['statePrefix'] + '.' + name
        status['id'] = sid; sf = status['fields'];
        if d['statePrefix']=='five' and name=='evasion': sf['modifiers']=[dict(primitive=520,flatAmount=0,basisPoints=100,category=0,source=10,perStack=True,damageLayer=0,damageTypeFilter=0,tagFilter=0)]
        sf.update(displayName=title, flavour=detail, iconKey='icon.' + sid)
        result['statuses']['operations'].append(status)
        icon_file = result.get('viewIcons', {}).get(name, f'icons/statuses/{d["statePrefix"]}_{name}.png')
        result['assets']['icon.' + sid] = dict(kind='icon', file=icon_file) if icon_file else result['assets'][f'icon.lineage.{d["code"]}']
    status_by_id = {s['id']: s for s in result['statuses']['operations']}
    for authored in result.get('combatStatuses', []):
        sid = 'yixian.sect.status.' + d['statePrefix'] + '.' + authored['key']
        row = status_by_id.get(sid)
        if row is None:
            row = copy.deepcopy(sword['statuses']['operations'][0]); row['id'] = sid
            row['fields'].update(displayName=authored['name'], iconKey='icon.'+sid)
            result['statuses']['operations'].append(row)
        row['fields'].update(authored['fields'])
        result['assets']['icon.'+sid] = dict(kind='icon', file=authored.get('icon', f'icons/statuses/{d["statePrefix"]}_{authored["key"]}.png'))
    return result
