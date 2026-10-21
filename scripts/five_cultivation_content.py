"""Retain retired learned IDs for native save loading; they grant no new profile."""
import copy

PREFIX = 'yixian.sect.practice.five.'
GROUPS = {40: 1, 20: 2, 30: 4}


def compile_cultivation(table, content, manifest):
    template = next(o['fields'] for o in content['abilities']['operations'] if o['id'] == 'yixian.sect.pack.five_deck')
    for attribute, group in GROUPS.items():
        for mask in range(1, 8):
            if not mask & group:
                continue
            fields = copy.deepcopy(template)
            fields.update(displayName='旧修行记录（已停用）',
                          flavour='仅保留旧存档身份，不提供修行效果。当前自动识别主属性，无须手动选择；原生多属性与主属性70%折算取大。')
            fields['ranks'][0]['operation']['values'] = [v for v in fields['ranks'][0]['operation']['values'] if v['key'] != 'cardTablePackKey']
            content['abilities']['operations'].append(dict(op='add', kind='abilities', id=PREFIX + str(attribute) + '.' + str(mask), fields=fields))
