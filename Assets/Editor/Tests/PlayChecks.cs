using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>One named thing to check about a running fight. It fails by throwing.</summary>
public class PlayCheck
{
    public readonly string Name;
    public readonly Func<IEnumerator> Body;
    public PlayCheck(string name, Func<IEnumerator> body) { Name = name; Body = body; }
}

/// <summary>
/// What a live fight has to be true of. Everything here was found by hand this session — a probe, a
/// screenshot and a guess each time — and every one lives where an edit-mode test cannot go: in
/// Awake, in OnEnable, or in the order things happen in during a fight.
///
/// Run them with <see cref="PlayTestRunner"/> (Tools > Tests > Run Play Tests). They are minutes,
/// not seconds, so they are not part of the ordinary suite.
/// </summary>
public static class PlayChecks
{
    public static List<PlayCheck> All() => new List<PlayCheck>
    {
        // First, because it is the only check that needs an actual bell rather than a fight in
        // progress: the others tolerate joining one, and this one is about where units START.
        new PlayCheck("nobody opens the fight shoved, or out of rank", NobodyStartsInsideTheSoftWall),
        new PlayCheck("every living unit can be seen to be alive", EveryLivingUnitHasAVisibleBar),
        new PlayCheck("a decoy is on its owner's side before anything looks at it", DecoyTakesItsOwnersSide),
        new PlayCheck("a bar comes back when its owner is alive again", ABarComesBackFromADeathFade),
        new PlayCheck("nobody fights a body they cannot reach", NobodyWalksPastAFight),
        new PlayCheck("a blow that lands reaches a voice", AHitIsHeard),
        new PlayCheck("a bar is the size of a body, whatever the art", ABarIsTheSizeOfABody),
        new PlayCheck("a body thrown into the wall loses the flat number, as a slam", AWallSlamIsFlatAndSaysSo),
        new PlayCheck("nobody stands a hand's width out of reach", NobodyStandsJustOutOfReach),
    };

    /// <summary>
    /// The deadlock: a unit whose target is just past its reach, standing still, not attacking.
    /// CombatAI's walk-in band once began at reach + 0.15 while attacking needs distance <= reach,
    /// so a unit in between did neither — and two melee units facing each other 1.55 apart, both
    /// with 1.5 reach, stood for the rest of the fight. Reported as "units standing still, not
    /// attacking anyone and not moving to a target"; measured at 12% of unit-frames with a target.
    ///
    /// Movement is judged over a window of frames, not one: the editor ticks at about 160 Hz and a
    /// walking unit moves under 0.02 a frame, which a per-frame test called standing still.
    /// </summary>
    private static IEnumerator NobodyStandsJustOutOfReach()
    {
        yield return PlayHarness.ReachTheBell();

        var trail = new Dictionary<Entity, Queue<Vector3>>();
        int withTarget = 0, justOutOfReach = 0;
        Entity worst = null; int worstFrames = 0; var perUnit = new Dictionary<Entity, int>();

        for (int sweep = 0; sweep < 240; sweep++)
        {
            foreach (var unit in PlayHarness.Living())
            {
                var ai = unit.CombatAI;
                if (ai == null || ai.CurrentTarget == null) continue;
                Vector3 p = unit.transform.position;
                if (!trail.TryGetValue(unit, out var q)) trail[unit] = q = new Queue<Vector3>();
                q.Enqueue(p); if (q.Count > 12) q.Dequeue();
                bool moved = q.Count < 12 || (q.Peek() - p).sqrMagnitude > 0.01f;

                // Only a unit that has every reason to act: walking stances, free to move, not held
                // by a taunt, a stun, a throw or a root, and not mid-swing.
                if (unit.EffectiveStance != Stance.Advance && unit.EffectiveStance != Stance.Dive) continue;
                if (unit.Knockback == null || !unit.Knockback.Steerable || unit.Knockback.IsStunned) continue;
                if (unit.Statuses != null && (unit.Statuses.Rooted || unit.Statuses.TauntedBy != null)) continue;
                if (ai.IsAttacking) continue;
                withTarget++;

                float d = Vector3.Distance(p, ai.CurrentTarget.transform.position);
                if (moved || d <= ai.AttackRange || d > ai.AttackRange + 0.2f) continue;
                justOutOfReach++;
                perUnit[unit] = (perUnit.TryGetValue(unit, out int n) ? n : 0) + 1;
                if (perUnit[unit] > worstFrames) { worstFrames = perUnit[unit]; worst = unit; }
            }
            yield return null;
        }

        if (withTarget < 200) yield break;   // too quiet a fight to say anything
        float share = justOutOfReach * 100f / withTarget;
        Debug.Log($"[PlayChecks] stood just out of reach in {share:0.0}% of frames ({justOutOfReach}/{withTarget})");
        // Ten percent, not one: with the bug this reads 60%; healthy fights read 0 to 3%, the 3
        // being a unit's few frames of noticing its target stepped away, which a 1% bar called a
        // failure. The bar is for the deadlock, not for reaction time.
        Assert.That(share, Is.LessThan(10f),
                    $"units stand a hand's width out of reach, neither walking nor attacking, in {share:0.0}% of frames" +
                    (worst != null ? " — worst: " + DisplayNames.Unit(worst) + " for " + worstFrames + " frames facing " + DisplayNames.Unit(worst.CombatAI.CurrentTarget) : ""));
    }

