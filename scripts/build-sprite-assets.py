"""Build the Little Ages isometric sprite kit: seasonal SVG sprites plus a placement manifest.

Run from anywhere with Python 3.10+; no third-party packages are needed:

    python scripts/build-sprite-assets.py

Sprites are drawn in tile units with the painter in sprite_iso.py (one tile is
2 x S pixels wide on screen). The manifest records, per sprite, its drawn size
and the image point that sits on the centre of its tile, both in S units, so the
renderer can place and scale any sprite at any zoom.
"""
from pathlib import Path
import json
import re
import shutil
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / 'scripts'))
import sprite_art as art  # noqa: E402
from sprite_iso import Canvas  # noqa: E402

OUTPUT = ROOT / 'src/LittleAges.Web/public/assets/sprites'
MANIFEST = ROOT / 'src/LittleAges.Web/src/world/sprite-manifest.json'
S = 40
SEASONS = ['spring', 'summer', 'autumn', 'winter']
VILLAGER_CLOTHING = ['#b95f4b', '#536f88', '#d09a48', '#6d8150', '#876390', '#3f7c78', '#9a704d', '#b77774']

# name: (draw(canvas, x, y), footprint (w, d) in tiles, size of the footprint's longest side on the map in tiles)
STRUCTURES = {
    'shelter-1': (art.tent, (1.5, 1.5), 1.35),
    'shelter-2': (art.round_hut, (1.5, 1.5), 1.4),
    'shelter-3': (art.cottage, (1.8, 1.4), 1.5),
    'shelter-4': (art.longhouse, (2.2, 1.4), 1.6),
    'shelter-5': (art.stone_house, (2.2, 1.6), 1.6),
    'stockpile': (art.stockpile, (2.2, 2.2), 0.95),
    'workshop': (art.workshop, (2.2, 1.8), 1.5),
    'granary': (art.granary, (1.8, 1.8), 1.3),
    'marketplace': (art.marketplace, (2.6, 2.6), 1.5),
    'construction': (art.construction, (1.8, 1.4), 1.4),
    'hearth': (art.hearth, (1.4, 1.4), 1.0),
    'loom': (art.loom, (1.6, 1.4), 1.0),
    'carehouse': (art.care_house, (2.0, 1.6), 1.3),
}
CROPS = {'fallow': 0.0, 'planted': 0.25, 'growing': 0.65, 'harvest': 1.0, 'dormant': 0.0}
FIELDS = {'bare': 0.0, 'sprout': 0.25, 'green': 0.65, 'ripe': 1.0}


def centered(draw, foot):
    """Draw with the footprint centred on the tile centre (0.5, 0.5)."""
    return lambda c: draw(c, 0.5 - foot[0] / 2, 0.5 - foot[1] / 2)


def point(x, y):
    return lambda draw: (lambda c: draw(c, x, y))


def save(manifest, folder, name, canvas, scale, pad=4):
    svg = canvas.svg(pad=pad)
    x0, y0, w, h = (float(v) for v in re.search(r'viewBox="([^"]+)"', svg).group(1).split())
    cx, cy = canvas.P(0.5, 0.5)
    path = OUTPUT / folder / f'{name}.svg'
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(svg, encoding='utf-8')
    manifest[f'{folder}/{name}'] = {
        'width': round(w / S * scale, 4), 'height': round(h / S * scale, 4),
        'anchorX': round((cx - x0) / S * scale, 4), 'anchorY': round((cy - y0) / S * scale, 4),
    }


def draw(name, fn, season):
    c = Canvas(S, f'{season[:2]}-{name}')
    c.snow = season == 'winter'
    fn(c)
    return c


def build():
    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    manifest = {}
    for season in SEASONS:
        art.SEASON = season
        for name, (fn, foot, size) in STRUCTURES.items():
            save(manifest, season, name, draw(name, centered(fn, foot), season), size / max(foot))
        for stage, ripe in CROPS.items():
            fn = lambda c, X, Y, ripe=ripe: art.farm(c, X, Y, 3, 2.2, ripe=ripe, seasonal=False)
            save(manifest, season, f'farm-{stage}', draw('farm', centered(fn, (3, 2.2)), season), 1.3 / 3)
        for stage, ripe in FIELDS.items():
            fn = lambda c, X, Y, ripe=ripe: art.farm(c, X, Y, 0.92, 0.92, ripe=ripe, seasonal=False, fence=False, scarecrow=False)
            save(manifest, season, f'field-{stage}', draw('field', centered(fn, (0.92, 0.92)), season), 1.0)
        for variant in range(3):
            fn = lambda c, x, y, variant=variant: art.oak(c, x, y, variant=variant)
            save(manifest, season, f'oak-{variant + 1}', draw(f'oak{variant}', point(0.5, 0.5)(fn), season), 1.0)
        for name, fn in {'pine': art.pine, 'berry': art.berry_bush, 'rocks': art.rocks}.items():
            save(manifest, season, name, draw(name, point(0.5, 0.5)(fn), season), 1.0)
    art.SEASON = 'spring'
    for name, fn in {'animal': lambda c, x, y: art.animal(c, x, y), 'predator': lambda c, x, y: art.animal(c, x, y, True),
                     'site': lambda c, x, y: art.site_marker(c, x, y), 'site-selected': lambda c, x, y: art.site_marker(c, x, y, art.GOLD),
                     'icon-food': art.icon_food, 'icon-wood': art.icon_wood, 'icon-stone': art.icon_stone, 'icon-people': art.icon_people}.items():
        save(manifest, 'common', name, draw(name, point(0.5, 0.5)(fn), 'spring'), 1.0)
    looks = len(art.LOOKS) + len(art.ELDER_LOOKS)
    for look in range(looks):
        for index, clothing in enumerate(VILLAGER_CLOTHING):
            for frame in (0, 1):
                name = f'villager-{look}-{index}-{frame}'
                fn = lambda c, x, y, clothing=clothing, look=look, frame=frame: art.person(c, x, y, cloth=clothing, look=look, frame=frame)
                save(manifest, 'people', name, draw(name, point(0.5, 0.5)(fn), 'spring'), 1.0, pad=2)
    for item in ('wood', 'stone', 'food'):
        fn = lambda c, x, y, item=item: art.carried_item(c, x, y, item)
        save(manifest, 'people', f'carry-{item}', draw(f'carry-{item}', point(0.5, 0.5)(fn), 'spring'), 1.0, pad=2)
    MANIFEST.write_text(json.dumps({'tileHalfWidth': 1, 'sprites': manifest}, indent=1, sort_keys=True) + '\n', encoding='utf-8')
    total = sum(p.stat().st_size for p in OUTPUT.rglob('*.svg'))
    print(f'{len(manifest)} sprites, {total:,} bytes')


if __name__ == '__main__':
    build()
