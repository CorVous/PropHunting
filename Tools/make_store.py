"""The weapon store's data, from Tools/weapon_catalog.json (the melee work's list of every hand-held model):
  Assets/Udon/Text/strings_store.tsv   every text the weapon wall and the Trophies board show (English; the
                                       localization work fills the other columns)
  Tools/store_prices.json              the shop weapons' prices (edit by hand; run again keeps them)
  Assets/Lobby/WeaponIcons.png         a 16 x 12 atlas of 128 px icons, one per catalog weapon in catalog order
                                       (the Trophies board's medals), background keyed out
StoreBuilder.cs reads all three when the world is built. Run from the project root:
  python Tools/make_store.py [--thumbs DIR]   (DIR: a folder of the mirror's 256 px thumbnails; without it they
                                              are fetched from the CC0 mirror into the cc0 cache)
The catalog must only ever grow at the end: a player's saved weapons are a bit per catalog position."""
import json, os, sys, zlib, hashlib, urllib.request, urllib.parse

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CAT = json.load(open(os.path.join(ROOT, 'Tools', 'weapon_catalog.json'), encoding='utf8'))['weapons']
MIRROR = 'http://cc0.cor.home/'
CACHE = os.environ.get('CC0_CACHE', os.path.join(os.path.expanduser('~'), '.cache', 'cc0-mirror'))

# The trophies (Cor approved 2026-11-19): hunter trophies unlock the melee picks, hider trophies the guns, in the
# catalog's order (13 cool, then 9 goofy). Fields: key, name, goal, rounds-to-earn low, high (an 8-person lobby).
HUNT = [
    ('first_catch', 'First Catch', 'Catch a hider.', 1, 8),
    ('hat_trick', 'Hat Trick', 'Catch 3 hiders in one round.', 4, 20),
    ('sharp_eye', 'Sharp Eye', 'Catch 3 hiders in one round without hitting a wrong prop.', 20, 60),
    ('clean_sweep', 'Clean Sweep', 'Catch every hider in a round (3 or more).', 30, 100),
    ('quick_draw', 'Quick Draw', 'Catch a hider in the first 15 s of the hunt.', 20, 80),
    ('buzzer_beater', 'Buzzer Beater', 'Catch the last hider in the final 10 s.', 30, 120),
    ('ten_catches', 'Ten Catches', 'Catch 10 hiders in total.', 15, 40),
    ('hundred', 'Hundred', 'Catch 100 hiders in total.', 200, 400),
    ('good_as_new', 'Good as New', 'Finish a round without hitting a wrong prop.', 5, 25),
    ('weathered', 'Weathered', 'Get through a final stand with your weapon in one piece.', 2, 10),
    ('pack_leader', 'Pack Leader', 'Win 10 rounds as a hunter.', 60, 100),
    ('globetrotter_hunt', 'Globetrotter', 'Catch a hider in town, medieval and space.', 10, 40),
    ('old_hand_hunt', 'Old Hand', 'Play 50 rounds as a hunter.', 150, 250),
    ('snapped', 'Snapped', 'Have your weapon break.', 5, 25),
    ('furniture_hater', 'Furniture Hater', 'Hit 9 wrong props in one round without breaking your weapon.', 10, 50),
    ('bonk_collector', 'Bonk Collector', 'Hit 250 wrong props in total.', 150, 300),
    ('swing_and_a_miss', 'Swing and a Miss', 'Swing 100 times in one round.', 1, 8),
    ('splinters', 'Splinters', 'Have your weapon break in the first 30 s.', 1, 8),
    ('close_shave', 'Close Shave', 'Catch a hider with your weapon at 10% or less.', 30, 150),
    ('shot_down', 'Shot Down', "Have your weapon broken by a hider's shots.", 40, 200),
    ('empty_handed', 'Empty Handed', 'Finish 10 rounds as a hunter with no catches.', 80, 160),
    ('scrapper', 'Scrapper', 'Vote to scrap a map.', 1, 2),
]
HIDE = [
    ('survivor', 'Survivor', 'Survive a round as a hider.', 1, 4),
    ('untouched', 'Untouched', 'Survive a round with full health.', 2, 8),
    ('last_prop', 'Last Prop Standing', 'Be the last hider left, and survive.', 10, 40),
    ('ten_survivals', 'Ten Survivals', 'Survive 10 rounds in total.', 20, 35),
    ('fifty_survivals', 'Fifty Survivals', 'Survive 50 rounds in total.', 110, 160),
    ('on_a_roll', 'On a Roll', 'Survive 5 rounds in a row as a hider.', 30, 100),
    ('first_shot', 'First Shot', 'Hit a hunter in the final stand.', 3, 10),
    ('sharpshooter', 'Sharpshooter', 'Land 10 shots in one final stand.', 20, 80),
    ('giant_slayer', 'Giant Slayer', "Break a hunter's weapon in the final stand.", 30, 120),
    ('props_win', 'Props Win', 'Win 10 rounds as a hider.', 25, 40),
    ('globetrotter_hide', 'Globetrotter', 'Survive in town, medieval and space.', 5, 20),
    ('old_hand_hide', 'Old Hand', 'Play 50 rounds as a hider.', 60, 80),
    ('long_haul', 'Long Haul', 'Survive a round on a large map.', 5, 20),
    ('found_instantly', 'Found Instantly', 'Get caught in the first 10 s of the hunt.', 20, 80),
    ('close_call', 'Close Call', 'Survive with 25 health or less.', 15, 60),
    ('quick_change', 'Quick Change', 'Use all 3 prop changes in one hunt, and survive.', 5, 20),
    ('bouncy', 'Bouncy', 'Hop 200 times as a prop.', 1, 5),
    ('chameleon', 'Chameleon', 'Be 25 different props in total.', 15, 25),
    ('spray_and_pray', 'Spray and Pray', 'Fire 20 shots in one final stand.', 3, 10),
    ('full_house', 'Full House', 'Play a round with 10 or more people.', 1, 40),
    ('usual_suspect', 'Usual Suspect', 'Be the first hider caught, 5 times.', 30, 60),
    ('wallflower', 'Wallflower', 'Survive without moving once the hunt starts.', 5, 20),
]

