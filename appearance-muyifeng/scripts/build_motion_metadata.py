#!/usr/bin/env python3
"""Describe new state cutouts and presentation profiles; never rewrite source PNGs."""
import copy
import json
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
MOD=ROOT/'mod/appearance.muyifeng'


def occupied(source, rect):
    x,y,w,h=rect
    with Image.open(MOD/source) as im:
        box=im.getchannel('A').crop((x,y,x+w,y+h)).point(lambda a:255 if a>=24 else 0).getbbox()
    if box is None:raise ValueError('Empty cutout: '+source)
    a,b,c,d=box
    return [x+a,y+b,c-a,d-b]


def opaque_marker(source,rect,point,radius=.2):
    x,y,w,h=rect
    with Image.open(MOD/source) as im:
        alpha=im.getchannel('A')
        candidates=[]
        for yy in range(max(2,int(point[1])-int(h*radius)), min(h-2,int(point[1])+int(h*radius))):
            for xx in range(max(2,int(point[0])-int(w*radius)),min(w-2,int(point[0])+int(w*radius))):
                if all(alpha.getpixel((x+xx+dx,y+yy+dy))>=160 for dx,dy in [(0,0),(-1,0),(1,0),(0,-1),(0,1)]):
                    candidates.append(((xx-point[0])**2+(yy-point[1])**2,[xx+.5,yy+.5]))
    if not candidates:raise ValueError('No visible neck/handle at '+source)
    return min(candidates)[1]


def enrich(atlas,character,roster=None,direction=None):
    roster=roster or json.loads((ROOT/'characters.json').read_text())
    direction=direction or json.loads((ROOT/'motion-art.json').read_text())
    slug=character['slug'];index=next(i for i,c in enumerate(roster) if c['slug']==slug)
    profile=direction['characters'][slug]
    atlas['rig']['presentation']={'kind':profile['kind'],'hanging':profile.get('hanging',False)}
    # Full source files remain byte-for-byte, only the selected head crop changes.
    if 'headSource' in profile:
        atlas['sheets']['veil']=profile['headSource'];atlas['parts']['Body/Head']['sheet']='veil'
    base=atlas['parts']['Body/Head'];base_neck=atlas['rig']['head']['neck']
    for state,source in direction['stateSheets'].items():
        atlas['sheets'][state]=source
        with Image.open(MOD/source) as im:w,h=im.size
        col,row=index%6,index//6
        left,right=round(w*col/6),round(w*(col+1)/6)
        top,bottom=round(h*direction['rowFractions'][row]),round(h*direction['rowFractions'][row+1])
        rect=occupied(source,[left,top,right-left,bottom-top]);_,_,cw,ch=rect
        # The state reference is an already-mirrored game render; mirror the new
        # cutout back to the original atlas's canonical left-facing bone frame.
        neck=opaque_marker(source,rect,[(1-base_neck[0]/base['rect'][2])*cw,base_neck[1]/base['rect'][3]*ch])
        ppu=base['pixelsPerUnit']*neck[1]/base_neck[1]
        atlas['parts']['Face/'+state]={'sheet':state,'rect':rect,'pivot':[.5,.5],
            'pixelsPerUnit':round(ppu,4),'alphaThreshold':24,'mirrorX':True,'componentSeed':neck,'landmarks':{'neck':neck}}
    prop=atlas['parts']['Weapon/Sword'];data=atlas['rig']['sword']
    if 'propSource' in profile:
        source=profile['propSource'];atlas['sheets']['prop-v2']=source
        with Image.open(MOD/source) as im:rect=occupied(source,[0,0,*im.size])
        _,_,w,h=rect
        # A short hanging lantern rather than an oversized staff. The handle is
        # the grip, and the illuminated paper is the source of cast/projectile FX.
        prop.update(sheet='prop-v2',rect=rect,pixelsPerUnit=round(h/profile['propHeight'],4),pivot=[.5,.5])
        prop.pop('rotation',None)
        grip=opaque_marker(source,rect,[w*.5,h*.025])
        data.update(grip=grip,angleDegrees=0,emitter=[w*.50,h*.48])
    else:
        w,h=prop['rect'][2:]
        grip=data['grip']
        target=[w*.5,h*(.06 if grip[1]>h*.5 else .93)] if character['prop']=='sword' else [w*.5,h*.4]
        data['emitter']=opaque_marker(atlas['sheets'][prop['sheet']],prop['rect'],target,radius=1)
    data['grip']=opaque_marker(atlas['sheets'][prop['sheet']],prop['rect'],data['grip'],radius=1)
    if 'propHeight' in profile:prop['pixelsPerUnit']=round(prop['rect'][3]/profile['propHeight'],4)
    if 'offhand' in atlas['rig']:
        w,h=atlas['parts']['Weapon/Offhand']['rect'][2:]
        part=atlas['parts']['Weapon/Offhand']
        atlas['rig']['offhand']['emitter']=opaque_marker(atlas['sheets'][part['sheet']],part['rect'],[w*.5,h*.06],radius=1)
        atlas['rig']['offhand']['grip']=opaque_marker(atlas['sheets'][part['sheet']],part['rect'],atlas['rig']['offhand']['grip'],radius=1)
    return atlas


def main():
    roster=json.loads((ROOT/'characters.json').read_text());direction=json.loads((ROOT/'motion-art.json').read_text())
    for c in roster:
        p=MOD/('art/muyifeng-sprites.json' if c['slug']=='muyifeng' else f"art/characters/{c['slug']}/atlas.json")
        atlas=json.loads(p.read_text());enrich(atlas,c,roster,direction)
        p.write_text(json.dumps(atlas,ensure_ascii=False,indent=2)+'\n')
    print('Described 18 presentation profiles / 36 facial states / lantern and veil; no PNG pixels changed.')


if __name__=='__main__':main()
