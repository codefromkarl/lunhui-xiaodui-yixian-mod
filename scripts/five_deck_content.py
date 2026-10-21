"""Persisted membership tokens and one native eight-card pack; no card shop."""
from identity_ids import card_id

PACK_ID = 'yixian.sect.pack.five_deck'
TOKEN_PREFIX = 'yixian.sect.deckcard.'
UNLOCK_PREFIX = 'yixian.sect.unlockedcard.'


def compile_deck(table, content, manifest):
    cards = [c for c in table['cards'] if c.get('sect') == 'yixian.sect.lineage.five']
    for key, name, icon, pack in [('five_deck', '五行自选卡包', 'icon.lineage.yixian_five', True)] + [
        (c['key'], c['name'], 'icon.' + card_id(c['key']), False) for c in cards
    ]:
        values = [dict(key='lineageCode', stringValue='yixian_five'), dict(key='step', intValue=0)]
        if pack:
            values.append(dict(key='cardTablePackKey', stringValue=key))
        fields = dict(displayName=name, iconKey=icon, sourceContentId='yixian.sect.lineage.five',
                      mode=20, acquisition=20, purchasePrice=0,
                      flavour='固定八张；突破解锁两张新牌，战斗中从已解锁牌库免费调整八张。' if pack else '五行卡组选择记录，不单独出售。',
                      referencedStatusIds=[], ranks=[dict(rank='D', tier=1, operation=dict(
                          handlerKey='passive.lineage', targetKind=110, values=values))])
        content['abilities']['operations'].append(dict(op='add', kind='abilities', id=PACK_ID if pack else TOKEN_PREFIX + key, fields=fields))
        if not pack:
            import copy
            unlocked = copy.deepcopy(fields)
            unlocked['flavour'] = '五行已解锁牌库；突破时取得，不单独出售。'
            content['abilities']['operations'].append(dict(op='add', kind='abilities', id=UNLOCK_PREFIX + key, fields=unlocked))
