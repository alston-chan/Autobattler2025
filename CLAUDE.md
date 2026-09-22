# Working in this project

Hard-won operational notes. Most of these cost an hour or more to learn the first time,
and every one of them fails *silently* — which is what makes them worth writing down.

## Unity MCP: the session degrades as you use it

Every `script-execute` compiles an assembly, and that triggers a domain reload. A domain
reload wipes statics and non-serialized fields **without re-running `Awake`**, so after a
few probes the running game is quietly broken:

- `ItemCollection.Active` → null (so `new Item(id)` throws for *every* id)
- `GameManager.Instance` → null
- `Entity.HitFeedback`, `Entity.Health`, and the other component references → null
- `FirearmCollection.Instances` → empty
- C# event subscriptions → gone

**These readings are all false alarms in a degraded session:**
`The name 'X' does not exist in the current context` · `X is null` · zero entities found ·
`sprite=none` · item construction throwing `NullReferenceException` · a component that
`Awake` unconditionally assigns reading as null.

**Rule: only trust a reading taken in a freshly started play session.** When a probe reports
something alarming, restart play and re-probe *before* believing it or acting on it. Batch
everything you want to know into ONE script rather than a series of them — each extra script
degrades the session further. This has produced several confident, completely wrong diagnoses.

## Hot Reload: edit, re-probe, recompile only when you must

Hot Reload (`Packages/com.singularitygroup.hotreload`) patches **method bodies** into the running
play session, so the stop → recompile → replay → re-setup cycle (about a minute of waiting each
time) is only needed for changes it cannot patch: a new or changed field, a new type, a changed
signature, an attribute, a field initializer, an enum. For a method-body change: edit the file,
wait for its patch to land, re-run the probe in the same play session.

Its trap is the same one as the domain reload's, from the other direction: a patched method runs
against whatever state the *old* code left. Statics are not reinitialised, a changed constructor
or `Awake` does not re-run, a changed field initializer does not touch existing instances. So
**when something looks wrong right after a hot reload, do a real recompile before believing it.**
And a change that adds a field must be followed by a real recompile before any probe is trusted at
all — Hot Reload may report it as applied while the inspector and serializer know nothing of it.
The MCP's own `script-execute` still compiles an assembly and reloads the domain as before; Hot
Reload changes nothing about that rule.

**A change Hot Reload cannot patch does not load until you make it.** Hot Reload turns Unity's
auto-refresh off while its server runs and queues unsupported changes (a field initializer, a new
field or type) for "later": `EditorApplication.isCompiling` stays true, `RequestScriptCompilation`,
`RequestScriptReload` and an asset refresh all do nothing, and — the trap — `tests-run` reports
Passed against the *old* assembly. Measured: after `LeashSeconds` went 1.5 → 5, the loaded value
read 1.5 for half an hour and 91 tests passed on it. `Window > Hot Reload > Recompile` (or the
menu item by that path from a script) is what makes it load. After any non-method-body change,
read the value back from the running editor before trusting a test run or a probe.

Measured on install (2026-09-03): a one-line change to a method body was live in the running play
session within 10 s of saving the file, with the frame counter, the game state and a static marker
all intact, and the reverse edit landed the same way. Check its server is up before relying on it
(`Window > Hot Reload`; the run tab says Started) — a patch that never lands looks exactly like a
change that did nothing. In that same session a static set by one probe survived four later probes,
which the domain-reload rule above says it should not have; whether that is Hot Reload suppressing
auto-refresh or the old rule blaming the wrong thing is unmeasured, so the rule stays.

## Editing the scene

- **Never `git checkout` or otherwise rewrite a scene file while Unity has it open.** It
  raises a modal — *"The open scene(s) have been modified externally"* — that blocks the
  editor's main thread, so every MCP call times out at 60s while the process still reports
  `Responding: True`. Unity's dialogs are custom-drawn IMGUI, so they cannot be found or
  clicked programmatically: only the user can dismiss it. Undo from *inside* Unity instead
  (destroy the objects, save the scene).
- **When every MCP call times out, look before concluding.** Windows UI Automation from
  PowerShell can enumerate Unity's top-level windows and read a native dialog's text and buttons
  without touching anything (`AutomationElement.RootElement.FindAll` filtered by Unity's process
  id; a native dialog has class `#32770`). Measured 2026-09-03: a 60 s-timeout "freeze" that
  looked like a modal was a long stall — one window, no dialog, `Responding=True` a few minutes
  later. Do not restart the editor on the timeout alone. Measured 2026-09-11: a domain reload after
  `Window > Hot Reload > Recompile` hung for good — `Editor.log` frozen at "Loading mode Default",
  0.1 CPU-seconds in ten minutes, the MCP plugin answering 503, no window but the main one. That one
  needed a restart (`Stop-Process`, then `Unity.exe -projectPath`); nothing on disk was lost.
