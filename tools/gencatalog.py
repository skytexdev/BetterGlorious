import json, re, sys
src = open(sys.argv[1], encoding='utf-8').read()
devs = json.load(open(sys.argv[2]))
m = re.search(r'const DeviceProductIdHandlerMap = \{(.*?)\n\};', src, re.S)
hmap = dict(re.findall(r'DeviceProductIdentifier\.(\w+)\]: (\w+)', m.group(1)))
fam = {'MouseV2ProDeviceHandler':'Sinowealth','MouseV2DeviceHandler':'PixartV2','MouseI2DeviceHandler':'PixartV2',
       'ModelV3DeviceHandler':'ModelV3','GMMK3HEDeviceHandler':'Gmmk3He','GMMK3DeviceHandler':'Gmmk3',
       'GMMKNumpadDeviceHandler':'GmmkNumpad'}
kind = {'Mouse':'Mouse','Keyboard':'Keyboard','Headset':'Headset','Mousepad':'Mousepad'}
seen = set(); rows = []
for d in devs:
    h = hmap.get(d['id'], '')
    f = fam.get(h, 'None') if d['battery'] == 'true' else 'None'
    for (cm, vid, pid, sp, su, gp, gu) in d['cm']:
        if cm.startswith('Bootloader'): continue
        link = 'Receiver' if cm.startswith('Rec') else 'Bluetooth' if cm.startswith('Bluetooth') else 'Usb'
        key = (vid.lower(), pid.lower())
        if key in seen: continue
        seen.add(key)
        rows.append('            new CatalogEntry("%s", "%s", DeviceKind.%s, BatteryFamily.%s, Link.%s, %s, %s, %s, %s, %s, %s),' % (
            d['id'], d['name'].replace('"','\\"'), kind.get(d['cat'],'Other'), f, link,
            '0x'+vid[2:].upper(), '0x'+pid[2:].upper(), '0x'+sp[2:].upper(), '0x'+su[2:].upper(), '0x'+gp[2:].upper(), '0x'+gu[2:].upper()))
print('\n'.join(rows))
print(len(rows), file=sys.stderr)
