using UnityEngine;

/// <summary>
/// One asset holding every combat-feel knob, so juice can be A/B tested from a single Inspector
/// instead of hunting across entities and prefabs.
///
/// Why a ScriptableObject: <b>edits made during Play mode persist</b>, unlike changes to scene
/// components. That makes it the right home for tuning values you want to iterate on while watching
/// a fight.
///
/// Auto-loaded from <c>Resources/CombatFeelSettings.asset</c> — no wiring needed anywhere.
/// </summary>
[CreateAssetMenu(menuName = "Data/Combat Feel Settings", fileName = "CombatFeelSettings")]
public class CombatFeelSettings : ScriptableObject
{
    [Header("Diagnostics")]
    [Tooltip("Turn hitstop off across the whole game. Hitstop deliberately freezes units for a " +
             "fraction of a second on impact, which is very hard to tell apart from the game " +
             "stuttering — so when frames feel wrong, turn this off first and see whether the " +
             "problem goes with it. A ScriptableObject, so it can be toggled during play.")]
    public bool enableHitstop = true;

    /// <summary>Whether the health/mana bars draw over the characters or behind them.</summary>
    public enum BarLayering { InFrontOfCharacters, BehindCharacters }

    private const string ResourcePath = "CombatFeelSettings";

    [Header("Character hit feedback")]
    public HitFeedback.Settings hitFeedback = new HitFeedback.Settings();

    [Header("Death")]
    public DeathFeedback.Settings death = new DeathFeedback.Settings();

    [Header("Damage numbers")]
    public DamageNumbersManager.Settings damageNumbers = new DamageNumbersManager.Settings();

    [Header("Ability feedback")]
    public AbilityFeedback.Settings abilityFeedback = new AbilityFeedback.Settings();

    [Header("Bars")]
    [Tooltip("InFront: bars draw over the characters (on the UI layer).\n" +
             "Behind: characters draw over the bars. Flip this and press R to compare.")]
    public BarLayering barLayering = BarLayering.InFrontOfCharacters;

    [Header("Health bar")]
    public ResourceBar.BarEffects healthBar = new ResourceBar.BarEffects();

    [Header("Mana bar")]
    [Tooltip("Mana rises steadily and only drops on a cast, so it usually wants calmer settings " +
             "than health — no flash, no shake.")]
    public ResourceBar.BarEffects manaBar = new ResourceBar.BarEffects
    {
        chipColor = new Color(0.65f, 0.85f, 1f, 1f),
        hitFlash = false,
        shake = false,
    };

    private static CombatFeelSettings _active;

    /// <summary>The global settings. Falls back to an in-memory default if the asset is missing.</summary>
    public static CombatFeelSettings Active
    {
        get
        {
            if (_active == null)
            {
                _active = Resources.Load<CombatFeelSettings>(ResourcePath);
                if (_active == null)
                {
                    _active = CreateInstance<CombatFeelSettings>();
                    Debug.LogWarning($"[CombatFeelSettings] No asset at Resources/{ResourcePath}. " +
                                     "Using defaults — create one via Assets > Create > Data > Combat Feel Settings.");
                }
            }
            return _active;
        }
    }
}