- **Never `Object.Instantiate` a scene prefab instance to duplicate a unit.** It unpacks the
  prefab and writes the entire rig into the scene: measured at 85,530 inserted lines versus
  333 for the correct route. Use
  `PrefabUtility.InstantiatePrefab(prefabAsset, parent)` and then
  `EditorUtility.CopySerialized(sourceEntity, newEntity)` to carry the configuration over
  without touching the prefab link. Check `PrefabUtility.IsPartOfPrefabInstance(go)` after.
- **Play-mode state leaks into the authored scene.** Units deactivated on death
  (`DeathFeedback.persistOnDeath`) stay deactivated in the editor's copy of the scene, and an
  edit-mode save then makes that permanent. This silently swapped three benched test heroes
  for three real ones.
- **Enabling or disabling a scene object in edit mode is not saved unless the scene is saved**
  — and a recompile reverts it. A test hero that "isn't firing" is usually a test hero that
  is not on the field.
- **Verify scene changes semantically, never by reading `git diff`.** Unity reorders
  PrefabInstance blocks, so the textual diff shows `m_IsActive` values appearing and
  disappearing that mean nothing. Parse both versions and compare per object:

```python
import io, re, subprocess
def heroes(text):
    out = {}
    for block in text.split('PrefabInstance:'):
        names = re.findall(r'propertyPath: m_Name\s*\n\s*value: (\S+)', block)
        acts  = re.findall(r'propertyPath: m_IsActive\s*\n\s*value: (\d+)', block)
        if names: out[names[0]] = acts[0] if acts else '1(default)'
    return out
cur  = heroes(io.open('Assets/Scenes/Main.unity', encoding='utf-8', errors='replace').read())
head = heroes(subprocess.run(['git','show','HEAD:Assets/Scenes/Main.unity'],
                             capture_output=True, text=True).stdout)
print([k for k in set(list(cur)+list(head)) if head.get(k,'(absent)') != cur.get(k,'(absent)')])
```

## Saving assets from editor tooling

`AssetDatabase.SaveAssets()` writes **every** dirty asset, not the one you meant. A play session
can leave a run asset dirty in memory — `DemoRun` came back from a fight carrying an `act`
reference it never had on disk — and the next save-all from a designer button wrote it out, which
failed `RunSaveTests.AFlatRunResumesAtItsIndex` with a null encounter and nothing in the diff to
say why. Prefer `AssetDatabase.SaveAssetIfDirty(asset)` for the asset you changed, and check
`git status` after any save from tooling: an unexpected `M` on a data asset is this.

**A new serialized field's C# default never reaches a loaded asset.** A ScriptableObject already in
memory survives the domain reload by serialization, so a changed field initializer — even inside a
nested `[Serializable]` settings block the YAML does not carry yet — reads as the *old* value after
a recompile, while a fresh `CreateInstance` would read the new one. Measured 2026-09-16: physics
damage defaults changed in code, tests passed, the editor still read the old numbers. Set the value on
the live asset from a script and `SaveAssetIfDirty` it (which also writes the block to disk), or edit
the `.asset` block by hand; then read it back.

## The item collection is generated

`Assets/Data/ItemCollection.asset` is rebuilt from `Assets/Data/Items.csv` + `Properties.csv` by
`Tools > Item Database > Import CSV into ItemCollection`, and the import **replaces** the list.
Anything added to the collection by hand is gone after the next import, and the first sign is a
test about the workshop bag failing. Add rows to the CSV, never to the asset. Gloves rows are
disabled there on purpose (armour is upper + lower now), and so are the `Spellbook.*` rows since
2026-09-17: abilities come from weapons (the rack). The spellbook layer and the old ability spell
classes (Shockwave, Double Strike, Multi Shot, the throws, the old Backstab) were deleted 2026-09-18;
every ability is a `CompositeSpell` taught by a weapon.

## Building test rigs

- Don't make enemies unkillable to lengthen a fight — the company is slaughtered and every
  probe afterwards reports nonsense about heroes that are dead and deactivated. Inflate
  *ally* health far more than enemy health.
- A hero enabled mid-play never gets gear: it misses `GameManager.SetupCharacterInventories`.
  Toggle in edit mode, then press Play.

