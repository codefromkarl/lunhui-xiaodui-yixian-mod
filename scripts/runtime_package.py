"""Resolve the complete runtime dependency set shared by installers and releases."""
import json
from pathlib import Path, PurePosixPath

PLUGINS = {'yixian.sect': 'plugins/YixianSect.dll', 'appearance.muyifeng': 'plugins/MuyifengAppearance.dll'}


def runtime_files(source, slug):
    source = Path(source).resolve()
    manifest = json.loads((source / 'mod.json').read_text(encoding='utf-8'))
    if manifest['id'] != slug or slug not in PLUGINS:
        raise ValueError('Unexpected package: ' + slug)
    if manifest.get('code', {}).get('assembly', PLUGINS[slug]) != PLUGINS[slug]:
        raise ValueError('Unexpected plugin: ' + slug)
    # Fail closed for foreign executables even though they are not selected.
    for p in source.rglob('*'):
        if p.is_file() and (p.suffix.lower() == '.exe' or p.suffix.lower() == '.dll' and p.relative_to(source).as_posix() != PLUGINS[slug]):
            raise ValueError('Host or unknown executable cannot be bundled: ' + str(p))
    files = set()

    def add(name):
        rel = PurePosixPath(name)
        if rel.is_absolute() or '..' in rel.parts or '\\' in name or ':' in name:
            raise ValueError('Unsafe runtime resource: ' + name)
        p = source / name
        if not p.is_file() or source not in p.resolve().parents:
            raise ValueError('Missing or unsafe runtime resource: ' + name)
        files.add(rel.as_posix())
        return p

    add('mod.json'); add(PLUGINS[slug])
    for name in [*manifest.get('content', []), *manifest.get('tables', {}).values(),
                 *[a['file'] for a in manifest.get('assets', {}).values()]]:
        add(name)
    if slug == 'appearance.muyifeng':
        config = json.loads(add('appearance.json').read_text(encoding='utf-8'))
        for row in [config, *config.get('characters', [])]:
            if row.get('pixelSource'): add(row['pixelSource'])
            if row.get('atlasSource'):
                atlas = json.loads(add(row['atlasSource']).read_text(encoding='utf-8'))
                for sheet in atlas['sheets'].values(): add(sheet)
    if (source / 'README.md').is_file(): add('README.md')
    return manifest, [(source / name, 'Mods/' + slug + '/' + name) for name in sorted(files)]
