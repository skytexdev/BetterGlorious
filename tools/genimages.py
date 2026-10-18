"""Copy the product renders shipped with Glorious CORE 2 and write the id -> image/colors/layout map.

usage: python tools/genimages.py <core2 out/main/index.js> <core2 out/renderer-process/images/devices> <web/public/devices> <web/src/deviceImages.json>

For every device and colorway:
  <image>-<color>.webp       the Render_Large PNG trimmed of its empty margin, capped at 900 px (WebP q90)
  <image>-<color>-map.webp   the untrimmed canvas at 1.5x CORE's layout frame, for the button map (mice only)
The map JSON comes from CORE's own device definitions: productId -> imagePath, productColors and, for
mice, the button layout (label boxes and where each callout line ends on the render).
Existing outputs newer than their source are kept, so re-runs are quick.
"""
import json, os, re, sys
from PIL import Image

index_js, images_root, out_dir, map_path = sys.argv[1:5]
MAX_SIDE = 900
MAP_SCALE = 1.5
L = lambda p: '\\\\?\\' + os.path.abspath(p) if os.name == 'nt' else p
src = open(index_js, encoding='utf-8').read()

# Numeric constants used inside layouts (e.g. MouseLayoutYPosAdjustment = -16).
consts = {k: int(v) for k, v in re.findall(r'\nconst (\w+) = (-?\d+);', src)}


def num(expr):
    e = re.sub(r'[A-Za-z_]\w*', lambda m: str(consts.get(m.group(0), 'X')), expr.strip())
    if not re.fullmatch(r'[\d\s+\-*/().]+', e):
        raise ValueError(expr)
    return eval(e)  # digits and operators only, checked above


def point(body, name):
    m = re.search(name + r': \{ x: ([^,]+), y: ([^}]+) \}', body)
    return (num(m.group(1)), num(m.group(2))) if m else None


# Component layouts: const ModelOComponentLayout = { LeftButton: new MouseComponentLayoutItem({ ... }), ... };
layouts = {}
for m in re.finditer(r'\nconst (\w+) = \{\n(  \w+: new MouseComponentLayoutItem\(\{.*?)\n\};', src, re.S):
    items = []
    for it in re.finditer(r'  (\w+): new MouseComponentLayoutItem\(\{(.*?)\n  \}\)', m.group(2), re.S):
        body = it.group(2)
        kind = re.search(r'type: "(\w+)"', body)
        if kind and kind.group(1) != 'Button':
            continue
        pos, term = point(body, 'position'), point(body, 'terminationPoint')
        size = re.search(r'size: \{ width: ([^,]+), height: ([^}]+) \}', body)
        w, h = (num(size.group(1)), num(size.group(2))) if size else (130, 40)
        if not pos or not term:
            continue
        items.append(dict(id=it.group(1), box=[pos[0], pos[1], w, h], term=list(term)))
    layouts[m.group(1)] = items

# Color sets: const ProductColors_X = [ new DeviceProductColor("Black", "Black/", "Black", "#121212"), ... ];
palettes = {}
for m in re.finditer(r'const (ProductColors_\w+) = \[(.*?)\];', src, re.S):
    palettes[m.group(1)] = [dict(name=a, subpath=b, suffix=c, hex=d) for a, b, c, d in
                            re.findall(r'new DeviceProductColor\("([^"]*)", "([^"]*)", "([^"]*)", "([^"]*)"\)', m.group(2))]


# CORE puts most callout ends on a button's top edge; nudge them onto the button itself
# (layout units, measured on the full-size renders). DPI buttons sit 13-20 units lower.
POINT_FIX_DEFAULT = {'DPICycleButton': (0, 14)}
POINT_FIX = {
    'Mouse_ModelOWireless': {'DPICycleButton': (0, 13)},
    'Mouse_ModelOMinusWireless': {'DPICycleButton': (0, 13)},
    'Mouse_ModelDWireless': {'DPICycleButton': (0, 17)},
    'Mouse_ModelDMinusWireless': {'DPICycleButton': (0, 17)},
    'Mouse_Series2ProWireless': {'DPICycleButton': (0, 17)},
    'Mouse_ModelD2Wireless': {'DPICycleButton': (0, 20)},
    'Mouse_ModelO3Wireless': {'DPICycleButton': (0, 15), 'LeftButton': (0, 25), 'RightButton': (0, 25)},
    'Mouse_ModelD3Wireless': {'DPICycleButton': (0, 15), 'LeftButton': (0, 25), 'RightButton': (0, 25)},
    'Mouse_ModelO3Wired': {'LeftButton': (0, 25), 'RightButton': (0, 25)},
    'Mouse_ModelD3Wired': {'LeftButton': (0, 25), 'RightButton': (0, 25)},
}


