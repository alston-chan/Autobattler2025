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
  later. Do not restart the editor on the timeout alone.
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
- **`Docs/` is gitignored** (`# Local design docs`). The design docs are deliberately
  untracked, so changes there are never committed.
- Vendor code in `Assets/HeroEditor` is edited only where it is genuinely broken for this
  project (`Projectile`'s 3D bullet, `CharacterInventorySetup`'s unimplemented firearm equip and its
  cape handling — a cape is typed Armor and used to wipe the armour equipped before it).
  Each such edit says in a comment what it replaced and why.
