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

    /// <summary>The reach of this unit's weapon attack.</summary>
    public float AttackRange => _attackRange;

    /// <summary>The bell.</summary>
    public void OnFightStart()
    {
        _closing = false;
    }
    private float[] _spellCooldowns;

    // The spells this unit actually casts this combat: its innate spells (weapon basic + always-on)
    // plus the ONE active learnable spell from its slots. Built at Initialize — the active slot is a
    // pre-combat choice, fixed for the fight, so no mid-combat resizing is needed.
    private List<Spell> _spells;

    private Entity _entity;

    /// <summary>The current enemy target this entity is pursuing.</summary>
    public Entity CurrentTarget { get; private set; }

    /// <summary>Mid-swing. Read by the pacing check, which has to skip exactly what the retarget rule skips.</summary>
    public bool IsAttacking => _isAttacking;

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
        _spells = _entity.CastableSpells();

        // Attack range comes from the first innate spell (the weapon basic attack).
        if (_spells.Count > 0 && _spells[0] != null)
            _attackRange = _spells[0].range;

        // The bar is sized to the ability it charges: the active verb's cost, else the first cost
        // ability the unit carries (an enemy's rolled ult), else the authored pool.
        if (_entity.Mana != null)
        {
            float cost = 0f;
            var active = _entity.ActiveSpell;
            if (active != null && active.IsUltimate) cost = active.manaCost;
            else foreach (var s in _spells) if (s != null && s.IsUltimate && !s.alwaysOn) { cost = s.manaCost; break; }
            _entity.Mana.SetMax(cost);
        }

        // Ready to go. Starting each spell on a full cooldown meant nothing could open a fight:
        // an ability with a nine second cooldown was unusable for the first nine seconds however
        // full the mana bar was, so a player who saw a charged bar and no ability was watching a
        // timer they were never shown. For a cost ability the bar IS the gate — it starts empty
        // every fight and has to be earned — and for a basic attack, swinging on the first frame in
        // range is what anyone would expect.
        _spellCooldowns = new float[_spells.Count];
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
        // Whom to fight: taunted, the taunter; else the target already chosen, until it dies; else
        // a fresh pick by the unit's mode (Targeting, where the whole rule is written down).
        var mode = _entity.EffectiveTarget;
        Entity chosen = Targeting.Choose(_entity, mode, CurrentTarget);

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

        if (chosen != null)
        {
            CurrentTarget = chosen;
            float distToTarget = Vector3.Distance(transform.position, CurrentTarget.transform.position);

            // Take the fight that is already here. Measured over a fight: melee units spent about
            // half of it walking, and in 55 to 83% of those frames an enemy was standing inside
            // their reach the whole time — thrown there, or arrived while this unit chased a target
            // that a knockback had just flung across the arena. Walking past a fight to get to one
            // that keeps moving is what reads as a unit that will not commit.
            //
            // Only for a unit that picks the nearest: it asked for the closest fight, and this is it.
            // A unit whose gear picked on purpose — the weakest, the farthest, its attacker — goes
            // on to the one it chose; a taunt holds anyone.
            bool taunted = _entity.Statuses != null && _entity.Statuses.TauntedBy != null;
            if (distToTarget > _attackRange && !_isAttacking && !taunted && mode == TargetMode.Nearest)
            {
                var here = ThreatInReach(_attackRange * WellInsideReach);
                if (here != null)
                {
                    Retarget(here);
                    distToTarget = Vector3.Distance(transform.position, CurrentTarget.transform.position);
                }
            }

            // Ask first, walk second. Something with the reach to be used from here should be used
            // from here, whatever the weapon's reach is.
            bool acted = Attack(CurrentTarget, distToTarget);

            if (!acted && !_isAttacking)
            {
                // Ground does not steer anyone: a unit walks to its target through a tar pool if that
                // is where the target is. Units used to walk out of hostile pools, stand off at their
                // rims and then route around them — three rules, each added to fix the pacing the one
                // before it caused. A pool is damage and a status now, nothing more.
                move = Close(distToTarget);
                SetAnimState(move.sqrMagnitude > 0.0001f);
            }
            else
            {
                SetAnimState(false);
            }
        }
        else
        {
            CurrentTarget = null;
            SetAnimState(false);
        }

        bool rooted = _entity.Statuses != null && _entity.Statuses.Rooted;
        // Steerable, not merely at rest: the last of a slide is a body that has landed, and a unit
        // that may not walk until the drift is gone spends the scrum's small shoves standing still.
        if (_entity.Knockback.Steerable && !_entity.Knockback.IsStunned && !rooted)
        {
            Vector3 finalMove = (move + separation * separationStrength) * Time.deltaTime;
            transform.position += finalMove;
            float stepped = finalMove.magnitude;
            if (stepped > 0f) CombatEvents.RaiseMoved(_entity, stepped);
        }
    }

    // Walking in, as opposed to standing and fighting.
    //
    // A unit sets off the moment its target is out of reach, and stops a little INSIDE reach — never
    // inside the body wall. The slack has been on both sides of reach and both were wrong in ways
    // no tuning fixes. Stopping at 0.8 of reach (1.20 for a 1.5 reach) was a tenth off the 1.10 two
    // bodies can stand at, so ResolveBodies pushed the unit out every frame and it walked in again:
    // run in, back off, repeat. Then the slack went OUTSIDE reach — set off only past reach + 0.15 —
    // and a unit between reach and that line neither walked nor could hit, so two melee units facing
    // each other 1.55 apart stood there for the rest of the fight. Inside reach, above the wall, is
    // the only place the stop can live.
    private bool _closing;

    /// <summary>How far inside its reach a unit stops, so the scrum's shoves do not pop it back out.</summary>
    private const float ReachSlack = 0.15f;

    /// <summary>A free fight has to be comfortably inside reach, not balanced on its edge.</summary>
    private const float WellInsideReach = 0.8f;

    /// <summary>
    /// Where to walk this frame, or nowhere: toward the target while it is out of reach, and not at
    /// all once it is in. Everyone moves this way, archers included — a bow simply has the reach to
    /// stand still. Units used to have stances: Kite backed an archer away from whatever came for it
    /// (sixteen directions scored, a chaser it had given up outrunning, a margin off the walls), and
    /// it was the most complicated movement in the game and the source of three pacing bugs. It was
    /// removed 2026-09-22 with Hold and Dive (Dive is a target mode now).
    /// </summary>
    private Vector3 Close(float distToTarget)
    {
        // Close the moment the target is out of reach; stop a little INSIDE reach, so the scrum's
        // shoves do not pop the unit back out — but never inside the body wall, where the two bodies
        // would push apart every frame and the unit would walk forever (the bug before this one).
        //
        // The slack used to sit outside reach: closing began at reach + 0.15, attacking needs
        // distance <= reach, so a unit between the two neither closed nor attacked — and two melee
        // units facing each other 1.55 apart, both with 1.5 reach, stood there for the rest of the
        // fight. Measured: 21% of all unit-frames with a target were idle, most of them this.
        float bodyWall = _entity.BodyRadius + (CurrentTarget != null ? CurrentTarget.BodyRadius : _entity.BodyRadius);
        float stopAt = Mathf.Max(_attackRange - ReachSlack, bodyWall + 0.05f);
        if (_closing) { if (distToTarget <= stopAt) _closing = false; }
        else if (distToTarget > _attackRange) _closing = true;

        return _closing ? Approach(distToTarget) : Vector3.zero;
    }

    /// <summary>Close on the target, drifting a little to the side so a column does not walk single file.</summary>
    private Vector3 Approach(float distToTarget)
    {
        Vector3 dir = (CurrentTarget.transform.position - transform.position).normalized;
        float fade = Mathf.Clamp01((distToTarget - _attackRange) / _attackRange);
        Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
        float offsetAmount = Mathf.PerlinNoise(transform.position.x, transform.position.y) - 0.5f;
        Vector3 lateralOffset = perp * offsetAmount * 0.8f * fade;
        return (dir + lateralOffset).normalized * moveSpeed;
    }

    /// <summary>
    /// The best enemy already within <paramref name="within"/>: one that is coming for us if there
    /// is one, else the closest. Never the current target.
    /// </summary>
    private Entity ThreatInReach(float within)
    {
        Entity best = null; float bestD = float.MaxValue; bool bestComing = false;
        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (e == null || e.isDead || e.isTeam == _entity.isTeam || !e.gameObject.activeInHierarchy || e == CurrentTarget) continue;
            if (!Targeting.IsEnemyOf(_entity, e)) continue;
            float d = Vector3.Distance(transform.position, e.transform.position);
            if (d > within) continue;
            bool coming = e.CombatAI != null && e.CombatAI.CurrentTarget == _entity;
            if (coming && !bestComing || (coming == bestComing && d < bestD)) { best = e; bestD = d; bestComing = coming; }
        }
        return best;
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

    /// <summary>
    /// Cast whatever is ready, and say whether anything was.
    ///
    /// Each spell is gated by ITS OWN range rather than by the weapon's reach. That distinction is
    /// the whole difference between an ability and a heavier swing: a unit used to have to walk
    /// into knife range before any ability was even considered, so an assassin with a dive that
    /// crosses the battlefield stood there charging its mana, closed the distance on foot, and only
    /// then teleported the last stride. Anything that reaches further than the weapon — a lobbed
    /// bomb, a thrown blade, a dive — was unreachable by construction.
    /// </summary>
    private bool Attack(Entity target, float distance)
    {
        if (_isAttacking || target == null || _spells == null || _spells.Count == 0) return false;

        // Pass 1: an affordable cost ability (ult) preempts the basic attack — cast-on-full.
        for (int i = 0; i < _spells.Count; i++)
        {
            var spell = _spells[i];
            if (spell == null || spell.alwaysOn || !spell.IsUltimate) continue;
            if (CombatFeelSettings.IsDisabled(spell)) continue;
            if (_spellCooldowns[i] > 0f || !spell.CanCast(_entity, target)) continue;
            if (!spell.MeetsWeaponRequirement(_entity)) continue;   // wrong weapon → ability inert
            if (_entity.Mana == null || _entity.Mana.currentMana < spell.manaCost) continue;
            if (distance > spell.range) continue;                   // its own reach, not the weapon's

            _entity.OpeningPending = false;   // the charge is over; from here it is whoever is closest
            StartCoroutine(CastSpellWithCooldown(i, target));
            return true;
        }

        // Pass 2: otherwise the first ready basic attack (the mana charger).
        for (int i = 0; i < _spells.Count; i++)
        {
            var spell = _spells[i];
            if (spell == null || spell.alwaysOn || spell.IsUltimate) continue;
            if (CombatFeelSettings.IsDisabled(spell)) continue;
            if (_spellCooldowns[i] > 0f || !spell.CanCast(_entity, target)) continue;
            if (!spell.MeetsWeaponRequirement(_entity)) continue;
            if (distance > spell.range) continue;

            _entity.OpeningPending = false;
            StartCoroutine(CastSpellWithCooldown(i, target));
            return true;
        }

        return false;
    }

    private void TryAlwaysOnSpells()
    {
        for (int i = 0; i < _spells.Count; i++)
        {
            if (_spells[i] != null && _spells[i].alwaysOn && !CombatFeelSettings.IsDisabled(_spells[i]) &&
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
            AbilityFeedback.AnnounceSpell(_entity, spell);
            CombatTelemetry.RecordUlt(_entity);
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

        CombatEvents.RaiseCast(_entity, spell);
        _refundRequested = false;

        yield return StartCoroutine(spell.Cast(_entity, target));

        // A spell that earned its cooldown back (a Backstab that killed) is ready again at once.
        if (_refundRequested && spellIndex < _spellCooldowns.Length) _spellCooldowns[spellIndex] = 0f;
        _refundRequested = false;

        // Basic weapon attacks are the primary mana source — so Attack Speed accelerates ults too.
        if (spell.ScalesWithAttackSpeed && _entity.Mana != null) _entity.Mana.OnBasicAttack();

        _isAttacking = false;
    }

    private bool _refundRequested;

    /// <summary>
    /// Turn onto this target now. A taunt already wins the next pick; this makes the turn visible
    /// this frame and drops any leash held against the old target.
    /// </summary>
    public void Retarget(Entity target)
    {
        if (target == null || target.isDead) return;
        CurrentTarget = target;
        _entity.OpeningPending = false;
    }

    /// <summary>Called by a spell mid-cast: when it finishes, its cooldown is cleared.</summary>
    public void RefundCooldown() => _refundRequested = true;

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
