using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The glossary: which words are coloured, which are left alone, and that decorating text twice
/// changes nothing (panels rebuild their labels constantly). Plus the drift guard — a status or a
/// verb that exists as an asset but not in the table would silently read as plain prose.
/// </summary>
public class KeywordsTests
{
    private static string Color(Keywords.Kind kind) => Keywords.ColorOf(kind);

    [Test]
    public void ACostAStatusAVerbAndAShoveAreEachColoured()
    {
        Assert.That(Keywords.Decorate("40 mana"), Is.EqualTo($"40 <color={Color(Keywords.Kind.Cost)}>mana</color>"));
        Assert.That(Keywords.Decorate("they are Tarred"), Does.Contain($"<color={Color(Keywords.Kind.Status)}>Tarred</color>"));
        Assert.That(Keywords.Decorate("cast Chain Whip"), Does.Contain($"<color={Color(Keywords.Kind.Verb)}>Chain Whip</color>"));
        Assert.That(Keywords.Decorate("and hurl it away at force 14"), Does.Contain($"<color={Color(Keywords.Kind.Physics)}>force</color>"));
        Assert.That(Keywords.Decorate("allies Beside you"), Does.Contain($"<color={Color(Keywords.Kind.Place)}>Beside</color>"));
    }

    [Test]
    public void DecoratingTwiceChangesNothing()
    {
        string once = Keywords.Decorate("Every enemy within 6 units is pulled toward you at force 10.");
        Assert.That(Keywords.Decorate(once), Is.EqualTo(once));
    }

    [Test]
    public void OurOwnTagsAndTheirContentsAreLeftAlone()
    {
        // A label the card already coloured must not be decorated again or nested.
        const string label = "<color=#BFC6D4>Ability</color>  Cannonball";
        string decorated = Keywords.Decorate(label);
        Assert.That(decorated, Does.StartWith("<color=#BFC6D4>Ability</color>"));
        Assert.That(decorated, Does.Contain($"<color={Color(Keywords.Kind.Verb)}>Cannonball</color>"));

        // Bold and size tags pass through untouched.
        Assert.That(Keywords.Decorate("<b>Roar</b>"), Is.EqualTo($"<b><color={Color(Keywords.Kind.Verb)}>Roar</color></b>"));
    }

    [Test]
    public void APhraseInsideAWordIsNotAKeyword()
    {
        Assert.That(Keywords.Decorate("Smokestack"), Is.EqualTo("Smokestack"));
        Assert.That(Keywords.Decorate("enforced"), Is.EqualTo("enforced"));
        Assert.That(Keywords.Decorate("reinforce"), Is.EqualTo("reinforce"));
    }

    [Test]
    public void ProperNounsNeedTheirCapitalButProseDoesNot()
    {
        Assert.That(Keywords.Decorate("the front rank"), Is.EqualTo("the front rank"), "lowercase prose is not the keyword");
        Assert.That(Keywords.Decorate("Front"), Does.Contain("<color="));
        Assert.That(Keywords.Decorate("Pull them in"), Does.Contain($"<color={Color(Keywords.Kind.Physics)}>Pull</color>"), "a sentence may open with a physics word");
    }

    [Test]
    public void NothingBreaksOnEmptyOrTaglessText()
    {
        Assert.That(Keywords.Decorate(null), Is.Null);
        Assert.That(Keywords.Decorate(""), Is.EqualTo(""));
        Assert.That(Keywords.Decorate("plain words"), Is.EqualTo("plain words"));
        Assert.That(Keywords.Decorate("an unclosed <tag"), Is.EqualTo("an unclosed <tag"));
    }

    [Test]
    public void EveryStatusAndVerbInTheProjectIsInTheGlossary()
    {
        var phrases = new HashSet<string>(Keywords.Phrases);

        foreach (var guid in AssetDatabase.FindAssets("t:Status", new[] { "Assets/Data/Statuses" }))
        {
            var status = AssetDatabase.LoadAssetAtPath<Status>(AssetDatabase.GUIDToAssetPath(guid));
            if (status == null) continue;
            Assert.That(phrases.Contains(status.DisplayName), Is.True,
                        status.DisplayName + " is a status nothing in the glossary colours");
        }

        foreach (var guid in AssetDatabase.FindAssets("t:CompositeSpell", new[] { "Assets/Data/Spells" }))
        {
            var spell = AssetDatabase.LoadAssetAtPath<CompositeSpell>(AssetDatabase.GUIDToAssetPath(guid));
            if (spell == null || spell.manaCost <= 0f) continue;   // basic attacks are not named in prose
            Assert.That(phrases.Contains(spell.DisplayName), Is.True,
                        spell.DisplayName + " is a verb nothing in the glossary colours");
        }
    }
}
