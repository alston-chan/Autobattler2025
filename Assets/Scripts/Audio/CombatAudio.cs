using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's ears. Subscribes to <see cref="CombatEvents"/> and plays a clip from
/// <see cref="SfxLibrary"/>; nothing in combat knows it exists.
///
/// That is the whole design, and it is only possible because the bus already announces each moment
/// once, from the one place it happens — <c>Hit</c>, <c>Kill</c>, <c>Cast</c>, <c>Impact</c>,
/// <c>ShieldEnded</c>, <c>Blink</c>. Audio that is called from inside combat code drifts: a second
/// damage path appears, and it is silent, and nobody notices for a month. Here a new way to die
/// still makes a death sound, because the death sound is attached to dying rather than to any of
/// the routes there.
///
/// Added by <see cref="GameManager"/> at startup (GameManager.Presentation), like
/// <see cref="DamageNumbersManager"/>. There is nothing to place in the scene and nothing to wire.
/// </summary>
public class CombatAudio : MonoBehaviour
{
    /// <summary>What a blow sounds like, decided by what the attacker is holding.</summary>
    public enum HitFlavour { Light, Heavy, Pierce, Magic, Gun }

    [Tooltip("Concurrent sounds. Past this the oldest voice is taken — which is right: in a fight " +
             "loud enough to need twenty voices, nobody is listening to the first one.")]
    public int voices = 24;

    [Tooltip("How far a sound is pushed left or right by where it happened. 0 is dead centre; " +
             "1 pans a unit at the edge of the screen fully to that side, which is too much. " +
             "A little width makes ten simultaneous fighters legible.")]
    [Range(0f, 1f)] public float panWidth = 0.35f;

    [Tooltip("Closing speed, in units/sec, at which a body impact is played at full volume. " +
             "Slower impacts scale down; nothing is ever silent.")]
    public float loudImpactSpeed = 9f;

    private AudioSource[] _pool;
    private int _nextVoice;
    private GameStateMachine _states;
    private Camera _camera;

    private static SfxLibrary Lib => SfxLibrary.Active;

    // ---------------------------------------------------------------- lifecycle

    private void Awake()
    {
        _camera = Camera.main;

        var host = new GameObject("Sfx Voices");
        host.transform.SetParent(transform, false);

        _pool = new AudioSource[Mathf.Max(1, voices)];
        for (int i = 0; i < _pool.Length; i++)
        {
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;   // a 2D side view: pan by hand, never attenuate by distance
            _pool[i] = source;
        }
    }

    private void OnEnable()
    {
        CombatEvents.Hit += OnHit;
        CombatEvents.Kill += OnKill;
        CombatEvents.Cast += OnCast;
        CombatEvents.Impact += OnImpact;
        CombatEvents.ShieldEnded += OnShieldEnded;
        CombatEvents.Blink += OnBlink;
    }

    private void OnDisable()
    {
        // CombatEvents is static: an un-cleared subscription outlives the scene and then plays
        // sounds for a fight that no longer exists.
        CombatEvents.Hit -= OnHit;
        CombatEvents.Kill -= OnKill;
        CombatEvents.Cast -= OnCast;
        CombatEvents.Impact -= OnImpact;
        CombatEvents.ShieldEnded -= OnShieldEnded;
        CombatEvents.Blink -= OnBlink;
        Listen(null);
    }

    /// <summary>The bell, and how a fight ended. Handed in by GameManager, which owns the machine.</summary>
    public void Listen(GameStateMachine states)
    {
        if (_states != null) _states.OnStateChanged -= OnStateChanged;
        _states = states;
        if (_states != null) _states.OnStateChanged += OnStateChanged;
    }

    // ---------------------------------------------------------------- the events

