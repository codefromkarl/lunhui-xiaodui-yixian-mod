"""Explicit deletion audit; archived source/fee evidence is not an active catalog."""
import json
from pathlib import Path

AUDIT = json.loads((Path(__file__).resolve().parents[1] / 'data/sword/retired-cards.json').read_text())
RETIRED_KEYS = frozenset(c['key'] for c in AUDIT['cards'])
RETIRED_SOURCES = frozenset(c['sourceId'] for c in AUDIT['cards'])
PACK_SIZES = AUDIT['packSizes']
PACK_ROLES = AUDIT['packRoles']

REPLACEMENTS = AUDIT.get('replacements', [])
REPLACEMENT_KEYS = frozenset(c['key'] for c in REPLACEMENTS)
REPLACEMENT_BY_RETIRED = {c['retiredKey']: c['key'] for c in REPLACEMENTS}
