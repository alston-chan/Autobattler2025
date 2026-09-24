using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The curated hair and eye palette (<see cref="FaceColours"/>): every swatch in
/// <c>Resources/FaceColours.json</c> parses, a roll only ever lands on a listed colour, weights are
/// honoured, and an empty or broken list falls back instead of painting a hero black.
/// </summary>
public class FaceColoursTests
{
    [Test]
    public void EveryShippedSwatchParses()
    {
        var palette = FaceColours.Load();
        Assert.That(palette.Hair, Is.Not.Empty, "FaceColours.json should list hair colours");
        Assert.That(palette.Eyes, Is.Not.Empty, "FaceColours.json should list eye colours");
        foreach (var s in palette.Hair.Concat(palette.Eyes))
        {
            Assert.That(ColorUtility.TryParseHtmlString(s.hex, out _), Is.True, $"'{s.name}' has an unreadable hex '{s.hex}'");
            Assert.That(s.weight, Is.GreaterThan(0), $"'{s.name}' has weight {s.weight}, so it can never roll");
        }
    }

    [Test]
    public void ARollLandsOnlyOnListedColoursByWeight()
    {
        var swatches = new List<FaceColours.Swatch>
        {
            new FaceColours.Swatch { name = "Red", hex = "#ff0000", weight = 3 },
            new FaceColours.Swatch { name = "Blue", hex = "#0000ff", weight = 1 },
        };
        int red = 0, blue = 0;
        for (int n = 0; n < 4000; n++)
        {
            var c = FaceColours.Pick(swatches, Color.green);
            if (c == Color.red) red++;
            else if (c == Color.blue) blue++;
            else Assert.Fail($"rolled {c}, which isn't in the list");
        }
        Assert.That(red / (float)(red + blue), Is.InRange(0.70f, 0.80f), "a weight of 3 against 1 should roll about three times as often");
    }

    [Test]
    public void AnEmptyOrBrokenListFallsBack()
    {
        Assert.That(FaceColours.Pick(new List<FaceColours.Swatch>(), Color.green), Is.EqualTo(Color.green));
        Assert.That(FaceColours.Pick(null, Color.green), Is.EqualTo(Color.green));
        var broken = new List<FaceColours.Swatch> { new FaceColours.Swatch { name = "Typo", hex = "#zzzzzz", weight = 5 } };
        Assert.That(FaceColours.Pick(broken, Color.green), Is.EqualTo(Color.green));
    }
}
