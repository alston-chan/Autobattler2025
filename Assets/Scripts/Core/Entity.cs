using System.Collections.Generic;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.FantasyMonsters.Common.Scripts;
using HeroEditor.Common.Enums;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Thin facade for a game entity. Holds references and identity,
/// delegates behaviour to Health, Knockback, and CombatAI components.
/// </summary>
public class Entity : MonoBehaviour
{
    #region References
    [Header("References")]
    public Character character;
    public Monster monster;
    public Appearance Appearance { get; private set; }
    public EquipmentManagement EquipmentManagement { get; private set; }
    public CharacterInventory characterInventory;

    // Components (assigned in Awake)
    public Health Health { get; private set; }
    public Knockback Knockback { get; private set; }
    public CombatAI CombatAI { get; private set; }
    public EntityStats Stats { get; private set; }
    public Hitstop Hitstop { get; private set; }
    public Mana Mana { get; private set; }
    public HitFeedback HitFeedback { get; private set; }
    public DeathFeedback DeathFeedback { get; private set; }
    public Resonance Resonance { get; private set; }
    #endregion

    #region Bow Aiming
    [Header("Bow Arm Aiming")]
    public Transform ArmL;
    public Transform ArmR;
    public float AngleToTarget;
    public float AngleToArm;
    public bool FixedArm;
    #endregion

    #region Team & Identity
    [Header("Team & Identity")]
    [SerializeField] public bool isCharacter = true;
    public bool isTeam = true;
    public bool isDead => Health != null && Health.IsDead;

    /// <summary>
    /// Whether this unit is in a fight, and so should move, aim and attack.
    ///
    /// Pushed in by whoever owns the game state on each transition, rather than read back out of a
    /// global every frame. A unit that must ask a manager for permission to act cannot be reasoned
    /// about — or exercised in a test — on its own; and the pull had a worse failure mode than the
    /// coupling it created. The manager is a singleton whose static reference is wiped while the
    /// editor reloads assemblies, so every entity silently stopped ticking with nothing logged to
    /// say why, which reads exactly like a combat bug.
    ///
    /// Deliberately NOT serialized. An assembly reload also resets the state machine to Setup, so a
    /// flag that survived one would leave units fighting a battle the rest of the game had already
    /// forgotten. A stopped fight is the honest outcome of reloading mid-combat.
    /// </summary>
    [System.NonSerialized] private bool _fighting;

    public bool IsFighting => _fighting;

    /// <summary>Told to us when a fight starts or ends. See <see cref="IsFighting"/>.</summary>
    public void SetFighting(bool fighting) => _fighting = fighting;

    [Header("Targeting")]
    [Tooltip("How this unit chooses whom to fight. Nearest is the ordinary front-line answer; " +
             "LowestHealth makes a finisher; Furthest reaches past the front rank.")]
    public TargetMode targetMode = TargetMode.Nearest;

    [Tooltip("How much better a rival target must be before this unit turns away from the one it " +
             "is already fighting, as a fraction: 0.25 means a quarter better. Zero makes a unit " +
             "flip between two equally close enemies every frame and close on neither.")]
    [Range(0f, 0.9f)] public float targetStickiness = 0.25f;

    /// <summary>
    /// The lane (row) and column this unit was deployed in, stamped at the bell by
    /// BoardSnapshot.Freeze and read by targeting for the rest of the fight. -1 off the board.
    /// Frozen on purpose: units scatter the instant combat starts, and a lane read live would hand
    /// the preference out and take it back as the AI shuffled people (Docs/PositionalKeywords.md).
    /// </summary>
    [System.NonSerialized] public int DeployedLane = -1;
    [System.NonSerialized] public int DeployedColumn = -1;

    /// <summary>
    /// True from the bell until this unit's first swing. The lane preference applies while it is
    /// set — units charge their lanes — and not afterwards: once the board has dissolved into a
    /// brawl, a preference for a row nobody stands in any more is noise. It has to last the whole
    /// charge, not just the first pick: a lane target a third further than the neighbour beats the
    /// stickiness margin, so a one-frame preference would be undone on the second frame.
    /// </summary>
    [System.NonSerialized] public bool OpeningPending;

