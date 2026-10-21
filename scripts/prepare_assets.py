#!/usr/bin/env python3
"""Check local artwork, or explicitly fetch source-backed images and render local icons."""
import argparse
import concurrent.futures
from io import BytesIO
import json
from pathlib import Path
import subprocess
import sys
import urllib.request

ROOT=Path(__file__).resolve().parents[1]

def source_rows():
    """Derive image identities from the same authoritative data used by content generation."""
    base='https://sharpobject.github.io/yxp_wiki'
    cards=json.loads((ROOT/'data/sword/cardtable.json').read_text())['cards']
    schools=[json.loads(p.read_text()) for p in sorted((ROOT/'data').glob('*/school.json'))]
    cards=cards+[c for school in schools for c in school['cards']]
    rows=[]
    for card in cards:
        if not card.get('sourceId'):continue
        sid=str(card['sourceId']);outputs=[{'path':'mod/yixian.sect/icons/cards/'+card['key']+'.png','format':'png'}]
        if sid.startswith(('100','400','700')):outputs.append({'path':'data/sources/original-correction/card-images/'+sid+'.webp','format':'raw'})
        if sid.startswith('700'):outputs.append({'path':'data/sources/five-elements/images/'+sid+'.webp','format':'raw'})
        rows.append({'name':card['name'],'sourceId':sid,'url':base+'/assets/cards/'+sid+'_zh.webp','page':card.get('sourceUrl',base+'/zh/cards/'+sid+'.html'),'outputs':outputs})
    for number,name in [(16,'剑韵修炼'),(69,'云之道'),(57,'剑阵守护'),(14,'云海连绵')]:
        rows.append({'name':name,'url':base+f'/assets/fates/Icon_Talent_{number}.webp','outputs':[{'path':f'mod/yixian.sect/icons/fates/Icon_Talent_{number}.png','format':'png'}]})
    for school in schools:
        for fate in school.get('fates',[]):
            url=fate.get('sourceIconUrl')
            if not url:continue
            rows.append({'name':fate['name'],'url':url,'outputs':[{'path':f'mod/yixian.sect/icons/fates/{school["definition"]["code"]}_{fate["key"]}.png','format':'png'},{'path':'data/sources/original-correction/fate-images/'+url.rsplit('/',1)[-1],'format':'raw'}]})
    for key,number in [('sword',1),('star',2),('five',3),('forge',4)]:
        rows.append({'name':'门派徽章 '+key,'url':base+f'/assets/sects/sect_badge_{number}.webp','outputs':[{'path':'mod/yixian.sect/icons/lineage.yixian_'+key+'.png','format':'png'}]})
    return rows

def target(relative):
    path=(ROOT/relative).resolve()
    if ROOT not in path.parents:raise ValueError('Asset output escapes project: '+relative)
    return path

def fetch(row):
    outputs=[(target(o['path']),o['format']) for o in row['outputs']]
    if all(p.is_file() for p,_ in outputs):return
    from PIL import Image
    raw=urllib.request.urlopen(row['url'],timeout=30).read()
    image=Image.open(BytesIO(raw)).convert('RGBA')
    for path,kind in outputs:
        if path.is_file():continue
        path.parent.mkdir(parents=True,exist_ok=True)
        if kind=='raw':path.write_bytes(raw)
        else:image.save(path,format='PNG')

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--download',action='store_true',help='fetch missing original art for permitted local use, then render code-generated icons')
    args=parser.parse_args()
    rows=source_rows()
    (ROOT/'data/third-party-assets.json').write_text(json.dumps({'notice':'Artwork remains owned by the original rights holders and is not distributed in this repository. Fetch only for permitted local use.','assets':rows},ensure_ascii=False,indent=2)+'\n')
    if args.download:
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:list(pool.map(fetch,rows))
        subprocess.run([sys.executable,str(ROOT/'scripts/build_prototype_art.py')],cwd=ROOT,check=True)
    manifest=json.loads((ROOT/'mod/yixian.sect/mod.json').read_text())
    missing=sorted({v['file'] for v in manifest['assets'].values() if not target('mod/yixian.sect/'+v['file']).is_file()})
    if missing:
        print(f'{len(missing)} missing local assets. Run python3 scripts/prepare_assets.py --download after reviewing THIRD_PARTY_ASSETS.md.',file=sys.stderr)
        for name in missing:print(name,file=sys.stderr)
        return 1
    print(f'PASS local artwork: {len(manifest["assets"])} registered references resolve. Third-party images remain untracked.')
    return 0

if __name__=='__main__':raise SystemExit(main())