UI = [
    ('store.buy', '{0}'),                        # the buy button: the price beside a coin
    ('store.short', 'Not enough coins'),
    ('store.coins', '{0}'),                      # the coin count on the cart
    ('store.unlock_by', 'Unlock: {0}'),           # the trophy pop-up's first line: the trophy's name
    ('store.rounds', '{0}-{1} rounds'),           # rounds to earn a trophy (a rough range; never a single number)
    ('store.progress', '{0} / {1}'),
    ('store.earned', '{0} / {1}'),               # trophies earned out of the half's total, over each half
    ('store.new_trophy', 'Trophy! {0}'),          # the notice when one is earned
    ('store.coins_earned', '+{0} coins'),         # the notice at the end of a round (a number, never "1 coin")
]


def tsv():
    rows = [('key', 'en', 'ja', 'ko', 'zh-CN', 'zh-HK', 'es', 'fr', 'de', 'pt-BR', 'ru')]
    for k, en in UI:
        rows.append((k, en))
    for t in HUNT + HIDE:
        rows.append(('trophy.%s.name' % t[0], t[1]))
        rows.append(('trophy.%s.goal' % t[0], t[2]))
    out = '\n'.join('\t'.join(list(r) + [''] * (11 - len(r))) for r in rows) + '\n'
    p = os.path.join(ROOT, 'Assets', 'Udon', 'Text', 'strings_store.tsv')
    os.makedirs(os.path.dirname(p), exist_ok=True)
    open(p, 'w', encoding='utf8', newline='\n').write(out)
    print('wrote', p, len(rows) - 1, 'texts')


def prices():
    p = os.path.join(ROOT, 'Tools', 'store_prices.json')
    old = json.load(open(p, encoding='utf8')) if os.path.exists(p) else {}
    out = {}
    for kind in ('melee', 'gun'):
        shop = [w for w in CAT if w['kind'] == kind and w['category'] == 'shop']
        # three equal bands; a stand-in order (a hash of the id) until the bands are picked by looks
        order = sorted(shop, key=lambda w: hashlib.md5(w['id'].encode()).hexdigest())
        n = len(order)
        for i, w in enumerate(order):
            out[w['id']] = old.get(w['id'], 100 if i < n / 3 else (200 if i < 2 * n / 3 else 400))
    json.dump(out, open(p, 'w', encoding='utf8'), indent=1, sort_keys=True)
    print('wrote', p, len(out), 'prices')


def thumb(path, folder):
    if folder:
        f = os.path.join(folder, os.path.basename(path))
        if os.path.exists(f):
            return f
    f = os.path.join(CACHE, *path.split('/'))
    if not os.path.exists(f):
        os.makedirs(os.path.dirname(f), exist_ok=True)
        urllib.request.urlretrieve(MIRROR + '/'.join(urllib.parse.quote(s) for s in path.split('/')), f)
    return f


def icons(folder):
    from PIL import Image
    S, COLS, ROWS = 128, 16, 12
    assert len(CAT) <= COLS * ROWS
    at = Image.new('RGBA', (COLS * S, ROWS * S), (0, 0, 0, 0))
    for i, w in enumerate(CAT):
        im = Image.open(thumb(w['thumb'], folder)).convert('RGBA')
        # key out the flat backdrop: anything close to the corner colour, grown from the edges
        px = im.load(); W, H = im.size; bg = px[2, 2][:3]
        seen = set(); stack = [(x, y) for x in range(W) for y in (0, H - 1)] + [(x, y) for y in range(H) for x in (0, W - 1)]
        while stack:
            x, y = stack.pop()
            if (x, y) in seen or not (0 <= x < W and 0 <= y < H):
                continue
            seen.add((x, y))
            c = px[x, y]
            if sum(abs(c[k] - bg[k]) for k in range(3)) > 30:
                continue
            px[x, y] = (c[0], c[1], c[2], 0)
            stack += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
        im = im.resize((S, S), Image.LANCZOS)
        at.paste(im, ((i % COLS) * S, (i // COLS) * S))
    p = os.path.join(ROOT, 'Assets', 'Lobby', 'WeaponIcons.png')
    at.save(p, optimize=True)
    print('wrote', p, len(CAT), 'icons')


if __name__ == '__main__':
    folder = sys.argv[sys.argv.index('--thumbs') + 1] if '--thumbs' in sys.argv else None
    assert len(HUNT) == 22 and len(HIDE) == 22
    tsv(); prices(); icons(folder)
