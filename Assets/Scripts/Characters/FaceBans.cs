using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Face parts a random character must never roll. HeroEditor's sprite collection mixes emoji,
/// expression and undead faces in with the ordinary ones, and <see cref="Appearance"/> used to pick
/// uniformly from all of it. The list is chosen by eye on the Face Part Bans page (every option
/// rendered on one head) and lives in <c>Resources/FaceBans.json</c> as sprite collection ids.
/// </summary>
[Serializable]
public class FaceBans
{
    public List<string> Hair = new List<string>();
    public List<string> Eyebrows = new List<string>();
    public List<string> Eyes = new List<string>();
    public List<string> Mouth = new List<string>();
    public List<string> Beard = new List<string>();

    private static FaceBans _active;

    public static FaceBans Active => _active ??= Load();

    public static FaceBans Load()
    {
        var text = Resources.Load<TextAsset>("FaceBans");
        return text == null ? new FaceBans() : JsonUtility.FromJson<FaceBans>(text.text) ?? new FaceBans();
    }

    /// <summary>
    /// A random option's id, skipping banned ones. If a category is banned entirely, the whole list is
    /// used rather than leaving a character with no eyes.
    /// </summary>
    public static string PickId<T>(IList<T> options, Func<T, string> id, ICollection<string> banned)
    {
        if (options == null || options.Count == 0) return null;
        var allowed = new List<T>();
        foreach (var o in options)
            if (banned == null || !banned.Contains(id(o))) allowed.Add(o);
        if (allowed.Count == 0) allowed.AddRange(options);
        return id(allowed[UnityEngine.Random.Range(0, allowed.Count)]);
    }
}
