#!/usr/bin/env python3
"""Reconcile selected original cards with the cached text and independently inspected faces."""
import json
import hashlib
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / 'data/sources/original-correction'
REALMS = ['炼气', '筑基', '金丹', '元婴', '化神']

def read(path): return json.loads(path.read_text())
def name(text): return text.replace('·', '•')

def check_face_costs(cards, costs, prefix, count=48):
    selected = [c for c in cards if str(c.get('sourceId', '')).startswith(prefix)]
    evidence_ids = {sid for sid in costs if sid.startswith(prefix)}
    assert len(selected) == count and {str(c['sourceId']) for c in selected} == evidence_ids, 'original cost evidence must cover all selected cards exactly: ' + prefix
    for card in selected:
        source = costs[str(card['sourceId'])]
        assert name(card['name']) == name(source['name']), card['name']
        assert 'cost' in card['rules'], 'original cost must be explicit, never inferred from missing text: ' + card['name']
        assert card['rules']['cost'] == source['cost'], 'original face cost mismatch: ' + card['name']
    return selected

def check_sword_costs(cards, costs):
    return check_face_costs(cards, costs, '100')

def main(only=None):
    costs = read(SOURCES / 'original-costs.json')['cards']
    count = 0
    if only is None:
        check_sword_costs(read(ROOT / 'data/sword/cardtable.json')['cards'], costs)
        sword = check_sword_costs(read(ROOT / 'mod/yixian.sect/content/cardtable.json')['cards'], costs)
        for card in sword:
            source = costs[str(card['sourceId'])]
            with Image.open(ROOT / 'mod/yixian.sect/icons/cards' / (card['key'] + '.png')) as image:
                assert list(image.size) == source['imageSize'] and hashlib.sha256(image.convert('RGBA').tobytes()).hexdigest() == source['imagePixelSha256'], 'inspected sword source artwork changed: ' + card['name']
        count += len(sword)
    for folder, catalog in [('five-elements', 'five-elements-alliance'), ('seven-stars', 'heptastar-pavilion')]:
        if only and folder != only: continue
        school = read(ROOT / 'data' / folder / 'school.json')
        prefix = '700' if folder == 'five-elements' else '400'
        check_face_costs(school['cards'], costs, prefix)
        check_face_costs(read(ROOT / 'mod/yixian.sect/content/cardtable.json')['cards'], costs, prefix)
        original = {c['id']: c for c in read(SOURCES / (catalog + '-cards.json'))}
        assert len(school['cards']) == 48
        assert len({str(c['sourceId']) for c in school['cards']}) == 48
        for card in school['cards']:
            sid = str(card['sourceId']); source = original[sid]
            assert name(card['name']) == name(source['name']), card['name']
            assert card['rank'] == REALMS.index(source['realm']) + 1 and card['realm'] == source['realm'], card['name']
            assert card['originalText'] == source['original'] and card['sourceUrl'] == source['url'], card['name']
            assert card['sourceLevel'] == 1, card['name']
            assert card['rules'].get('cost', 0) == costs[sid]['cost'], card['name']
            evidence = card.get('costEvidence') or card.get('costSource')
            assert card.get('adaptation') and evidence and '待核' not in evidence, card['name']
            forbidden = ['starSpend', 'chargeGain', 'orbitGain', 'edgeGain', 'flameGain', 'growthGain', 'wardGain']
            assert not any(card['rules'].get(key) for key in forbidden), 'retired invented mechanic: ' + card['name']
            raw = SOURCES / 'card-images' / (sid + '.webp')
            asset = ROOT / 'mod/yixian.sect/icons/cards' / (card['key'] + '.png')
            with Image.open(raw) as before, Image.open(asset) as after:
                assert before.size == after.size and after.format == 'PNG', 'original artwork was resized/replaced: ' + card['name']
                pixels = before.convert('RGBA').tobytes()
                assert pixels == after.convert('RGBA').tobytes(), 'original artwork pixels changed: ' + card['name']
                assert list(before.size) == costs[sid]['imageSize'] and hashlib.sha256(pixels).hexdigest() == costs[sid]['imagePixelSha256'], 'inspected original artwork changed: ' + card['name']
            count += 1
        assert len(school['packs']) == 12
        assert [sum(p['rank'] == r for p in school['packs']) for r in range(1, 6)] == [2, 2, 2, 3, 3]
        assert len(school['fates']) == 4 and all(f.get('sourceIconUrl') for f in school['fates'])
    if only is None:
        print(f'PASS original sources: {count} original card-face costs with pixel hashes; 96 five/star names, realms, level-1 texts, original PNG art and explicit adaptations')
    else:
        print(f'PASS original sources: {count} school cards, original names/realms/text/costs/art/adaptations')

if __name__ == '__main__': main()
