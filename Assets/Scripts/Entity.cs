using System.Collections;
using System.Collections.Generic;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.FantasyMonsters.Common.Scripts;
using Assets.HeroEditor.Common.Scripts.ExampleScripts;
using UnityEngine;

public class Entity : MonoBehaviour
{
    #region References
    [Header("References")]
    public Character character;
    public Monster monster;
    public Appearance Appearance { get; private set; }
    public EquipmentManagement EquipmentManagement { get; private set; }
    public ResourceBar healthBar;
    #endregion

    // Bow aiming logic
    [Header("Bow Arm Aiming")]
    public Transform ArmL;
    public Transform ArmR;
    public float AngleToTarget;
    public float AngleToArm;
    public bool FixedArm;

    #region Team & Identity
    [Header("Team & Identity")]
    [SerializeField] public bool isCharacter = true;
    public bool isTeam = true;
    public bool isDead = false;
    #endregion

    #region Health
    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth;
    public Vector3 healthBarOffset = new Vector3(0, 3.0f, 1);
    #endregion

    #region Combat
    [Header("Combat")]
    private float attackRange = 1.0f;
    private bool isAttacking = false;
    private Entity currentTarget;
    // Bow/ranged logic
    [Header("Ranged/Bow")]
    [SerializeField] private bool isRanged = false;
    public Transform fireTransform;
    #endregion

    // Spell system
    [Header("Spells")]
    public List<Spell> spells;
    private float[] spellCooldowns;

    #region Movement
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float separationDistance = 1.0f;
    [SerializeField] private float separationStrength = 0.5f;
    #endregion

    #region Knockback
    private Vector3 knockbackVelocity = Vector3.zero;
    private float knockbackDamping = 10f;
    private float knockbackStunTime = 0.05f;
    private float knockbackStunTimer = 0f;
    private float knockbackImmunityTime = 0.8f;
    private float knockbackImmunityTimer = 0f;
    #endregion

    private void Awake()
    {
        character = GetComponent<Character>();
        monster = GetComponent<Monster>();
        Appearance = GetComponent<Appearance>();
        EquipmentManagement = GetComponent<EquipmentManagement>();

        // Set attack range based on spell data if available, otherwise fallback to default
        if (spells != null && spells.Count > 0 && spells[0] != null)
        {
            attackRange = spells[0].range;
        }
        else
        {
            attackRange = 1.5f;
        }

        currentHealth = maxHealth;

        if (spells == null) spells = new List<Spell>();
        spellCooldowns = new float[spells.Count];
        for (int i = 0; i < spellCooldowns.Length; i++)
        {
            spellCooldowns[i] = spells[i].cooldown;
        }
    }

    private void Update()
    {
        if (isDead) return;

        FaceTarget();
        HandleKnockback();
        HandleTimers();
        UpdateSpellCooldowns();
        HandleAI();

        for (int i = 0; i < spells.Count; i++)
        {
            if (spells[i] != null && spells[i].alwaysOn && spells[i].CanCast(this, null) && spellCooldowns[i] <= 0)
            {
                StartCoroutine(CastSpellWithCooldown(i, null));
                break; // Only cast one spell per attack
            }
        }
    }

