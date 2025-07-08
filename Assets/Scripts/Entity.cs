using System.Collections;
using System.Collections.Generic;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using UnityEngine;

public class Entity : MonoBehaviour
{
    private Character character;

    public Appearance appearance;

    public EquipmentManagement equipmentManagement;

    [SerializeField] private bool isCharacter = true;

    public bool isTeam = true;

    [SerializeField] private float maxHealth = 100f;
    private float currentHealth;

    // Parameters
    [SerializeField] float attackRange = 1.0f;
    float moveSpeed = 3f;
    float separationDistance = 1.0f; // Reduced for less hesitation
    float separationStrength = 0.5f; // Reduced for smoother movement

    [SerializeField] private float minAttackCooldown = 0.7f;
    [SerializeField] private float maxAttackCooldown = 1.3f;

    private float attackCooldown;

    [SerializeField] private float attackDamage = 10f;
    private bool isAttacking = false;

    private Entity currentTarget;

    // Knockback 
    private Vector3 knockbackVelocity = Vector3.zero;
    private float knockbackDamping = 10f;
    private float knockbackStunTime = 0.05f;
    private float knockbackStunTimer = 0f;

    private float knockbackImmunityTime = 0.8f;
    private float knockbackImmunityTimer = 0f;

    [SerializeField] private float critChance = 0.2f; // 20% crit chance by default 
    [SerializeField] private float critKnockbackForce = 3.5f;
    [SerializeField] private float normalKnockbackForce = 0f; // No knockback on normal hits

    public void Awake()
    {
        character = GetComponent<Character>();
        appearance = GetComponent<Appearance>();
        equipmentManagement = GetComponent<EquipmentManagement>();

        currentHealth = maxHealth;
        attackCooldown = Random.Range(minAttackCooldown, maxAttackCooldown);
    }

    // Update is called once per frame
    void Update()
    {
        if (character.GetState().Equals(CharacterState.DeathB)) return;

        // Face the target if one exists
        if (currentTarget != null)
        {
            Vector3 toTarget = currentTarget.transform.position - transform.position;
            if (toTarget.x != 0)
            {
                Vector3 scale = transform.localScale;
                scale.x = Mathf.Abs(scale.x) * Mathf.Sign(toTarget.x);
                transform.localScale = scale;
            }
        }

        // Apply knockback velocity (ease-out, exponential decay)
        if (knockbackVelocity.magnitude > 0.01f)
        {
            transform.position += knockbackVelocity * Time.deltaTime;
            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, 1 - Mathf.Exp(-knockbackDamping * Time.deltaTime));
        }

        // Knockback stun timer
        if (knockbackStunTimer > 0f)
        {
            knockbackStunTimer -= Time.deltaTime;
        }

        // Knockback immunity timer
        if (knockbackImmunityTimer > 0f)
        {
            knockbackImmunityTimer -= Time.deltaTime;
        }

        Entity[] allEntities = FindObjectsOfType<Entity>();
        Entity closestEnemy = null;
        float closestDist = Mathf.Infinity;
        Vector3 separation = Vector3.zero;
        int neighborCount = 0;

        foreach (var other in allEntities)
        {
            if (other == this) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);

            // Separation logic (boids)
            if (dist < separationDistance)
            {
                separation += (transform.position - other.transform.position).normalized / dist;
                neighborCount++;
                // Debug.Log($"{name} applying separation from {other.name}, dist: {dist}");
            }

            // Find closest enemy (no detection range)
            if (other.isTeam != this.isTeam && dist < closestDist)
            {
                closestDist = dist;
                closestEnemy = other;
            }
        }

        // Apply separation
        if (neighborCount > 0)
        {
            separation /= neighborCount;
        }

        Vector3 move = Vector3.zero;

        // Move towards closest enemy and attack if in range
        if (closestEnemy != null)
        {
            currentTarget = closestEnemy;
            float distToTarget = Vector3.Distance(transform.position, currentTarget.transform.position);

            if (distToTarget > attackRange)
            {
                if (!isAttacking)
                {
                    Vector3 dir = (currentTarget.transform.position - transform.position).normalized;
                    // Lateral offset fades out as unit approaches attack range
                    float fade = Mathf.Clamp01((distToTarget - attackRange) / attackRange); // 1 when far, 0 when close
                    Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
                    float offsetAmount = Mathf.PerlinNoise(transform.position.x, transform.position.y) - 0.5f;
                    Vector3 lateralOffset = perp * offsetAmount * 0.8f * fade;
                    move = (dir + lateralOffset).normalized * moveSpeed;
                    character.SetState(CharacterState.Run);
                    // Debug.Log($"{name} moving toward {currentTarget.name}, dist: {distToTarget}, offset: {lateralOffset}");
                }
                else
                {
                    character.SetState(CharacterState.Idle);
                    // Debug.Log($"{name} is attacking and not moving");
                }
            }
            else
            {
                character.SetState(CharacterState.Idle);
                // Debug.Log($"{name} attacking {currentTarget.name}");
                Attack(currentTarget);
            }
        }
        else
        {
            currentTarget = null;
            character.SetState(CharacterState.Idle);
            // Debug.Log($"{name} has no target and is idling");
        }

        // Only move if not being knocked back
        if (knockbackVelocity.magnitude < 0.01f && knockbackStunTimer <= 0f)
        {
            Vector3 finalMove = (move + separation * separationStrength) * Time.deltaTime;
            transform.position += finalMove;
        }
    }

    public void EquipRandom()
    {
        equipmentManagement.EquipRandomArmor();
        equipmentManagement.EquipRandomHelmet();
        equipmentManagement.EquipRandomShield();
        equipmentManagement.EquipRandomWeapon();
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
        // if (isRanged)
        // {
        //     character.GetReady(); // Ensure Ready state for bow animation
        //     // yield return StartCoroutine(character.Shoot());
        //     float chargeTime = 0.5f; // Default charge time if no clip
        //     if (clipCharge != null) chargeTime = clipCharge.length;
        //     character.Animator.SetInteger("Charge", 1); // Start charging
        //     yield return new WaitForSeconds(chargeTime);
        //     character.Animator.SetInteger("Charge", 2); // Release
        //     if (createArrows && arrowPrefab != null && fireTransform != null)
        //     {
        //         CreateArrow();
        //     }
        //     yield return new WaitForSeconds(0.1f); // Small delay after shot
        // }
        // else
        // Melee logic
        if (Random.value < 0.5f)
        {
            character.Slash();
        }
        else
        {
            character.Jab();
        }
        yield return new WaitForSeconds(0.2f); // Optional: attack wind-up
        bool isCrit = Random.value < critChance;
        target.TakeDamage(attackDamage);
        // Apply knockback only on critical hits
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
        yield return new WaitForSeconds(attackCooldown);
        isAttacking = false;
    }

    public void ApplyKnockback(Vector3 direction, float force)
    {
        if (knockbackImmunityTimer > 0f) return; // Immune to knockback
        knockbackVelocity += direction.normalized * force;
        knockbackStunTimer = knockbackStunTime;
        knockbackImmunityTimer = knockbackImmunityTime;
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        if (character != null)
        {
            // character.Hit();
            character.HitAsScale();
            StartCoroutine(character.HitAsRed());
        }
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        // character.SetState(CharacterState.DeathB);
        Destroy(gameObject);
    }
}
