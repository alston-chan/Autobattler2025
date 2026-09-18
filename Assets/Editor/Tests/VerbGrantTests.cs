using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// A verb survives its own tier-up. Resonance revokes and re-grants a grant whose tier changed, and
/// whatever an engraving asks of the hero while that happens has to answer for the state it is being
/// told about — the sixth cast of a fight used to leave the hero with no ability at all.
/// </summary>
public class VerbGrantTests
{
    /// <summary>A verb engraving that records what the hero's verb list looked like while it was told.</summary>
    private class Watching : GrantSpellEngraving
    {
        public int verbsSeenOnGrant = -1, verbsSeenOnRevoke = -1;
        public override void OnGranted(Entity owner, int tier) { verbsSeenOnGrant = owner.Resonance.GrantedVerbs().Count; base.OnGranted(owner, tier); }
        public override void OnRevoked(Entity owner, int tier) { verbsSeenOnRevoke = owner.Resonance.GrantedVerbs().Count; base.OnRevoked(owner, tier); }
    }

    private readonly List<Object> _made = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
        _made.Clear();
    }

    private Entity UnitWithResonance()
    {
        var go = new GameObject("unit"); _made.Add(go);
        var entity = go.AddComponent<Entity>();
        var resonance = go.AddComponent<Resonance>();
        resonance.Initialize(entity);
        // Awake does not run in edit mode; the property is what Awake would have set.
        typeof(Entity).GetProperty("Resonance").GetSetMethod(true).Invoke(entity, new object[] { resonance });
        return entity;
    }

    private Watching Verb()
    {
        var verb = ScriptableObject.CreateInstance<Watching>(); _made.Add(verb);
        var spell = ScriptableObject.CreateInstance<CompositeSpell>(); _made.Add(spell);
        spell.manaCost = 50f;
        verb.spell = spell;
        return verb;
    }

    [Test]
    public void AnEngravingIsToldAboutItsGrantAfterTheBooksHaveIt()
    {
        var unit = UnitWithResonance();
        var verb = Verb();
        unit.Resonance.banked.Add(new Resonance.Banked { engraving = verb, tier = 1 });

        unit.Resonance.Refresh();

        var instance = Instance(unit, verb);
        Assert.That(instance.verbsSeenOnGrant, Is.EqualTo(1), "OnGranted ran before the grant was in the books");
        Assert.That(unit.spellSlots, Has.Member(verb.spell));
    }

    [Test]
    public void ATierUpKeepsTheVerbInTheSlots()
    {
        var unit = UnitWithResonance();
        var verb = Verb();
        var mark = new Resonance.Banked { engraving = verb, tier = 1 };
        unit.Resonance.banked.Add(mark);
        unit.Resonance.Refresh();

        mark.tier = 2;
        unit.Resonance.Refresh();

        var instance = Instance(unit, verb);
        Assert.That(instance.verbsSeenOnRevoke, Is.EqualTo(0), "OnRevoked ran while the grant was still in the books");
        Assert.That(instance.verbsSeenOnGrant, Is.EqualTo(1));
        Assert.That(unit.spellSlots.FindAll(s => s == verb.spell).Count, Is.EqualTo(1), "the verb must be slotted exactly once after a tier-up");
        Assert.That(unit.Resonance.TierOfVerb(verb.spell), Is.EqualTo(2));
    }

    [Test]
    public void GrantsChangedFiresOnlyWhenSomethingMoved()
    {
        var unit = UnitWithResonance();
        var verb = Verb();
        int fired = 0;
        unit.Resonance.OnGrantsChanged += () => fired++;

        unit.Resonance.Refresh();
        Assert.That(fired, Is.EqualTo(0), "nothing worn, nothing banked, nothing to announce");

        unit.Resonance.banked.Add(new Resonance.Banked { engraving = verb, tier = 1 });
        unit.Resonance.Refresh();
        Assert.That(fired, Is.EqualTo(1));

        unit.Resonance.Refresh();
        Assert.That(fired, Is.EqualTo(1), "a refresh that changes nothing must not rebuild the slots");
    }

    [Test]
    public void TheSlotsAreOnlyRebuiltWhenTheVerbsDiffer()
    {
        var a = ScriptableObject.CreateInstance<CompositeSpell>(); _made.Add(a);
        var b = ScriptableObject.CreateInstance<CompositeSpell>(); _made.Add(b);
        Assert.That(CharacterInventory.SameVerbs(new List<Spell> { a, b }, new List<Spell> { b, a }), Is.True, "order is the player's, not the books'");
        Assert.That(CharacterInventory.SameVerbs(new List<Spell> { a }, new List<Spell> { a, b }), Is.False);
        Assert.That(CharacterInventory.SameVerbs(new List<Spell>(), new List<Spell>()), Is.True);
        Assert.That(CharacterInventory.SameVerbs(null, new List<Spell> { a }), Is.False);
    }

    /// <summary>The hero's private copy of the engraving, which is what Resonance actually calls.</summary>
    private static Watching Instance(Entity unit, Watching asset)
    {
        var instances = (Dictionary<string, Engraving>)typeof(Resonance)
            .GetField("_instances", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .GetValue(unit.Resonance);
        foreach (var kv in instances) if (kv.Value is Watching w && w.spell == asset.spell) return w;
        Assert.Fail("no instance of the verb engraving was made");
        return null;
    }
}
