#!/usr/bin/env python3
"""Prepare Mod status glyphs and preview sheets; never paint over sourced card/fate art."""
import json
import os
import math
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_FONT = ('/System/Library/Fonts/STHeiti Medium.ttc' if sys.platform == 'darwin'
                else '/usr/share/fonts/opentype/noto/NotoSansCJK-Medium.ttc')
FONT = os.environ.get('YIXIAN_FONT', DEFAULT_FONT)
COLORS = ['#d7bb78', '#79bed5', '#8fc6a2', '#e89070', '#cbb18e']

def build_five_status_icons():
    directory = ROOT / 'mod/yixian.sect/icons/statuses'
    directory.mkdir(parents=True, exist_ok=True)
    for key, glyph, color in zip(('metal', 'water', 'wood', 'fire', 'earth'), '金水木火土', COLORS):
        im = Image.new('RGBA', (128, 128), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
        d.rounded_rectangle((4, 4, 123, 123), radius=22, fill='#192320', outline=color, width=6)
        d.text((64, 61), glyph, font=ImageFont.truetype(FONT, 88), fill=color, anchor='mm')
        im.resize((64, 64), Image.Resampling.LANCZOS).save(directory / ('five_' + key + '.png'))

def main():
    build_five_status_icons()
    outputs = []
    icon_outputs = []
    for path in sorted((ROOT / 'data').glob('*/school.json')):
        school = json.loads(path.read_text()); star = school['definition']['statePrefix'] == 'star'
        prefix = school['definition']['statePrefix']; color = '#b8b2e5' if star else '#9dc9a7'
        view_rows=school['views']+[[row['key'],row['name'],''] for row in school.get('combatStatuses',[]) if row['key'] not in {v[0] for v in school['views']}]
        for view, title, _ in view_rows:
            # Keep the five large single-element badges exactly as previously approved.
            if prefix == 'five' and view in ('metal','water','wood','fire','earth'): continue
            path = ROOT / 'mod/yixian.sect' / school.get('viewIcons', {}).get(view, f'icons/statuses/{prefix}_{view}.png')
            path.parent.mkdir(parents=True,exist_ok=True)
            icon = Image.new('RGBA',(128,128),(0,0,0,0)); pen = ImageDraw.Draw(icon)
            pen.rounded_rectangle((4,4,123,123),radius=22,fill='#131d2a' if star else '#192320',outline=color,width=6)
            pen.text((64,61),title[:2],font=ImageFont.truetype(FONT,49),fill=color,anchor='mm')
            icon.save(path);icon_outputs.append(icon)
        for fate in school.get('fates',[]):
            if fate.get('sourceIconUrl'): continue
            path=ROOT/'mod/yixian.sect/icons/fates'/f'{school["definition"]["code"]}_{fate["key"]}.png';path.parent.mkdir(parents=True,exist_ok=True)
            icon=Image.new('RGBA',(128,128),(0,0,0,0));pen=ImageDraw.Draw(icon)
            pen.regular_polygon((64,64,59),n_sides=7 if star else 5,rotation=0,fill='#131d2a' if star else '#192320',outline=color,width=4)
            pen.text((64,56),fate['name'][:2],font=ImageFont.truetype(FONT,43),fill=color,anchor='mm')
            pen.text((64,91),'仙命',font=ImageFont.truetype(FONT,19),fill='#e1ddd0',anchor='mm')
            icon.save(path);icon_outputs.append(icon)
        for index, card in enumerate(school['cards']):
            if card.get('sourceId'):
                # Original card faces are prepared by prepare_original_assets.py, never painted over.
                if (ROOT/'mod/yixian.sect/icons/cards'/(card['key']+'.png')).exists(): outputs.append(Image.open(ROOT/'mod/yixian.sect/icons/cards'/(card['key']+'.png')).convert('RGB').resize((154,254)))
                continue
            im = Image.new('RGB', (308, 508), '#131d2a' if star else '#192320'); d = ImageDraw.Draw(im)
            color = '#b8b2e5' if star else COLORS[((card.get('element') or index + 1) - 1) % 5]
            d.rounded_rectangle((14, 14, 293, 493), radius=14, outline=color, width=3)
            d.rectangle((24, 25, 283, 482), outline='#4f5c60', width=1)
            d.ellipse((54, 120, 254, 320), outline=color, width=3)
            if star:
                points = [(154 + 82 * math.cos(i * math.tau / 7 - 1.5), 220 + 82 * math.sin(i * math.tau / 7 - 1.5)) for i in range(7)]
                d.line(points + [points[0]], fill='#5c618e', width=2)
                for i, (x, y) in enumerate(points):
                    d.ellipse((x-5, y-5, x+5, y+5), fill=color if i <= index % 7 else '#46546a')
                glyph = card.get('artGlyph') or (['引', '卦', '星', '守', '落', '斗', '观', '移'][index] if index < 8 else card['name'][:2])
            else:
                points = [(154 + 83 * math.cos(i * math.tau / 5 - math.pi / 2), 220 + 83 * math.sin(i * math.tau / 5 - math.pi / 2)) for i in range(5)]
                for i, (x, y) in enumerate(points):
                    nx, ny = points[(i+1) % 5]; d.line((x,y,nx,ny), fill='#586b5d', width=2)
                    d.ellipse((x-7,y-7,x+7,y+7), fill=COLORS[i])
                glyph = card.get('artGlyph') or (['金', '水', '木', '火', '土', '气', '生', '元'][index] if index < 8 else card['name'][:2])
            d.text((154, 211), glyph, font=ImageFont.truetype(FONT, 66 if len(glyph)==1 else 47), fill=color, anchor='mm')
            d.text((154, 62), '七 星 阁' if star else '五 行 道 盟', font=ImageFont.truetype(FONT, 24), fill=color, anchor='mm')
            title = card['name'].replace('·', '\n')
            d.multiline_text((154, 365), title, font=ImageFont.truetype(FONT, 29), fill='#e1ddd0', anchor='mm', align='center', spacing=10)
            d.text((154, 457), ['炼气','筑基','金丹','元婴','化神'][card['rank']-1] + ' · 门 派 适 配', font=ImageFont.truetype(FONT, 16), fill='#a2afa9', anchor='mm')
            im = im.resize((154, 254), Image.Resampling.LANCZOS)
            out = ROOT / 'mod/yixian.sect/icons/cards' / (card['key'] + '.png'); im.save(out); outputs.append(im)
    if outputs:
        sheet = Image.new('RGB', (154*8, 254*((len(outputs)+7)//8)), '#171c21')
        for i, im in enumerate(outputs): sheet.paste(im, ((i%8)*154, (i//8)*254))
        (ROOT / 'logs').mkdir(exist_ok=True); sheet.save(ROOT / 'logs/prototype-art.png')
    if icon_outputs:
        sheet=Image.new('RGBA',(128*8,128*((len(icon_outputs)+7)//8)), '#171c21')
        for i,icon in enumerate(icon_outputs):sheet.paste(icon,((i%8)*128,(i//8)*128),icon)
        sheet.save(ROOT/'logs/sect-expansion-icons.png')
    print(f'Previewed {len(outputs)} school card faces; rendered {len(icon_outputs)} Mod state/fate icons')

if __name__ == '__main__':
    main()