# Button centers measured on 7-9 px/unit close-ups of each full-size render (layout units, ~0.1-0.3 precise):
# wheel = middle of the wheel well, DPI = middle of the DPI button, side buttons = middle of each button.
# Left/right clicks are then mirrored around the measured wheel center. Wired twins share the wireless
# render apart from the cable (the O3/D3 wired renders sit ~2 units higher).
MEASURED = {
    'Mouse_ModelOWireless': {'MiddleButton': (376.4, 78.6), 'DPICycleButton': (376.4, 128.3), 'ForwardButton': (296.1, 138), 'BackButton': (297.4, 176.5)},
    'Mouse_ModelOMinusWireless': {'MiddleButton': (376.4, 78.6), 'DPICycleButton': (376.4, 128.3), 'ForwardButton': (296.1, 138), 'BackButton': (297.4, 176.5)},
    'Mouse_ModelOProWireless': {'MiddleButton': (376.4, 75.8), 'DPICycleButton': (375.9, 129.5), 'ForwardButton': (295.8, 135), 'BackButton': (297.9, 174.7)},
    'Mouse_ModelOWired': {'MiddleButton': (368.2, 87.2), 'DPICycleButton': (368.0, 139.7), 'ForwardButton': (290.7, 144.7), 'BackButton': (292.4, 183)},
    'Mouse_ModelOWirelessClassic': {'MiddleButton': (373.9, 100.5), 'DPICycleButton': (373.3, 151.9), 'ForwardButton': (297.3, 159), 'BackButton': (299.8, 198.6)},
    'Mouse_ModelOEternalWired': {'MiddleButton': (373.8, 92.9), 'DPICycleButton': (373.8, 166), 'ForwardButton': (296.6, 171.1), 'BackButton': (300.1, 214.3)},
    'Mouse_ModelO2Wireless': {'MiddleButton': (375.5, 94), 'DPICycleButton': (375.6, 145), 'ForwardButton': (298.9, 152), 'BackButton': (298.9, 191.8)},
    'Mouse_ModelO2Wired': {'MiddleButton': (375.5, 94), 'DPICycleButton': (375.6, 145), 'ForwardButton': (298.9, 152), 'BackButton': (298.9, 191.8)},
    'Mouse_ModelO2ProWireless': {'MiddleButton': (381.4, 86.8), 'DPICycleButton': (381.6, 134), 'ForwardButton': (303.7, 144.8), 'BackButton': (304.6, 184)},
    'Mouse_ModelO3Wireless': {'MiddleButton': (373.8, 105.1), 'DPICycleButton': (373.2, 153.1), 'ForwardButton': (297.7, 170.6), 'BackButton': (299.1, 212.3)},
    'Mouse_ModelO3Wired': {'MiddleButton': (373.8, 102.8), 'DPICycleButton': (373.2, 150.8), 'ForwardButton': (297.7, 168.3), 'BackButton': (299.1, 210)},
    'Mouse_ModelDWireless': {'MiddleButton': (375.6, 81.3), 'DPICycleButton': (375.6, 146.7), 'ForwardButton': (299, 149.7), 'BackButton': (301.4, 198)},
    'Mouse_ModelDMinusWireless': {'MiddleButton': (375.6, 81.3), 'DPICycleButton': (375.6, 146.7), 'ForwardButton': (299, 149.7), 'BackButton': (301.4, 198)},
    'Mouse_ModelDProWireless': {'MiddleButton': (377.3, 73.6), 'DPICycleButton': (377.2, 144.2), 'ForwardButton': (300.3, 143.6), 'BackButton': (300.7, 195)},
    'Mouse_ModelD2Wireless': {'MiddleButton': (375.8, 104), 'DPICycleButton': (375.8, 166), 'ForwardButton': (306.3, 169.4), 'BackButton': (308.9, 217)},
    'Mouse_ModelD2Wired': {'MiddleButton': (375.8, 104), 'DPICycleButton': (375.8, 166), 'ForwardButton': (306.3, 169.4), 'BackButton': (308.9, 217)},
    'Mouse_ModelD2ProWireless': {'MiddleButton': (381.9, 86.3), 'DPICycleButton': (382.1, 148.3), 'ForwardButton': (307.7, 150.1), 'BackButton': (310.3, 201.9)},
    'Mouse_ModelD3Wireless': {'MiddleButton': (373.7, 105), 'DPICycleButton': (373.6, 156.4), 'ForwardButton': (297.9, 168), 'BackButton': (301.4, 214.9)},
    'Mouse_ModelD3Wired': {'MiddleButton': (373.7, 103.2), 'DPICycleButton': (373.6, 154.6), 'ForwardButton': (297.9, 166.2), 'BackButton': (301.4, 213.1)},
    'Mouse_Series2ProWireless': {'MiddleButton': (377.8, 99), 'DPICycleButton': (378.2, 151.4), 'ForwardButton': (300.2, 164.2), 'BackButton': (303.3, 206.5)},
    'Mouse_SeriesOneProWireless': {'MiddleButton': (377.6, 83), 'DPICycleButton': (377.4, 135.3), 'ForwardButton': (295.4, 146), 'BackButton': (299.4, 192)},
    'Mouse_ModelIWired': {'MiddleButton': (381.6, 90.8), 'DPIUpButton': (380.3, 145.7), 'DPIDownButton': (380.3, 171.8)},
    'Mouse_ModelI2Wireless': {'MiddleButton': (385.6, 93.9), 'DPIUpButton': (383.4, 153.6), 'DPIDownButton': (383.4, 180.2),
                              'DPIShiftButton': (301.5, 149), 'HomeButton': (302.6, 165.9), 'ForwardButton': (305.1, 198), 'BackButton': (308.3, 240)},
}
MEASURED['Mouse_ModelI2Wired'] = MEASURED['Mouse_ModelI2Wireless']


