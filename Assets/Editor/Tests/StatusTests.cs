using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The status rules with no scene: stacking, expiry, and the intrinsic multipliers. Time is a number
/// passed in, so a Mark that lasts six seconds is a Mark that is gone at t=6 and there at t=5.99.
/// </summary>
public class StatusTests
{
    private static Status Make(string name, Status.Stacking stacking = Status.Stacking.Refresh, int maxStacks = 1,
                               float duration = 6f, float taken = 1f, float dealt = 1f, bool untargetable = false)
    {
        var s = ScriptableObject.CreateInstance<Status>();
        s.name = name; s.displayName = name; s.stacking = stacking; s.maxStacks = maxStacks;
        s.defaultDuration = duration; s.damageTakenPerStack = taken; s.damageDealtPerStack = dealt; s.untargetable = untargetable;
        return s;
    }

    [Test]
    public void AMarkIsThereUntilItsTimeAndGoneAfter()
    {
        var mark = Make("Mark", duration: 6f);
        var set = new StatusSet();
        set.Apply(mark, now: 10f);
        set.Tick(15.99f);
        Assert.That(set.Has(mark), Is.True);
        set.Tick(16f);
        Assert.That(set.Has(mark), Is.False);
    }

    [Test]
    public void RefreshRestartsTheClockAndNeverDoubles()
    {
        var mark = Make("Mark");
        var set = new StatusSet();
        set.Apply(mark, now: 0f, duration: 4f);
        set.Apply(mark, now: 3f, duration: 4f);
        Assert.That(set.Stacks(mark), Is.EqualTo(1));
        Assert.That(set.All.Count, Is.EqualTo(1));
        set.Tick(6.9f);
        Assert.That(set.Has(mark), Is.True, "the second application pushed the end out");
    }

    [Test]
    public void StacksClimbToTheCapAndTheMultiplierCompounds()
    {
        var bleed = Make("Bleed", Status.Stacking.Stack, maxStacks: 3, taken: 1.1f);
        var set = new StatusSet();
        set.Apply(bleed, 0f); set.Apply(bleed, 0f); set.Apply(bleed, 0f); set.Apply(bleed, 0f);
        Assert.That(set.Stacks(bleed), Is.EqualTo(3));
        Assert.That(set.DamageTakenMultiplier, Is.EqualTo(1.1f * 1.1f * 1.1f).Within(0.0001f));
    }

    [Test]
    public void IgnoreLeavesTheFirstApplicationAlone()
    {
        var stun = Make("Stun", Status.Stacking.Ignore);
        var set = new StatusSet();
        var first = set.Apply(stun, 0f, duration: 2f);
        var second = set.Apply(stun, 1f, duration: 10f);
        Assert.That(second, Is.Null);
        Assert.That(first.expiresAt, Is.EqualTo(2f));
    }

    [Test]
    public void AStatusWithNoDurationLastsUntilCleared()
    {
        var hidden = Make("Hidden", duration: 0f, untargetable: true);
        var set = new StatusSet();
        set.Apply(hidden, 0f);
        set.Tick(1e6f);
        Assert.That(set.Untargetable, Is.True);
        set.Clear();
        Assert.That(set.Untargetable, Is.False);
    }

    [Test]
    public void DealtAndTakenAreSeparateAxes()
    {
        var curse = Make("Curse", dealt: 0.8f);
        var mark = Make("Mark", taken: 1.1f);
        var set = new StatusSet();
        set.Apply(curse, 0f); set.Apply(mark, 0f);
        Assert.That(set.DamageDealtMultiplier, Is.EqualTo(0.8f).Within(0.0001f));
        Assert.That(set.DamageTakenMultiplier, Is.EqualTo(1.1f).Within(0.0001f));
    }

    [Test]
    public void ABurnTicksOnItsIntervalAndEveryStackCounts()
    {
        var burn = Make("Burn", Status.Stacking.Stack, maxStacks: 3, duration: 4f);
        burn.tickInterval = 1f; burn.tickPercentMaxHealthPerStack = 0.01f;
        var set = new StatusSet();
        set.Apply(burn, 0f); set.Apply(burn, 0f);
        var due = new System.Collections.Generic.List<StatusSet.Active>();
        set.CollectDue(0.5f, due);
        Assert.That(due, Is.Empty, "not yet");
        set.CollectDue(1f, due);
        Assert.That(due.Count, Is.EqualTo(1));
        Assert.That(due[0].stacks, Is.EqualTo(2));
        set.CollectDue(1.5f, due);
        Assert.That(due, Is.Empty, "the next tick is at 2");
        set.CollectDue(2f, due);
        Assert.That(due.Count, Is.EqualTo(1));
    }

    [Test]
    public void ATauntNamesItsSourceOnlyWhileItLasts()
    {
        var taunt = Make("Taunted", duration: 3f);
        taunt.tauntsToSource = true;
        var set = new StatusSet();
        Assert.That(set.TauntedBy, Is.Null);
        set.Apply(taunt, 0f, source: null);
        Assert.That(set.TauntedBy, Is.Null, "a taunt with no living source taunts nobody");
        set.Tick(3f);
        Assert.That(set.Has(taunt), Is.False);
    }

    [Test]
    public void RemovalIsAnnouncedOnceWhetherByTimeOrByHand()
    {
        var mark = Make("Mark", duration: 1f);
        var set = new StatusSet();
        int removed = 0;
        set.OnRemoved += _ => removed++;
        set.Apply(mark, 0f);
        set.Tick(2f);
        Assert.That(set.Remove(mark), Is.False, "already gone");
        Assert.That(removed, Is.EqualTo(1));
    }
}