    /// <summary>
    /// Until when this unit cannot be picked as a target.
    ///
    /// Not serialized and not a stat: it is a brief window, bought by doing something — vanishing
    /// behind the enemy line — and it buys time rather than immunity. If every enemy is hidden at
    /// once, <see cref="Targeting"/> ignores it and the fight continues, because a battle that
    /// politely stops is worse than a dive that goes unpunished.
    /// </summary>
    [System.NonSerialized] private float _hiddenUntil;

    public bool IsAggroDropped => Time.time < _hiddenUntil;

    /// <summary>Slip out of sight for a moment. Extends an existing window, never shortens it.</summary>
    public void DropAggro(float seconds)
    {
        if (seconds <= 0f) return;
        _hiddenUntil = Mathf.Max(_hiddenUntil, Time.time + seconds);
    }
    #endregion

    #region Data
    [Header("Unit Data (optional)")]
    [Tooltip("Assign a UnitData asset to drive stats from data. Leave null to use serialized fields below.")]
    public UnitData unitData;
    #endregion

    #region Fallback fields (used when unitData is null)
    [Header("Health")]
    public float maxHealth = 100f;
    public Vector3 healthBarOffset = new Vector3(0, 3.0f, 1);

    /// <summary>Convenience accessor. Always reads from Health component — no stale copies.</summary>
    public float currentHealth => Health != null ? Health.currentHealth : 0f;

    [Header("Attack")]
    [Tooltip("Attacks-per-second multiplier for weapon spells (melee/bow). 1 = normal speed.")]
    public float attackSpeed = 1f;

    [Header("Ranged/Bow")]
    [SerializeField] private bool isRanged = false;
    public bool IsRanged => unitData != null ? unitData.isRanged : isRanged;

    /// <summary>
    /// Choose melee or ranged before the entity wakes, for units built at runtime. It decides which
    /// weapon gets equipped and which basic attack fits, so it has to be settled first — a bow-armed
    /// unit holding a melee attack can't reach anything. Ignored once <see cref="unitData"/> is set,
    /// which owns the flag instead.
    /// </summary>
    public void SetRanged(bool ranged) => isRanged = ranged;

    /// <summary>
    /// Turn the unit to face left or right. Facing is encoded in <c>localScale.x</c>, and monster
    /// art is authored facing the opposite way to character art — so the sign is inverted for them.
    /// That inversion is easy to get wrong, which is why every caller goes through here rather than
    /// flipping the scale itself.
    /// </summary>
    public void SetFacing(bool faceRight)
    {
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (faceRight ? 1f : -1f) * (isCharacter ? 1f : -1f);
        transform.localScale = scale;
    }
    public Transform fireTransform;

    /// <summary>
    /// What KIND of weapon this unit is holding, for spells that ask for one.
    ///
    /// The rig's own WeaponType cannot answer this: a wand and a sword are both Melee1H to
    /// HeroEditor, since they are held and swung the same way. The distinction only exists in the
    /// item's ItemClass, so it is recorded here as gear changes — by the inventory for the company,
    /// and by the random loadout for enemies, which have no inventory to read.
    /// </summary>
    public Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass weaponClass =
        Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass.Unknown;

    public void SetWeaponClass(Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemClass value) =>
        weaponClass = value;

    /// <summary>
    /// Swap the weapon basic attack — spells[0], which is what the rest of the game treats as "how
    /// this unit hits things".
    ///
    /// Two things read that slot once and cache the answer, so both have to be told. EntityStats
    /// seeds Damage from the attack's BaseDamage, and CombatAI takes its attack range and cooldowns
    /// from it; a unit handed a new weapon otherwise keeps swinging at the old damage from the old
    /// distance. Only the base is rewritten, so modifiers from gear and engravings survive.
    /// </summary>
    public void SetBasicAttack(Spell attack)
    {
        if (attack == null) return;

        if (spells == null) spells = new List<Spell>();
        if (spells.Count == 0) spells.Add(attack);
        else if (spells[0] == attack) return;
        else spells[0] = attack;

        if (Stats != null && Stats.Damage != null) Stats.Damage.BaseValue = attack.BaseDamage;
        if (CombatAI != null) CombatAI.RefreshSpells();
    }

