using NUnit.Framework;
using UnityEditor;

/// <summary>
/// A verb describes itself to the player — on a reward card, in the item panel, in the resonance
/// block — and the description begins with whom it picks. That used to fall back to the enum's own
/// name, so a card read "CurrentTarget → 1× weapon damage": a field name where a sentence belongs.
///
/// The drift guard matters more than the wording. A new <c>Who</c> value compiles, works, and reads
/// as its own identifier until someone happens to look at the right card.
/// </summary>
public class SelectorWordsTests
{
    [Test]
    public void EveryWayOfPickingHasWordsForIt()
    {
        foreach (var who in Selector.AllWho)
        {
            var selector = new Selector { who = who, status = null };
            string words = selector.Describe();

            Assert.That(words, Is.Not.Empty, who + " describes as nothing");
            Assert.That(words, Is.Not.EqualTo(who.ToString()),
                        who + " still describes as its own enum name");
            Assert.That(words, Does.Not.Match("[a-z][A-Z]"),
                        who + " describes as \"" + words + "\", which is an identifier, not words");
        }
    }

    [Test]
    public void TheCommonOnesReadAsASentenceBegins()
    {
        Assert.That(new Selector { who = Selector.Who.CurrentTarget }.Describe(), Is.EqualTo("your target"));
        Assert.That(new Selector { who = Selector.Who.FarthestEnemy }.Describe(), Is.EqualTo("the farthest enemy"));
        Assert.That(new Selector { who = Selector.Who.LowestHealthEnemy }.Describe(), Is.EqualTo("the weakest enemy"));
        Assert.That(new Selector { who = Selector.Who.Self }.Describe(), Is.EqualTo("you"));
    }

    [Test]
    public void NoShippedVerbDescribesItselfWithAnIdentifier()
    {
        // The whole point: what a player actually reads on the cards that exist today.
        foreach (var guid in AssetDatabase.FindAssets("t:CompositeSpell", new[] { "Assets/Data/Spells" }))
        {
            var spell = AssetDatabase.LoadAssetAtPath<CompositeSpell>(AssetDatabase.GUIDToAssetPath(guid));
            if (spell == null) continue;

            string reads = spell.Reads;
            foreach (var who in Selector.AllWho)
                Assert.That(reads, Does.Not.Contain(who.ToString()),
                            spell.name + " reads \"" + reads + "\"");
        }
    }
}
