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
    };

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
        bool rang = false;
        Action<GameState, GameState> watch = (from, to) => { if (to == GameState.Combat) rang = true; };
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

        var physics = CombatPhysics.Active;
        if (physics == null || !physics.enableBodies || physics.softWall <= 0f || physics.softWallPush <= 0f)
            yield break;     // the wall is off; there is nothing to be shoved by
        if (ArenaBounds.Instance == null) yield break;

        // The symptom, not the rule. Asking for zero wall pressure asks the impossible: the
        // formation is wider than the arena's safe area, so the bodies at the edge are pushed back
        // toward the band by their own neighbours and settle just inside it. What the player sees is
        // the SPEED of the resulting slide, so that is what this measures. The reported bug ran at
        // about 1.0 units/sec; the equilibrium after the fix is under 0.1.
        Entity worst = null;
        float fastestDrift = 0f;
        foreach (var unit in PlayHarness.Living())
        {
            float room = ArenaBounds.Instance.EdgeRoom(unit.transform.position);
            if (room >= physics.softWall) continue;
            float drift = (1f - room / physics.softWall) * physics.softWallPush;
            if (drift > fastestDrift) { fastestDrift = drift; worst = unit; }
        }

        Assert.That(PlayHarness.Living(), Is.Not.Empty, "no living units at the bell");
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
        for (int sweep = 0; sweep < 120; sweep++)
        {
            foreach (var unit in PlayHarness.Living())
            {
                var ai = unit.CombatAI;
                if (ai == null || ai.CurrentTarget == null) continue;
                if (unit.EffectiveStance == Stance.Kite) continue;                       // kiters are meant to back off
                if (unit.EffectiveCommitment == Commitment.Relentless) continue;         // and these never let go
                if (unit.Knockback != null && !unit.Knockback.Steerable) continue;       // being thrown is not walking

                // A taunted unit is meant to walk past everyone to reach what taunted it — the
                // retarget rule this check measures skips it too. Without this the check failed
                // at 43-47% whenever the decoy checks before it had left a decoy alive: every
                // counted frame was one unit, taunted, walking past a kiter to reach the decoy.
                if (unit.Statuses != null && unit.Statuses.TauntedBy != null) continue;

                float reach = ai.AttackRange;
                if (Vector3.Distance(unit.transform.position, ai.CurrentTarget.transform.position) <= reach) continue;

                walking++;
                foreach (var other in PlayHarness.Living())
                {
                    if (other == unit || other == ai.CurrentTarget || other.isTeam == unit.isTeam) continue;
                    if (Vector3.Distance(unit.transform.position, other.transform.position) <= reach * 0.8f)
                    { walkingPastSomeone++; break; }
                }
            }
            yield return null;
        }

        if (walking < 20) yield break;   // too quiet a fight to say anything

        float share = walkingPastSomeone * 100f / walking;
        Debug.Log($"[PlayChecks] walked past a reachable enemy in {share:0}% of walking frames ({walkingPastSomeone}/{walking})");
        Assert.That(share, Is.LessThan(35f),
                    $"units walk past a fight they could be having in {share:0}% of the frames they spend walking");
    }
}
