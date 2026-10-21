#!/usr/bin/env python3
"""Check canonical content and retained sword gameplay against historical rule fixtures."""
import json
from pathlib import Path
import re
from card_costs import cost_text, refresh_sword_costs
from identity_ids import SWORD, card_id, pack_id

ROOT = Path(__file__).resolve().parents[1]
MOD = ROOT / 'mod/yixian.sect'

# User-approved icon corrections; only content identities and their icon keys migrate.
FATE_ICONS = {
    'icon.yixian.sect.fate.sword.sword_rhyme': 'icons/fates/Icon_Talent_16.png',
    'icon.yixian.sect.fate.sword.cloud_way': 'icons/fates/Icon_Talent_69.png',
    'icon.yixian.sect.fate.sword.formation_guard': 'icons/fates/Icon_Talent_57.png',
    'icon.yixian.sect.fate.sword.cloud_sea': 'icons/fates/Icon_Talent_14.png',
    'icon.yixian.sect.status.fate_guard': 'icons/fates/Icon_Talent_57.png',
}

# Independent acceptance of the user-approved first four area adaptations. This is
# not a blanket exemption for original rule changes or for other cards.
SWORD_AREAS = {
    'yixian_sword_2': dict(shape='line', linked=True, secondaryBp=5000, maxTargets=1),
    'yixian_sword_9': dict(shape='splash', secondaryBp=4000, maxTargets=2),
    'yixian_sword_14': dict(shape='row', linked=True, secondaryBp=5000),
    'yixian_sword_46': dict(shape='radius', secondaryBp=3000),
}

def read(path): return json.loads(path.read_text())

