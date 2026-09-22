using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The parts of the audio layer that are decisions rather than playback: which variation comes
/// next, which bank a unit's blows come from, and how loud an impact is.
///
/// The rotation is the one worth pinning down. A bag that repeats itself is not a crash — it is a
/// game that sounds slightly cheap, which nothing fails and nobody reports.
/// </summary>
public class SfxTests
{
    /// <summary>A deterministic Fisher-Yates roll, so a shuffle can be asserted on at all.</summary>
    private static SfxBag Rigged(params int[] rolls)
    {
        int i = 0;
        return new SfxBag(max => rolls[i++ % rolls.Length] % max);
    }

    // ---------- the rotation ----------

    [Test]
    public void EveryVariationIsHeardOnceBeforeAnyIsHeardTwice()
    {
        var bag = new SfxBag();
        var seen = new HashSet<int>();
        for (int i = 0; i < 5; i++) seen.Add(bag.Next(5));

        Assert.That(seen, Has.Count.EqualTo(5), "a deal of five gave the same clip twice");
    }

    [Test]
    public void NoVariationIsEverPlayedTwiceInARow()
    {
        // Across the seam between deals, which is the only place it can happen — and which a
        // hand-rolled shuffle gets wrong about a quarter of the time with four clips.
        var bag = new SfxBag();
        int last = -1;
        for (int i = 0; i < 400; i++)
        {
            int next = bag.Next(4);
            Assert.That(next, Is.Not.EqualTo(last), "clip " + next + " played twice in a row at pull " + i);
            last = next;
        }
    }

    [Test]
    public void AShuffleIsAShuffleAndNotTheSameOrderEachTime()
    {
        // Rigged so the deals differ: if Refill ever stopped shuffling, this is what would catch it.
        var bag = Rigged(0, 1, 0, 2, 1, 2);
        var first = new List<int> { bag.Next(4), bag.Next(4), bag.Next(4), bag.Next(4) };
        var second = new List<int> { bag.Next(4), bag.Next(4), bag.Next(4), bag.Next(4) };

        Assert.That(second, Is.Not.EqualTo(first), "both deals came out in the same order");
    }

    [Test]
    public void ABankWithOneClipOrNoneIsHandled()
    {
        var bag = new SfxBag();
        Assert.That(bag.Next(1), Is.EqualTo(0));
        Assert.That(bag.Next(1), Is.EqualTo(0), "one clip has nothing to rotate to");
        Assert.That(bag.Next(0), Is.EqualTo(-1), "an empty bank has no clip to name");
    }

    [Test]
    public void ABankThatGrowsIsRedealtRatherThanIndexedOutOfRange()
    {
        // Clips dropped into a bank mid-session: the bag must not keep handing out the old range.
        var bag = new SfxBag();
        bag.Next(2);
        for (int i = 0; i < 20; i++)
            Assert.That(bag.Next(6), Is.InRange(0, 5));
    }

    // ---------- the gap ----------

    [Test]
    public void ACrowdOfSimultaneousHitsPlaysOnceRatherThanFiveTimesAtOnce()
    {
        var bank = new SfxBank { clips = new[] { AClip(), AClip() }, minGapSeconds = 0.05f };

        Assert.That(bank.Take(10f), Is.Not.Null, "the first hit of the volley");
        Assert.That(bank.Take(10f), Is.Null, "the second hit in the same frame");
        Assert.That(bank.Take(10.02f), Is.Null, "still inside the gap");
        Assert.That(bank.Take(10.06f), Is.Not.Null, "past the gap, it speaks again");
    }

    [Test]
    public void AnEmptyBankIsSilentRatherThanThrowing()
    {
        // Half the library will be empty for weeks while the clips are made.
        Assert.That(new SfxBank().Take(0f), Is.Null);
        Assert.That(new SfxBank { clips = new AudioClip[] { null, null } }.Take(0f), Is.Null);
    }

    // ---------- which sound ----------

    [Test]
    public void AUnitSoundsLikeTheWeaponAttackItActuallyMakes()
    {
        Assert.That(CombatAudio.Flavour(A<BowAttackSpell>()), Is.EqualTo(CombatAudio.HitFlavour.Pierce));
        Assert.That(CombatAudio.Flavour(A<WandAttackSpell>()), Is.EqualTo(CombatAudio.HitFlavour.Magic));
        Assert.That(CombatAudio.Flavour(A<FirearmAttackSpell>()), Is.EqualTo(CombatAudio.HitFlavour.Gun));
        Assert.That(CombatAudio.Flavour(A<HeavyAttackSpell>()), Is.EqualTo(CombatAudio.HitFlavour.Heavy));
        Assert.That(CombatAudio.Flavour(A<MeleeAttackSpell>()), Is.EqualTo(CombatAudio.HitFlavour.Light));
        Assert.That(CombatAudio.Flavour(A<DualWieldAttackSpell>()), Is.EqualTo(CombatAudio.HitFlavour.Light));
    }

    [Test]
    public void AUnitWithNoWeaponAttackStillMakesASound()
    {
        Assert.That(CombatAudio.Flavour((Spell)null), Is.EqualTo(CombatAudio.HitFlavour.Light));
        Assert.That(CombatAudio.Flavour((Entity)null), Is.EqualTo(CombatAudio.HitFlavour.Light));
    }

    [Test]
    public void AGentleBumpIsQuietAndAFullThrowIsLoudAndNeitherIsSilent()
    {
        Assert.That(CombatAudio.Loudness(9f, 9f), Is.EqualTo(1f).Within(0.001f));
        Assert.That(CombatAudio.Loudness(30f, 9f), Is.EqualTo(1f).Within(0.001f), "and never louder than full");
        Assert.That(CombatAudio.Loudness(4f, 9f), Is.LessThan(1f).And.GreaterThan(0.3f));

        // A body that visibly moved another body and made no sound reads as a missing feature.
        Assert.That(CombatAudio.Loudness(0f, 9f), Is.GreaterThan(0f));
    }

    // ---------- the library as content ----------

    [Test]
    public void TheLibraryNeverListsOneSpellTwice()
    {
        // Two entries for one verb means the second is dead weight that looks authored. The asset
        // may not exist yet; an empty library passes this for the right reason.
        var seen = new HashSet<Spell>();
        foreach (var entry in SfxLibrary.Active.spellSounds)
        {
            Assert.That(entry.spell, Is.Not.Null, "an entry in the library names no spell");
            Assert.That(seen.Add(entry.spell), Is.True, entry.spell.name + " is listed twice");
        }
    }

    // ---------- helpers ----------

    private static AudioClip AClip() => AudioClip.Create("test", 16, 1, 8000, false);

    private static Spell A<T>() where T : Spell => ScriptableObject.CreateInstance<T>();
}
