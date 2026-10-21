#!/usr/bin/env python3
"""Arrange unaltered runtime screenshot crops for visual review, not production art."""
import argparse
import json
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[1]
FONT='/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results',required=True,type=Path)
    parser.add_argument('--out',required=True,type=Path)
    args=parser.parse_args();args.out.mkdir(parents=True,exist_ok=True)
    roster=json.loads((ROOT/'characters.json').read_text())
    font=ImageFont.truetype(FONT,19)
    bg=(11,19,31)
    def source(c,phase):
        return args.results/(phase+'.png' if c['slug']=='muyifeng' else f"character-{c['id']}-{phase}.png")
    for phase in ['idle','move','attack','damaged','death','mirror','restore']:
        for mode,box in [('detail',(565,465,895,770)),('standard',(650,240,820,375))]:
            w,h=box[2]-box[0],box[3]-box[1];sheet=Image.new('RGB',(6*w,3*(h+30)),bg)
            draw=ImageDraw.Draw(sheet)
            for index,c in enumerate(roster):
                frame=Image.open(source(c,phase));x,y=(index%6)*w,(index//6)*(h+30)
                sheet.paste(frame.crop(box),(x,y));draw.text((x+8,y+h+2),c['name'],font=font,fill=(218,230,240))
            sheet.save(args.out/f'{mode}-{phase}.jpg',quality=94)
    beats=['000','022','038','060','082','100']
    representatives=['muyifeng','yanxue','dulingyuan','muhu']
    sheet=Image.new('RGB',(6*330,4*335),bg);draw=ImageDraw.Draw(sheet)
    for row,slug in enumerate(representatives):
        c=next(c for c in roster if c['slug']==slug)
        for col,beat in enumerate(beats):
            frame=Image.open(args.results/f"motion-{c['id']}-{beat}.png");x,y=col*330,row*335
            sheet.paste(frame.crop((565,465,895,770)),(x,y));draw.text((x+8,y+307),c['name']+' / '+beat+'%',font=font,fill=(218,230,240))
    sheet.save(args.out/'four-profile-beats.jpg',quality=94)
    (args.out/'SOURCES.json').write_text(json.dumps({'results':str(args.results),'source_report':'results.json','detail_crop':[565,465,895,770],'standard_crop':[650,240,820,375],'resized':False,'production_images_edited':False},ensure_ascii=False,indent=2)+'\n')
    print(args.out)


if __name__=='__main__':main()
