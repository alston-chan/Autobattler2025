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

## Editing the scene

- **Never `git checkout` or otherwise rewrite a scene file while Unity has it open.** It
  raises a modal — *"The open scene(s) have been modified externally"* — that blocks the
  editor's main thread, so every MCP call times out at 60s while the process still reports
  `Responding: True`. Unity's dialogs are custom-drawn IMGUI, so they cannot be found or
  clicked programmatically: only the user can dismiss it. Undo from *inside* Unity instead
  (destroy the objects, save the scene).
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

## Naming against HeroEditor

Vendor namespaces collide with obvious type names. `Firearms` is both
`Assets.HeroEditor.Common.Scripts.CharacterScripts.Firearms` (a namespace) and
`Character.Firearms` (a field), so a global `Firearms` class is ambiguous wherever either is
in scope — hence `FirearmRig`. Check for a vendor namespace before naming a new global type.

## Project facts that look like bugs

- **The company fields five heroes.** Extra heroes are benched as *inactive* scene objects;
  that is the mechanism, not corruption. An inactive hero never runs `Awake`, so probes show
  `Health == null` and no animator — indistinguishable from a broken unit at a glance. A
  *dead* hero is also inactive; tell them apart by whether `Health` was ever initialised.
- **`Docs/` is gitignored** (`# Local design docs`). The design docs are deliberately
  untracked, so changes there are never committed.
- Vendor code in `Assets/HeroEditor` is edited only where it is genuinely broken for this
  project (`Projectile`'s 3D bullet, `CharacterInventorySetup`'s unimplemented firearm equip).
  Each such edit says in a comment what it replaced and why.
