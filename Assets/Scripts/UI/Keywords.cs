using System;
using System.Text;
using UnityEngine;

/// <summary>
/// The game's vocabulary, coloured so a card can be read at a glance: a cost, a status, a verb's
/// name, something that moves a body, a place on the board. One table, applied by every panel that
/// draws prose (<see cref="Decorate"/>), so a word means the same colour everywhere and a new
/// description is decorated without its author doing anything.
///
/// Rules that keep it from turning into a rainbow: five colours, no more; a phrase is decorated only
/// where it stands as a word; and text already inside a colour tag is left alone, which also makes
/// decorating twice a no-op (panels rebuild their text often).
/// </summary>
public static class Keywords
{
    public enum Kind
    {
        /// <summary>What a cast costs. The mana blue the bars and the card already use.</summary>
        Cost,
        /// <summary>A status a unit wears: Marked, Tarred, Rooted.</summary>
        Status,
        /// <summary>A verb's name, in the gold the company's own labels use.</summary>
        Verb,
        /// <summary>Something that moves a body — the game's whole idea, so it gets the loudest colour.</summary>
        Physics,
        /// <summary>A place on the board at the bell (Docs/PositionalKeywords.md).</summary>
        Place,
    }

    public static string ColorOf(Kind kind)
    {
        switch (kind)
        {
            case Kind.Cost: return "#5C9AF2";
            case Kind.Status: return "#C48BE6";
            case Kind.Verb: return "#F0C05A";
            case Kind.Physics: return "#E9864A";
            default: return "#63C3B0";
        }
    }

    /// <summary>A phrase and what it is. Case matters for the proper nouns; prose words match either way.</summary>
    private readonly struct Term
    {
        public readonly string Phrase; public readonly Kind Kind; public readonly bool CaseSensitive;
        public Term(string phrase, Kind kind, bool caseSensitive) { Phrase = phrase; Kind = kind; CaseSensitive = caseSensitive; }
    }

    // Proper nouns are case-sensitive so "front rank" in prose stays plain while the keyword Front is
    // coloured; ordinary words are not, because a sentence may start with one.
    private static readonly Term[] Table =
    {
        new Term("mana", Kind.Cost, false),

        new Term("Marked", Kind.Status, true),
        new Term("Mark", Kind.Status, true),
        new Term("Exposed", Kind.Status, true),
        new Term("Tarred", Kind.Status, true),
        new Term("Burning", Kind.Status, true),
        new Term("Burn", Kind.Status, true),
        new Term("Poisoned", Kind.Status, true),
        new Term("Poison", Kind.Status, true),
        new Term("Rooted", Kind.Status, true),
        new Term("Shielded", Kind.Status, true),
        new Term("Taunted", Kind.Status, true),
        new Term("Held Line", Kind.Status, true),

        new Term("Cannonball", Kind.Verb, true),
        new Term("Repulsion Nova", Kind.Verb, true),
        new Term("Singularity", Kind.Verb, true),
        new Term("Chain Whip", Kind.Verb, true),
        new Term("Bull Rush", Kind.Verb, true),
        new Term("Arrow Rain", Kind.Verb, true),
        new Term("Tar Pool", Kind.Verb, true),
        new Term("Ricochet", Kind.Verb, true),
        new Term("Whirl", Kind.Verb, true),
        new Term("Backstab", Kind.Verb, true),
        new Term("Fan of Knives", Kind.Verb, true),
        new Term("Shield Wall", Kind.Verb, true),
        new Term("Shadow Cannon", Kind.Verb, true),
        new Term("Roar", Kind.Verb, true),
        new Term("Smoke", Kind.Verb, true),
        new Term("Substitute", Kind.Verb, true),
        new Term("Shadowstep", Kind.Verb, true),

        new Term("knocked back", Kind.Physics, false),
        new Term("knocks back", Kind.Physics, false),
        new Term("knock back", Kind.Physics, false),
        new Term("knockback", Kind.Physics, false),
        new Term("pulled", Kind.Physics, false),
        new Term("pulls", Kind.Physics, false),
        new Term("pull", Kind.Physics, false),
        new Term("charges", Kind.Physics, false),
        new Term("charging", Kind.Physics, false),
        new Term("charge", Kind.Physics, false),
        new Term("thrown", Kind.Physics, false),
        new Term("throws", Kind.Physics, false),
        new Term("throw", Kind.Physics, false),
        new Term("flung", Kind.Physics, false),
        new Term("shoved", Kind.Physics, false),
        new Term("shoves", Kind.Physics, false),
        new Term("shove", Kind.Physics, false),
        new Term("slammed", Kind.Physics, false),
        new Term("slams", Kind.Physics, false),
        new Term("force", Kind.Physics, false),

        new Term("Beside", Kind.Place, true),
        new Term("Rank", Kind.Place, true),
        new Term("Covered", Kind.Place, true),
        new Term("Front", Kind.Place, true),
        new Term("Rear", Kind.Place, true),
        new Term("back line", Kind.Place, false),
    };

    private static Term[] _byLength;

    /// <summary>Longest first, so "knocked back" wins over "back line" where both could start.</summary>
    private static Term[] ByLength
    {
        get
        {
            if (_byLength != null) return _byLength;
            _byLength = (Term[])Table.Clone();
            Array.Sort(_byLength, (a, b) => b.Phrase.Length.CompareTo(a.Phrase.Length));
            return _byLength;
        }
    }

    /// <summary>Every phrase the glossary knows, for the test that keeps it in step with the assets.</summary>
    public static string[] Phrases
    {
        get { var all = new string[Table.Length]; for (int i = 0; i < Table.Length; i++) all[i] = Table[i].Phrase; return all; }
    }

    /// <summary>
    /// The same text with the vocabulary coloured. Rich-text tags pass through untouched, and
    /// anything already inside a colour tag is left as it is — so a panel may decorate its text
    /// every frame without the tags piling up.
    /// </summary>
    public static string Decorate(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = new StringBuilder(text.Length + 64);
        int colorDepth = 0;

        for (int i = 0; i < text.Length; )
        {
            char c = text[i];

            // A tag: copy it whole, and remember whether we are inside a colour.
            if (c == '<')
            {
                int close = text.IndexOf('>', i);
                if (close < 0) { result.Append(text, i, text.Length - i); break; }
                string tag = text.Substring(i, close - i + 1);
                if (tag.StartsWith("<color", StringComparison.OrdinalIgnoreCase)) colorDepth++;
                else if (tag.StartsWith("</color", StringComparison.OrdinalIgnoreCase)) colorDepth = Mathf.Max(0, colorDepth - 1);
                result.Append(tag);
                i = close + 1;
                continue;
            }

            if (colorDepth == 0 && StartsWord(text, i))
            {
                var match = Match(text, i);
                if (match.Phrase != null)
                {
                    result.Append("<color=").Append(ColorOf(match.Kind)).Append('>')
                          .Append(text, i, match.Phrase.Length)
                          .Append("</color>");
                    i += match.Phrase.Length;
                    continue;
                }
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    private static bool StartsWord(string text, int i) => i == 0 || !IsWordChar(text[i - 1]);
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '\'';

    private static Term Match(string text, int i)
    {
        var terms = ByLength;
        for (int t = 0; t < terms.Length; t++)
        {
            var term = terms[t];
            int len = term.Phrase.Length;
            if (i + len > text.Length) continue;
            var comparison = term.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (string.Compare(text, i, term.Phrase, 0, len, comparison) != 0) continue;
            if (i + len < text.Length && IsWordChar(text[i + len])) continue;   // a word, not a prefix
            return term;
        }
        return default;
    }
}
