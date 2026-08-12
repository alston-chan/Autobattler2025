# Equipment Designer

A local, zero-install tool for designing equipment against your **real sprites**, and generating
`ItemDefinition` ScriptableObjects from the result.

---

## ⚡ Quick start

> ### Double-click **`Tools\start-designer.bat`**
> It rebuilds the catalog, starts the server, and opens your browser. Done.

Or by hand, from the project root:

```bash
node Tools/serve.js
```

then open **http://localhost:8642**

| | |
| --- | --- |
| 🔗 **Link** | **http://localhost:8642** |
| 💾 **Saves to** | `Tools/equipment-design.json` — automatically, ~0.5 s after each edit |
| ✅ **Working?** | Header shows **✓ auto-save on** in green |
| ⚠️ **Not working?** | Header shows **local only — Export to save** → you opened the HTML directly instead of via the server |
| 🛑 **Stop** | Close the window, or `Ctrl+C` |

**Rebuild the catalog** (`node Tools/build-data.js`) only when you change `Assets/Data/Items.csv` or
`Tools/designer-template.html` — the launcher does it for you every time anyway.

---

## The loop

```
Items.csv + 5,134 icon PNGs
        │  node Tools/build-data.js
        ▼
  EquipmentDesigner.html   (browse · filter · author)
        │  auto-save  ⇄  node Tools/serve.js
        ▼
  equipment-design.json          ← the database. commit this.
        │  Unity: Tools > Equipment > Import Design JSON
        ▼
  Assets/Data/ItemDefinitions/*.asset   (ItemDefinition + EffectPools SOs)
```

## Usage

**1. Build the data** *(re-run whenever `Items.csv` changes)*

```bash
node Tools/build-data.js
```

Indexes all 1,868 enabled items, resolves each one's icon PNG, and auto-tags a **theme** from the
sprite name. Currently resolves **1868/1868 icons**.

`build-data.js` also **bakes the catalog into `EquipmentDesigner.html`** (from
`designer-template.html`), so the tool is self-contained — no sibling-file or CORS dependency.

> **Edit `designer-template.html`**, not `EquipmentDesigner.html` — the latter is generated and gets
> overwritten on every build.

**2. Open the designer**

**Recommended — run the local server** (zero dependencies). This is what gives you **auto-save to a
real file**:

```bash
node Tools/serve.js
```

Then open **http://localhost:8642**. Every edit auto-saves (debounced ~0.5 s) to
`Tools/equipment-design.json`, and the header shows **✓ auto-save on**. On startup the page *loads*
that file, so **the JSON — not browser storage — is the source of truth**. Commit it.

**Or double-click `Tools/EquipmentDesigner.html`** to open it from disk. Everything works except
auto-save: your work lives in browser `localStorage` until you press **Export JSON**. The header reads
**local only — Export to save**, so the mode is never ambiguous.

### Troubleshooting

| Symptom | Cause |
| --- | --- |
| **Grid is empty / "0 designed"** | Viewing it in a preview pane, or the file predates baking. Re-run `node Tools/build-data.js`. |
| **Rarity filters show 0 items** | Expected — **all 1,868 items ship as `Common`**; rarity is what *you* assign. Counts update live. |
| **No images, UI works** | Opened via a `data:` URL. Open from disk, or use the server. |
| **Header says "local only"** | Not served by `serve.js` — start it and use `http://localhost:8642` for auto-save. |

**3. Design**

- Filter by **pack · slot · theme · rarity · status**, or search by name.
  - **Pack** is parsed straight from the SpriteId (`{Collection}.{Pack}.{Slot}.{Name}`) — **real
    metadata, 28 groups**, so it's the reliable axis. Several map onto design concepts already:
    `FantasyHeroes.Knights`, `.Samurai`, `.Vikings`, `.SandLords` / `.SwampLords` (enemy factions),
    `UndeadHeroes.Skeletons` / `.Zombies` / `.Mummies`.
  - **Theme** is a **keyword guess** from the sprite name (see `THEMES` in `build-data.js`) — useful,
    but edit the regexes when it's wrong. ~854 items are deliberately `Unthemed`: generic gear
    (`CommonSword`, `BalancedAxe`) is the Common-tier fodder and shouldn't carry an identity.