    [Header("Signature item")]
    [Tooltip("Item id equipped at the start of a run. This is where a hero's identity comes from: " +
             "wearing it grants its Engraving, and resonating it banks that Engraving permanently " +
             "(Docs/Resonance.md). Leave empty for no signature.")]
    [ValueDropdown("ItemIds")]
    public string signatureItemId;

    [Tooltip("Worn from the first fight of a run that starts heroes in a kit rather than a random " +
             "roll (RunData.startingGear). Kept small on purpose — a weapon if the signature isn't " +
             "one, and a piece of armour — because the run is where the rest is found. Empty means " +
             "the run's fallback kit.")]
    [ValueDropdown("ItemIds")]
    public List<string> startingItemIds = new List<string>();

    private static IEnumerable<ValueDropdownItem<string>> ItemIds() => Catalog.ItemIds();

    [Header("Innate spells")]
    [Tooltip("Always-available spells: the weapon basic attack and any always-on spells. NOT the " +
             "learnable ability loadout — those live in spellSlots.")]
    public List<Spell> spells;

    [Header("Spell slots (learnable, hero-bound)")]
    [Tooltip("Up to 3 spells this character has learned. Bound to this character — they never move " +
             "to another. Only the ACTIVE slot is cast in combat; the other two are reserves the " +
             "player swaps between fights.\n\nAssign Spell assets here in the editor to set a " +
             "character's STARTING loadout — at startup each is auto-equipped as its spellbook into " +
             "the spell row (needs a matching SpellbookDatabase entry). Put ults here, NOT in the " +
             "innate 'spells' list above.")]
    public List<Spell> spellSlots = new List<Spell>();
    [Tooltip("Which slot (0-based) is the one cast in combat.")]
    public int activeSpellSlot = 0;

    public const int MaxSpellSlots = 3;

    /// <summary>The single learnable spell cast in combat — the active slot's spell, or null.</summary>
    public Spell ActiveSpell =>
        spellSlots != null && activeSpellSlot >= 0 && activeSpellSlot < spellSlots.Count
            ? spellSlots[activeSpellSlot] : null;

    /// <summary>
    /// Everything this unit can cast: the innate set (weapon basic attack, always-on spells) plus
    /// the one learnable spell in the active slot.
    ///
    /// The two lists exist for different reasons — innate spells come from the weapon and from the
    /// spawn roll, slots come from spellbooks — but nothing downstream cares which is which, only
    /// what the unit will actually do. Keeping that answer in one place is what lets the inspector
    /// card promise the same thing CombatAI fights from; while the card read only the slot, enemies
    /// (whose ability is always innate) showed as having none.
    /// </summary>
    public List<Spell> CastableSpells()
    {
        var castable = new List<Spell>();
        if (spells != null) castable.AddRange(spells);
        if (ActiveSpell != null && !castable.Contains(ActiveSpell)) castable.Add(ActiveSpell);
        return castable;
    }
    #endregion

    // Convenience — kept so existing code (spells, projectiles) still compiles
    public ResourceBar healthBar
    {
        get => Health != null ? Health.healthBar : null;
        set { if (Health != null) Health.healthBar = value; }
    }