    /// <summary>
    /// The physics rule end to end: a real throw reaches a real wall, the hurt that arrives is
    /// exactly the flat wall number, and it is tagged as a slam so the damage number can say SLAM.
    /// The arithmetic is unit-tested on its own; this is the proof that the number the rule states
    /// is the number a unit actually loses, with nothing in between quietly scaling it.
    /// </summary>
    private static IEnumerator AWallSlamIsFlatAndSaysSo()
    {
        yield return PlayHarness.ReachTheBell();

        var s = CombatPhysics.Active;
        if (s == null || !s.enableImpacts || !CombatFeelSettings.Active.enableKnockback) yield break;
        Assert.That(ArenaBounds.Instance, Is.Not.Null, "no arena to have a wall");

        // A free-standing character: not a rooted decoy (it does not move), not a body already
        // flying (its own throw would hide ours), and not thrown between fights, when nothing is
        // hurt at all. This check failed once with "never happened" and no way to tell which.
        // The LOWEST such character: a body thrown down from the top of the field hit an ally on
        // the way, passed its momentum on, and never reached the floor. Nothing stands below the
        // lowest unit, so its path to the floor is clear by definition.
        Entity unit = null;
        foreach (var u in PlayHarness.Living())
        {
            if (u.Health == null || u.Knockback == null || !u.isCharacter) continue;
            if (!u.Knockback.Steerable) continue;
            if (u.Statuses != null && u.Statuses.Rooted) continue;
            // Not a unit with knockback resistance: this is a check about the wall, and the Wall
            // Keeper's 40% turned the throw into a shove that stopped short of it.
            if (u.Stats != null && u.Stats.KnockbackResistance != null && u.Stats.KnockbackResistance.Value > 0f) continue;
            if (unit == null || u.transform.position.y < unit.transform.position.y) unit = u;
        }
        Assert.That(unit, Is.Not.Null, "no free-standing, unresisting character to throw");
        Assert.That(GameManager.Instance.StateMachine.Current, Is.EqualTo(GameState.Combat), "nothing is hurt outside a fight");
        unit.Health.HealToFull();
        Vector3 before = unit.transform.position;

        DamageInfo? slam = null;
        Action<DamageInfo> watch = info => { if (info.kind == DamageKind.Slam && slam == null) slam = info; };
        unit.Health.OnDamaged += watch;
        try
        {
            // Straight down: the floor is the nearest wall from anywhere on the field, so a throw
            // this hard cannot fail to reach it, and it reaches it inside a second.
            unit.ApplyKnockback(Vector3.down, 20f, null);
            float deadline = Time.time + 6f;
            while (slam == null && Time.time < deadline) yield return null;
        }
        finally { unit.Health.OnDamaged -= watch; }

        Assert.That(slam, Is.Not.Null,
                    DisplayNames.Unit(unit) + " was thrown down from " + before + " and is at " + unit.transform.position +
                    " (state " + GameManager.Instance.StateMachine.Current + ", dead=" + unit.isDead +
                    ", steerable=" + unit.Knockback.Steerable + ") — no slam arrived");

        // What the rule sent, before blocking and shields took their share: amount + blocked is the
        // number TakeDamage was handed, and that is the one the rule promises.
        float sent = slam.Value.amount + slam.Value.blocked;
        float promised = s.wallSlamPercent * unit.Health.maxHealth;
        Assert.That(sent, Is.EqualTo(promised).Within(0.5f),
                    DisplayNames.Unit(unit) + " hit the wall for " + sent.ToString("0") + " and the rule says " +
                    promised.ToString("0") + " (" + (s.wallSlamPercent * 100f).ToString("0") + "% of " +
                    unit.Health.maxHealth.ToString("0") + ")");

        // And armour took its share: a slam is physical, and this is the damage pipeline running on
        // a real unit wearing real items. A shield may have taken more on top, never less.
        // The share from the curve's closed form, NOT from Mitigation.Reduce: an expectation computed
        // through the code under test agreed with it perfectly when that code was broken to do
        // nothing — "expected 0, got 0". A check has to know the answer on its own.
        float armour = unit.Stats != null && unit.Stats.Armor != null ? unit.Stats.Armor.Value : 0f;
        float armoursShare = sent * armour / (Mitigation.Constant + armour);
        Assert.That(slam.Value.blocked, Is.GreaterThanOrEqualTo(armoursShare - 0.5f),
                    DisplayNames.Unit(unit) + " has " + armour.ToString("0") + " armour, which should take " +
                    armoursShare.ToString("0.0") + " off a " + sent.ToString("0") + " slam; mitigation took " +
                    slam.Value.blocked.ToString("0.0"));
    }

