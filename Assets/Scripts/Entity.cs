using System.Collections;
using System.Collections.Generic;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.FantasyMonsters.Common.Scripts;
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
    [SerializeField] private float attackRange = 1.0f;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float minAttackCooldown = 0.7f;
    [SerializeField] private float maxAttackCooldown = 1.3f;
    [SerializeField] private float critChance = 0.2f;
    [SerializeField] private float critKnockbackForce = 3.5f;
    [SerializeField] private float normalKnockbackForce = 0f;
    private float attackCooldown;
    private bool isAttacking = false;
    private Entity currentTarget;
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
        EquipmentManagement.EquipRandomArmor();
        EquipmentManagement.EquipRandomHelmet();
        EquipmentManagement.EquipRandomShield();
        EquipmentManagement.EquipRandomWeapon();
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
        // Play attack animation depending on type
        if (character != null)
        {
            if (Random.value < 0.5f)
                character.Slash();
            else
                character.Jab();
        }
        else if (monster != null)
        {
            if (Random.value < 0.5f)
                monster.Attack();
            else
                monster.AttackAlt();
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
        yield return new WaitForSeconds(attackCooldown);
        isAttacking = false;
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
