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
        new PlayCheck("every living unit can be seen to be alive", EveryLivingUnitHasAVisibleBar),
        new PlayCheck("a decoy is on its owner's side before anything looks at it", DecoyTakesItsOwnersSide),
        new PlayCheck("a bar comes back when its owner is alive again", ABarComesBackFromADeathFade),
        new PlayCheck("nobody fights a body they cannot reach", NobodyWalksPastAFight),
        new PlayCheck("a blow that lands reaches a voice", AHitIsHeard),
    };

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