## script-execute quirks

- `isMethodBody: true` wraps the code in a method, so `using X = Y;` aliases are a compile
  error (`CS1001`). Fully qualify types instead.
- Windows `python3` cannot see Git Bash's `/tmp`. Stage payloads in the scratchpad directory.
- The MCP layer may run a script twice. Make probes idempotent, and never write an unbounded
  `while` loop — one hung Unity's main thread and needed a force-kill.
- No `using` directives of any kind in a method body — fully qualify (`UnityEditor.AssetDatabase`,
  `System.Linq.Enumerable.FirstOrDefault(...)`), and no local generic functions. `System.Tuple` is
  ambiguous with `ExCSS.Unity` — use `object[]`.
- HeroEditor's `ItemWorkspace.SelectedItem` has a protected setter; a probe that equips through the
  inventory's own path sets it by reflection (`GetProperty(...).GetSetMethod(true).Invoke`) and then
  calls `Equip()`. Equipping the same item id twice in one play session trips a `SingleOrDefault`
  in the inventory — restart play between equip probes. `console-clear-logs` isolates a run's logs.
- Daggers are paired (`DualWield.IsPaired`), so a dagger and a shield cannot be worn together.
- `console-get-logs` lists oldest-first and keeps lines from earlier runs even after
  `console-clear-logs` in play mode. Tag each probe's result line with something unique to that run
  and take the *newest* match (`tail -1`), never the first — a stale line from the previous run reads
  exactly like a fresh result.
- **`console-get-logs` stops receiving once the session has degraded.** The plugin's log capture is a
  C# subscription, and a domain reload takes it with everything else: after a few `script-execute`
  calls in one play session the tool keeps answering with the same last hundred lines (ending at
  "PLAY requested") while the game logs on, and `console-clear-logs` errors. Measured 2026-09-16:
  three probe runs produced no visible line at all. Have a probe write its report to a file in the
  scratchpad (`File.AppendAllText`) and poll the file; `scratchpad/file_run.sh` is that runner.
- The MCP running a script twice is real: a play probe that adds spells and subscribes handlers ran
  twice in one call and doubled every count. Guard with a marker object (`GameObject.Find("X_ARMED")`)
  created on arming and destroyed when the probe ends.
- **A probe that subscribes to `EditorApplication.update` must catch its own exceptions.** The
  callback is never removed when it throws, so it throws again the next tick, forever — and because
  the exception propagates out of `Internal_CallUpdateFunctions`, it takes the rest of that tick's
  callbacks with it, the MCP plugin's pump included. Measured 2026-09-21: a probe that read a
  `CharacterInventory` after play mode stopped put a NullReferenceException in `Editor.log` on every
  tick, and from that moment *every* `script-execute` timed out at 60 s while the editor still
  reported `Responding: True` and showed no dialog. Nothing but a domain reload clears the
  subscription, and nothing could reach the editor to cause one: it needed a restart. Wrap the whole
  body in `try { ... } catch { EditorApplication.update -= cb; ... }` — every probe in the scratchpad
  now carries that guard, marked `PROBE_GUARDED`.

## Play checks: the tests that need a running game

`Tools > Tests > Run Play Tests` starts a play session, runs `PlayChecks`, writes
`Temp/PlayTests.txt` and stops. Start it and poll that file — a whole fight is minutes and the MCP
`tests-run` tool gives up at sixty seconds, so it reports to a file rather than to its caller, the
same shape every probe here arrived at. The ordinary `tests-run` is untouched and still about a
second.

Three things cost an afternoon to learn:

- **NUnit cannot host these.** The obvious build is `[UnityTest]` with `EnterPlayMode`, and the
  framework answers *"EditMode test can only yield null, but not &lt;EnterPlayMode&gt;"*. That
  capability is only granted to tests in a test assembly defined by an asmdef — and an asmdef
  assembly cannot reference the predefined `Assembly-CSharp`, which is where this whole game lives.
  So `PlayTestRunner` drives the checks itself, off `EditorApplication.update`, and NUnit's `Assert`
  is used only for the assertions and their messages.
- **A hand-rolled coroutine driver must step into nested `IEnumerator`s.** Calling `MoveNext` on
  only the outermost one ran all four checks to completion in a tenth of a second and reported four
  passes. A harness that lies is worse than no harness: keep a `Stack<IEnumerator>`, push whatever
  `Current` turns out to be one, and **prove it fails** by reintroducing a real bug before trusting
  a green run.
