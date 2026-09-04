"""Armour becomes two pieces: Upper (vest + pauldrons + gloves) and Lower (belt + boots).

Data migration, all text: Items.csv, Properties.csv, the reward pools, the resonance database, the
drafts, and the tests' constants. Gloves rows are disabled rather than deleted, so the change can be
undone by re-enabling them. Ids never change: the upper keeps the vest's id, the lower the boots'.
"""
import csv, io, re, collections, sys

def rw(path, fn):
    t = io.open(path, encoding='utf-8', newline='').read()
    new = fn(t)
    if new != t:
        io.open(path, 'w', encoding='utf-8', newline='').write(new); print("changed", path)
    else:
        print("unchanged", path)

# ---- Items.csv: gloves off, names say Upper / Lower
def items(t):
    nl = '\r\n' if '\r\n' in t else '\n'
    lines = t.split(nl)
    head = lines[0].split(',')
    iE, iId, iType, iName = head.index('Enabled'), head.index('Id'), head.index('Type'), head.index('Name_EN')
    out = [lines[0]]; disabled = renamed = 0
    for line in lines[1:]:
        if not line.strip(): out.append(line); continue
        cells = next(csv.reader([line]))
        if cells[iType] == 'Gloves' and cells[iE].upper() == 'TRUE':
            cells[iE] = 'FALSE'; disabled += 1
        elif cells[iType] == 'VestBeltPauldron' and cells[iName].endswith(' (Vest)'):
            cells[iName] = cells[iName][:-7] + ' (Upper)'; renamed += 1
        elif cells[iType] == 'Boots' and cells[iName].endswith(' (Boots)'):
            cells[iName] = cells[iName][:-8] + ' (Lower)'; renamed += 1
        buf = io.StringIO(); csv.writer(buf, lineterminator='').writerow(cells); out.append(buf.getvalue())
    print(f"  items: {disabled} gloves rows disabled, {renamed} names now say Upper/Lower")
    return nl.join(out)
rw('Assets/Data/Items.csv', items)

# ---- Properties.csv: the gloves' stats move to the upper (summed where both had the stat)
def props(t):
    nl = '\r\n' if '\r\n' in t else '\n'
    lines = t.split(nl)
    rows = [next(csv.reader([l])) for l in lines[1:] if l.strip()]
    merged = collections.OrderedDict()   # (id, prop) -> value
    moved = 0
    for item_id, prop, value in rows:
        if item_id.endswith('.gloves'):
            item_id = item_id[:-7] + '.vest'; moved += 1
        key = (item_id, prop)
        if key in merged:
            try: merged[key] = str(int(float(merged[key]) + float(value))).rstrip('0').rstrip('.') if float(merged[key]) + float(value) == int(float(merged[key]) + float(value)) else str(float(merged[key]) + float(value))
            except ValueError: merged[key] = value
        else:
            merged[key] = value
    out = [lines[0]] + [f"{i},{p},{v}" for (i, p), v in merged.items()]
    print(f"  properties: {moved} gloves stats moved onto the upper; {len(rows)} rows -> {len(merged)}")
    return nl.join(out) + nl
rw('Assets/Data/Properties.csv', props)

# ---- references by id: .gloves -> .vest, without duplicating a vest already listed
def refs(t):
    ids = re.findall(r'^(\s*- )(\S.*?)\.gloves\s*$', t, re.M)
    def sub(m):
        vest = m.group(2) + '.vest'
        return '' if re.search(r'^\s*- ' + re.escape(vest) + r'\s*$', t, re.M) else m.group(1) + vest
    t = re.sub(r'^(\s*- )(\S.*?)\.gloves\s*\n', lambda m: (sub(m) + '\n') if sub(m) else '', t, flags=re.M)
    t = re.sub(r'(itemId: \S.*?)\.gloves\b', lambda m: m.group(1) + '.vest', t)
    return t
for p in ['Assets/Data/Run/StandardRewards.asset', 'Assets/Data/Run/Act1/EliteRewards.asset',
          'Assets/Resources/ResonanceDatabase.asset', 'Assets/Data/SetDrafts.asset']:
    rw(p, refs)

# ---- tests: the Marked gloves are the Marked upper now
for p in ['Assets/Editor/Tests/BagStockTests.cs', 'Assets/Editor/Tests/MarkedEngravingTests.cs']:
    rw(p, lambda t: t.replace('FantasyHeroes.Basic.Armor.BanditArmor.gloves', 'FantasyHeroes.Basic.Armor.BanditArmor.vest'))

# ---- the lower wears the belt icon: at icon size the belt reads as the set's colours, the boots as a blob
def belt_icon(t):
    nl = '
' if '
' in t else '
'
    lines = t.split(nl); head = lines[0].split(','); iType, iIcon = head.index('Type'), head.index('IconId')
    out = [lines[0]]
    for line in lines[1:]:
        if not line.strip(): out.append(line); continue
        cells = next(csv.reader([line]))
        if cells[iType] == 'Boots' and '.Boots.' in cells[iIcon]: cells[iIcon] = cells[iIcon].replace('.Boots.', '.Belt.')
        buf = io.StringIO(); csv.writer(buf, lineterminator='').writerow(cells); out.append(buf.getvalue())
    return nl.join(out)
rw('Assets/Data/Items.csv', belt_icon)
