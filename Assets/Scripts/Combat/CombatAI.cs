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

    // Whether a kiting unit is mid-retreat: it starts backing at one distance and stops at a
    // longer one, so it does not shuffle on the line between the two.
    private bool _kiting;

    // The leash (see Targeting.LeashSeconds): when progress toward the target was last made, and
    // the target a broken leash keeps off the table for a moment.
    private float _lastProgressTime;
    private float _lastDistanceToTarget = float.MaxValue;
    private Entity _leashed;
    private float _leashedUntil;
    private const float LeashHoldoffSeconds = 2f;

    /// <summary>How many times any unit's leash has broken this session — for measuring the rule.</summary>
    public static int LeashBreaks;

    // The stance's memory: when the bell rang and how hurt the unit was then, so Hold knows when
    // its wait is over and whether it has been hurt since.
    private float _fightStart;
    private float _healthAtBell;

    /// <summary>The bell. Stances that wait or hold measure from here.</summary>
    public void OnFightStart()
    {
        _fightStart = Time.time;
        _healthAtBell = _entity != null && _entity.Health != null ? _entity.Health.currentHealth : 0f;

        // A new fight is a new question: whoever we could not outrun last time is not here.
        _unescapable = null;
        _kiting = false;
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
        // Whom to fight is its own question now, asked of Targeting, which knows about modes and
        // about not flip-flopping between two enemies a hair apart. A target the leash just broke
        // on is kept out of the running for a moment, or it would be chosen straight back.
        Entity avoid = Time.time < _leashedUntil ? _leashed : null;
        // A diver goes for the back line: the farthest enemy, whatever the unit's own mode says.
        var mode = _entity.EffectiveStance == Stance.Dive ? TargetMode.Furthest : _entity.EffectiveTarget;
        Entity closestEnemy = Targeting.Choose(_entity, mode, CurrentTarget,
                                               _entity.targetStickiness, avoid);

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
            if (closestEnemy != CurrentTarget)
            {
                // A fresh target: the leash starts now.
                CurrentTarget = closestEnemy;
                _lastProgressTime = Time.time;
                _lastDistanceToTarget = float.MaxValue;
            }
            float distToTarget = Vector3.Distance(transform.position, CurrentTarget.transform.position);

            // Take the fight that is already here. Measured over a fight: melee units spent about
            // half of it walking, and in 55 to 83% of those frames an enemy was standing inside
            // their reach the whole time — thrown there, or arrived while this unit chased a target
            // that a knockback had just flung across the arena. Walking past a fight to get to one
            // that keeps moving is what reads as a unit that will not commit.
            //
            // The leash reaches the same conclusion — "something in reach is the better fight right
            // now" — but only after two and a half seconds without progress, so it fired about once
            // a fight. This asks the question every frame instead, and only when the answer costs
            // nothing: our target is out of reach and someone else is comfortably inside it.
            // Relentless never lets go; that is what the commitment means.
            bool taunted = _entity.Statuses != null && _entity.Statuses.TauntedBy != null;
            if (distToTarget > _attackRange && !_isAttacking && !taunted &&
                _entity.EffectiveCommitment != Commitment.Relentless)
            {
                var here = ThreatInReach(_attackRange * SettleFraction);
                if (here != null)
                {
                    Retarget(here);
                    distToTarget = Vector3.Distance(transform.position, CurrentTarget.transform.position);
                }
            }

            // Ask first, walk second. Something with the reach to be used from here should be used
            // from here, whatever the weapon's reach is.
            bool acted = Attack(CurrentTarget, distToTarget);

            // The leash is reach: a target that has been out of reach for longer than the unit's
            // commitment allows is let go. Closing the distance no longer counts as progress — a unit
            // knocked away and walking back closes the distance every frame and never reached anyone,
            // which is the pinball the leash was meant to end. Relentless never lets go.
            bool inReach = distToTarget <= _attackRange || _isAttacking;
            if (acted || inReach) _lastProgressTime = Time.time;
            _lastDistanceToTarget = distToTarget;
            float leash = Targeting.LeashFor(_entity.EffectiveCommitment);
            if (Targeting.LockOn && !inReach && !taunted && Time.time - _lastProgressTime > leash)
            {
                // Something in reach that is coming for us, or anything in reach at all, is the
                // better fight right now; failing that, pick again without this one.
                var threat = ThreatInReach();
                LeashBreaks++;
                _leashed = CurrentTarget;
                _leashedUntil = Time.time + LeashHoldoffSeconds;
                _lastProgressTime = Time.time;
                if (threat != null) { Retarget(threat); }
                else { CurrentTarget = null; SetAnimState(false); return; }   // pick again next frame, without this one
            }


            if (!acted && !_isAttacking)
            {
                // Hostile ground first: a unit standing in the enemy's pool leaves it before it does
                // anything its stance would have it do, straight away from the centre. Mid-swing it
                // stays and finishes; that, and a throw back in, is what the pool is for.
                var pool = Zone.HostileAt(_entity);
                if (pool != null) _fleeingPool = pool;
                else if (_fleeingPool != null && Vector3.Distance(transform.position, _fleeingPool.transform.position) > _fleeingPool.Radius + PoolMargin) _fleeingPool = null;
                if (_fleeingPool != null)
                {
                    // Keep going a little past the rim, or a unit whose target stands across the pool
                    // steps out, steps back in, and shivers on the edge for the pool's whole life.
                    Vector3 away = transform.position - _fleeingPool.transform.position; away.z = 0f;
                    move = (away.sqrMagnitude > 0.0001f ? away.normalized : (_entity.isTeam ? Vector3.left : Vector3.right)) * moveSpeed;
                }
                else move = StanceMove(distToTarget);
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
        if (!_entity.Knockback.IsActive && !_entity.Knockback.IsStunned && !rooted)
        {
            Vector3 finalMove = (move + separation * separationStrength) * Time.deltaTime;
            transform.position += finalMove;
            float stepped = finalMove.magnitude;
            if (stepped > 0f) CombatEvents.RaiseMoved(_entity, stepped);
        }
    }

    /// <summary>
    /// Where the stance says to go this frame, or nowhere. Advance closes on the target; Kite backs
    /// away from whatever is nearest when it comes inside the unit's reach, and otherwise closes
    /// like anyone else; Hold stands its ground until hurt or until the wait runs out; Dive is
    /// Advance with a different target. Docs/Combat.md, "Stances".
    /// </summary>
    /// <summary>The hostile pool this unit is walking out of, until it is clear of the rim by <see cref="PoolMargin"/>.</summary>
    private Zone _fleeingPool;
    private const float PoolMargin = 0.6f;

    // The chaser this unit has accepted it cannot outrun, and the retreat it is judging (Kiting).
    // Held until that chaser leaves or gives up on us, so the decision is made once rather than
    // retaken every frame — which is what turned a failed retreat into a shuffle on the spot.
    private Entity _unescapable;
    private float _retreatStarted;
    private float _retreatStartDistance;

    // Walking in, as opposed to standing and fighting. Reach is a boundary like any other here, and
    // like the others it needs a band: bodies shove each other half a unit at a time, so a melee
    // unit whose target sits exactly at its reach was stepping forward and back every time the
    // scrum breathed. It closes to comfortably inside reach and does not set off again until the
    // target is properly outside it.
    private bool _closing;
    private const float SettleFraction = 0.8f;

    private Vector3 StanceMove(float distToTarget)
    {
        var s = CombatPhysics.Active;
        switch (_entity.EffectiveStance)
        {
            case Stance.Kite:
            {
                // Kiting the way a player micros an archer: back off from the brawler coming for you,
                // not from every enemy on the field; keep going once you have started, until it is
                // clearly out of reach; fall back toward your own line; and when the wall is at
                // your back, stop running and shoot.
                var threat = NearestThreat(s, out float threatDist);
                float start = _attackRange * s.kiteFraction;
                float stop = start + s.kiteHysteresis;

                // Whoever we gave up running from stops counting once they are off us, so a chaser
                // that turns away, dies, or is thrown clear can be kited again.
                if (_unescapable != null && (threat != _unescapable || _unescapable.isDead ||
                                             !_unescapable.gameObject.activeInHierarchy || threatDist >= stop))
                    _unescapable = null;

                bool retreat = threat != null && threat != _unescapable &&
                               (threatDist < start || (_kiting && threatDist < stop));
                if (retreat)
                {
                    if (!_kiting) { _retreatStarted = Time.time; _retreatStartDistance = threatDist; }

                    Vector3 dir = KiteDirection(threat, s);
                    // Cornered — nothing scored — or running that is not opening the gap. Either
                    // way the retreat has failed, and a unit that keeps trying it paces on the spot
                    // (Kiting). Accept the fight: with the chaser inside our reach we shoot it from
                    // here, which is a worse position and a better answer than the shuffle.
                    if (dir.sqrMagnitude <= 0.0001f ||
                        !Kiting.Escaping(Time.time - _retreatStarted, _retreatStartDistance, threatDist))
                    {
                        _unescapable = threat;
                    }
                    else
                    {
                        _kiting = true;
                        return dir * moveSpeed * s.kiteSpeed;
                    }
                }
                _kiting = false;
                break;
            }
            case Stance.Hold:
            {
                bool hurt = _entity.Health != null && _entity.Health.currentHealth < _healthAtBell - 0.5f;
                if (distToTarget > _attackRange && !hurt && Time.time - _fightStart < s.holdSeconds) return Vector3.zero;
                break;
            }
        }
        if (_closing) { if (distToTarget <= _attackRange * SettleFraction) _closing = false; }
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
    /// The nearest enemy worth running from: one that is fighting this unit and cannot shoot back
    /// from where it stands (its reach is shorter than ours). Another archer is not a reason to run;
    /// nor is a brawler busy with someone else. With the setting off, simply the nearest enemy.
    /// </summary>
    private Entity NearestThreat(CombatPhysics.Settings s, out float distance)
    {
        if (!s.kiteOnlyWhenTargeted) return NearestEnemy(out distance);
        Entity best = null; distance = float.MaxValue;
        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (e == null || e.isDead || e.isTeam == _entity.isTeam || !e.gameObject.activeInHierarchy || e.CombatAI == null) continue;
            if (e.CombatAI.CurrentTarget != _entity) continue;
            if (e.CombatAI.AttackRange >= _attackRange) continue;
            float d = Vector3.Distance(transform.position, e.transform.position);
            if (d < distance) { distance = d; best = e; }
        }
        return best;
    }

    /// <summary>
    /// Which way to back off. Sixteen directions are scored: a step must open distance from the
    /// threat and land at least the wall margin inside the arena; among those, the one that opens
    /// the most, leans most toward our own back line, and keeps the most room from the edge wins.
    /// So a unit with the wall at its back curves around the threat instead of pressing into the
    /// wall, and one that is already inside the margin walks back out before it thinks about the
    /// threat. Nothing scores: cornered, the unit stands and shoots.
    /// </summary>
    private Vector3 KiteDirection(Entity threat, CombatPhysics.Settings s)
    {
        Vector3 here = transform.position;
        Vector3 away = here - threat.transform.position; away.z = 0f;
        if (away.sqrMagnitude < 0.0001f) away = _entity.isTeam ? Vector3.left : Vector3.right;
        away.Normalize();
        Vector3 home = _entity.isTeam ? Vector3.left : Vector3.right;

        float step = Mathf.Max(0.5f, moveSpeed * s.kiteSpeed * 0.25f);
        float margin = Mathf.Max(0f, s.kiteWallMargin);
        float roomHere = ArenaBounds.RoomToEdge(here);
        bool insideMargin = roomHere < margin;

        Vector3 best = Vector3.zero; float bestScore = float.MinValue;
        for (int k = 0; k < 16; k++)
        {
            float a = k * Mathf.PI * 2f / 16f;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Vector3 to = here + dir * step;
            float roomTo = ArenaBounds.RoomToEdge(to);
            float gain = Vector3.Dot(dir, away);

            // Already too near the wall: any step that gets us back out is allowed, even sideways
            // past the threat, as long as it does not walk straight into it.
            if (insideMargin) { if (roomTo <= roomHere || gain < -0.3f) continue; }
            else if (roomTo < margin || gain < 0.15f) continue;

            float score = gain + Vector3.Dot(dir, home) * s.kiteHomeBias + Mathf.Clamp01(roomTo / (margin * 2f + 0.01f)) * 0.5f;
            if (score > bestScore) { bestScore = score; best = dir; }
        }
        return best;
    }

    /// <summary>
    /// An enemy within this unit's reach worth turning on: one that is targeting us first, else the
    /// nearest. What a unit swings at when the one it wanted cannot be reached.
    /// </summary>
    private Entity ThreatInReach() => ThreatInReach(_attackRange);

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

    private Entity NearestEnemy(out float distance)
    {
        Entity best = null; distance = float.MaxValue;
        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (e == null || e.isDead || e.isTeam == _entity.isTeam || !e.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(transform.position, e.transform.position);
            if (d < distance) { distance = d; best = e; }
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
        WeaponDraw.End(_entity);   // a cast cut short still puts the drawn weapon away

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
        WeaponDraw.End(_entity);

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
            AbilityFeedback.Announce(_entity, spell.DisplayName);
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

        // A racked weapon's verb is cast with that weapon: drawn now, after the last swing has
        // finished, and put away once the cast is done, so the hand weapon is back for the next swing.
        WeaponDraw.Begin(_entity, spell);
        yield return StartCoroutine(spell.Cast(_entity, target));
        WeaponDraw.End(_entity);

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
        _leashed = null; _leashedUntil = 0f; _lastProgressTime = Time.time;
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
