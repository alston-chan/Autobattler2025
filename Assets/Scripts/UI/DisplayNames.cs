using System.Text;

/// <summary>
/// Turning the names things have in the project into names to show a player.
///
/// Almost nothing in this game was named for the player. A hero is a scene object called
/// <c>Hero_Melee_KnightShield</c>, an enemy is <c>HumanPrefab(Clone)</c>, and an item's authored
/// name is its sprite's — <c>WarHammer</c>, <c>ArielDress [Paint] (Lower)</c>. Those are all real
/// names that real code needs; none of them should ever reach a card, a reward or a scoreboard.
///
/// One place to fix it, because the same unit is shown on its card, its bar and the scoreboard, and
/// a unit that reads three ways is three units as far as the player is concerned.
/// </summary>
public static class DisplayNames
{
    /// <summary>
    /// A unit's name: what it was given, else its data's, else its object name tidied up.
    /// </summary>
    public static string Unit(Entity unit)
    {
        if (unit == null) return "";
        if (!string.IsNullOrEmpty(unit.displayName)) return unit.displayName;
        if (unit.unitData != null && !string.IsNullOrEmpty(unit.unitData.unitName)) return unit.unitData.unitName;
        return Tidy(unit.name, unitName: true);
    }

    /// <summary>An item's name for a card or a reward: its authored name, spaced out and de-tagged.</summary>
    public static string Item(string authoredName) => Tidy(authoredName, unitName: false);

    /// <summary>
    /// The shared tidy: strip the debris Unity and HeroEditor leave on a name, then put the spaces
    /// back into the run-together words the asset pipeline produced.
    /// </summary>
    /// <param name="unitName">
    /// True for a unit, which also drops the words that say what kind of object it is rather than
    /// which unit it is: the <c>Hero_</c> / <c>Enemy_</c> prefix a scene uses to group them, and
    /// <c>Melee</c>, which says how the thing fights and is already obvious from looking at it.
    /// </param>
    private static string Tidy(string raw, bool unitName)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        string name = raw.Replace("(Clone)", " ")
                         .Replace("[Paint]", " ")   // HeroEditor's mark for a recolourable sprite
                         .Replace('_', ' ')
                         .Trim();

        // "HumanPrefab" is the asset's name for itself, not the thing's name.
        if (name.EndsWith("Prefab")) name = name.Substring(0, name.Length - "Prefab".Length);

        name = Space(name);

        if (unitName)
        {
            name = DropWord(name, "Hero");
            name = DropWord(name, "Enemy");
            name = DropWord(name, "Melee");
        }

        return Collapse(name);
    }

    /// <summary>
    /// Put a space where the capitals say a word began: "WarHammer" is two words, "2H" is not, and
    /// "SpearmanHelm1" ends in a number that wants air around it. A capital only starts a word when
    /// it follows a lowercase letter, or when it is the last capital of a run and a lowercase
    /// follows ("ABCWord"); that second rule is what keeps an acronym in one piece.
    /// </summary>
    private static string Space(string name)
    {
        var result = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0)
            {
                char previous = name[i - 1];
                bool startsWord =
                    (char.IsUpper(c) && char.IsLower(previous)) ||
                    (char.IsUpper(c) && char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1])) ||
                    (char.IsDigit(c) && char.IsLower(previous));
                if (startsWord && previous != ' ') result.Append(' ');
            }
            result.Append(c);
        }
        return result.ToString();
    }

    /// <summary>Remove one whole word, wherever it stands. Case-sensitive: "Melee" is the label, "melee" is prose.</summary>
    private static string DropWord(string name, string word)
    {
        var kept = new StringBuilder(name.Length);
        foreach (var part in name.Split(' '))
        {
            if (part == word) continue;
            if (kept.Length > 0) kept.Append(' ');
            kept.Append(part);
        }
        // Everything was noise — better the noisy name than a blank one.
        return kept.Length > 0 ? kept.ToString() : name;
    }

    private static string Collapse(string name)
    {
        var result = new StringBuilder(name.Length);
        bool lastWasSpace = true;   // leading spaces are dropped
        foreach (var c in name)
        {
            bool space = c == ' ';
            if (space && lastWasSpace) continue;
            result.Append(c);
            lastWasSpace = space;
        }
        return result.ToString().TrimEnd();
    }
}
