"""Hidden, inert snapshot readouts for decision hints; never gameplay modifiers."""
import copy

def compile_decisions(table, content):
    groups = {'five': ['persistentCount'], 'star': ['healingTotal'], 'sword': ['spiritDiscount', 'nextShield', 'pierce']}
    for c in table['cards']:
        r = c['rules']
        if c.get('sect', 'yixian.sect.lineage.sword').endswith('.five') and (r.get('array') or r.get('activationArray')):
            groups['five'].append('consumed.' + c['key'])
        if c.get('sect', 'yixian.sect.lineage.sword').endswith('.star') and any(r.get(k) for k in ['postAttack','postHeal','postGuard','postAgain']):
            groups['star'].append('used' + c['sourceId'])
    for school, keys in groups.items():
        for key in keys:
            row = copy.deepcopy(content['statuses']['operations'][0])
            row['id'] = f'yixian.sect.status.readout.{school}.{key}'
            row['fields'].update(displayName='决策快照', flavour='只读战斗提示，不提供战斗加成。', display=30, category=0, maximumStacks=1, modifiers=[], timedEffects=[])
            content['statuses']['operations'].append(row)
