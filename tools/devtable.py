import re, sys, json
src = open(sys.argv[1], encoding='utf-8').read()
lines = src.split('\n')
# handler mapping region
hmap = {}
for m in re.finditer(r'case DeviceProductIdentifier\.(\w+):', src):
    pass
blocks = re.finditer(r'\nconst (\w+) = \{\n  productId: DeviceProductIdentifier\.(\w+),\n  name: "([^"]+)",(.*?)\n\};', src, re.S)
out = []
for b in blocks:
    var, pid_id, name, body = b.groups()
    cat = re.search(r'category: DeviceCategory\.(\w+)', body)
    batt = re.search(r'\bbattery: (true|false)', body)
    cms = []
    for cm in re.finditer(r'(\w+): \{\s*vid: "(0x\w+)",\s*pid: "(0x\w+)",\s*setData: \{\s*usagePage: "(0x\w+)",\s*usage: "(0x\w+)"\s*\},\s*getData: \{\s*usagePage: "(0x\w+)",\s*usage: "(0x\w+)"', body):
        cms.append(cm.groups())
    out.append(dict(id=pid_id, name=name, cat=cat.group(1) if cat else '?', battery=batt.group(1) if batt else '?', cm=cms))
# handler mapping: find switch with "new XxxDeviceHandler"
handler = {}
for m in re.finditer(r'((?:case DeviceProductIdentifier\.\w+:\s*)+)(?:\{\s*)?(?:return |\w+ = )?new (\w+DeviceHandler)', src):
    for c in re.findall(r'DeviceProductIdentifier\.(\w+)', m.group(1)):
        handler.setdefault(c, m.group(2))
for d in out:
    d['handler'] = handler.get(d['id'], '?')
    print(f"{d['cat']:9} | {d['name']:42} | batt={d['battery']:5} | {d['handler']}")
    for c in d['cm']:
        print(f"           {c[0]:11} {c[1]}:{c[2]}  set {c[3]}/{c[4]}  get {c[5]}/{c[6]}")
json.dump(out, open(sys.argv[2], 'w'), indent=1)
print(len(out), 'devices')
