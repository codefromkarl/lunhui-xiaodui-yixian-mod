"""Compile source-backed school rules into native abilities, descriptions and school content."""
import copy
import importlib
from card_costs import printed_cost
from identity_ids import card_id, pack_id, fate_id
from presentation_copy import COPY, card_summary, pack_detail, rules_for, apply_status_copy

ATTRS = {10: '筋肉', 20: '反应', 30: '体质', 40: '能量'}

def formula(rules, key):
    value = str(rules.get(key, 0))
    if rules.get(key + 'Scaling'):
        return value + ''.join(f'＋{term["bp"] / 100:g}%×{ATTRS[term["attribute"]]}' for term in rules[key + 'Scaling'])
    if rules.get(key + 'Bp'):
        value += f'＋{rules[key + "Bp"] / 100:g}%×{ATTRS[rules.get(key + "Attr", 40)]}'
    return value

def describe(card):
    if card.get('school') == 'five' and card.get('sourceId'):
        return importlib.import_module('five_content').describe_card(card, formula)
    r = card['rules']; lines = []
    if r.get('originalRules'):
        star_content = importlib.import_module('star_content')
        for key, label in [('damage','造成物理伤害'),('shield','获得护盾'),('heal','恢复生命'),('qi','恢复斗气')]:
            if r.get(key) or r.get(key+'Bp'):
                if key == 'heal': lines.append(star_content.heal_text(formula(r,key), r.get('selectedAllyHeal', False)))
                else: lines.append(f'{label}〔{formula(r,key)}〕'+(f' ×{r["hits"]}段。' if key=='damage' and r.get('hits',1)>1 else '。'))
        lines.extend(star_content.describe_effects(card))
        lines.append(f'消耗：{printed_cost(r)}斗气。')
        return '\n'.join(lines)
    raise ValueError('缺少原作规则标记：' + card['name'])

def compile_school(school, sword):
    result = copy.deepcopy(school); d = result['definition']; cards = result['cards']
    d['rulesDescription'] = rules_for(d['statePrefix'])
    by_key = {c['key']: c for c in cards}
    fate_definitions = result.get('fates', [])
    for fate in fate_definitions:
        fate['description'] = COPY['fateDescriptions'][d['statePrefix']][fate['key']]
    result['fateDefinitions'] = fate_definitions
    result['abilities'] = {'operations': []}; result['statuses'] = {'operations': []}; result['fates'] = {'operations': []}
    result['assets'] = {f'icon.lineage.{d["code"]}': dict(kind='icon', file=f'icons/lineage.{d["code"]}.png')}
    for card in cards:
        card['school'] = d['statePrefix']; card['description'] = describe(card)
        card['summary'] = card_summary(card)
        aid = card_id(card['key']); icon = 'icon.' + aid; r = card['rules']
        enemy = card.get('targetEnemy', bool(r.get('damage') or r.get('damageBp')))
        target = 20 if card.get('targetAlly') else 50 if enemy else 10
        values = [dict(key=k, intValue=v) for k, v in dict(apCost=0, energyCost=printed_cost(r), energyPool=40, range=5).items()]
        fields = dict(displayName=card['name'], iconKey=icon, sourceContentId=d['id'], mode=10, acquisition=20, purchasePrice=0, flavour='', referencedStatusIds=[], ranks=[dict(rank='DCBAS'[card['rank']-1], tier=1, operation=dict(handlerKey='effect.script', targetKind=target, values=values))])
        result['abilities']['operations'].append(dict(op='add', kind='abilities', id=aid, fields=fields))
        result['assets'][icon] = dict(kind='icon', file='icons/cards/' + card['key'] + '.png')
    for pack in result['packs']:
        rank = pack['rank']
        pack['acquisition'] = (f'获得{d["name"]}时自动赠送并启用。' if rank == 1 else '突破筑基时从两个筑基包中免费选一；另一包可购买。' if rank == 2 else '达到' + ['炼气','筑基','金丹','元婴','化神'][rank-1] + '后购买，不赠送；低阶未拥有包持续出售。')
        pack['role'] = COPY['packRoles'][pack['key']]
        pack['description'] = pack_detail(pack, by_key)
        aid = pack_id(pack['key']); icon = 'icon.' + aid
        operation = dict(handlerKey='passive.lineage', targetKind=110, values=[dict(key='lineageCode', stringValue=d['code']), dict(key='step', intValue=rank-1), dict(key='cardTablePackKey', stringValue=pack['key'])])
        fields = dict(displayName=pack['name'], iconKey=icon, sourceContentId=d['id'], mode=20, acquisition=20 if rank == 1 else 10, purchasePrice=pack['price'], flavour=pack['acquisition'], referencedStatusIds=[], ranks=[dict(rank='DCBAS'[rank-1], tier=1, operation=operation)])
        if d['statePrefix'] == 'five':
            fields.update(acquisition=20, purchasePrice=0, flavour='原卡资料分类，不出售、不启用。')
            pack['acquisition'] = '原卡资料分类；突破解锁两张新牌；战斗中从已解锁牌库选八张组成卡包。'
            pack['description'] = pack_detail(pack, by_key)
        result['abilities']['operations'].append(dict(op='add', kind='abilities', id=aid, fields=fields))
        result['assets'][icon] = result['assets']['icon.' + card_id(pack['cards'][0])]
    lineage = copy.deepcopy(sword['lineages']['operations'][0]); lineage['id'] = d['id']; f = lineage['fields']
    f.update(displayName=d['name'], iconKey=f'icon.lineage.{d["code"]}', lineageCode=d['code'], rankTitles=['炼气','筑基','金丹','元婴','化神'], flavour=f'{d["summary"]}\n每人最多一个弈仙门派，可搭配原生血统。\n' + d['rulesDescription'])
    # Only the native declaration structure is shared. Each school owns ALL its
    # numeric passive terms; Sword specialization must never leak into Star/Five.
    for index, step in enumerate(f['steps']):
        step['passive']['values'] = [dict(key='lineageCode', stringValue=d['code']), dict(key='step', intValue=index)]
        step['passive']['values'].extend(dict(key=key, intValue=value) for key, value in d['lineageStats'][index].items())
    result['lineages'] = dict(operations=[lineage])
    for fate in fate_definitions:
        aid = fate_id(d['id'], fate['key']); icon = 'icon.' + aid
        description = fate['description'] + '\n独立被动，本局限购一次，不占手牌。'
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
    apply_status_copy(d['statePrefix'], result['statuses']['operations'])
    return result