    private void Awake()
    {
        character = GetComponent<Character>();
        monster = GetComponent<Monster>();
        Appearance = GetComponent<Appearance>();
        EquipmentManagement = GetComponent<EquipmentManagement>();

        // Ensure components exist (add at runtime if not already on the prefab)
        Health = GetComponent<Health>();
        if (Health == null) Health = gameObject.AddComponent<Health>();

        Knockback = GetComponent<Knockback>();
        if (Knockback == null) Knockback = gameObject.AddComponent<Knockback>();

        CombatAI = GetComponent<CombatAI>();
        if (CombatAI == null) CombatAI = gameObject.AddComponent<CombatAI>();

        Stats = GetComponent<EntityStats>();
        if (Stats == null) Stats = gameObject.AddComponent<EntityStats>();

        Hitstop = GetComponent<Hitstop>();
        if (Hitstop == null) Hitstop = gameObject.AddComponent<Hitstop>();

        Mana = GetComponent<Mana>();
        if (Mana == null) Mana = gameObject.AddComponent<Mana>();

        HitFeedback = GetComponent<HitFeedback>();
        if (HitFeedback == null) HitFeedback = gameObject.AddComponent<HitFeedback>();

        DeathFeedback = GetComponent<DeathFeedback>();
        if (DeathFeedback == null) DeathFeedback = gameObject.AddComponent<DeathFeedback>();

        Resonance = GetComponent<Resonance>();
        if (Resonance == null) Resonance = gameObject.AddComponent<Resonance>();

        // Apply UnitData if assigned, otherwise use serialized fields
        if (unitData != null)
        {
            isCharacter = unitData.isCharacter;
            maxHealth = unitData.maxHealth;
            attackSpeed = unitData.attackSpeed;
            healthBarOffset = unitData.healthBarOffset;
            if (unitData.spells != null && unitData.spells.Count > 0)
                spells = new List<Spell>(unitData.spells);
        }

        if (spells == null) spells = new List<Spell>();

        // Initialize components
        Health.maxHealth = maxHealth;
        Health.healthBarOffset = healthBarOffset;
        Health.Initialize(this);

        CombatAI.Initialize(this);
        Stats.Initialize(this);

        // Health starts before Stats does, so it cannot read the equipped maximum at its own init.
        // Square it up now that Stats is live.
        Health.SyncMaxFromStats();
        Hitstop.Initialize(this);
        Mana.Initialize(this);
        HitFeedback.Initialize(this);
        DeathFeedback.Initialize(this);
        Resonance.Initialize(this);
    }

    private void OnEnable()
    {
        EntityRegistry.Register(this);

        // Paired with the unsubscribe in OnDisable, and here rather than in Awake because Awake runs
        // once while OnDisable runs every time a unit falls: the fallen are deactivated so they can
        // be revived for the next fight, and that deactivation took the subscription with it. A hero
        // who died once never announced a death again, so from the second fight onward the win/lose
        // check could not see them fall — and a battle whose last ally dies silently never ends.
        if (Health != null)
        {
            Health.OnDied -= HandleDeath;   // never subscribe twice
            Health.OnDied += HandleDeath;
        }
    }

    private void OnDisable()
    {
        EntityRegistry.Unregister(this);
        if (Health != null) Health.OnDied -= HandleDeath;
    }

    /// <summary>
    /// Fired when any entity dies. Announced rather than reported to a particular manager: a unit
    /// dying is a fact about the unit, and who cares about it — the win/lose check today, a kill
    /// feed or a bounty tomorrow — is not the dying unit's business to know.
    /// </summary>
    public static event System.Action<Entity> OnAnyDied;

    private void HandleDeath()
    {
        // Bars are owned by UnitBarsManager, which tears them down on EntityRegistry.OnUnregistered
        // (fired from OnDisable). Whoever creates a thing destroys it.

        OnAnyDied?.Invoke(this);
    }

    private void Update()
    {
        if (!_fighting || isDead) return;

        // Hitstop freezes the entity: skip movement/knockback/AI while active.
        // (Hitstop counts down in its own Update and freezes the animator itself.)
        if (Hitstop != null && Hitstop.IsActive) return;

        Knockback.Tick();
        CombatAI.Tick();

        // Keep the entity on-screen. Movement and knockback both write transform.position directly
        // (no Rigidbody), so nothing physics-based constrains them — clamp into the play area after
        // both have moved this frame. This is also what stops a knockback like Shockwave from
        // launching a character off the edge.
        transform.position = ArenaBounds.ClampToArena(transform.position);
    }

    private void LateUpdate()
    {
        // A corpse must not keep tracking with its weapon arm while the death animation plays.
        if (isDead) return;

        if (!IsRanged || CombatAI.CurrentTarget == null || character == null) return;
        if (!character.IsReady()) return;

        if (!TryGetAimingArm(out Transform arm, out Transform weapon)) return;

        RotateArm(arm, weapon,
                  FixedArm ? arm.position + 1000 * Vector3.right : CombatAI.CurrentTarget.transform.position,
                  -40, 40);
    }

