using UnityEngine;

/// <summary>
/// The statuses the code itself needs to name — a shield puts Shielded on, a taunt is a Taunted
/// with a source — as one asset in Resources, so runtime systems reach them without an inspector
/// reference on every prefab. Items reference status assets directly; this is only for the words
/// the engine speaks on its own.
/// </summary>
[CreateAssetMenu(menuName = "Data/Status Library", fileName = "StatusLibrary")]
public class StatusLibrary : ScriptableObject
{
    public Status mark;
    public Status exposed;
    public Status shielded;
    public Status taunted;
    public Status burn;
    public Status poison;
    public Status rooted;

    private static StatusLibrary _active;
    public static StatusLibrary Active
    {
        get
        {
            if (_active == null) _active = Resources.Load<StatusLibrary>("StatusLibrary");
            return _active;
        }
    }

    public static Status Mark => Active != null ? Active.mark : null;
    public static Status Exposed => Active != null ? Active.exposed : null;
    public static Status Shielded => Active != null ? Active.shielded : null;
    public static Status Taunted => Active != null ? Active.taunted : null;
    public static Status Burn => Active != null ? Active.burn : null;
    public static Status Poison => Active != null ? Active.poison : null;
    public static Status Rooted => Active != null ? Active.rooted : null;
}
