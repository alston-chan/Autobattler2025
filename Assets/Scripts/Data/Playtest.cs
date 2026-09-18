using UnityEngine;

/// <summary>
/// Which scenario is in force. A single asset in Resources, so the choice survives a domain reload
/// and a scene restart, and lives nowhere the scene or the run would pick up.
/// </summary>
[CreateAssetMenu(menuName = "Data/Playtest", fileName = "Playtest")]
public class Playtest : ScriptableObject
{
    [Tooltip("The scenario applied at the start of play. None: the game as authored.")]
    public PlaytestScenario active;

    private static Playtest _active;

    public static Playtest Active
    {
        get
        {
            if (_active == null) _active = Resources.Load<Playtest>("Playtest");
            return _active;
        }
    }

    /// <summary>The scenario in force, or null for the game as authored.</summary>
    public static PlaytestScenario Scenario => Active != null ? Active.active : null;
}
