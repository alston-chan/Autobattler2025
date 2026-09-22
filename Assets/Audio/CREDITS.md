# Audio credits

Where every sound in this game came from. One line per source, added when the file is added —
provenance is impossible to reconstruct later, and a clip whose origin nobody remembers is a clip
that has to be thrown away before release.

## Mixkit — free sound effects

<https://mixkit.co/free-sound-effects/sword/>

Placeholder combat audio while the real library is generated. These are stock clips, not authored
for this game: they are here so the fight can be heard and tuned now, and several will be replaced.

| File | What it is | Bank it belongs in |
|---|---|---|
| `mixkit-sword-slash-swoosh-1476.mp3` | sword swing through air | swing (see note) |
| `mixkit-metal-hit-woosh-1485.wav` | metallic swing | swing |
| `mixkit-dagger-woosh-1487.wav` | light, fast swing | swing |
| `mixkit-sword-blade-swish-1506.wav` | long blade swish | swing |
| `mixkit-fast-sword-whoosh-2792.wav` | fast swing | swing |
| `mixkit-swift-sword-strike-2166.wav` | a blade landing | `hitLight` |
| `mixkit-sword-strikes-armor-2765.wav` | a blade landing on armour | `hitLight` |

**Licence: not yet confirmed.** Mixkit publishes a "Sound Effects Free License" at
<https://mixkit.co/license/>, but the text sits behind a consent gate, so nothing about its terms is
recorded here rather than recorded wrongly. Read it and write the answer in this file before the
game ships — specifically whether attribution is required (if it is, this file has to reach players,
not just the repo) and whether redistribution inside a game build is permitted.

**Two notes on using them:**

- Five of the seven are *swings*, and `SfxLibrary` has no swing bank yet — `CombatAudio.SwingBank`
  currently borrows the hit bank, which would make a whoosh play as the sound of a blow landing.
  They need a bank of their own, fired from `CombatEvents.Cast` rather than `Hit`.
- Files dropped in outside the editor have no `.meta` yet and cannot be referenced until Unity
  imports them. Focus the editor, or `AssetDatabase.Refresh()`.

## Generated — ElevenLabs

Nothing yet. When clips arrive, record the prompt alongside the file: a generated sound is only
reproducible if the words that made it survive, and "make another one like that" is otherwise a
re-roll from scratch. Keep the generation date — the model behind a prompt changes.
