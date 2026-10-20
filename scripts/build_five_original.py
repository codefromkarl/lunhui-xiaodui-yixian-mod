#!/usr/bin/env python3
"""Rebuild Five Elements from a checked-in upstream snapshot; never downloads/installs."""
import json
from pathlib import Path
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'data/sources/five-elements'
SCHOOL=ROOT/'data/five-elements/school.json'
# Adaptation units: original attack/shield/life point=3; lingqi=4 fighting qi.
# All multipliers apply to one card's initial effect, never each persistent tick.
S={
1:dict(damage=18,damageBp=8000,activate=1),
2:dict(damage=18,damageBp=8000,linkedEdge=2),
3:dict(qi=4,heal=12,healBp=5000,activate=3),
4:dict(damage=9,hits=2,damageBp=4000,linkedHeal=15,linkedHealBp=5000,cost=1),
6:dict(qi=8,activate=2),
9:dict(qi=4,burn=9,activate=4),
10:dict(damage=27,damageBp=10000,linkedDamage=6,cost=1),
11:dict(damage=9,damageBp=5000,shield=12,shieldBp=5000,activate=5),
14:dict(damage=15,damageBp=7000,qi=4,nextActivate=True),
15:dict(damage=21,damageBp=9000,linkedPierce=True),
16:dict(edge=4,formation='metal'),
17:dict(damage=9,hits=2,damageBp=4500,linkedPower=1,cost=1),
18:dict(qi=8,linkedHeal=9,linkedHealBp=4000,healPerPower=3),
19:dict(tide=2,linkedFree=True,cost=2),
22:dict(damage=12,hits=2,damageBp=5000,linkedDamage=12,cost=1),
23:dict(shield=12,shieldBp=6000,formation='earth'),
25:dict(shield=12,shieldBp=7000,edge=4,edgePerShield=12),
26:dict(damage=18,damageBp=10000,hit='edge'),
28:dict(damage=3,hits=2,damageBp=2500,breakShield=True,linkedHealedDraw=True,cost=1),
29:dict(qi=8,formation='water'),
31:dict(burn=6,formation='fire'),
32:dict(power=1,linkedBurn=21,burnPerPower=3,cost=1),
36:dict(maxHealth=30,formation='wood'),
95:dict(damage=45,damageBp=15000,damagePerQi=3,deckDiscount=True,cost=5,cyclePayoff=True),
20:dict(maxHealth=6,maxCurrent=True,linkedWaterGuard=True,cost=1),
27:dict(damage=12,hits=3,damageBp=5000,hit='heal'),
30:dict(qi=8,maxHealth=6,maxCurrent=True,tidePerQi=8),
33:dict(damage=27,damageBp=11000,damagePerLostShield=18),
34:dict(damage=12,damageBp=8000,noEdge=True,linkedDraw=True),
35:dict(linkedIron=True),
38:dict(burn=12,burnMaximumOnly=True,linkedDraw=True),
39:dict(damage=12,hits=2,damageBp=6000,hit='burn'),
40:dict(shield=36,shieldBp=12000,shieldPerLostShield=18),
61:dict(qi=8,heal=6,healBp=4000,powerPerQi=12),
66:dict(damage=12,damageBp=11000,damagePerDeck=12,breakShield=True,cost=1,cyclePayoff=True),
96:dict(shield=12,shieldBp=6000,linkedDraw=True),
42:dict(damage=21,damageBp=13000,hit='double'),
43:dict(power=1,heal=9,healBp=6000,linkedDraw=True,cost=1),
53:dict(shield=36,shieldBp=14000,linkedRetain=2),
56:dict(damage=18,hits=3,damageBp=7000,hit='capCurrent',cost=2),
57:dict(tide=3,linkedMaxHealth=18,maxPerTide=3,maxCurrent=True,cost=2),
58:dict(formation='chaos',cyclePayoff=True),
114:dict(shield=6,nextHitShield=6,linkedEvasionFlat=10),
97:dict(qi=8,damage=6,hits=2,damageBp=3500,linkedSpring=4),
98:dict(qi=12,powerTo=2,selfCostPerPower=12,linkedBurnPerPower=6),
99:dict(damage=18,hits=2,damageBp=7500,edgeRefund=60),
100:dict(tide=1,maxHealth=12,maxCurrent=True,linkedEvasion=True),
101:dict(qi=8,qiPerDeck=4,nextActivate=True,activatePrevious=True,deckExtra=True,cyclePayoff=True),
}
# Twelve packs retain their ID, shelf, price and rank; only their content changes.
PACKS=[
('炼气·金木启灵',[1,2,3,4],'灵印激活金木，金针养锋，木芽多段回血。'),
('炼气·水火土印',[6,9,10,11],'水火灵印回气，火窜攻击，土印建立护盾。'),
('筑基·金土灵阵',[14,15,16,23],'浑天印接续激活，金灵阵养锋，穿心破防，土灵阵叠盾。'),
('筑基·水木火术',[17,18,19,22],'木灵加攻回血，水势积累并条件免气，赤焰多段。'),
('金丹·金木养锋',[25,26,28,36],'蓄锐护盾养锋，锋芒击伤再蓄，巡林破盾，木阵持续治疗。'),
('金丹·水火灵击',[29,31,32,95],'水阵回气、火阵削血，轰爆使用加攻，五行灵击按牌池五行减费。'),
('元婴·金火速攻',[34,35,38,39],'飞梭与瞬燃摸牌，铁骨减伤，灼心击伤追加削血。'),
('元婴·水木养息',[20,27,30,61],'水灵减伤蓄势，玫刺击伤治疗，暗香按灵气获得加攻。'),
('元婴·厚土混元',[33,40,66,96],'护盾损失积累流沙与绝壁，遁地摸牌，混元碎击按启用牌池五行增伤。'),
('化神·金火终式',[42,56,98,99],'巨鼎击伤翻倍，烈燎原多段削血，焚脉加攻，回锋刃逐段返还锋锐。'),
('化神·水木生机',[43,57,97,114],'柳纷飞续牌，纳百川按水势治疗，春风拂攻击养生，逐尘步击伤成盾。'),
('化神·土水混元',[53,58,100,101],'合八荒恢复失盾，乘风浪强化闪避，混元化灵补激活，无极阵按激活续牌。'),
]

