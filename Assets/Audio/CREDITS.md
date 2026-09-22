# Audio credits

Where every sound in this game came from. One line per file, added when the file is added —
provenance is impossible to reconstruct later, and a clip whose origin nobody remembers is a clip
that has to be thrown away before release.

Every file here is checked by `AudioCreditsTests`: a clip in `Assets/Audio` that this file does not
name fails the suite, and so does a row naming a clip that is gone. The list cannot quietly go
stale, and an uncredited clip is caught in the commit that adds it rather than the week before
release.

## Mixkit — free sound effects

<https://mixkit.co/free-sound-effects/sword/>

Placeholder combat audio while the real library is generated. Stock clips, not authored for this
game: they are here so the fight can be heard and tuned now, and most will be replaced.

| File | What it is | Where it plays |
|---|---|---|
| `mixkit-metal-hit-woosh-1485.wav` | metallic swing | **sword attack** (`DefaultMeleeAttack`) |
| `mixkit-dagger-woosh-1487.wav` | light, fast swing | **dagger attack** (`DefaultDualWieldAttack`, "Twin Blades") |
| `mixkit-knife-fast-hit-2184.wav` | a blade landing, fast | **Fan of Knives** — the thrown stars |
| `mixkit-sword-slash-swoosh-1476.mp3` | sword swing through air | unused |
| `mixkit-sword-blade-swish-1506.wav` | long blade swish | unused |
| `mixkit-fast-sword-whoosh-2792.wav` | fast swing | unused |
| `mixkit-metallic-sword-scrape-2799.wav` | blade scraping metal | unused — a candidate for a blocked hit |
| `mixkit-swift-sword-strike-2166.wav` | a blade landing | unused — a candidate for `hitLight` |
| `mixkit-sword-strikes-armor-2765.wav` | a blade landing on armour | unused — a candidate for `hitLight` |
| `mixkit-heavy-sword-hit-2794.wav` | a heavy blow landing | unused — a candidate for `hitHeavy` |
| `mixkit-sword-blade-attack-in-medieval-battle-2762.wav` | a blow landing, battle-flavoured | unused — a candidate for `hitHeavy` |

**Licence: not yet confirmed.** Mixkit publishes a "Sound Effects Free License" at
<https://mixkit.co/license/>, but the text sits behind a consent gate, so nothing about its terms is
recorded here rather than recorded wrongly. Read it and write the answer in this file before the
game ships — specifically whether attribution is required (if it is, this file has to reach players,
not just the repo) and whether redistribution inside a game build is permitted.

## Provenance not yet recorded

| File | What it is | Where it plays |
|---|---|---|
| `backstab.wav` | a stab, with a sting | **Backstab** (`NinjaBackstab`) |

`backstab.wav` arrived without a source. It is wired up and audible, so it will ship unless someone
remembers: **write down where it came from, or replace it.** A clip that cannot be placed has to be
cut, and the cheapest moment to answer this is now.

## Generated — ElevenLabs

Nothing yet. When clips arrive, record the prompt alongside the file: a generated sound is only
reproducible if the words that made it survive, and "make another one like that" is otherwise a
re-roll from scratch. Keep the generation date — the model behind a prompt changes.

## How a clip gets used

A weapon's swing is a spell — a sword casts `DefaultMeleeAttack`, daggers cast
`DefaultDualWieldAttack` — so naming the spell in `SfxLibrary.spellSounds` is already naming the
weapon, and there is no separate swing layer to wire. Anything not named there falls back to the
hit bank for whatever the caster is holding, which is why an unlisted verb is quiet rather than
silent.

Spell sounds play when the cast **begins**. A sound that should instead play when a spell *lands*
has nowhere to go yet: `CombatEvents.Hit` does not carry the spell that caused it.
