using NUnit.Framework;
using UnityEditor;

/// <summary>
/// Every spellbook teaches a spell that can say what it does. A book whose description is blank
/// shows the player a name and nothing else — which is what all of them showed before, and what a
/// spell added tomorrow would show again if nothing checked.
/// </summary>
public class SpellbookTests
{
    private static SpellbookDatabase Database()
    {
        var database = AssetDatabase.LoadAssetAtPath<SpellbookDatabase>("Assets/Resources/SpellbookDatabase.asset");
        Assert.That(database, Is.Not.Null, "no SpellbookDatabase under Resources");
        return database;
    }

    [Test]
    public void EverySpellbookTeachesADescribedSpell()
    {
        foreach (var entry in Database().entries)
        {
            if (entry == null) continue;
            Assert.That(entry.spell, Is.Not.Null, entry.itemId + " teaches nothing");
            Assert.That(entry.spell.description, Is.Not.Null.And.Not.Empty,
                        entry.spell.name + " has no description — the book would show only a name");
        }
    }

    [Test]
    public void DescriptionsQuoteTheirCosts()
    {
        // The cost is the one number every ability shares and the one a player plans around; a
        // description that leaves it out is describing a different ability.
        foreach (var entry in Database().entries)
        {
            if (entry == null || entry.spell == null || !entry.spell.IsUltimate) continue;
            Assert.That(entry.spell.description.ToLowerInvariant(), Does.Contain("mana"),
                        entry.spell.name + "'s description does not say what it costs");
        }
    }

    [Test]
    public void AWeaponAttackNeedNotExplainItself()
    {
        var swing = AssetDatabase.LoadAssetAtPath<Spell>("Assets/Data/Spells/DefaultMeleeAttack.asset");
        Assert.That(swing, Is.Not.Null);
        Assert.That(string.IsNullOrEmpty(swing.description), Is.True, "basic attacks are not spellbooks");
    }
}
