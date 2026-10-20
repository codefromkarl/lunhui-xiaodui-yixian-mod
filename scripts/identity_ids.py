"""Canonical namespaced content identities. No legacy aliases or save migration."""
PACKAGE = 'yixian.sect'
SWORD = PACKAGE + '.lineage.sword'
CARD_PREFIX = PACKAGE + '.card.'

def card_id(key): return CARD_PREFIX + key
def pack_id(key): return PACKAGE + '.pack.' + key
def fate_id(lineage, key): return PACKAGE + '.fate.' + lineage.rsplit('.', 1)[-1] + '.' + key
