"""Water-only individual purchases; selection entries reuse existing authored cards."""
from identity_ids import card_id
PREFIX='yixian.sect.selection.'
def compile_pilot(table,content,manifest,config):
    five=[c for c in table['cards'] if c.get('sect')=='yixian.sect.lineage.five']
    for c in five:
        key=c['key'];rank=c['rank'];paid=c.get('element')==2 and rank>1
        price=config['waterPriceByRank'][rank-1] if paid else 0
        fields=dict(displayName=('水系单卡·' if paid else '选牌·')+c['name'],iconKey='icon.'+card_id(key),
            sourceContentId='yixian.sect.lineage.five',mode=20,acquisition=10 if paid else 20,purchasePrice=price,
            flavour='单卡解锁；原卡包已拥有此牌则无需重复购买。购买不自动改变抽牌池。',referencedStatusIds=[],
            ranks=[dict(rank='DCBAS'[rank-1],tier=1,operation=dict(handlerKey='passive.lineage',targetKind=110,
                values=[dict(key='lineageCode',stringValue='yixian_five'),dict(key='step',intValue=rank-1),
                        dict(key='cardTablePackKey',stringValue='select_'+key)]))])
        content['abilities']['operations'].append(dict(op='add',kind='abilities',id=PREFIX+key,fields=fields))