def main():
    baseline = ROOT / 'tests/fixtures/sword-v032'
    table = read(MOD / 'content/cardtable.json')
    old_table = cost_text(read(baseline / 'content/cardtable.json'))
    old_abilities = cost_text(read(baseline / 'content/abilities.json'))
    # Historical fixtures check gameplay, not save compatibility. Associate their active
    # rows by card name/key so the approved fee refresh needs no old-ID mapping table.
    keys_by_name = {c['name']: c['key'] for c in old_table['cards']}
    for row in old_abilities['operations']:
        fields = row['fields']
        if fields['mode'] == 10:
            row['id'] = card_id(keys_by_name[fields['displayName']])
    # The historical fixture omitted/misread card-face fees. Normalize only this
    # approved correction from the independent original-art ledger; retain every
    # other gameplay field and do not rewrite the fixture itself.
    original_costs = read(ROOT / 'data/sources/original-correction/original-costs.json')['cards']
    for card in old_table['cards']:
        card['rules']['cost'] = original_costs[card['sourceId']]['cost']
    refresh_sword_costs(old_table, old_abilities)
    assert {c['key']: c['area'] for c in table['cards'] if 'area' in c} == SWORD_AREAS, 'unauthorized area adaptation'
    for card in old_table['cards']:
        if card['key'] in SWORD_AREAS:
            card['area'] = SWORD_AREAS[card['key']]
    for name in ('cards', 'packs'):
        current = [row for row in table[name] if row.get('sect', SWORD) == SWORD]
        # Presentation is intentionally rewritten; every gameplay and provenance field stays exact.
        def without_copy(row): return {k: v for k, v in row.items() if k not in ('description', 'summary')}
        assert [without_copy(row) for row in current] == [without_copy(row) for row in old_table[name]], f'sword {name} gameplay changed'
    asset_references = []
    def gameplay(row):
        result = dict(row, fields=dict(row['fields']))
        if row['kind'] in ('abilities', 'lineages'): result.pop('id')
        for key in ('iconKey', 'sourceContentId', 'flavour'): result['fields'].pop(key, None)
        return result
    for name in ('abilities', 'lineages', 'statuses', 'fates', 'fate-statuses'):
        old = (old_abilities if name == 'abilities' else cost_text(read(baseline / 'content' / (name + '.json'))))['operations']
        current = read(MOD / 'content' / (name + '.json'))['operations']
        assert len({row['id'] for row in current}) == len(current), f'duplicate {name} identity'
        assert len(current) >= len(old), f'missing sword {name}'
        # Sword declarations precede appended school declarations; preserve that order.
        for previous, row in zip(old, current):
            assert gameplay(row) == gameplay(previous), f'sword gameplay changed: {previous["fields"]["displayName"]}'
            if 'iconKey' in previous['fields']:
                asset_references.append((previous['fields']['iconKey'], row['fields']['iconKey']))
    manifest = read(MOD / 'mod.json')
    assert manifest['id'] == 'yixian.sect'
    declarations = [row for name in ('abilities', 'lineages', 'statuses', 'fates', 'fate-statuses')
                    for row in read(MOD / 'content' / (name + '.json'))['operations']]
    all_ids = {row['id'] for row in declarations}
    assert len(all_ids) == len(declarations), 'duplicate identity across content tables'
    assert all(row['id'].startswith('yixian.sect.') for row in declarations if row['op'] == 'add'), 'non-namespaced content declaration'
    assert 'legacyIds' not in table and 'identityVersion' not in table, 'legacy compatibility payload must not ship'
    assert {s['id'] for s in table['sects']} == {'yixian.sect.lineage.' + school for school in ('sword', 'five', 'star')}
    for pack in table['packs']: assert pack_id(pack['key']) in all_ids, f'noncanonical pack identity: {pack["key"]}'
    for row in declarations:
        fields = row['fields']
        if 'sourceContentId' in fields: assert fields['sourceContentId'] in all_ids
        if 'iconKey' in fields: assert fields['iconKey'] in manifest['assets']
    assert re.search(r'<Version>([^<]+)</Version>', (ROOT / 'src/YixianSect.csproj').read_text())[1] == manifest['version'], 'assembly/manifest version drift'
    original_assets = read(baseline / 'mod.json')['assets']
    referenced = set()
    for old_icon, icon in asset_references:
        expected = dict(kind='icon', file=FATE_ICONS[icon]) if icon in FATE_ICONS else original_assets[old_icon]
        assert manifest['assets'][icon] == expected, f'sword asset changed: {icon}'
        referenced.add(old_icon)
    for name, asset in original_assets.items():
        if name not in referenced: assert manifest['assets'][name] == asset, f'unreferenced asset changed: {name}'
    for name, file in FATE_ICONS.items():
        assert manifest['assets'].get(name) == dict(kind='icon', file=file), f'original fate icon missing: {name}'
    for asset in manifest['assets'].values(): assert (MOD / asset['file']).is_file(), asset
    assert len(set(p['index'] for p in table['packs'])) == len(table['packs'])
    assert all(p['index'] != 50 for p in table['packs']), 'deleted common-pack identity reused'
    by_key = {c['key']: c for c in table['cards']}
    assert len(by_key) == len(table['cards'])
    memberships = []
    for pack in table['packs']:
        assert len(pack['cards']) == len(set(pack['cards'])) == 4
        for key in pack['cards']:
            card = by_key[key]; memberships.append(key)
            assert card['rank'] == pack['rank'] and card.get('sect', SWORD) == pack.get('sect', SWORD)
            assert card['name'] in pack['description'] and card['description'] in pack['description']
    assert sorted(memberships) == sorted(by_key), 'missing, duplicate or unreferenced cards'
    assert table['playBaseCost'] == 0, 'universal base fee returned'
    assert {4 * c['rules']['cost'] for c in table['cards'] if c.get('sect', SWORD) == SWORD} == {0, 4, 8, 12}, 'sword original printed costs lost their tiers'
    abilities = {o['id']: o for o in read(MOD / 'content/abilities.json')['operations']}
    for card in table['cards']:
        cost = card['rules'].get('cost', 0)
        assert type(cost) is int and cost >= 0, f'invalid lingqi fee: {card["key"]}'
        ability = abilities[card_id(card['key'])]
        for rank in ability['fields']['ranks']:
            values = {v['key']: v.get('intValue') for v in rank['operation']['values']}
            assert values['energyCost'] == cost * 4 and values['apCost'] == 0 and values['energyPool'] == 40, f'card payment declaration mismatch: {card["key"]}'
        fee_line = next(line for line in card['description'].splitlines() if line.startswith('消耗：'))
        assert fee_line == f'消耗：{cost * 4}斗气。', f'card displayed cost mismatch: {card["key"]}'
    print(f'PASS contracts: namespaced identities; sword rules retained with four explicit area adaptations and approved fee/copy correction; original fate icons and other assets retained; {len(table["sects"])} schools, {len(by_key)} cards, {len(table["packs"])} complete packs; version {manifest["version"]}')

if __name__ == '__main__': main()
