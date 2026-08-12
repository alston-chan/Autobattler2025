// Scans Items.csv + the HeroEditor icon folders and emits equipment-data.js
// Run:  node Tools/build-data.js     (from the project root)
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const ASSETS = path.join(ROOT, 'Assets');
const CSV = path.join(ASSETS, 'Data', 'Items.csv');
const OUT = path.join(__dirname, 'equipment-data.js');

// ---------- 1. index every icon PNG by "slotFolder/basename" ----------
const iconIndex = new Map(); // "vest/angelicdress" -> relative path

function walk(dir, cb) {
  let entries;
  try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
  for (const e of entries) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, cb);
    else cb(p);
  }
}

walk(path.join(ASSETS, 'HeroEditor'), (file) => {
  if (!file.toLowerCase().endsWith('.png')) return;
  const rel = path.relative(ROOT, file).split(path.sep).join('/');
  if (!/\/Icons\//i.test(rel)) return;                    // icons only
  const m = rel.match(/\/Icons\/Equipment\/([^/]+)\//i);   // slot folder
  if (!m) return;
  const slot = m[1].toLowerCase();
  const base = path.basename(file, '.png').toLowerCase();
  const key = `${slot}/${base}`;
  if (!iconIndex.has(key)) iconIndex.set(key, rel);
});

// ---------- 2. theme detection from sprite/item names ----------
// ORDER MATTERS — first match wins, so the list runs MOST-SPECIFIC → MOST-GENERIC.
// Element words (fire/ice/storm) are stronger signals than role words (warrior/guard),
// so they must be tested first: "FireWarriorArmor" is Ember, not Fury.
const THEMES = [
  ['Ember',      /fire|pyro|blaze|ember|sunlight|endinglight|flame|cinder|burn|magma|infern|ash|scorch/i],
  ['Rime',       /frost|ice|frozen|snow|water|rime|glaci|chill|winter|arctic/i],
  ['Storm',      /storm|thunder|lightning|strelets|tempest|volt|shock/i],
  ['Corruption', /corrupt|torment|cryingdemon|bloodied|nightmare|darkknight|darklord|necro|reaper|warlock|witch|ominous|skeleton|zombie|mummy|death|plague|grim|cursed|doom|dread|demon|bone|vile|rot|blight/i],
  ['Arcane',     /arcane|elemental|mage|wizard|technomancer|moonlight|lunar|magnetism|genie|scintillate|infinity|adept|azure|cosmos|crystal|dream|astral|rune|enchant/i],
  ['Swarm',      /druid|forest|forester|shaman|deer|terra|hunter|nature|beast|manticore|pack|vermin|rat|thorn|vine|bloom/i],
  ['Shadow',     /assassin|thief|ninja|bandit|drifter|falcon|scout|sniper|illusion|rogue|shadow|phantom|stalker|silent|veil/i],
  ['Aegis',      /angelic|blessed|devotion|bishop|cardinal|cleric|crusad|paladin|heavenly|incorrupt|guardian|guard|templ|inquisit|wallkeeper|towerkeeper|battleguard|whiteguard|sacred|holy|divine/i],
  ['Vitality',   /immortal|ancestor|cataphract|phobos|heavy|ironcuirass|ironplate|ironarmor|soulheavy|tenno|mountain|ironisland|samuraiheavy|bulk|titan|colossus/i],
  ['Fury',       /berserk|champion|destroyer|raging|gladiator|spartan|vicious|marauder|orc|nemean|viking|barbarian|warrior|reaver|blood|savage|brutal|rage/i],
];

// Weapon classes that ARE an archetype's signature weapon get a fallback theme.
// Swords/axes/etc. stay Unthemed on purpose — generic gear is the Common-tier fodder,
// and not every item should carry a theme.
const CLASS_FALLBACK = { Dagger: 'Shadow', Wand: 'Arcane', Bow: 'Shadow' };

function detectTheme(name, cls) {
  for (const [theme, re] of THEMES) if (re.test(name)) return theme;
  return CLASS_FALLBACK[cls] || 'Unthemed';
}

// ---------- 2b. set family ----------
// Armor and its matching helmet are named independently, in two conventions:
//   1. "{Name}Armor"  ↔ "{Name}Helm|Hat|Hood|…"     (shared prefix)
//   2. "ArmorOf{Name}" ↔ "Helm(et)Of{Name}"          (shared suffix)
// Normalising both to a common stem lets the tool group a whole 4-piece set together.
const GARMENT = /(Armor|Robe|Dress|Outfit|Costume|Vestment|Tunic|Clothes|Mail|Plate|Cuirass|Jacket|Chainmail|Loincloth|Vest|Harness|Attire|Garb|Helmet|Helm|Hat|Hood|Mask|Ribbon|Halo|Crown|Cap|Headband|Earpiece|Circlet|Eyeguard|Gear)/i;

function setFamily(spriteName) {
  let s = String(spriteName).replace(/\s*\[.*?\]\s*/g, '').trim();   // drop " [Paint]"
  const of = s.match(new RegExp('^' + GARMENT.source + 'Of(.+)$', 'i'));
  if (of) s = of[2];                                   // ArmorOfAncestors -> Ancestors
  else s = s.replace(new RegExp(GARMENT.source + '$', 'i'), '');     // BerserkArmor -> Berserk
  s = s.replace(/(TypeA|TypeB|TypeC)$/i, '').replace(/\d+$/, '');    // drop variant markers
  return (s || spriteName).toLowerCase();
}

// ---------- 3. parse the CSV ----------
const lines = fs.readFileSync(CSV, 'utf8').split(/\r?\n/).filter(Boolean);
const header = lines[0].split(',');
const col = (n) => header.indexOf(n);
const cId = col('Id'), cType = col('Type'), cClass = col('Class'),
      cRarity = col('Rarity'), cSprite = col('SpriteId'),
      cIcon = col('IconId'), cName = col('Name_EN'), cEnabled = col('Enabled');

const items = [];
const missing = [];

for (let i = 1; i < lines.length; i++) {
  const f = lines[i].split(',');
  if (f.length < header.length) continue;
  if ((f[cEnabled] || '').toUpperCase() !== 'TRUE') continue;

  const iconId = (f[cIcon] || '').trim();
  const parts = iconId.split('.');
  const baseName = parts[parts.length - 1] || '';
  const slotFolder = (parts[parts.length - 2] || '').toLowerCase();

  let icon = iconIndex.get(`${slotFolder}/${baseName.toLowerCase()}`) || null;
  if (!icon) {
    // fallback: any slot folder with that basename
    for (const [k, v] of iconIndex) {
      if (k.endsWith(`/${baseName.toLowerCase()}`)) { icon = v; break; }
    }
  }
  if (!icon) missing.push(iconId);

  const spriteName = (f[cSprite] || '').split('.').pop();
  // Source pack, straight from the SpriteId: {Collection}.{Pack}.{Slot}.{Name}
  // Real metadata — reliable, unlike the keyword-based theme guess.
  const sp = (f[cSprite] || '').split('.');
  const pack = sp.length >= 2 ? `${sp[0]}.${sp[1]}` : 'Unknown';
  items.push({
    id: f[cId],
    name: (f[cName] || f[cId] || '').trim(),
    slot: f[cType],
    cls: f[cClass],
    rarity: f[cRarity] || 'Common',
    sprite: spriteName,
    family: setFamily(spriteName),
    pack,
    icon,
    theme: detectTheme(`${spriteName} ${f[cId]}`, f[cClass]),
  });
}

const dataJs = 'const ITEMS = ' + JSON.stringify(items) + ';';
fs.writeFileSync(OUT, dataJs + '\n', 'utf8');

// ---------- bake a self-contained designer ----------
// The data is inlined so the page works from any location, with no sibling-file
// or CORS dependency. (Sprite images are still referenced by relative path, so
// open the result from disk in a real browser.)
const TEMPLATE = path.join(__dirname, 'designer-template.html');
const DESIGNER = path.join(__dirname, 'EquipmentDesigner.html');
if (fs.existsSync(TEMPLATE)) {
  const html = fs.readFileSync(TEMPLATE, 'utf8')
    .replace('<!--DATA-->', '<script>\n' + dataJs + '\n</script>');
  fs.writeFileSync(DESIGNER, html, 'utf8');
  console.log(`Baked designer -> ${path.relative(ROOT, DESIGNER)} (self-contained)`);
} else {
  console.warn('! designer-template.html not found — skipped baking EquipmentDesigner.html');
}

// ---------- report ----------
const byPack = {};
items.forEach(i => byPack[i.pack] = (byPack[i.pack] || 0) + 1);
console.log(`Packs: ${Object.keys(byPack).length} — ` +
  Object.entries(byPack).sort((a,b)=>b[1]-a[1]).slice(0,6).map(([k,v])=>`${k}:${v}`).join('  ') + ' …');

const byTheme = {};
items.forEach(i => byTheme[i.theme] = (byTheme[i.theme] || 0) + 1);
console.log(`Wrote ${items.length} items -> ${path.relative(ROOT, OUT)}`);
console.log(`Icons resolved: ${items.filter(i => i.icon).length}/${items.length}  (missing ${missing.length})`);
console.log('By theme:', Object.entries(byTheme).sort((a,b)=>b[1]-a[1]).map(([k,v])=>`${k}:${v}`).join('  '));
