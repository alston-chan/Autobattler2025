using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every sound the game makes, on one asset — the same shape as <see cref="CombatFeelSettings"/>
/// and for the same reason: a ScriptableObject keeps edits made during Play mode, so volumes and
/// gaps can be balanced while a fight is actually running rather than from memory afterwards.
///
/// Auto-loaded from <c>Resources/SfxLibrary.asset</c>. A bank with no clips in it is silent and
/// costs nothing, so the library works while it is half-full: fill in the melee hits, hear them in
/// the next fight, leave the rest for later.
/// </summary>
[CreateAssetMenu(menuName = "Data/Sfx Library", fileName = "SfxLibrary")]
public class SfxLibrary : ScriptableObject
{
    /// <summary>A bank tied to one spell — the verbs, and any weapon attack worth its own sound.</summary>
    [Serializable]
    public class SpellSound
    {
        public Spell spell;
        public SfxBank bank = new SfxBank();
        [Tooltip("Played when the cast begins rather than when it lands. Off for a sound that IS " +
                 "the landing (a nova's woomph), on for a wind-up (a bow being drawn).")]
        public bool onCast = true;
    }

    private const string ResourcePath = "SfxLibrary";

    [Header("Diagnostics")]
    [Tooltip("Silence the whole game. For telling a sound problem apart from a timing problem — " +
             "a ScriptableObject, so it can be toggled mid-fight.")]
    public bool enabled = true;

    [Range(0f, 1f)]
    [Tooltip("Scales everything below. The per-bank volumes are the mix; this is the fader.")]
    public float masterVolume = 1f;

    [Header("Weapon hits — chosen by what the attacker is holding")]
    [Tooltip("A sword, a dagger, a fist: the routine hit. This is the most-heard sound in the game.")]
    public SfxBank hitLight = new SfxBank();
    [Tooltip("A hammer, an axe, a lance — anything swung with both hands.")]
    public SfxBank hitHeavy = new SfxBank();
    [Tooltip("An arrow arriving.")]
    public SfxBank hitPierce = new SfxBank();
    [Tooltip("A wand bolt landing.")]
    public SfxBank hitMagic = new SfxBank();
    [Tooltip("A firearm's shot landing.")]
    public SfxBank hitGun = new SfxBank();

    [Tooltip("Layered ON TOP of the hit above, not instead of it — so a crit is the ordinary sound " +
             "plus a sting, which is what makes it read as the same blow landing harder.")]
    public SfxBank crit = new SfxBank();

    [Header("Bodies — the physics layer")]
    [Tooltip("A Light unit landing on something. Mass decides which of these three plays, so the " +
             "player can hear the difference a heavy kit makes before reading it on the card.")]
    public SfxBank bodyLight = new SfxBank();
    public SfxBank bodyMedium = new SfxBank();
    public SfxBank bodyHeavy = new SfxBank();
    [Tooltip("A body thrown into the arena wall.")]
    public SfxBank wallSlam = new SfxBank();

    [Header("Deaths")]
    [Tooltip("A human going down.")]
    public SfxBank deathHuman = new SfxBank();
    [Tooltip("A monster going down — rats, and anything else on the FantasyMonsters rig.")]
    public SfxBank deathBeast = new SfxBank();
    [Tooltip("Something that was never alive: a scarecrow, a decoy.")]
    public SfxBank deathProp = new SfxBank();

    [Header("Shields")]
    public SfxBank shieldUp = new SfxBank();
    [Tooltip("Broken by a hit. A shield that merely lapsed says nothing — silence is how the " +
             "player tells the two apart.")]
    public SfxBank shieldBreak = new SfxBank();

    [Header("Movement")]
    [Tooltip("A unit moved in one step: a blink, a substitute, a throw.")]
    public SfxBank blink = new SfxBank();

    [Header("The run")]
    public SfxBank bell = new SfxBank();
    public SfxBank victory = new SfxBank();
    public SfxBank defeat = new SfxBank();
    public SfxBank runEnd = new SfxBank();

    [Header("Spells")]
    [Tooltip("A sound per verb. Anything not listed falls back to the weapon sound for whoever " +
             "cast it, so an unlisted spell is quiet rather than silent.")]
    public List<SpellSound> spellSounds = new List<SpellSound>();

    /// <summary>The bank for this spell, or null to fall back to the caster's weapon.</summary>
    public SfxBank For(Spell spell)
    {
        if (spell == null || spellSounds == null) return null;
        for (int i = 0; i < spellSounds.Count; i++)
            if (spellSounds[i] != null && spellSounds[i].spell == spell) return spellSounds[i].bank;
        return null;
    }

    private static SfxLibrary _active;

    /// <summary>
    /// The global library. Falls back to an empty in-memory one if the asset is missing, so a
    /// project without it is silent rather than throwing on every swing.
    /// </summary>
    public static SfxLibrary Active
    {
        get
        {
            if (_active == null)
            {
                _active = Resources.Load<SfxLibrary>(ResourcePath);
                if (_active == null) _active = CreateInstance<SfxLibrary>();
            }
            return _active;
        }
    }
}
