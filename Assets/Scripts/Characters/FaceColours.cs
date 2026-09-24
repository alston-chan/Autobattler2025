using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The hair and eye colours a random character may roll. <see cref="Appearance"/> used to take any
/// RGB at all, which is where neon-green hair and near-black eyes came from. The palette lives in
/// <c>Resources/FaceColours.json</c> as hex swatches with a weight, so natural colours come up more
/// often than the anime ones. The renderer multiplies the sprite by the colour, so a swatch reads
/// darker on the sprite than it does as a hex.
/// </summary>
[Serializable]
public class FaceColours
{
    [Serializable]
    public class Swatch
    {
        public string name;
        public string hex;
        public int weight = 1;
    }

    public List<Swatch> Hair = new List<Swatch>();
    public List<Swatch> Eyes = new List<Swatch>();

    private static FaceColours _active;

    public static FaceColours Active => _active ??= Load();

    public static FaceColours Load()
    {
        var text = Resources.Load<TextAsset>("FaceColours");
        return text == null ? new FaceColours() : JsonUtility.FromJson<FaceColours>(text.text) ?? new FaceColours();
    }

    /// <summary>A swatch drawn by weight; <paramref name="fallback"/> when the list has nothing usable.</summary>
    public static Color Pick(IList<Swatch> swatches, Color fallback)
    {
        int total = 0;
        if (swatches != null)
            foreach (var s in swatches)
                if (s.weight > 0 && ColorUtility.TryParseHtmlString(s.hex, out _)) total += s.weight;
        if (total == 0) return fallback;

        int roll = UnityEngine.Random.Range(0, total);
        foreach (var s in swatches)
        {
            if (s.weight <= 0 || !ColorUtility.TryParseHtmlString(s.hex, out var c)) continue;
            if (roll < s.weight) return c;
            roll -= s.weight;
        }
        return fallback;
    }
}
