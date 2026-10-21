#!/usr/bin/env python3
"""Author crop/joint metadata from generated cutouts, without modifying any PNG."""
import argparse
from collections import deque
import copy
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / 'mod/appearance.muyifeng'
CELL_KEYS = ['Body/Head', 'HairBack', 'Cloth/Body', 'Cloth/Left',
             'Cloth/LeftLower', 'Cloth/Right', 'Cloth/RightLower', 'Hand/Grip_L',
             'Hand/Grip_R', 'Hand/Open_L', 'Hand/Open_R', 'Pant/Left',
             'Pant/Right', 'Body/Foot_R', 'Body/Foot_L', 'Weapon/Sword']


def occupied_box(alpha, rect):
    x, y, w, h = rect
    bounds = alpha.crop((x, y, x+w, y+h)).point(lambda a: 255 if a >= 24 else 0).getbbox()
    if bounds is None:
        raise ValueError('Empty source crop: ' + str(rect))
    a, b, c, d = bounds
    return [x+a, y+b, c-a, d-b]


def cutout_boxes(image):
    """Read connected opaque pieces; ignore alpha-only matting fringes."""
    width, height = image.size
    step = 3
    small = image.getchannel('A').resize((width//step, height//step), Image.Resampling.NEAREST)
    w, h = small.size
    pixels = bytearray(small.tobytes())
    chosen = {}
    centers = [.18, .47, .68, .89]
    for index in range(w*h):
        if pixels[index] < 24:
            continue
        pixels[index] = 0
        queue = deque([index])
        area = sx = sy = 0
        left = top = 10**9
        right = bottom = 0
        while queue:
            current = queue.popleft()
            x, y = current % w, current // w
            area += 1; sx += x; sy += y
            left = min(left, x); right = max(right, x)
            top = min(top, y); bottom = max(bottom, y)
            for nx, ny in ((x-1,y), (x+1,y), (x,y-1), (x,y+1)):
                if 0 <= nx < w and 0 <= ny < h:
                    p = ny*w+nx
                    if pixels[p] >= 24:
                        pixels[p] = 0; queue.append(p)
        if area < 40:
            continue
        col = min(3, int((sx/area)/w*4))
        row = min(range(4), key=lambda i: abs((sy/area)/h-centers[i]))
        cell = row*4+col
        if cell in chosen and chosen[cell][0] >= area:
            continue
        x, y = max(0,left*step-4), max(0,top*step-4)
        r, b = min(width,(right+1)*step+4), min(height,(bottom+1)*step+4)
        chosen[cell] = (area, occupied_box(image.getchannel('A'), [x,y,r-x,b-y]))
    missing = [CELL_KEYS[i] for i in range(16) if i not in chosen]
    if missing:
        raise ValueError('Missing isolated atlas cells: ' + ', '.join(missing))
    return {CELL_KEYS[i]: chosen[i][1] for i in range(16)}


def authored_atlas(template, template_image, image, character):
    atlas = copy.deepcopy(template)
    slug = character['slug']
    atlas['sheets'] = {'character': f'art/characters/{slug}/atlas-v1.png'}
    atlas['parts'].pop('Weapon/Offhand')
    atlas['rig'].pop('offhand')
    boxes = cutout_boxes(image)
    original_boxes = {key: occupied_box(template_image.getchannel('A'), part['rect'])
                      for key, part in template['parts'].items()}
    def mapped(key, point):
        rect = template['parts'][key]['rect']
        original = original_boxes[key]
        target = boxes[key]
        return [round((rect[0]+point[0]-original[0])/original[2]*target[2],3),
                round((rect[1]+point[1]-original[1])/original[3]*target[3],3)]
    for key, part in atlas['parts'].items():
        part['rect'] = boxes[key]
        part['pixelsPerUnit'] *= image.height/template_image.height
        for mark, point in part.get('landmarks', {}).items():
            part['landmarks'][mark] = mapped(key, point)
    rig = atlas['rig']
    for key, point in list(rig['head'].items()): rig['head'][key] = mapped('Body/Head',point)
    for key, point in list(rig['body'].items()): rig['body'][key] = mapped('Cloth/Body',point)
    for side, upper, lower in [('Left','Cloth/Left','Cloth/LeftLower'),('Right','Cloth/Right','Cloth/RightLower')]:
        sleeve = rig['sleeve'+side]
        for mark, point in list(sleeve.items()): sleeve[mark] = mapped(upper if mark in ('shoulder','elbow') else lower,point)
        leg = rig['leg'+side]
        foot_key, pant_key = leg['foot'], leg['pant']
        for mark in ('sole','ankle'): leg[mark] = mapped(foot_key,leg[mark])
        w,h = boxes[pant_key][2:]
        leg['hip'] = [w*.5,h*.12]; leg['pantAnkle'] = [w*.5,h*.88]
    # Rear layer excludes duplicate front crowns, horns and ears.
    hair = atlas['parts']['HairBack']
    x,y,w,h = hair['rect']; cut=round(h*.4)
    hair['rect'] = [x,y+cut,w,h-cut]; hair['pivot'] = [.5,.95]
    primary = atlas['parts']['Weapon/Sword']
    w,h = primary['rect'][2:]
    downward = slug in ('lujianxin','lichengyun','wuxingzhi')
    primary['pixelsPerUnit'] = 475 if character['prop']=='sword' else 600
    rig['sword'] = {'grip':[w*.5,h*(.13 if downward else .76)],
                    'angleDegrees':(-65 if downward else 115) if character['prop']=='sword' else 0}
    if slug=='lichengyun': primary['pixelsPerUnit']=600
    if slug=='muhu':
        hair['rect']=boxes['HairBack']; hair['pivot']=[.5,.5];hair['offset']=[0,-.15]
        rig['sword']['grip']=[w*.15,h*.55]
        figure=rig['referenceFigure']
        for key in figure:
            if key!='neck': figure[key][0]=round(280+(figure[key][0]-280)*1.35)
    if character['prop']=='parasol':rig['sword']['grip']=[w*.5,h*.9]
    return atlas


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--complete',action='store_true',help='Require all planned sources before writing config')
    args=parser.parse_args()
    characters=json.loads((ROOT/'characters.json').read_text())
    missing=[c['name'] for c in characters[1:] if not (MOD/'art/characters'/c['slug']/'atlas-v1.png').is_file()]
    if args.complete and missing:parser.error('Missing generated sources: '+', '.join(missing))
    template=json.loads((MOD/'art/characters/yanxue/atlas-layout-v1.json').read_text())
    entries=[]
    with Image.open(MOD/'art/characters/yanxue/atlas-v1.png') as template_image:
        for character in characters[1:]:
            slug=character['slug'];directory=MOD/'art/characters'/slug;source=directory/'atlas-v1.png'
            if not source.is_file():continue
            with Image.open(source) as image:
                if image.mode!='RGBA' or image.getchannel('A').getextrema()!=(0,255):raise ValueError('Genuine alpha required: '+slug)
                layout=directory/'atlas-layout-v1.json'
                atlas=json.loads(layout.read_text()) if layout.is_file() else authored_atlas(template,template_image,image,character)
            for part in atlas['parts'].values():part['alphaThreshold']=24
            # Optional manual per-character joint/art-direction corrections are
            # authored in the canonical roster, never baked into altered pixels.
            def merge(target, changes):
                for key,value in changes.items():
                    if isinstance(value,dict) and isinstance(target.get(key),dict):merge(target[key],value)
                    else:target[key]=copy.deepcopy(value)
            merge(atlas,character.get('atlasOverrides',{}))
            (directory/'atlas.json').write_text(json.dumps(atlas,ensure_ascii=False,indent=2)+'\n')
            prompt=ROOT/'output/three-sects-prompts'/(slug+'-v1.txt')
            if prompt.is_file():(directory/'prompt-v1.txt').write_text(prompt.read_text())
            provenance={'character':character['name'],'sourceId':character['sourceId'],'referenceUrl':character['sourceUrl'],
                'referenceImageUrl':character['referenceImageUrl'],'referenceKind':'community reference of original default character art',
                'generationTool':'built-in image_gen','styleReference':'approved Muyifeng pixel cutouts and Yan Xue atlas layout',
                'generatedSource':'atlas-v1.png','prompt':'prompt-v1.txt','pixelEdits':False,'metadata':'atlas.json'}
            (directory/'provenance.json').write_text(json.dumps(provenance,ensure_ascii=False,indent=2)+'\n')
            entries.append({**{k:character[k] for k in ('id','name','group','sourceId','token')},'spritePrefix':'Yixian/'+slug,'atlasSource':f'art/characters/{slug}/atlas.json'})
    config=json.loads((MOD/'appearance.json').read_text());config['characters']=entries
    (MOD/'appearance.json').write_text(json.dumps(config,ensure_ascii=False,indent=2)+'\n')
    print(f'Described {len(entries)} new atlases; missing {len(missing)}; PNG pixels unchanged.')

if __name__=='__main__':main()