    /// <summary>
    /// Which arm follows the target, and what on the end of it has to finish up pointing there.
    ///
    /// Both answers change with the weapon, which is why neither is a constant. A bow is drawn in the
    /// left hand and aimed by its riser; a gun is held in the right and aimed down its barrel.
    ///
    /// This used to assume the bow's answer to both, and decided a unit was holding one by asking
    /// whether the rig had bow renderers — which it still has while a gun is out, only disabled. So a
    /// gunner tracked the target with an empty left arm aiming an invisible bow, while the hand
    /// actually holding the revolver stayed wherever the animation had left it.
    ///
    /// Aiming the muzzle rather than the gun's body is deliberate: the shot is spawned at
    /// FireTransform and sent at the target, so pointing that same transform at the target is what
    /// makes the barrel agree with where the bullet actually goes.
    /// </summary>
    private bool TryGetAimingArm(out Transform arm, out Transform weapon)
    {
        arm = null;
        weapon = null;

        if (FirearmRig.IsHoldingFirearm(character))
        {
            arm = ArmR;
            weapon = character.Firearm != null ? character.Firearm.FireTransform : null;
            return arm != null && weapon != null;
        }

        if (character.WeaponType == WeaponType.Bow &&
            character.BowRenderers != null && character.BowRenderers.Count > 3 &&
            character.BowRenderers[3] != null)
        {
            arm = ArmL;
            weapon = character.BowRenderers[3].transform;
            return arm != null;
        }

        return false;
    }

    #region Public API — delegates to components

    /// <summary><paramref name="source"/> and <paramref name="isCrit"/> are optional — feedback only.</summary>
    public void TakeDamage(float amount, Entity source = null, bool isCrit = false)
    {
        Health.TakeDamage(amount, source, isCrit);
    }

    /// <summary>
    /// Flash the entity red then restore. Delegates to Character or Monster.
    /// </summary>
    public void HitAsRed(float waitTime)
    {
        if (character != null)
            StartCoroutine(character.HitAsRed(waitTime));
        else if (monster != null)
            StartCoroutine(monster.HitAsRed(waitTime));
    }

    /// <summary>
    /// Play hit scale animation. Delegates to Character or Monster.
    /// </summary>
    public void HitScale()
    {
        if (character != null)
            character.HitAsScale();
        else if (monster != null)
            monster.Spring();
    }

    /// <summary>
    /// Play a hit-reaction (flinch/stagger) animation. Characters have a dedicated Hit
    /// animation; monsters have no hurt state, so they rely on the squash from HitScale().
    /// </summary>
    public void HitReact()
    {
        if (character != null)
            character.Hit();
    }

    public void ApplyKnockback(Vector3 direction, float force)
    {
        if (!CombatFeelSettings.Active.enableKnockback) return;
        Knockback.Apply(direction, force);
    }

    /// <summary>Trigger a brief hitstop freeze-frame on this entity.</summary>
    public void ApplyHitstop(float duration)
    {
        if (Hitstop != null) Hitstop.Freeze(duration);
    }

    public void EquipRandom()
    {
        EquipmentManagement.EquipRandom(IsRanged);
    }

    #endregion

    #region Bow Aiming Helpers

    public void RotateArm(Transform arm, Transform weapon, Vector2 target, float angleMin, float angleMax)
    {
        target = arm.transform.InverseTransformPoint(target);
        var angleToTarget = Vector2.SignedAngle(Vector2.right, target);
        var angleToArm = Vector2.SignedAngle(weapon.right, arm.transform.right) * Mathf.Sign(weapon.lossyScale.x);
        var fix = weapon.InverseTransformPoint(arm.transform.position).y / target.magnitude;
        AngleToTarget = angleToTarget;
        AngleToArm = angleToArm;
        if (fix < -1) fix = -1;
        else if (fix > 1) fix = 1;
        var angleFix = Mathf.Asin(fix) * Mathf.Rad2Deg;
        var angle = angleToTarget + angleFix + arm.transform.localEulerAngles.z;
        angle = NormalizeAngle(angle);
        if (angle > angleMax) angle = angleMax;
        else if (angle < angleMin) angle = angleMin;
        if (float.IsNaN(angle)) Debug.LogWarning(angle);
        arm.transform.localEulerAngles = new Vector3(0, 0, angle + angleToArm);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    #endregion
}
