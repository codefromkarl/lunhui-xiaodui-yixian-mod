#!/usr/bin/env python3
"""Convert cached original card/fate WebP to PNG; never redraw original artwork."""
import json
from pathlib import Path
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
SOURCES=ROOT/'data/sources/original-correction'
MOD=ROOT/'mod/yixian.sect'
def main():
    count=0
    import build_prototype_art
    build_prototype_art.main()
    for path in sorted((ROOT/'data').glob('*/school.json')):
        school=json.loads(path.read_text());code=school['definition']['code']
        for card in school['cards']:
            if not card.get('sourceId'):continue
            source=SOURCES/'card-images'/(str(card['sourceId'])+'.webp')
            target=MOD/'icons/cards'/(card['key']+'.png');target.parent.mkdir(parents=True,exist_ok=True)
            with Image.open(source) as image:image.save(target)
            count+=1
        for fate in school.get('fates',[]):
            if not fate.get('sourceIconUrl'):raise ValueError('原作仙命图来源缺失：'+fate['name'])
            source=SOURCES/'fate-images'/fate['sourceIconUrl'].rsplit('/',1)[-1]
            target=MOD/'icons/fates'/f'{code}_{fate["key"]}.png';target.parent.mkdir(parents=True,exist_ok=True)
            with Image.open(source) as image:image.save(target)
    print(f'Prepared {count} original card faces and eight original fate icons (format conversion only)')
if __name__=='__main__':main()