    private void OnHit(HitInfo hit)
    {
        Vector3 where = hit.target != null ? hit.target.transform.position : Vector3.zero;
        Play(BankFor(Flavour(hit.source)), where, WeaponLevel(Lib, UnderAnAbility));

        // Layered, not substituted — a crit is the same blow landing harder. Not part of the bed:
        // a crit is one of the moments, so it is neither held down nor ducked.
        if (hit.isCrit) Play(Lib.crit, where);
    }

    // When the last ability started being heard. Weapon sounds inside this window are ducked.
    private float _abilityHeardUntil = float.NegativeInfinity;
    private bool UnderAnAbility => Time.unscaledTime < _abilityHeardUntil;

    /// <summary>
    /// How loud the weapon bed — swings, and the hits they land — plays right now. Under the
    /// abilities always, and further under them while one is being heard. Public and pure so the
    /// rule can be tested as a rule; the play check then confirms it reaches an AudioSource.
    /// </summary>
    public static float WeaponLevel(SfxLibrary lib, bool underAnAbility)
    {
        float level = lib.weaponAttackLevel;
        if (underAnAbility) level *= 1f - Mathf.Clamp01(lib.duckWeaponsUnderAbilities);
        return level;
    }

    private void OnKill(Entity killer, Entity victim)
    {
        if (victim == null) return;
        Play(DeathBank(victim), victim.transform.position);
    }

    private void OnCast(Entity caster, Spell spell)
    {
        var lib = Lib;
        var bank = lib.For(spell);
        bool authored = bank != null && bank.HasClips;

        // An unlisted spell still makes the noise its weapon makes, so a new verb is quiet rather
        // than silent — and so the swing of a basic attack is covered by the same path.
        if (!authored) bank = SwingBank(Flavour(caster));

        // An ability is the loud layer and opens the duck; everything else is the bed. A borrowed
        // swing plays at bed level even for an ability, because it IS a swing sound — loud, it
        // would announce a verb with a sword noise.
        float level;
        if (spell != null && spell.IsAbility && authored)
        {
            _abilityHeardUntil = Time.unscaledTime + Mathf.Max(0f, lib.duckSeconds);
            level = lib.abilityLevel;
        }
        else level = WeaponLevel(lib, UnderAnAbility);

        Play(bank, caster != null ? caster.transform.position : Vector3.zero, level);
    }

    private void OnImpact(ImpactInfo impact)
    {
        Vector3 where = impact.mover != null ? impact.mover.transform.position : Vector3.zero;
        float loudness = Loudness(impact.speed, loudImpactSpeed);

        // struck == null is the wall (CombatEvents).
        Play(impact.struck == null ? Lib.wallSlam : BodyBank(impact.mover), where, loudness);
    }

    private void OnShieldEnded(Entity entity, bool broken)
    {
        // A shield that merely lapsed says nothing: silence is how the player tells them apart.
        if (broken && entity != null) Play(Lib.shieldBreak, entity.transform.position);
    }

    private void OnBlink(Entity entity, Vector3 from, Vector3 to) => Play(Lib.blink, to);

    private void OnStateChanged(GameState from, GameState to)
    {
        if (to == GameState.Combat) Play(Lib.bell, Centre);
        else if (to == GameState.RoundEnd) Play(AnyoneLeftStanding() ? Lib.victory : Lib.defeat, Centre);
        else if (to == GameState.RunEnd) Play(Lib.runEnd, Centre);
    }

    // ---------------------------------------------------------------- choosing

    /// <summary>
    /// What this unit's blows sound like. Read off the type of its weapon attack — spells[0], which
    /// is what the rest of the game already treats as "how this unit hits" — rather than off the
    /// item, so a unit that has had its attack swapped sounds like the attack it actually makes.
    /// </summary>
    public static HitFlavour Flavour(Entity attacker)
    {
        return Flavour(attacker != null && attacker.spells != null && attacker.spells.Count > 0
            ? attacker.spells[0] : null);
    }

