#!/usr/bin/env python3
"""Generate one package from project-local authoritative school data. Never installs."""
import argparse
import json
from school_content import compile_school
from card_costs import cost_text, refresh_sword_costs
from identity_ids import SWORD
from presentation_copy import compact_sword, rules_for
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / 'mod/yixian.sect'

def read(path):
    return json.loads(path.read_text())

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')

def main():
    global MOD
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mod-root', type=Path, default=MOD)
    MOD = parser.parse_args().mod_root.resolve()
    sword = ROOT / 'data/sword'
    manifest = cost_text(read(sword / 'manifest.json'))
    table = cost_text(read(sword / 'cardtable.json'))
    table['sects'] = [dict(id=SWORD, name='云灵剑宗', code='yixian_sword', statePrefix='sword', summary='云剑连云、剑意爆发、灵剑回气', rulesDescription=rules_for('sword'))]
    content = {name: cost_text(read(sword / (name + '.json'))) for name in ('abilities', 'lineages', 'statuses', 'fates', 'fate-statuses')}
    refresh_sword_costs(table, content['abilities'])
    compact_sword(table, content)
    import copy
    maximum_status = copy.deepcopy(content['statuses']['operations'][0])
    maximum_status['id'] = 'yixian.sect.status.maximum_adjustment'
    maximum_status['fields'].update(displayName='本场生命上限', flavour='原作卡牌造成的本场最大生命变化；不会直接改写血统或局外存档属性。', iconKey='icon.yixian.sect.status.maximum_adjustment', maximumStacks=1)
    content['statuses']['operations'].append(maximum_status)
    manifest['assets']['icon.yixian.sect.status.maximum_adjustment'] = dict(kind='icon', file='icons/lineage.yixian_star.png')
    table['fates'] = []
    for path in sorted((ROOT / 'data').glob('*/school.json')):
        school = compile_school(read(path), content)
        sect = school['definition']
        table['sects'].append(sect)
        table['fates'].extend(dict(f, sect=sect['id']) for f in school['fateDefinitions'])
        for name in ('cards', 'packs'):
            for row in school[name]:
                row['sect'] = sect['id']
                table[name].append(row)
        for name in content:
            content[name]['operations'].extend(school.get(name, {}).get('operations', []))
        manifest['assets'].update(school.get('assets', {}))
    from water_pilot_content import compile_pilot
    pilot=read(ROOT / 'data/five-water-pilot.json')
    compile_pilot(table,content,manifest,pilot)
    write(MOD / 'content/five-water-pilot.json',pilot)
    # Existing sword order is not sorted/rebuilt: cp~ native ordinals must stay stable.
    table['sectLineages'] = [s['id'] for s in table['sects']]
    for name, value in content.items():
        write(MOD / 'content' / (name + '.json'), value)
    write(MOD / 'content/cardtable.json', table)
    if len(table['sects']) > 1:
        manifest['version'] = '0.7.0'
        manifest['name'] = '弈仙门派'
        manifest['description'] = '云灵剑宗、五行道盟、七星阁三大门派，涵盖五个境界，共144张卡牌、36个卡包与12项仙命。每名角色可加入一个弈仙门派，并搭配原生血统。'
    manifest['economy'] = dict(shop=[dict(id=s['id'], conditions=dict(forbids=[other['id'] for other in table['sects'] if other['id'] != s['id']])) for s in table['sects']])
    write(MOD / 'mod.json', manifest)
    lines = ['# 弈仙门派当前卡包与规则', '', '三个门派各五境界48张牌、12个四张包、4个独立仙命。', '']
    lines.extend(['五行纯水试点新增8项水系单卡购买，原混系卡包保留；已购五行牌可自由选入抽牌池。详见 [纯水试点](pure-water-pilot-results.md)。', ''])
    for sect in table['sects']:
        lines.extend(['## ' + sect['name'], '', sect['summary'], '', sect['rulesDescription'], ''])
        for fate in table['fates']:
            if fate['sect'] == sect['id']:
                lines.extend(['### 仙命 · ' + fate['name'] + f'（{fate["price"]}奖励点）', '', fate['description'], ''])
        for pack in table['packs']:
            if pack.get('sect', SWORD) == sect['id']:
                lines.extend(['### ' + pack['name'], '', pack['description'], ''])
    (ROOT / 'docs/card-packs.md').write_text('\n'.join(lines))
    for key, asset in manifest['assets'].items():
        if not (MOD / asset['file']).is_file():
            raise FileNotFoundError(f'{key}: {asset["file"]}')
    print(f'Generated {len(table["sects"])} sects / {len(table["cards"])} cards / {len(table["packs"])} packs')

if __name__ == '__main__':
    main()