def main():
 school=json.loads(SCHOOL.read_text());source=json.loads((SRC/'selected.json').read_text());by_num={int(x['id'])-7000000:x for x in source}
 old=[dict(c) for c in school['cards']]
 # Freeze the old slot-to-source assignment once, so generation stays reproducible.
 mapfile=SRC/'slot-map.json'
 if mapfile.exists():mapping=json.loads(mapfile.read_text())
 else:
  mapping=[]
  for pack,(_,nums,_) in zip(school['packs'],PACKS):
   for key,num in zip(pack['cards'],nums):
    prev=next(c for c in old if c['key']==key);mapping.append(dict(key=key,oldName=prev['name'],sourceId=str(7000000+num)))
  mapfile.write_text(json.dumps(mapping,ensure_ascii=False,indent=2)+'\n')
 by_id={m['sourceId']:m for m in mapping};cards=[]
 for row in source:
  num=int(row['id'])-7000000;rules=dict(S[num]);name=row['name'];element=next((v for k,v in [('金',1),('水',2),('木',3),('火',4),('土',5)] if name.startswith(k+'灵')),0)
  if rules.get('damageBp'):rules['damageAttr']=40
  for k in ['shield','heal','linkedHeal']:
   if rules.get(k+'Bp'):rules[k+'Attr']=30 if 'Heal' in k or k=='heal' else 40
  adaptations=[]
  if rules.get('linkedDraw') or rules.get('formation')=='chaos' or num in [28,58]:adaptations.append('再次行动改为额外摸1张，不返还行动力，手牌上限10。')
  if num in [28,66]:adaptations.append('碎防改为先清除目标现有护盾，不取消后续获得的护盾。')
  if num==101:adaptations.append('固定相邻牌改为激活上一张已打出牌的五行，并令下一张五行牌先激活；额外种数只按启用包计算。')
  if num in [100,114]:adaptations.append('身法改为原生闪避等级，持续2个自身回合；不改变行动力或自动摸牌。')
  if num==35:adaptations.append('金灵减伤按最终伤害减15，最低减至1，持续至第2个后续自身回合开始。')
  if num==20:adaptations.append('水灵减伤作用于最终物理伤害，持续至第2个后续自身回合开始。')
  if '上限' in row['original']:adaptations.append('生命上限变化只在本场生效；增减生命及上限同时改变当前生命，仅上限变化不额外治疗／扣血，但当前生命受新上限截断。')
  card=dict(key=by_id[row['id']]['key'],name=name,rank=['炼气','筑基','金丹','元婴','化神'].index(row['realm'])+1,kind=0,element=element,sourceId=row['id'],sourceUrl='https://sharpobject.github.io/yxp_wiki/zh/cards/'+row['id']+'.html',original=row.get('imageOriginal',row['original']),sourceDiscrepancy=row.get('sourceDiscrepancy',''),adaptations=adaptations,rules=rules)
  cards.append(card)
  Image.open(SRC/'images'/(row['id']+'.webp')).convert('RGBA').save(ROOT/'mod/yixian.sect/icons/cards'/(card['key']+'.png'))
 new_by={c['sourceId']:c for c in cards}
 for pack,(name,nums,role) in zip(school['packs'],PACKS):
  assert all(new_by[str(7000000+n)]['rank']==pack['rank'] for n in nums)
  pack.update(name=name,cards=[new_by[str(7000000+n)]['key'] for n in nums],role=role)
 school['cards']=cards
 school['definition']['summary']='原作常规牌精选48张：灵印激活五行，同属性／相生或已激活时触发五灵效果。金灵锋锐、水灵水势、木灵加攻治疗、火灵削血、土灵护盾及混元种数。原文与适配差异见单卡详情。'
 views=[('last','前一五行','上一张打出的五行；中性牌打断衔接，不清除已激活五行。'),('wild','浑天引导','下一张五行牌先激活其所属五行。'),('elements','旧五行计数','兼容旧局；实际激活状态分别显示五灵图标。')]
 views += [(k,n+'灵激活',n+'灵已激活，本场对应效果无条件触发。') for k,n in zip(['metal','water','wood','fire','earth'],'金水木火土')]
 views += [('edge','锋锐','击伤后清空，每层追加3真实生命伤害；未击伤不消耗。'),('tide','水势','每轮结束每层对敌方首个存活目标造成3物理伤害，受防御／护盾结算。'),('power','加攻','本场五行攻击每段每层+3伤害。'),('iron','金灵铁骨','受到的最终伤害减少15，最低保留1点。'),('waterGuard','水灵潜遁','受到的最终物理伤害减少40%。'),('evasion','乘风浪','原生闪避等级提升，2个自身回合。'),('metalFormation','金灵阵','以后每用金灵牌获得1锋锐，同种灵阵不叠加。'),('waterFormation','水灵阵','以后每用水灵牌恢复4斗气，同种灵阵不叠加。'),('woodFormation','木灵阵','以后每用木灵牌恢复6生命，同种灵阵不叠加。'),('fireFormation','火灵阵','以后每用火灵牌令目标损失6真实生命，同种灵阵不叠加。'),('earthFormation','土灵阵','以后每用土灵牌获得6护盾，同种灵阵不叠加。'),('chaosDraw','混元无极阵','剩余激活额外摸牌次数，每次激活消耗一次。'),('restoreShield','合八荒','下几次护盾减少时恢复减少的护盾。')]
 views += [('flame','旧余烬','旧版原创循环，保留定义供旧局读取，新战斗清除。'),('growth','旧生息','旧版原创循环，保留定义供旧局读取，新战斗清除。'),('ward','旧厚土','旧版原创循环，保留定义供旧局读取，新战斗清除。')]
 school['views']=views
 school['provenance']=dict(catalog='data/sources/five-elements/catalog.json',selected='data/sources/five-elements/selected.json',scope='67张原作常规牌中精选48张；统一采用1级原文，非角色牌／赛季牌。',costEvidence='data/sources/five-elements/images/ 原作1级卡图左上角；逐图检查；无图标为0灵气。')
 SCHOOL.write_text(json.dumps(school,ensure_ascii=False,indent=2)+'\n')
 print('Built 48 original-source Five Elements cards; kept 48 IDs / 12 pack IDs')
if __name__=='__main__':main()