    /// <summary>
    /// Where units stand when the bell rings, against the soft wall that pushes them in.
    ///
    /// The soft wall moves a body by writing its position (CombatPhysics), and the walk animation is
    /// driven by what the AI <i>decided</i> to do. A unit that has decided to stand still while the
    /// wall slides it is therefore an idle sprite gliding across the ground — which reads as a
    /// pathing bug and was reported as one. The fix is for nobody to be inside the band at the bell,
    /// so this asserts the formation and the arena agree about where the field is.
    /// </summary>
    private static IEnumerator NobodyStartsInsideTheSoftWall()
    {
        yield return PlayHarness.Until(() => GameManager.Instance != null, "the game to wake up");

        // A real bell, not "a fight is happening". Mid-fight a unit near the edge has usually been
        // kited or thrown there, which is allowed; this check is about where units are SEATED, and
        // sampling a fight in progress made it fail at random on units that had every right to be
        // in the corner. So watch for the transition itself and sample on the frame it happens.
        // The seats are read inside the state change itself: by the check's next tick a diver may
        // already have blinked.
        bool rang = false;
        var seats = new Dictionary<Entity, Vector3>();
        Action<GameState, GameState> watch = (from, to) =>
        {
            if (to != GameState.Combat || rang) return;
            rang = true;
            foreach (var unit in PlayHarness.Living()) seats[unit] = unit.transform.position;
        };
        var states = GameManager.Instance.StateMachine;
        states.OnStateChanged += watch;

        var telemetry = UnityEngine.Object.FindObjectOfType<CombatTelemetry>();
        if (telemetry != null) telemetry.autoAdvance = true;

        try { yield return PlayHarness.Until(() => rang, "a fight to begin", 120f); }
        finally
        {
            states.OnStateChanged -= watch;
            telemetry = UnityEngine.Object.FindObjectOfType<CombatTelemetry>();
            if (telemetry != null) telemetry.autoAdvance = false;
        }

        // Everyone stands on the centre of a cell. The first fix for the gliding below moved units
        // off their cells and away from the wall, and a unit standing beside its tile looked wrong
        // enough to be reported; the wall gives way now instead. Enemies are seated by the spawner
        // and the company by the formation, and both go through CellToWorld.
        if (BattleGrid.Instance != null)
        {
            var grid = BattleGrid.Instance;
            foreach (var seat in seats)
            {
                var unit = seat.Key; Vector3 at = seat.Value;
                grid.ClosestCell(unit.isTeam, at, out int column, out int row);
                float off = Vector2.Distance(at, grid.CellToWorld(unit.isTeam, column, row));
                Assert.That(off, Is.LessThan(0.1f), DisplayNames.Unit(unit) + " opens the fight " + off.ToString("0.00") +
                            " off the centre of its cell (" + column + "," + row + "), and " +
                            Vector2.Distance(at, unit.transform.position).ToString("0.00") + " from there a moment later");
            }
        }

        var physics = CombatPhysics.Active;
        float band = CombatPhysics.WallBand;
        if (physics == null || !physics.enableBodies || band <= 0f || physics.softWallPush <= 0f)
            yield break;     // the wall is off; there is nothing to be shoved by
        if (ArenaBounds.Instance == null) yield break;

        // Measured as the speed a unit would be slid, because the speed is what the player sees.
        // The reported bug ran at about 1.0 units/sec. With the wall kept out of the grid this
        // reads zero; the bar leaves room for a neighbour's nudge at the bell.
        Entity worst = null;
        float fastestDrift = 0f;
        foreach (var seat in seats)
        {
            float room = ArenaBounds.Instance.EdgeRoom(seat.Value);
            if (room >= band) continue;
            float drift = (1f - room / band) * physics.softWallPush;
            if (drift > fastestDrift) { fastestDrift = drift; worst = seat.Key; }
        }

        Assert.That(seats, Is.Not.Empty, "no living units at the bell");
        Assert.That(fastestDrift, Is.LessThan(0.25f),
                    (worst != null ? DisplayNames.Unit(worst) : "someone") + " opens the fight sliding " +
                    fastestDrift.ToString("0.00") + " units/sec toward the centre with no walk " +
                    "animation, because the soft wall is pushing a unit that has decided to stand still");

        // And the company is in rank: whoever fights at range opens behind whoever fights up close.
        // GridFormation.AutoPlace used to fill the front rank in list order, and in the four-verb
        // playtest that seated both archers a lancer's reach from the enemy with the daggers behind
        // them. Kiters back off and advancers step up from the first frame, so this can only get
        // truer after the bell — a failure here is a seating failure, not a timing one.
        if (BattleGrid.Instance != null)
        {
            float centre = BattleGrid.Instance.CentreLine;
            float nearestRanged = float.MaxValue, furthestMelee = float.MinValue;
            Entity rangedInFront = null, meleeBehind = null;
            foreach (var unit in PlayHarness.Living())
            {
                if (!unit.isTeam) continue;
                float depth = Mathf.Abs(unit.transform.position.x - centre);
                if (unit.FightsAtRange) { if (depth < nearestRanged) { nearestRanged = depth; rangedInFront = unit; } }
                else if (depth > furthestMelee) { furthestMelee = depth; meleeBehind = unit; }
            }
            if (rangedInFront != null && meleeBehind != null)
                Assert.That(nearestRanged, Is.GreaterThanOrEqualTo(furthestMelee),
                            DisplayNames.Unit(rangedInFront) + " (ranged) opens " + nearestRanged.ToString("0.0") +
                            " from the centre line, in front of " + DisplayNames.Unit(meleeBehind) + " (melee) at " +
                            furthestMelee.ToString("0.0") + " — the company was seated in list order, not by role");
        }
    }

