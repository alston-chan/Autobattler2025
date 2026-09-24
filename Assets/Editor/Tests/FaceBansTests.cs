using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The face ban list (<see cref="FaceBans"/>): a banned part is never rolled, a fully banned category
/// still gives the character a face, and every id in <c>Resources/FaceBans.json</c> names a real
/// sprite — a typo or a renamed vendor sprite would otherwise ban nothing, silently.
/// </summary>
public class FaceBansTests
{
    [Test]
    public void ABannedPartIsNeverRolled()
    {
        var options = new List<string> { "a", "b", "c", "d" };
        var banned = new HashSet<string> { "b", "d" };
        for (int n = 0; n < 500; n++)
            Assert.That(FaceBans.PickId(options, s => s, banned), Is.EqualTo("a").Or.EqualTo("c"));
    }

    [Test]
    public void AFullyBannedCategoryStillGivesAFace()
    {
        var options = new List<string> { "a", "b" };
        var banned = new HashSet<string> { "a", "b" };
        Assert.That(FaceBans.PickId(options, s => s, banned), Is.EqualTo("a").Or.EqualTo("b"));
    }

    [Test]
    public void EveryBannedIdNamesARealSprite()
    {
        var bans = FaceBans.Load();
        Assert.That(bans, Is.Not.Null, "Resources/FaceBans.json should parse");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HumanPrefab.prefab");
        var sc = prefab.GetComponent<Character>().SpriteCollection;
        var lists = new Dictionary<string, (List<string> banned, HashSet<string> real)>
        {
            { "Hair", (bans.Hair, new HashSet<string>(sc.Hair.Select(i => i.Id))) },
            { "Eyebrows", (bans.Eyebrows, new HashSet<string>(sc.Eyebrows.Select(i => i.Id))) },
            { "Eyes", (bans.Eyes, new HashSet<string>(sc.Eyes.Select(i => i.Id))) },
            { "Mouth", (bans.Mouth, new HashSet<string>(sc.Mouth.Select(i => i.Id))) },
            { "Beard", (bans.Beard, new HashSet<string>(sc.Beard.Select(i => i.Id))) },
        };
        foreach (var kv in lists)
        {
            var unknown = kv.Value.banned.Where(id => !kv.Value.real.Contains(id)).ToList();
            Assert.That(unknown, Is.Empty, $"FaceBans.json {kv.Key} names ids the sprite collection doesn't have");
        }
    }
}