    /// <summary>The same decision, from the weapon attack alone — which is all it ever depended on.</summary>
    public static HitFlavour Flavour(Spell basic)
    {
        switch (basic)
        {
            case BowAttackSpell _: return HitFlavour.Pierce;
            case WandAttackSpell _: return HitFlavour.Magic;
            case FirearmAttackSpell _: return HitFlavour.Gun;
            case HeavyAttackSpell _: return HitFlavour.Heavy;
            default: return HitFlavour.Light;   // melee, paired daggers, and anything unarmed
        }
    }

    /// <summary>
    /// How loud an impact at this speed is. Never zero: a body that visibly moved another body and
    /// made no sound reads as a missing feature, not as a gentle nudge.
    /// </summary>
    public static float Loudness(float speed, float reference)
    {
        if (reference <= 0f) return 1f;
        return Mathf.Clamp(speed / reference, 0.3f, 1f);
    }

    /// <summary>The bank a unit of this flavour is heard through. Public so a check can ask.</summary>
    public SfxBank BankFor(HitFlavour flavour)
    {
        switch (flavour)
        {
            case HitFlavour.Heavy: return Lib.hitHeavy;
            case HitFlavour.Pierce: return Lib.hitPierce;
            case HitFlavour.Magic: return Lib.hitMagic;
            case HitFlavour.Gun: return Lib.hitGun;
            default: return Lib.hitLight;
        }
    }

    // The wind-up has no bank of its own yet: a swing that is not separately authored borrows the
    // hit, quietly, which is better than nothing and obviously temporary once clips exist.
    private SfxBank SwingBank(HitFlavour flavour) => BankFor(flavour);

    /// <summary>
    /// The three mass words, as three sounds. This is the payoff for mass being authored at all —
    /// a Heavy unit landing has to sound like more than a Light one, or the stat is only a number
    /// on a card.
    /// </summary>
    private SfxBank BodyBank(Entity mover)
    {
        if (mover == null) return Lib.bodyMedium;
        switch (BodyMass.Word(mover.Mass))
        {
            case "Light": return Lib.bodyLight;
            case "Heavy": return Lib.bodyHeavy;
            default: return Lib.bodyMedium;
        }
    }

    private SfxBank DeathBank(Entity victim)
    {
        if (victim.monster != null) return Lib.deathBeast;
        return victim.isCharacter ? Lib.deathHuman : Lib.deathProp;
    }

    private bool AnyoneLeftStanding()
    {
        foreach (var e in EntityRegistry.All)
            if (e != null && e.isTeam && !e.isDead) return true;
        return false;
    }

    private Vector3 Centre => _camera != null ? _camera.transform.position : Vector3.zero;

    // ---------------------------------------------------------------- playing

    private void Play(SfxBank bank, Vector3 where, float volumeScale = 1f)
    {
        var lib = Lib;
        if (bank == null || !lib.enabled || lib.masterVolume <= 0f) return;

        var clip = bank.Take(Time.unscaledTime);   // unscaled: the fight clock speeds time up
        if (clip == null) return;

        var voice = FreeVoice();
        voice.clip = clip;
        voice.volume = Mathf.Clamp01(bank.volume * volumeScale * lib.masterVolume);
        voice.pitch = bank.Pitch();
        voice.panStereo = Pan(where);
        voice.Play();
    }

    private AudioSource FreeVoice()
    {
        for (int i = 0; i < _pool.Length; i++)
        {
            var candidate = _pool[(_nextVoice + i) % _pool.Length];
            if (!candidate.isPlaying)
            {
                _nextVoice = (_nextVoice + i + 1) % _pool.Length;
                return candidate;
            }
        }

        // Every voice busy: take the next one in rotation, which is the oldest still sounding.
        var stolen = _pool[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _pool.Length;
        return stolen;
    }

    private float Pan(Vector3 where)
    {
        if (panWidth <= 0f) return 0f;
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return 0f;

        float x = _camera.WorldToViewportPoint(where).x;   // 0 at the left edge, 1 at the right
        return Mathf.Clamp(x * 2f - 1f, -1f, 1f) * panWidth;
    }
}