    /// <summary>
    /// A bar belongs to a body, not to a piece of art. UnitBarsManager copies the entity's localScale
    /// onto the bar, and a decoy scales its ROOT to make an arbitrary sprite stand a body high — up
    /// to six times — so a decoy made from a small sprite wore a health bar six times everyone
    /// else's. Reported as "large health bars spawning".
    /// </summary>
    private static IEnumerator ABarIsTheSizeOfABody()
    {
        yield return PlayHarness.ReachTheBell();

        var owner = PlayHarness.Living()[0];
        float bodyBar = owner.healthBar != null ? Mathf.Abs(owner.healthBar.transform.lossyScale.x) : 0f;
        Assert.That(bodyBar, Is.GreaterThan(0f), "the unit to compare against has no bar");

        // A deliberately tiny sprite: this is exactly the input that made the root scale up.
        var texture = new Texture2D(4, 4);
        var tiny = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);

        var decoy = Decoy.Spawn(owner, owner.transform.position, 50f, 4f, null, tiny, "BarSizeCheckDecoy");
        Assert.That(decoy, Is.Not.Null, "no decoy was made");

        yield return null;   // a frame, so the bars manager has provisioned it

        Assert.That(PlayHarness.WhyNoBar(decoy), Is.Null, "the decoy has no visible bar to measure");
        float decoyBar = Mathf.Abs(decoy.healthBar.transform.lossyScale.x);

        Assert.That(decoyBar, Is.LessThan(bodyBar * 2f),
                    "a decoy's bar is " + (decoyBar / bodyBar).ToString("0.0") +
                    "x a real unit's — its art size has leaked into the bar");