def snap(items, alpha, k):
    """Put points on the buttons using the render's own outline (alpha mask, k = layout units per px):
    wheel/DPI on the shell's center line, left/right clicks halfway between edge and center line
    (mirrored), side buttons just inside the left edge at their height."""
    W, H = alpha.size
    px = alpha.load()
    box = alpha.point(lambda v: 255 if v > 128 else 0).getbbox()
    mid = (box[0] + box[2]) / 2 * k

    def left_edge(y_units):
        row = max(0, min(H - 1, int(y_units / k)))
        for x in range(W):
            if px[x, row] > 128:
                return x * k
        return box[0] * k

    by_id = {it['id']: it for it in items}
    for name in ('MiddleButton', 'DPICycleButton'):
        if name in by_id:
            by_id[name]['point'][0] = round(mid, 1)
    if 'LeftButton' in by_id:
        y = by_id['LeftButton']['point'][1]
        lx = (left_edge(y) + mid) / 2
        by_id['LeftButton']['point'][0] = round(lx, 1)
        if 'RightButton' in by_id:
            by_id['RightButton']['point'] = [round(2 * mid - lx, 1), y]
    for name in ('ForwardButton', 'BackButton'):
        if name in by_id:
            by_id[name]['point'][0] = round(left_edge(by_id[name]['point'][1]) + 4.5, 1)
    return items


def apply_measured(items, image):
    m = MEASURED.get(image, {})
    by_id = {it['id']: it for it in items}
    for name, pt in m.items():
        if name in by_id:
            by_id[name]['point'] = list(pt)
    # keep left/right clicks symmetric around the real center line (symmetric shells only)
    if 'MiddleButton' in m and 'ModelI' not in image and 'LeftButton' in by_id and 'RightButton' in by_id:
        lx, rx = by_id['LeftButton']['point'][0], by_id['RightButton']['point'][0]
        half, mid = (rx - lx) / 2, m['MiddleButton'][0]
        by_id['LeftButton']['point'][0] = round(mid - half, 1)
        by_id['RightButton']['point'][0] = round(mid + half, 1)
    return items


def resolve(layout, image, alpha=None, k=None):
    """Absolute callout points in layout units: left boxes measure from x, right boxes from x + w."""
    fix = dict(POINT_FIX_DEFAULT, **POINT_FIX.get(image, {}))
    items = []
    for it in layout['items']:
        x, y, w, h = it['box']
        left = x + w / 2 < layout['width'] / 2
        dx, dy = fix.get(it['id'], (0, 0))
        point = [(x if left else x + w) + it['term'][0] + dx, y + it['term'][1] + dy]
        items.append(dict(id=it['id'], side='left' if left else 'right', point=point))
    # Symmetric shells get their points snapped to the outline; the Model I family is ergonomic, keep CORE's.
    if alpha is not None and 'ModelI' not in image:
        snap(items, alpha, k)
    return apply_measured(items, image)