    private void LateUpdate()
    {
        // Bow aiming logic (for ranged characters)
        if (isRanged && currentTarget != null && character != null && ArmL != null)
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
                RotateArm(arm, weapon, FixedArm ? arm.position + 1000 * Vector3.right : currentTarget.transform.position, -40, 40);
            }
        }
    }

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

    private void FaceTarget()
    {
        if (currentTarget != null)
        {
            Vector3 toTarget = currentTarget.transform.position - transform.position;
            if (toTarget.x != 0)
            {
                Vector3 scale = transform.localScale;
                scale.x = Mathf.Abs(scale.x) * Mathf.Sign(toTarget.x) * (isCharacter ? 1 : -1);
                transform.localScale = scale;
            }
        }
    }

    private void HandleKnockback()
    {
        if (knockbackVelocity.magnitude > 0.01f)
        {
            transform.position += knockbackVelocity * Time.deltaTime;
            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, 1 - Mathf.Exp(-knockbackDamping * Time.deltaTime));
        }
    }

    private void HandleTimers()
    {
        if (knockbackStunTimer > 0f) knockbackStunTimer -= Time.deltaTime;
        if (knockbackImmunityTimer > 0f) knockbackImmunityTimer -= Time.deltaTime;
    }

    private void HandleAI()
    {
        Entity[] allEntities = FindObjectsOfType<Entity>();
        Entity closestEnemy = null;
        float closestDist = Mathf.Infinity;
        Vector3 separation = Vector3.zero;
        int neighborCount = 0;

        foreach (var other in allEntities)
        {
            if (other == this) continue;
            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist < separationDistance)
            {
                separation += (transform.position - other.transform.position).normalized / dist;
                neighborCount++;
            }
            if (other.isTeam != this.isTeam && dist < closestDist)
            {
                closestDist = dist;
                closestEnemy = other;
            }
        }
        if (neighborCount > 0) separation /= neighborCount;

        Vector3 move = Vector3.zero;
        if (closestEnemy != null)
        {
            currentTarget = closestEnemy;
            float distToTarget = Vector3.Distance(transform.position, currentTarget.transform.position);
            if (distToTarget > attackRange)
            {
                if (!isAttacking)
                {
                    Vector3 dir = (currentTarget.transform.position - transform.position).normalized;
                    float fade = Mathf.Clamp01((distToTarget - attackRange) / attackRange);
                    Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
                    float offsetAmount = Mathf.PerlinNoise(transform.position.x, transform.position.y) - 0.5f;
                    Vector3 lateralOffset = perp * offsetAmount * 0.8f * fade;
                    move = (dir + lateralOffset).normalized * moveSpeed;
                    if (character != null)
                        character.SetState(CharacterState.Run);
                    else if (monster != null)
                        monster.SetState(MonsterState.Run);
                }
                else
                {
                    if (character != null)
                        character.SetState(CharacterState.Idle);
                    else if (monster != null)
                        monster.SetState(MonsterState.Idle);
                }
            }
            else
            {
                if (character != null)
                    character.SetState(CharacterState.Idle);
                else if (monster != null)
                    monster.SetState(MonsterState.Idle);
                Attack(currentTarget);
            }
        }
        else
        {
            currentTarget = null;
            if (character != null)
                character.SetState(CharacterState.Idle);
            else if (monster != null)
                monster.SetState(MonsterState.Idle);
        }
        if (knockbackVelocity.magnitude < 0.01f && knockbackStunTimer <= 0f)
        {
            Vector3 finalMove = (move + separation * separationStrength) * Time.deltaTime;
            transform.position += finalMove;
        }
    }

    public void EquipRandom()
    {
        EquipmentManagement.EquipRandom(isRanged);
    }


    public void Attack(Entity target)
    {
        if (!isAttacking && target != null && spells != null && spells.Count > 0)
        {
            for (int i = 0; i < spells.Count; i++)
            {
                if (spells[i] != null && !spells[i].alwaysOn && spells[i].CanCast(this, target) && spellCooldowns[i] <= 0)
                {
                    StartCoroutine(CastSpellWithCooldown(i, target));
                    break; // Only cast one spell per attack
                }
            }
        }
    }

    private IEnumerator CastSpellWithCooldown(int spellIndex, Entity target)
    {
        isAttacking = true;
        spellCooldowns[spellIndex] = spells[spellIndex].cooldown; // Set cooldown immediately to prevent double-casting
        yield return StartCoroutine(spells[spellIndex].Cast(this, target));
        isAttacking = false;
    }

    // Decrement spell cooldowns every frame
    private void UpdateSpellCooldowns()
    {
        if (spellCooldowns == null) return;
        for (int i = 0; i < spellCooldowns.Length; i++)
        {
            if (spellCooldowns[i] > 0)
                spellCooldowns[i] -= Time.deltaTime;
        }
    }


    public void ApplyKnockback(Vector3 direction, float force)
    {
        if (knockbackImmunityTimer > 0f) return;
        knockbackVelocity += direction.normalized * force;
        knockbackStunTimer = knockbackStunTime;
        // knockbackImmunityTimer = knockbackImmunityTime;
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        if (healthBar != null) healthBar.SetSize(currentHealth / maxHealth);

        if (character != null)
        {
            character.HitAsScale();
            StartCoroutine(character.HitAsRed(0.1f));
        }
        else if (monster != null)
        {
            monster.Spring();
            StartCoroutine(monster.HitAsRed(0.1f));
        }
        if (!isDead && currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;

        if (character != null)
        {
            character.SetState(CharacterState.DeathB);
        }
        else if (monster != null)
        {
            monster.Die();
        }
        Destroy(healthBar.gameObject);
        Destroy(gameObject);
    }
}
