using System.Collections.Generic;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.FantasyMonsters.Common.Scripts;
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

    [Header("Ranged/Bow")]
    [SerializeField] private bool isRanged = false;
    public bool IsRanged => unitData != null ? unitData.isRanged : isRanged;
    public Transform fireTransform;

    [Header("Spells")]
    public List<Spell> spells;
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

        // Apply UnitData if assigned, otherwise use serialized fields
        if (unitData != null)
        {
            isCharacter = unitData.isCharacter;
            maxHealth = unitData.maxHealth;
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

        // Subscribe to death event for cleanup and round-end checks
        Health.OnDied += HandleDeath;
    }

    private void OnEnable()
    {
        EntityRegistry.Register(this);
    }

    private void OnDisable()
    {
        EntityRegistry.Unregister(this);
        if (Health != null) Health.OnDied -= HandleDeath;
    }

    private void HandleDeath()
    {
        // Clean up health bar (ownership is here, not in Health)
        if (Health.healthBar != null)
            Destroy(Health.healthBar.gameObject);

        // Notify GameManager for win/lose evaluation
        if (GameManager.Instance != null)
            GameManager.Instance.OnEntityDied(this);
    }

    private void Update()
    {
        if (!GameManager.Instance.isGameStarted) return;
        if (isDead) return;

        Knockback.Tick();
        CombatAI.Tick();
    }

    private void LateUpdate()
    {
        // Bow aiming logic (for ranged characters)
        if (IsRanged && CombatAI.CurrentTarget != null && character != null && ArmL != null)
        {
            Transform arm = ArmL;
            Transform weapon = null;
            if (character.BowRenderers != null && character.BowRenderers.Count > 3)
            {
                weapon = character.BowRenderers[3].transform;
            }
            else
            {
                return;
            }
            if (character.IsReady())
            {
                RotateArm(arm, weapon, FixedArm ? arm.position + 1000 * Vector3.right : CombatAI.CurrentTarget.transform.position, -40, 40);
            }
        }
    }

    #region Public API — delegates to components

    public void TakeDamage(float amount)
    {
        Health.TakeDamage(amount);
    }

    public void ApplyKnockback(Vector3 direction, float force)
    {
        Knockback.Apply(direction, force);
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
