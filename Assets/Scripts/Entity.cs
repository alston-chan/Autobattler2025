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
    private Character character;
    private Monster monster;
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
    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;
    #endregion

    #region Combat
    [Header("Combat")]
    [SerializeField] private float meleeRange = 1.5f;
    [SerializeField] private float rangedRange = 15f;
    [SerializeField, HideInInspector] private float attackRange = 1.0f;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float minAttackCooldown = 0.7f;
    [SerializeField] private float maxAttackCooldown = 1.3f;
    [SerializeField] private float critChance = 0.2f;
    [SerializeField] private float critKnockbackForce = 3.5f;
    [SerializeField] private float normalKnockbackForce = 0f;
    private float attackCooldown;
    private bool isAttacking = false;
    private Entity currentTarget;
    // Bow/ranged logic
    [Header("Ranged/Bow")]
    [SerializeField] private bool isRanged = false;
    [SerializeField] private GameObject arrowPrefab;
    [SerializeField] private Transform fireTransform;
    [SerializeField] private AnimationClip clipCharge;
    [SerializeField] private bool createArrows = true;
    #endregion

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

        // Set attack range based on entity type
        attackRange = isRanged ? rangedRange : meleeRange;

        attackCooldown = Random.Range(minAttackCooldown, maxAttackCooldown);

        currentHealth = maxHealth;
        healthBar.SetSize(currentHealth / maxHealth);
        if (!isTeam)
        {
            healthBar.SetColor(Color.red);
        }
    }

    private void Update()
    {
        if (isDead) return;

        FaceTarget();
        HandleKnockback();
        HandleTimers();
        HandleAI();
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
        if (!isAttacking && target != null)
        {
            StartCoroutine(AttackCoroutine(target));
        }
    }

    private IEnumerator AttackCoroutine(Entity target)
    {
        isAttacking = true;
        if (isRanged && character != null)
        {
            character.GetReady();
            float chargeTime = 0.5f;
            if (clipCharge != null) chargeTime = clipCharge.length;
            character.Animator.SetInteger("Charge", 1);
            yield return new WaitForSeconds(chargeTime);
            character.Animator.SetInteger("Charge", 2);
            if (createArrows && arrowPrefab != null && fireTransform != null)
            {
                CreateArrow();
            }
            yield return new WaitForSeconds(0.1f);
        }
        else
        {
            // Melee or monster logic
            if (character != null)
            {
                if (Random.value < 0.5f)
                    character.Slash();
                else
                    character.Jab();
            }
            else if (monster != null)
            {
                monster.Attack();
            }
            yield return new WaitForSeconds(0.2f);
            bool isCrit = Random.value < critChance;
            target.TakeDamage(attackDamage);
            if (isCrit)
            {
                Vector3 knockbackDir = (target.transform.position - transform.position).normalized;
                target.ApplyKnockback(knockbackDir, critKnockbackForce);
            }
            else if (normalKnockbackForce > 0f)
            {
                Vector3 knockbackDir = (target.transform.position - transform.position).normalized;
                target.ApplyKnockback(knockbackDir, normalKnockbackForce);
            }
        }
        yield return new WaitForSeconds(attackCooldown);
        isAttacking = false;
    }

    private void CreateArrow()
    {
        var arrow = Instantiate(arrowPrefab, fireTransform);
        var sr = arrow.GetComponent<SpriteRenderer>();
        var rb = arrow.GetComponent<Rigidbody2D>();
        const float speed = 18.75f;
        arrow.transform.localPosition = Vector3.zero;
        arrow.transform.localRotation = Quaternion.identity;
        arrow.transform.SetParent(null);
        rb.velocity = speed * fireTransform.right * Mathf.Sign(character.transform.lossyScale.x) * Random.Range(0.85f, 1.15f);
        // Set shooter and target reference
        var projectile = arrow.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.damage = attackDamage;
            projectile.knockbackForce = critKnockbackForce;
            projectile.shooter = this;
            projectile.target = currentTarget;
        }
    }

    public void ApplyKnockback(Vector3 direction, float force)
    {
        if (knockbackImmunityTimer > 0f) return;
        knockbackVelocity += direction.normalized * force;
        knockbackStunTimer = knockbackStunTime;
        knockbackImmunityTimer = knockbackImmunityTime;
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        if (healthBar != null) healthBar.SetSize(currentHealth / maxHealth);

        if (character != null)
        {
            character.HitAsScale();
            StartCoroutine(character.HitAsRed());
        }
        else if (monster != null)
        {
            monster.Spring();
            StartCoroutine(monster.HitAsRed());
        }
        if (currentHealth <= 0)
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
        Destroy(gameObject);
    }
}
