using System.Collections;
using System.Collections.Generic;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.FantasyMonsters.Common.Scripts;
using UnityEngine;

/// <summary>
/// AI logic: targeting, movement, attack decisions, spell casting.
/// Uses EntityRegistry instead of FindObjectsOfType.
/// </summary>
public class CombatAI : MonoBehaviour
{
    private float moveSpeed = 3f;
    private float separationDistance = 1.0f;
    private float separationStrength = 0.5f;

    private float _attackRange = 1.5f;
    private bool _isAttacking;
    private float[] _spellCooldowns;

    private Entity _entity;

    /// <summary>The current enemy target this entity is pursuing.</summary>
    public Entity CurrentTarget { get; private set; }

    public void Initialize(Entity entity)
    {
        _entity = entity;

        // Apply UnitData-driven movement stats, fall back to defaults
        if (_entity.unitData != null)
        {
            moveSpeed = _entity.unitData.moveSpeed;
            separationDistance = _entity.unitData.separationDistance;
            separationStrength = _entity.unitData.separationStrength;
        }

        // Set attack range from first spell if available
        if (_entity.spells != null && _entity.spells.Count > 0 && _entity.spells[0] != null)
            _attackRange = _entity.spells[0].range;

        _spellCooldowns = new float[_entity.spells.Count];
        for (int i = 0; i < _spellCooldowns.Length; i++)
            _spellCooldowns[i] = _entity.spells[i].cooldown;
    }

    public void Tick()
    {
        FaceTarget();
        UpdateSpellCooldowns();
        HandleAI();
        TryAlwaysOnSpells();
    }

    private void FaceTarget()
    {
        if (CurrentTarget == null) return;

        Vector3 toTarget = CurrentTarget.transform.position - transform.position;
        if (toTarget.x != 0)
        {
            Vector3 scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * Mathf.Sign(toTarget.x) * (_entity.isCharacter ? 1 : -1);
            transform.localScale = scale;
        }
    }

    private void HandleAI()
    {
        var allEntities = EntityRegistry.All;
        Entity closestEnemy = null;
        float closestDist = Mathf.Infinity;
        Vector3 separation = Vector3.zero;
        int neighborCount = 0;

        for (int idx = 0; idx < allEntities.Count; idx++)
        {
            var other = allEntities[idx];
            if (other == _entity) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);

            if (dist < separationDistance)
            {
                separation += (transform.position - other.transform.position).normalized / dist;
                neighborCount++;
            }

            if (other.isTeam != _entity.isTeam && dist < closestDist)
            {
                closestDist = dist;
                closestEnemy = other;
            }
        }

        if (neighborCount > 0) separation /= neighborCount;

        Vector3 move = Vector3.zero;

        if (closestEnemy != null)
        {
            CurrentTarget = closestEnemy;
            float distToTarget = Vector3.Distance(transform.position, CurrentTarget.transform.position);

            if (distToTarget > _attackRange)
            {
                if (!_isAttacking)
                {
                    Vector3 dir = (CurrentTarget.transform.position - transform.position).normalized;
                    float fade = Mathf.Clamp01((distToTarget - _attackRange) / _attackRange);
                    Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
                    float offsetAmount = Mathf.PerlinNoise(transform.position.x, transform.position.y) - 0.5f;
                    Vector3 lateralOffset = perp * offsetAmount * 0.8f * fade;
                    move = (dir + lateralOffset).normalized * moveSpeed;
                    SetAnimState(true);
                }
                else
                {
                    SetAnimState(false);
                }
            }
            else
            {
                SetAnimState(false);
                Attack(CurrentTarget);
            }
        }
        else
        {
            CurrentTarget = null;
            SetAnimState(false);
        }

        if (!_entity.Knockback.IsActive && !_entity.Knockback.IsStunned)
        {
            Vector3 finalMove = (move + separation * separationStrength) * Time.deltaTime;
            transform.position += finalMove;
        }
    }

    private void SetAnimState(bool running)
    {
        if (_entity.character != null)
            _entity.character.SetState(running ? CharacterState.Run : CharacterState.Idle);
        else if (_entity.monster != null)
            _entity.monster.SetState(running ? MonsterState.Run : MonsterState.Idle);
    }

    private void Attack(Entity target)
    {
        if (_isAttacking || target == null || _entity.spells == null || _entity.spells.Count == 0) return;

        for (int i = 0; i < _entity.spells.Count; i++)
        {
            if (_entity.spells[i] != null && !_entity.spells[i].alwaysOn &&
                _entity.spells[i].CanCast(_entity, target) && _spellCooldowns[i] <= 0)
            {
                StartCoroutine(CastSpellWithCooldown(i, target));
                break;
            }
        }
    }

    private void TryAlwaysOnSpells()
    {
        for (int i = 0; i < _entity.spells.Count; i++)
        {
            if (_entity.spells[i] != null && _entity.spells[i].alwaysOn &&
                _entity.spells[i].CanCast(_entity, null) && _spellCooldowns[i] <= 0)
            {
                StartCoroutine(CastSpellWithCooldown(i, null));
                break;
            }
        }
    }

    private IEnumerator CastSpellWithCooldown(int spellIndex, Entity target)
    {
        _isAttacking = true;
        _spellCooldowns[spellIndex] = _entity.spells[spellIndex].cooldown;
        yield return StartCoroutine(_entity.spells[spellIndex].Cast(_entity, target));
        _isAttacking = false;
    }

    private void UpdateSpellCooldowns()
    {
        if (_spellCooldowns == null) return;
        for (int i = 0; i < _spellCooldowns.Length; i++)
        {
            if (_spellCooldowns[i] > 0)
                _spellCooldowns[i] -= Time.deltaTime;
        }
    }
}
