"""Native-sized attribute budgets, independent allocation and retained economics."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from school_content import compile_school


def read(path):
    return json.loads((ROOT / path).read_text())


class LineageStatsTests(unittest.TestCase):
    def test_exact_source_generated_and_native_sized_budgets(self):
        expected = read('tests/fixtures/lineage-stats.json')
        generated = read('mod/yixian.sect/content/lineages.json')['operations']
        sword = dict(lineages=read('data/sword/lineages.json'), statuses=read('data/sword/statuses.json'))
        source = sword['lineages']['operations'] + [compile_school(read('data/' + name + '/school.json'), sword)['lineages']['operations'][0]
                                                     for name in ('five-elements', 'seven-stars')]
        self.assertEqual([o['fields']['steps'] for o in source], [o['fields']['steps'] for o in generated])
        for row in generated:
            school = row['id'].rsplit('.', 1)[-1]
            spec = expected['schools'][school]
            self.assertEqual(row['fields']['energyPoolKind'], 40)
            for i, step in enumerate(row['fields']['steps']):
                values = {v['key']: v.get('intValue', v.get('stringValue')) for v in step['passive']['values']}
                attrs = [values.get(k, 0) for k in ('muscleFlat', 'reactionFlat', 'constitutionFlat', 'energyFlat')]
                if school == 'five':
                    self.assertEqual(attrs, [0, 0, 0, 0])
                    self.assertEqual(spec['primary'][i] + spec['support'][i], expected['attributeBudget'][i])
                else:
                    self.assertEqual(attrs, spec['attributes'][i])
                    self.assertEqual(sum(attrs), expected['attributeBudget'][i])
                self.assertEqual(values['maximumMagic'], spec['poolBase'][i])
                self.assertEqual(values['maximumHealthBp'], spec['healthBp'][i])
                self.assertEqual(step['price'], expected['prices'][i])
        five = read('data/five-elements/school.json')['definition']['cultivation']
        self.assertEqual(five, dict(fallbackBp=7000, growth=expected['attributeBudget'], primaryGrowth=expected['schools']['five']['primary']))

    def test_star_and_five_never_inherit_sword_stats(self):
        sword = dict(lineages=read('data/sword/lineages.json'), statuses=read('data/sword/statuses.json'))
        for name in ('five-elements', 'seven-stars'):
            school = read('data/' + name + '/school.json')
            original = compile_school(school, sword)['lineages']
            changed = copy.deepcopy(sword)
            for step in changed['lineages']['operations'][0]['fields']['steps']:
                step['passive']['values'] += [dict(key='energyFlat', intValue=999), dict(key='allDefense', intValue=99)]
            self.assertEqual(compile_school(school, changed)['lineages'], original)


if __name__ == '__main__':
    unittest.main()