- **Entering play mode reloads the domain**, so the run is two halves: `Run()` leaves a note in
  EditorPrefs and asks for play mode, and an `[InitializeOnLoad]` static picks it up on the far side.
  The checks then share one session — `PlayHarness.ReachTheBell` works whether the game is in Setup,
  already fighting, or between fights — because a session per check would mean carrying the run's
  progress across a reload too.
- **Because they share a session, a check's leftovers are the next check's fight.** The decoy checks
  leave decoys alive for six seconds, decoys taunt, and the pacing check that runs next counted every
  taunted unit walking past a kiter to reach one as "walking past a fight" — 43–47%, failing at
  random, for an afternoon. A check that spawns something must either clean it up or the checks
  after it must expect it; and a metric that stands in for a game rule must carry the rule's
  exclusions (taunted, Relentless, kiting, thrown). Likewise a check about *where units start* must
  watch for the Combat transition itself, not "is a fight happening" — mid-session that samples a
  fight in progress. `PlayChecks` runs that one first for exactly this reason.

## Odin

Odin Inspector is installed (`Assets/Plugins/Sirenix`) for its **attributes only**. Never derive from
`SerializedScriptableObject` / `SerializedMonoBehaviour` or otherwise turn on Odin serialization:
it stores those fields as opaque bytes in the YAML, which breaks readable diffs and every text-based
asset edit this project relies on. Item ids are offered as dropdowns through `Catalog.ItemIds()`
(one `ItemIds()` provider per class, referenced by member name so it works on any Odin version);
`Tools > Equipment > Designer` is the Odin window that puts an item, its resonance entry and its
engraving on one page. `Sirenix` is one more vendor namespace to check before naming a global type.

## Naming against HeroEditor

Vendor namespaces collide with obvious type names. `Firearms` is both
`Assets.HeroEditor.Common.Scripts.CharacterScripts.Firearms` (a namespace) and
`Character.Firearms` (a field), so a global `Firearms` class is ambiguous wherever either is
in scope — hence `FirearmRig`. Check for a vendor namespace before naming a new global type.

## Tests

Live in `Assets/Editor/Tests/`, plain NUnit, and **no assembly definition is needed or wanted**.
Being under an `Editor/` folder puts them in `Assembly-CSharp-Editor`, which already references
`Assembly-CSharp` — where all the game code is — and resolves NUnit. An `.asmdef` would actively
break this: asmdef assemblies cannot reference the predefined `Assembly-CSharp`, so testing this
code that way would mean moving the whole game into an asmdef, and HeroEditor with it, since the
game depends on it.

Run them:

```bash
npx unity-mcp-cli run-tool tests-run . --input '{"testMode":"EditMode"}'
```

About a second for the current suite. In the editor it is Window → General → Test Runner →
EditMode → Run All. Note that `tests-run` reports compilation errors clearly and reliably, which
`console-get-logs` does not — when a refresh seems to have gone quiet, run the tests to find out.

Writing them:

- Anything touching items must set `ItemCollection.Active` itself. The game assigns it from an
  inspector field on the inventory prefab at runtime, so it is null in a test; a `[OneTimeSetUp]`
  loading `Assets/Data/ItemCollection.asset` is the pattern.
- Assert on collections with `Has.Member` / `Has.No.Member`. `Does.Contain` binds to the string
  overload and fails to compile against a `List<Item>`.
- **A test that has never failed is not evidence.** Break the rule on purpose, watch the right test
  go red with a message that explains it, then restore. The suite here was checked that way.

## Project facts that look like bugs

- **The company fields five heroes.** Extra heroes are benched as *inactive* scene objects;
  that is the mechanism, not corruption. An inactive hero never runs `Awake`, so probes show
  `Health == null` and no animator — indistinguishable from a broken unit at a glance. A
  *dead* hero is also inactive; tell them apart by whether `Health` was ever initialised.
  **The fallen leave `EntityRegistry`** (it is filled from `OnEnable`), so any end-of-fight loop over
  the registry skips them — the stand-down and combat-end hooks must also walk `allyCharacters`, and
  a revive must reset everything the fight left on the body (`DeathFeedback.RestoreAfterRevive`).
  A hero killed mid-swing once revived unable to attack or move because nothing had cleared
  `CombatAI`'s attacking flag, and one killed inside a hitstop revived frozen.