def fit(im, layout):
    """CORE draws the render 'contain'-fitted into the layout frame; pad it to the frame's aspect the same way."""
    W, H = layout['width'], layout['height']
    cw, ch = max(im.width, round(im.height * W / H)), max(im.height, round(im.width * H / W))
    if (cw, ch) == im.size:
        return im
    canvas = Image.new('RGBA', (cw, ch), (0, 0, 0, 0))
    canvas.paste(im, ((cw - im.width) // 2, (ch - im.height) // 2))
    return canvas


def fresh(out, source):
    return os.path.exists(out) and os.path.getmtime(out) >= os.path.getmtime(L(source))


os.makedirs(out_dir, exist_ok=True)
result = {}
for m in re.finditer(r'\nconst \w+ = \{\n  productId: DeviceProductIdentifier\.(\w+),(.*?)\n\};', src, re.S):
    pid, body = m.group(1), m.group(2)
    img = re.search(r'imagePath: "([^"]+)"', body)
    pal = re.search(r'productColors: (ProductColors_\w+)', body)
    if not img:
        continue
    image = img.group(1)

    layout = None
    dl = re.search(r'deviceLayout: \{\s*renderWidth: (\d+),\s*renderHeight: (\d+),.*?components: (\w+),', body, re.S)
    if dl and dl.group(3) in layouts and layouts[dl.group(3)]:
        layout = dict(width=int(dl.group(1)), height=int(dl.group(2)), items=layouts[dl.group(3)])

    colors = []
    for c in palettes.get(pal.group(1) if pal else '', [dict(name='Black', subpath='Black/', suffix='Black', hex='#121212')]):
        f = os.path.join(images_root, image, c['subpath'], f"{image}{c['suffix']}_Render_Large.png")
        if not os.path.exists(L(f)):
            continue
        base = f"{image}-{c['name'].lower()}"
        out = os.path.join(out_dir, base + '.webp')
        if not fresh(out, f):
            im = Image.open(L(f)).convert('RGBA')
            box = im.getchannel('A').getbbox()
            if box:
                im = im.crop(box)
            im.thumbnail((MAX_SIDE, MAX_SIDE), Image.LANCZOS)
            im.save(out, 'WEBP', quality=90, method=6)
        entry = dict(id=c['name'].lower(), label=c['name'], hex=c['hex'], file=base + '.webp')
        # Lighting mask (where the LEDs shine through), cut and scaled exactly like the render so it lines up.
        mf = f.replace('_Render_Large.png', '_Mask.png')
        if os.path.exists(L(mf)):
            mout = os.path.join(out_dir, base + '-mask.webp')
            if not fresh(mout, mf):
                src_im = Image.open(L(f)).convert('RGBA')
                box = src_im.getchannel('A').getbbox()
                size = src_im.crop(box).size
                scale = min(1, MAX_SIDE / max(size))
                target = (max(1, int(size[0] * scale)), max(1, int(size[1] * scale)))
                if target != Image.open(L(out)).size:
                    target = Image.open(L(out)).size
                m = Image.open(L(mf)).convert('RGBA').getchannel('A').crop(box).resize(target, Image.LANCZOS)
                Image.merge('RGBA', (m.point(lambda v: 255),) * 3 + (m,)).save(mout, 'WEBP', quality=80, method=6)
            entry['mask'] = base + '-mask.webp'
        if layout and 'bbox' not in layout:
            full = fit(Image.open(L(f)).convert('RGBA'), layout)
            b = full.getchannel('A').getbbox()
            k = layout['width'] / full.width
            layout = dict(layout, bbox=[round(b[0] * k, 1), round(b[1] * k, 1), round(b[2] * k, 1), round(b[3] * k, 1)],
                          items=resolve(layout, image, full.getchannel('A'), k))
        if layout:
            mp = os.path.join(out_dir, base + '-map.webp')
            if not fresh(mp, f):
                im = fit(Image.open(L(f)).convert('RGBA'), layout)
                im = im.resize((round(layout['width'] * MAP_SCALE), round(layout['height'] * MAP_SCALE)), Image.LANCZOS)
                im.save(mp, 'WEBP', quality=88, method=6)
            entry['map'] = base + '-map.webp'
        colors.append(entry)
    if colors:
        result[pid] = dict(image=image, colors=colors, **({'layout': layout} if layout else {}))

json.dump(result, open(map_path, 'w', encoding='utf-8'), indent=1)
print(len(result), 'devices,', sum(len(v['colors']) for v in result.values()), 'images,',
      sum(1 for v in result.values() if 'layout' in v), 'with button layouts')
