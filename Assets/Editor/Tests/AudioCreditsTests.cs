using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Every clip in the game is accounted for in <c>Assets/Audio/CREDITS.md</c>.
///
/// This is a licensing guard rather than a code one. Placeholder audio arrives in handfuls, from
/// stock libraries with terms that differ, and the moment a clip's origin is forgotten it has to be
/// cut — you cannot ship a sound you cannot prove you may ship. Provenance is trivial to record on
/// the day and impossible to reconstruct six months later, so the check is: the file exists, the
/// credits name it.
///
/// It fails on the commit that adds an uncredited clip, which is the only moment anyone still knows
/// where it came from.
/// </summary>
public class AudioCreditsTests
{
    private const string AudioFolder = "Assets/Audio";
    private const string Credits = AudioFolder + "/CREDITS.md";

    private static readonly string[] Extensions = { "*.wav", "*.mp3", "*.ogg", "*.aiff", "*.aif" };

    private static List<string> ClipsOnDisk()
    {
        var clips = new List<string>();
        if (!Directory.Exists(AudioFolder)) return clips;

        foreach (var pattern in Extensions)
            foreach (var path in Directory.GetFiles(AudioFolder, pattern, SearchOption.AllDirectories))
                clips.Add(Path.GetFileName(path));

        return clips;
    }

    [Test]
    public void EveryClipInTheGameSaysWhereItCameFrom()
    {
        var clips = ClipsOnDisk();
        if (clips.Count == 0) Assert.Pass("no audio yet");

        Assert.That(File.Exists(Credits), Is.True, Credits + " is missing and there are clips to credit");
        string credits = File.ReadAllText(Credits);

        foreach (var clip in clips)
            Assert.That(credits, Does.Contain(clip),
                        clip + " is in the project and not in CREDITS.md — record where it came from " +
                        "now, while someone still knows");
    }

    [Test]
    public void TheCreditsDoNotNameClipsThatAreGone()
    {
        // The other direction: a row for a deleted file makes the list look maintained when it is
        // not, and the next person trusts it.
        if (!File.Exists(Credits)) Assert.Pass("no credits file yet");

        var onDisk = new HashSet<string>(ClipsOnDisk());
        foreach (var line in File.ReadAllLines(Credits))
        {
            foreach (var ext in new[] { ".wav", ".mp3", ".ogg", ".aiff", ".aif" })
            {
                int at = line.IndexOf(ext, System.StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;

                // The filename is whatever sits between the backticks around it.
                int open = line.LastIndexOf('`', at);
                int close = line.IndexOf('`', at);
                if (open < 0 || close < 0) continue;

                string named = line.Substring(open + 1, close - open - 1);
                Assert.That(onDisk, Has.Member(named), "CREDITS.md credits " + named + ", which is not in the project");
            }
        }
    }
}
