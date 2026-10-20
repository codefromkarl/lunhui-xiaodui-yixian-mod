#!/usr/bin/env python3
"""Cache verifiable original card pages/art. Never edits runtime content or installs."""
import concurrent.futures
import json
from pathlib import Path
import urllib.request
from bs4 import BeautifulSoup
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'data/sources/original-correction'
BASE='https://sharpobject.github.io/yxp_wiki'
def fetch(card):
    path=OUT/'card-images'/(card['id']+'.webp');path.parent.mkdir(parents=True,exist_ok=True)
    if not path.exists():path.write_bytes(urllib.request.urlopen(BASE+'/assets/cards/'+card['id']+'_zh.webp',timeout=25).read())
    page=OUT/'card-pages'/(card['id']+'.html');page.parent.mkdir(parents=True,exist_ok=True)
    if not page.exists():page.write_bytes(urllib.request.urlopen(card['url'],timeout=25).read())
    soup=BeautifulSoup(page.read_text(),'html.parser')
    levels=[]
    for row in soup.select('article.level-card'):
        levels.append(dict(level=row.select_one('.level-card-head strong').get_text(strip=True),text=row.select_one('p').get_text('\n',strip=True)))
    return {**card,'levels':levels,'imageUrl':BASE+'/assets/cards/'+card['id']+'_zh.webp'}
def main():
    for sect in ['heptastar-pavilion','five-elements-alliance']:
        cards=json.loads((OUT/(sect+'-cards.json')).read_text())
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:rows=list(pool.map(fetch,cards))
        (OUT/(sect+'-cards.json')).write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n')
        print(sect,len(rows),'original pages and art cached',flush=True)
if __name__=='__main__':main()