- Click any item → author its **Affix** (stat + **roll range** + unit), **Signature Engraving**
  (name + text), **Resonance requirement** and three tier thresholds, plus notes. Theme and rarity can
  be overridden per item.
- 🧩 **Group by set** — collapses the grid into **sprite families**, showing every slot of a set on one
  row (*"show me all the AngelicDress gear together"*), with a per-set `designed` count. This is the
  fast way to author a themed set's complementary pieces, since you see them side by side.
- **Fixed vs rolled** *(see [EquipmentDesign.md](../Docs/EquipmentDesign.md) Principle 1b)*: the
  signature Engraving is 🔒 **fixed** — it's the identity players hunt and attune toward. Affix
  **values roll** in your range. Selecting **Epic/Legendary** reveals a **Roll pool** field: those
  rarities get a 🎲 **second Engraving rolled from that pool** at drop time, so the same item plays
  differently across runs.
- The editor shows the **verb** expected at that rarity (HOOK / AMPLIFY / BREAK / REDEFINE — see
  [Rarity.md](../Docs/Rarity.md)).
- **"Next undesigned →"** walks the current filter — the fast way to grind out a theme ladder.
- With the server running, edits **auto-save to `equipment-design.json`**. Without it, work sits in
  `localStorage` until you press **Export JSON**.

**3b. Author the pools** — tabs **Affix Pool** and **Engraving Pool**

- **Affix Pool** — the rolled stat modifiers. Name · Stat · Unit · Min · Max · Slots · Themes · Weight.
  `Slots`/`Themes` restrict where an affix can appear (blank or `any` = anywhere); higher `Weight` rolls
  more often. **"Seed starter set"** fills 12 sensible affixes to edit.
- **Engraving Pool** — the 🎲 second Engravings that **Epic/Legendary** items roll at drop time. Name ·
  Text · Verb · Pools · Slots · Weight. An item's **Roll pool** field matches against `Pools`. Keep
  `Verb` one tier *below* the host item so the signature stays the star. **"Seed starter set"** fills 14.
- Rows edit inline and save instantly; ✕ deletes.

**4. Generate ScriptableObjects**

In Unity: **Tools ▸ Equipment ▸ Import Design JSON** → pick your exported file. Creates/updates:

- one **`ItemDefinition`** asset per designed item, and
- a single **`EffectPools.asset`** (`EffectPoolDatabase`) holding both pools,

all in `Assets/Data/ItemDefinitions/`. `EffectPoolDatabase` also ships query helpers —
`AffixesFor(slot, theme)`, `EngravingsFor(poolTag, slot)`, and a `WeightedPick` — so the drop
generator can roll straight off it.

## Files

| File | Purpose |
| --- | --- |
| `build-data.js` | Scans the catalog, resolves icons, detects themes; bakes the designer |
| `serve.js` | Local server — serves the tool + sprites, and auto-saves the design JSON |
| `equipment-design.json` | ⭐ **The database.** Auto-saved by the server; commit it |
| `designer-template.html` | ✏️ **The source you edit** |
| `EquipmentDesigner.html` | **Generated** — template + baked catalog, self-contained (~530 KB) |
| `equipment-data.js` | Generated. The raw catalog, kept for scripting/inspection |
| `Assets/Scripts/Data/ItemDefinition.cs` | The ScriptableObject the design becomes |
| `Assets/Scripts/Editor/EquipmentDesignImporter.cs` | JSON → ScriptableObject importer |

## Notes

- **Import** re-loads a previously exported JSON, so the design database is portable and diffable.
  Commit `equipment-design.json` — that file *is* your equipment database.
- Theme auto-detection is keyword-based (see `THEMES` in `build-data.js`); ~1,014 items get a theme,
  the rest land in `Unthemed` and can be assigned by hand. Tweak the regexes to improve coverage.
- `ItemDefinition` currently stores design text. When the ability system lands
  ([Architecture.md](../Docs/Architecture.md) Phase 1), swap the string fields for real
  `Ability`/`Effect` references — the importer is the only thing that needs to change.
