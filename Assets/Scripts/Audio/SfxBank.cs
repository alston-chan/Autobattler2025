using System;
using UnityEngine;

/// <summary>
/// One sound in the game, in all the versions of it that exist. Drop four or six clips of the same
/// event in and it plays them round-robin with a little pitch jitter (see <see cref="SfxBag"/>).
///
/// <b>The gap is the important field.</b> Ten units fight at once, and a volley or a nova lands five
/// hits inside the same frame. Five copies of one clip starting together is not five hits — it is
/// one hit, six decibels louder, with a phasing artefact on top. <see cref="minGapSeconds"/> plays
/// the first and drops the rest, which is both cleaner and what the player hears anyway.
/// </summary>
[Serializable]
public class SfxBank
{
    [Tooltip("Variations of the same sound. Four to six; they are played in a shuffled rotation, " +
             "never twice in a row.")]
    public AudioClip[] clips;

    [Range(0f, 1f)]
    public float volume = 0.8f;

    [Tooltip("Random pitch spread, ±. A little (0.05) stops a repeated clip sounding mechanical; " +
             "a lot (0.2) sounds like a different object each time. 0 for anything tuned — a bell, " +
             "a fanfare.")]
    [Range(0f, 0.4f)]
    public float pitchJitter = 0.06f;

    [Tooltip("Shortest time between two plays of this bank. Anything asked for inside the gap is " +
             "dropped, not queued. Raise it for sounds that arrive in crowds (hits, impacts); " +
             "leave it near zero for sounds that cannot be missed (a death, the bell).")]
    public float minGapSeconds = 0.04f;

    // Not serialized: rotation and timing are play-session state, and a ScriptableObject that kept
    // them would carry one fight's leftovers into the next (and into the asset on disk).
    [NonSerialized] private SfxBag _bag;
    [NonSerialized] private float _lastPlayed = float.NegativeInfinity;

    public bool HasClips => clips != null && clips.Length > 0;

    /// <summary>Whether this bank is allowed to speak at <paramref name="now"/>.</summary>
    public bool Open(float now) => HasClips && now - _lastPlayed >= minGapSeconds;

    /// <summary>
    /// The clip to play now, or null when the bank is empty or still inside its gap. Taking a clip
    /// is what starts the gap — a caller that asks and discards would silence the next real hit.
    /// </summary>
    public AudioClip Take(float now)
    {
        if (!Open(now)) return null;

        _bag ??= new SfxBag();
        int i = _bag.Next(clips.Length);
        if (i < 0) return null;

        var clip = clips[i];
        if (clip == null) return null;      // a hole in the array, not a reason to start the gap

        _lastPlayed = now;
        return clip;
    }

    public float Pitch() => pitchJitter <= 0f ? 1f : 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
}
