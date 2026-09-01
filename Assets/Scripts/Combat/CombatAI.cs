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

    // The spells this unit actually casts this combat: its innate spells (weapon basic + always-on)
    // plus the ONE active learnable spell from its slots. Built at Initialize — the active slot is a
    // pre-combat choice, fixed for the fight, so no mid-combat resizing is needed.
    private List<Spell> _spells;

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

        RefreshSpells();
    }

    /// <summary>
    /// (Re)build the combat spell set = innate spells (weapon basic + always-on) + the single active
    /// learnable spell. Call after the character's spell slots change (equipping/unequipping a
    /// spellbook, or swapping the active slot). Equipment is set up after Awake, so the initial
    /// Initialize alone would miss slotted spells — this is what picks them up.
    /// </summary>
    public void RefreshSpells()
    {
        _spells = new List<Spell>();
        if (_entity.spells != null) _spells.AddRange(_entity.spells);
        if (_entity.ActiveSpell != null) _spells.Add(_entity.ActiveSpell);

        // Attack range comes from the first innate spell (the weapon basic attack).
        if (_spells.Count > 0 && _spells[0] != null)
            _attackRange = _spells[0].range;

        _spellCooldowns = new float[_spells.Count];
        for (int i = 0; i < _spellCooldowns.Length; i++)
            _spellCooldowns[i] = EffectiveCooldown(i);
    }

    /// <summary>
    /// Cooldown for a spell, shortened by the entity's attack speed for weapon attacks
    /// (melee/bow). Non-weapon spells use their raw cooldown.
    /// </summary>
    private float EffectiveCooldown(int spellIndex)
    {
        var spell = _spells[spellIndex];
        if (spell == null) return 0f;

        float cd = spell.cooldown;
        if (spell.ScalesWithAttackSpeed && _entity.Stats != null && _entity.Stats.AttackSpeed != null)
        {
            float atk = _entity.Stats.AttackSpeed.Value;
            if (atk > 0.01f) cd /= atk;
        }
        return cd;
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
        if (CurrentTarget == null || CurrentTarget.isDead) return;

        float toTargetX = CurrentTarget.transform.position.x - transform.position.x;
        if (toTargetX != 0f) _entity.SetFacing(toTargetX > 0f);
    }

    private void HandleAI()
    {
        // Whom to fight is its own question now, asked of Targeting, which knows about modes and
        // about not flip-flopping between two enemies a hair apart.
        Entity closestEnemy = Targeting.Choose(_entity, _entity.targetMode, CurrentTarget,
                                               _entity.targetStickiness);

        // Keeping clear of the neighbours is a separate concern and stays here.
        var allEntities = EntityRegistry.All;
        Vector3 separation = Vector3.zero;
        int neighborCount = 0;

        for (int idx = 0; idx < allEntities.Count; idx++)
        {
            var other = allEntities[idx];
            if (other == _entity) continue;

            // Corpses stay registered while their death sequence plays, and must not push living
            // units around — a body should be walked over, not swerved around.
            if (other.isDead) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist < separationDistance)
            {
                separation += (transform.position - other.transform.position).normalized / dist;
                neighborCount++;
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

    /// <summary>
    /// Abandon an attack that is part-way through, without leaving combat.
    ///
    /// Changing weapons mid-swing used to leave the old spell running: the archer's draw coroutine
    /// carried on and released an arrow after the bow had already been replaced by a sword. Worse,
    /// HeroEditor resets the upper body to idle whenever the weapon TYPE changes, so the draw was
    /// wiped from the screen while the shot still happened — an attack with no animation behind it.
    ///
    /// Swapping between two weapons of the same type never had this problem, because that reset is
    /// skipped when the type is unchanged; it is only the bow-to-melee kind of change that bites.
    /// </summary>
    public void InterruptCast()
    {
        if (!_isAttacking) return;

        StopAllCoroutines();
        _isAttacking = false;

        var animator = _entity.character != null ? _entity.character.Animator
                     : _entity.monster != null ? _entity.monster.Animator
                     : null;
        if (animator == null) return;

        animator.speed = 1f;

        // Same reason as StopCombat: a cancelled draw never runs its own cleanup, and a Charge left
        // at 1 makes the next shot's SetInteger a no-op, so the bow silently stops animating.
        if (_entity.character != null) animator.SetInteger("Charge", 0);
    }

    /// <summary>
    /// Stand down when the fight ends. Nothing ticks the AI once combat is over, so a unit caught
    /// mid-chase would keep running on the spot forever — its animation state is only ever changed
    /// from <see cref="Tick"/>.
    ///
    /// Any in-flight cast is cancelled too, since a spell finishing during the setup phase would
    /// loose arrows at a fight that has already been decided. Those coroutines adjust
    /// <c>animator.speed</c> for the duration of a swing and restore it at the end, so cancelling
    /// mid-swing means restoring it here instead — otherwise the unit idles at double speed.
    /// </summary>
    public void StopCombat()
    {
        StopAllCoroutines();
        _isAttacking = false;
        CurrentTarget = null;

        var animator = _entity.character != null ? _entity.character.Animator
                     : _entity.monster != null ? _entity.monster.Animator
                     : null;
        if (animator != null)
        {
            animator.speed = 1f;

            // A cancelled bow cast never runs its own cleanup, so the draw state has to be cleared
            // here. Left mid-draw, the next shot's SetInteger("Charge", 1) is a no-op and the archer
            // fires without ever playing the animation.
            if (_entity.character != null) animator.SetInteger("Charge", 0);
        }

        SetAnimState(false);
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
        if (_isAttacking || target == null || _spells == null || _spells.Count == 0) return;

        // Pass 1: an affordable cost ability (ult) preempts the basic attack — cast-on-full.
        for (int i = 0; i < _spells.Count; i++)
        {
            var spell = _spells[i];
            if (spell == null || spell.alwaysOn || !spell.IsUltimate) continue;
            if (_spellCooldowns[i] > 0f || !spell.CanCast(_entity, target)) continue;
            if (!spell.MeetsWeaponRequirement(_entity)) continue;   // wrong weapon → ability inert
            if (_entity.Mana == null || _entity.Mana.currentMana < spell.manaCost) continue;

            StartCoroutine(CastSpellWithCooldown(i, target));
            return;
        }

        // Pass 2: otherwise the first ready basic attack (the mana charger).
        for (int i = 0; i < _spells.Count; i++)
        {
            var spell = _spells[i];
            if (spell == null || spell.alwaysOn || spell.IsUltimate) continue;
            if (_spellCooldowns[i] > 0f || !spell.CanCast(_entity, target)) continue;
            if (!spell.MeetsWeaponRequirement(_entity)) continue;

            StartCoroutine(CastSpellWithCooldown(i, target));
            return;
        }
    }

    private void TryAlwaysOnSpells()
    {
        for (int i = 0; i < _spells.Count; i++)
        {
            if (_spells[i] != null && _spells[i].alwaysOn &&
                _spells[i].CanCast(_entity, null) && _spellCooldowns[i] <= 0)
            {
                StartCoroutine(CastSpellWithCooldown(i, null));
                break;
            }
        }
    }

    private IEnumerator CastSpellWithCooldown(int spellIndex, Entity target)
    {
        var spell = _spells[spellIndex];
        _isAttacking = true;
        _spellCooldowns[spellIndex] = EffectiveCooldown(spellIndex);

        // Pay for and announce a cost ability up front, so the bar empties and the name callout
        // fires exactly as the ult begins.
        if (spell.IsUltimate && _entity.Mana != null)
        {
            _entity.Mana.TrySpend(spell.manaCost);
            AbilityFeedback.Announce(_entity, string.IsNullOrEmpty(spell.spellName) ? spell.name : spell.spellName);
        }

        // Every spell comes through here, the weapon's own attack included, so the two are counted
        // apart. Lumping them together made "abilities cast" tick on each auto-attack, which turned
        // an item asking the player to use their kit into one that filled itself by standing still.
        if (_entity.Resonance != null)
        {
            _entity.Resonance.Accrue(spell.ScalesWithAttackSpeed
                ? ResonanceRequirement.BasicAttacks
                : ResonanceRequirement.AbilitiesCast, 1f);
        }

        yield return StartCoroutine(spell.Cast(_entity, target));

        // Basic weapon attacks are the primary mana source — so Attack Speed accelerates ults too.
        if (spell.ScalesWithAttackSpeed && _entity.Mana != null) _entity.Mana.OnBasicAttack();

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