- **An engraving hears about its grant after the books have it.** `Resonance.Refresh` updates
  `_active` and only then calls `OnGranted` / `OnRevoked`, and fires `OnGrantsChanged` once at the end;
  a hero's inventory rebuilds the spell slots from that event, and only when the set of verbs differs.
  It was the other way round until 2026-09-18, and the symptom was a hero with no ability: a fight's
  sixth cast crossed tier II, the verb's grant was revoked and re-granted, and its `OnGranted` rebuilt the
  slots from a list its own grant was not yet in. Measured: `slots=[]` mid-fight with the wand still
  worn and still granted. The extra `SyncSpellSlots` after the startup refresh in GameManager was the
  same bug patched at one call site.
- **Not every clip in `Assets/Audio` is ours.** Some are Mixkit stock placeholders
  (<https://mixkit.co/free-sound-effects/sword/>) standing in until the real library exists, and
  their licence terms have not been confirmed yet — the text is behind a consent gate on
  mixkit.co/license. `Assets/Audio/CREDITS.md` is the record: add the source line in the same commit
  as the file, because provenance cannot be reconstructed afterwards and a clip nobody can place has
  to be cut before release. Generated clips record the prompt that made them; without it, "one more
  like that" is a re-roll from scratch.
- **Physics damage is flat per slam, and knockback resistance is `PropertyId.Resistance`.** A thrown
  unit loses `wallSlamPercent` into the wall and takes `bodySlamPercent` off a unit it hits; below
  `impactSpeed` nothing happens; a throw's `impactMultiplier` is the only thing that changes it, and
  `CombatPhysics.DescribeThrow` prints the rule on every verb from the live numbers. There is no mass
  any more (it was a stat the player had to weigh on every reward screen for an effect they could
  barely see). Knockback resistance is `PropertyId.KnockbackResist`, as a percent in
  `Properties.csv`, and is meant to be rare. Slams are physical: armour reduces them like any hit. Slams show as `SLAM n` in their own colour and have a
  `slam` column in the telemetry, so "where did that damage come from" has an answer on screen.
- **`PropertyId` is a serialization contract: append, never insert.** `ItemCollection.asset` stores
  a property as its integer (`Id: 30`), so a member added anywhere but the end silently renumbers
  every property on every item. `Armor`, `MagicResist` and `KnockbackResist` are appended after the
  vendor's `Shock`; `PropertyIdContractTests` pins the numbers. Damage goes through one pipeline
  (`Health.TakeDamage`): armour or magic resist by `DamageType` on the LoL curve
  (`Mitigation.Reduce`, 100/(100+rating)), then statuses, then shields. Blocking is gone — it was a
  flat, half-capped reduction whose worth depended on what was swinging. Effects, statuses and the
  projectile declare their type; the wand and its verbs are magical.
- **Armour-slot rows in `Properties.csv` are authored by tier, not by hand and not uniformly.** The
  vendor's example CSV gave every vest the same four numbers; `Tools > Item Database > Author Armour
  Rows By Tier` (`DefensiveStatsAuthor`) rewrites vests, boots, helmets, shields and capes from a
  tier read off the name — plate is armour and health, leather a little armour and speed, cloth is
  magic resist, a cosmetic is nothing — then re-imports. The table is the one place to retune; the
  rare `KnockbackResist` lines survive a rewrite; weapons are untouched. `MitigationTests` pins the
  kits' tiers and that armour is not a base value.
- **`Docs/` is gitignored** (`# Local design docs`). The design docs are deliberately
  untracked, so changes there are never committed.
- Vendor code in `Assets/HeroEditor` is edited only where it is genuinely broken for this
  project (`Projectile`'s 3D bullet, `CharacterInventorySetup`'s unimplemented firearm equip and its
  cape handling — a cape is typed Armor and used to wipe the armour equipped before it — and
  `Equipment.FindItem`, which mirrors a paired dagger into the off-hand slot as it already did for
  two-handers; `ItemInfo`'s and `InventoryItem`'s label fields, retyped from `UnityEngine.UI.Text` to
  `TMP_Text` on 2026-09-19 so the workshop draws TextMeshPro like the rest of the game — the three
  prefabs it uses (`CustomInventory`, `AvatarPrefab`, HeroEditor's `Item`) were converted with it, and
  a field retyped BEFORE its prefab is converted loses its reference, so re-point by name afterwards;
  and both `ScaleSpring`s, HeroEditor's and FantasyMonsters', whose hit squash wrote back the
  facing it captured when the hit landed, so a unit turned during the squash — the fight ending and standing
  it down — snapped back to face the wrong way). Vendor files are CRLF: a text edit must match `
`.
  Each such edit says in a comment what it replaced and why.