        UnityEngine.Object.Destroy(decoy.gameObject);   // leave nothing for the next check to chase
    }

    /// <summary>
    /// The audio path, end to end: the bus announces a hit, <see cref="CombatAudio"/> is listening,
    /// and a clip ends up on a voice. Every part of that lives in OnEnable and in a static event —
    /// the two places an edit-mode test cannot see. The failure this guards against is silent by
    /// definition: a subscription that was never made sounds exactly like a library with no clips
    /// in it yet, which is what the library will legitimately be for weeks.
    /// </summary>
    private static IEnumerator AHitIsHeard()
    {
        yield return PlayHarness.ReachTheBell();

        var audio = UnityEngine.Object.FindObjectOfType<CombatAudio>();
        Assert.That(audio, Is.Not.Null, "nothing in the scene is listening to the fight");

        var voices = audio.GetComponentsInChildren<AudioSource>(true);
        Assert.That(voices.Length, Is.GreaterThan(0), "CombatAudio built no voices to play through");

        // A second of silence, so this says nothing about how anything sounds — only about routing.
        var probe = AudioClip.Create("SfxProbe", 8000, 1, 8000, false);

        // The bank this particular unit is heard through — a bow and a hammer deliberately do not
        // share one, so seeding a fixed bank would only be testing whoever happened to be first.
        var unit = PlayHarness.Living()[0];
        var bank = audio.BankFor(CombatAudio.Flavour(unit));
        var hadClips = bank.clips;
        bank.clips = new[] { probe };

        try
        {
            CombatEvents.RaiseHit(new HitInfo(unit, unit, 1f, false, false));

            bool onAVoice = false;
            foreach (var voice in voices) if (voice.clip == probe) onAVoice = true;
            Assert.That(onAVoice, Is.True,
                        "a hit was announced and no voice picked it up — CombatAudio is not subscribed");
        }
        finally
        {
            bank.clips = hadClips;   // the library is a real asset; leave it exactly as found
        }

        // And the other route, which is the one every authored clip actually uses: a spell named in
        // the library is heard when it is cast. This one needs no seeding — it asserts against the
        // real wiring, so it fails if a clip is unassigned or the row is pointed at the wrong spell.
        var sword = UnityEditor.AssetDatabase.LoadAssetAtPath<Spell>("Assets/Data/Spells/DefaultMeleeAttack.asset");
        Assert.That(sword, Is.Not.Null, "DefaultMeleeAttack has moved — this check is out of date");

        var swordBank = SfxLibrary.Active.For(sword);
        Assert.That(swordBank, Is.Not.Null, "the library has no row for the sword attack");
        Assert.That(swordBank.HasClips, Is.True, "the sword attack's row has no clip in it");

        var swung = PlayHarness.Living()[0];
        CombatEvents.RaiseCast(swung, sword);

        bool heard = false;
        foreach (var voice in voices)
            foreach (var authored in swordBank.clips)
                if (voice.clip == authored) heard = true;

        Assert.That(heard, Is.True, "a sword attack was cast and none of its clips reached a voice");

        // And the mix reaches the speaker: an ability's voice is louder than the swing's. The rule
        // is unit-tested on its own; this is the proof that the number the rule produces is the
        // number the AudioSource is given, with the row's trim and the master fader on top.
        AudioSource swordVoice = null;
        foreach (var voice in voices)
            foreach (var authored in swordBank.clips)
                if (voice.clip == authored) swordVoice = voice;

        var whirl = UnityEditor.AssetDatabase.LoadAssetAtPath<Spell>("Assets/Data/Spells/Whirl.asset");
        var whirlBank = whirl != null ? SfxLibrary.Active.For(whirl) : null;
        Assert.That(whirlBank, Is.Not.Null, "the library has no row for Whirl — pick another ability for this check");

        var abilityProbe = AudioClip.Create("AbilityProbe", 8000, 1, 8000, false);
        var whirlHad = whirlBank.clips;
        whirlBank.clips = new[] { abilityProbe };
        try
        {
            CombatEvents.RaiseCast(swung, whirl);
            AudioSource abilityVoice = null;
            foreach (var voice in voices) if (voice.clip == abilityProbe) abilityVoice = voice;
            Assert.That(abilityVoice, Is.Not.Null, "an ability was cast and no voice picked it up");
            Assert.That(abilityVoice.volume, Is.GreaterThan(swordVoice.volume),
                        "an ability plays at " + abilityVoice.volume.ToString("0.00") + " and a sword swing at " +
                        swordVoice.volume.ToString("0.00") + " — the bed is not under the abilities");
        }
        finally
        {
            whirlBank.clips = whirlHad;
        }
    }

    /// <summary>
    /// Bars, at the bell and right through a fight. Reported twice as disappearing, and never
    /// reproducible by hand; this is the check that would have settled it either way.
    /// </summary>
    private static IEnumerator EveryLivingUnitHasAVisibleBar()
    {
        yield return PlayHarness.ReachTheBell();

        var atBell = PlayHarness.Living();
        Assert.That(atBell.Count, Is.GreaterThanOrEqualTo(6), "a five-a-side should field ten units");
        foreach (var unit in atBell)
            Assert.That(PlayHarness.WhyNoBar(unit), Is.Null, DisplayNames.Unit(unit) + " at the bell");

        for (int sweep = 0; sweep < 14; sweep++)
        {
            yield return PlayHarness.Fight(1.5f);
            var living = PlayHarness.Living();
            foreach (var unit in living)
            {
                string why = PlayHarness.WhyNoBar(unit);
                Assert.That(why, Is.Null, DisplayNames.Unit(unit) + " lost its bar mid-fight: " + why);
            }
            if (living.Count < 3) break;   // the fight is over
        }
    }

    /// <summary>
    /// A decoy's team, at the moment it registers rather than afterwards. The bug was one line wide
    /// and invisible once the frame ended: AddComponent runs Awake and OnEnable there and then, so
    /// the decoy registered — and had its bar coloured — before its side was set.
    /// </summary>
    private static IEnumerator DecoyTakesItsOwnersSide()
    {
        yield return PlayHarness.ReachTheBell();
        yield return PlayHarness.Fight(2f);

        Entity ally = null, foe = null;
        foreach (var unit in PlayHarness.Living())
        {
            if (unit.isTeam && ally == null) ally = unit;
            if (!unit.isTeam && foe == null) foe = unit;
        }
        Assert.That(ally, Is.Not.Null, "no living ally to leave a decoy");
        Assert.That(foe, Is.Not.Null, "no living enemy to leave a decoy");

        bool? sideAtRegistration = null;
        Action<Entity> watch = e =>
        {
            if (e != null && sideAtRegistration == null && e.name.Contains("Decoy")) sideAtRegistration = e.isTeam;
        };

        EntityRegistry.OnRegistered += watch;
        Entity decoy;
        try { decoy = Decoy.Spawn(foe, foe.transform.position, 100f, 6f, null, null, "EnemyCheckDecoy"); }
        finally { EntityRegistry.OnRegistered -= watch; }

        Assert.That(decoy, Is.Not.Null, "no decoy was made");
        Assert.That(sideAtRegistration, Is.EqualTo(false),
                    "an enemy's decoy registered as one of the company — whatever reads isTeam at that " +
                    "moment, the health bar included, gets it wrong");

        yield return null;   // a frame, so the bars manager has provisioned and coloured it

        Assert.That(PlayHarness.WhyNoBar(decoy), Is.Null, "the enemy decoy has no visible bar");
        Assert.That(PlayHarness.BarIsEnemyColoured(decoy), Is.True, "an enemy's decoy wears the company's colour");

        // The other way round, so this is a check about sides rather than about red.
        var friendly = Decoy.Spawn(ally, ally.transform.position, 100f, 6f, null, null, "AllyCheckDecoy");
        yield return null;
        Assert.That(friendly.isTeam, Is.True);
        Assert.That(PlayHarness.BarIsEnemyColoured(friendly), Is.False, "an ally's decoy wears the enemy's colour");

        // Leave nothing behind. A decoy taunts and lives six seconds, and the checks after this one
        // run inside those seconds: the pacing check counted units walking to these as walking past
        // a fight, and failed at random for an afternoon.
        UnityEngine.Object.Destroy(decoy.gameObject);
        UnityEngine.Object.Destroy(friendly.gameObject);
    }

    /// <summary>
    /// A bar dimmed the way a death dims it, on a unit that is alive. Nothing used to put it back,
    /// so a bar that outlived its owner's death stayed at whatever alpha it reached — at worst
    /// invisible, for the rest of the run, with nothing in the logs.
    /// </summary>
    private static IEnumerator ABarComesBackFromADeathFade()
    {
        yield return PlayHarness.ReachTheBell();

        var unit = PlayHarness.Living()[0];
        var bar = unit.healthBar;
        Assert.That(bar, Is.Not.Null, DisplayNames.Unit(unit) + " has no bar to begin with");

        var fade = typeof(ResourceBar).GetField("_deathFade",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var apply = typeof(ResourceBar).GetMethod("ApplyDeathFade",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.That(fade, Is.Not.Null, "ResourceBar no longer has _deathFade — this check is out of date");

        fade.SetValue(bar, 0f);
        if (apply != null) apply.Invoke(bar, null);
        Assert.That(PlayHarness.WhyNoBar(unit), Is.Not.Null, "the bar was supposed to be invisible at this point");

        yield return null;
        yield return null;

        Assert.That(unit.isDead, Is.False, "the unit under check died; the result would mean nothing");
        Assert.That(PlayHarness.WhyNoBar(unit), Is.Null,
                    "a living unit's bar stayed faded out — nothing puts the fade back");
    }

    /// <summary>
    /// The pacing, as a number. A melee unit walking to a target it cannot reach, while an enemy
    /// stands inside its reach the whole time, is the thing that read as a unit refusing to commit:
    /// measured at 55 to 83% of walking frames before the rule that takes the nearer fight.
    /// </summary>
    private static IEnumerator NobodyWalksPastAFight()
    {
        yield return PlayHarness.ReachTheBell();

        int walking = 0, walkingPastSomeone = 0;
        var offenders = new Dictionary<string, int>();
        for (int sweep = 0; sweep < 120; sweep++)
        {
            foreach (var unit in PlayHarness.Living())
            {
                var ai = unit.CombatAI;
                if (ai == null || ai.CurrentTarget == null) continue;

                // The same exclusions as the rule this measures (CombatAI, "take the fight that is
                // already here"): a kiter is meant to back off, Relentless never lets go, a thrown
                // body is not walking, a taunted one walks past everyone by design, and a unit
                // mid-swing at a target that just stepped out of reach is finishing its swing.
                if (unit.EffectiveStance == Stance.Kite) continue;
                if (unit.EffectiveCommitment == Commitment.Relentless) continue;
                if (unit.Knockback != null && !unit.Knockback.Steerable) continue;
                if (unit.Statuses != null && unit.Statuses.TauntedBy != null) continue;
                if (ai.IsAttacking) continue;

                float reach = ai.AttackRange;
                if (Vector3.Distance(unit.transform.position, ai.CurrentTarget.transform.position) <= reach) continue;

                walking++;
                foreach (var other in PlayHarness.Living())
                {
                    if (other == unit || other == ai.CurrentTarget || other.isTeam == unit.isTeam) continue;
                    if (Vector3.Distance(unit.transform.position, other.transform.position) > reach * 0.8f) continue;
                    walkingPastSomeone++;
                    string who = DisplayNames.Unit(unit) + " [" + unit.EffectiveStance + "] -> " +
                                 DisplayNames.Unit(ai.CurrentTarget) + " past " + DisplayNames.Unit(other);
                    offenders[who] = offenders.TryGetValue(who, out int n) ? n + 1 : 1;
                    break;
                }
            }
            yield return null;
        }

        if (walking < 20) yield break;   // too quiet a fight to say anything

        // A failure names its frames. This check failed at random for an afternoon before it did,
        // and every guess about why was wrong until the frames were listed.
        var worst = new List<KeyValuePair<string, int>>(offenders);
        worst.Sort((a, b) => b.Value.CompareTo(a.Value));
        var detail = new System.Text.StringBuilder();
        for (int i = 0; i < worst.Count && i < 4; i++) detail.Append(" | ").Append(worst[i].Value).Append(" frames: ").Append(worst[i].Key);

        float share = walkingPastSomeone * 100f / walking;
        Debug.Log($"[PlayChecks] walked past a reachable enemy in {share:0}% of walking frames ({walkingPastSomeone}/{walking})");
        Assert.That(share, Is.LessThan(35f),
                    $"units walk past a fight they could be having in {share:0}% of the frames they spend walking" + detail);
    }
}
