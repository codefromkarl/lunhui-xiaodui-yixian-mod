#!/usr/bin/env python3
"""Reconcile selected original cards with the cached text and independently inspected faces."""
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / 'data/sources/original-correction'
REALMS = ['炼气', '筑基', '金丹', '元婴', '化神']

def read(path): return json.loads(path.read_text())
def name(text): return text.replace('·', '•')

def main(only=None):
    costs = read(SOURCES / 'original-costs.json')['cards']
    count = 0
    for folder, catalog in [('five-elements', 'five-elements-alliance'), ('seven-stars', 'heptastar-pavilion')]:
        if only and folder != only: continue
        school = read(ROOT / 'data' / folder / 'school.json')
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
            count += 1
        assert len(school['packs']) == 12
        assert [sum(p['rank'] == r for p in school['packs']) for r in range(1, 6)] == [2, 2, 2, 3, 3]
        assert len(school['fates']) == 4 and all(f.get('sourceIconUrl') for f in school['fates'])
    print(f'PASS original sources: {count} real names, realms, level-1 texts, face costs, original PNG art and explicit adaptations')

if __name__ == '__main__': main()
