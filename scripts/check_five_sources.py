#!/usr/bin/env python3
"""Independently reconcile the five-school selection against archived source evidence."""
import json
from collections import Counter
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
# Checked against each selected level-1 card image's upper-left lingqi symbol.
PAID={7000004:1,7000010:1,7000017:1,7000019:2,7000022:1,7000028:1,7000032:1,7000095:5,7000020:1,7000066:1,7000043:1,7000056:2,7000057:2}
REALMS=['炼气','筑基','金丹','元婴','化神']
def main():
 source=ROOT/'data/sources/five-elements'
 catalog={r['id']:r for r in json.loads((source/'catalog.json').read_text())}
 selected=json.loads((source/'selected.json').read_text());assert len(selected)==48
 school=json.loads((ROOT/'data/five-elements/school.json').read_text())
 cards=school['cards'];assert len(cards)==48 and len({c['sourceId'] for c in cards})==48
 wanted={r['id']:r for r in selected};assert {c['sourceId'] for c in cards}==set(wanted)
 for c in cards:
  row=wanted[c['sourceId']];raw=catalog[c['sourceId']]
  assert c['name']==row['name']==raw['name'],f'non-original card name: {c["key"]}'
  assert REALMS[c['rank']-1]==row['realm']==raw['realm'],f'cross-realm card: {c["name"]}'
  assert c['rules'].get('cost',0)==PAID.get(int(c['sourceId']),0),f'printed lingqi cost mismatch: {c["name"]}'
  assert c['sourceUrl']=='https://sharpobject.github.io/yxp_wiki/zh/cards/'+c['sourceId']+'.html'
  assert c['original']==row.get('imageOriginal',row['original']),f'original text mismatch: {c["name"]}'
  assert (source/'images'/(c['sourceId']+'.webp')).is_file()
 assert Counter(c['rank'] for c in cards)=={1:8,2:8,3:8,4:12,5:12}
 by_key={c['key']:c for c in cards};members=[]
 for p in school['packs']:
  assert len(p['cards'])==len(set(p['cards']))==4
  for key in p['cards']:assert by_key[key]['rank']==p['rank'];members.append(key)
 assert sorted(members)==sorted(by_key)
 slots=json.loads((source/'slot-map.json').read_text())
 assert {(x['key'],x['sourceId']) for x in slots}=={(c['key'],c['sourceId']) for c in cards}
 print('PASS five source reconciliation: 48 real names/IDs/original texts, five correct realms, image-verified lingqi costs, 12 complete packs and retained 48 slots')
if __name__=='__main__':main()
