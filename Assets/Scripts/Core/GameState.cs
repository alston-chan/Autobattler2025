/// <summary>
/// All possible phases the game can be in.
/// </summary>
public enum GameState
{
    /// <summary>Pre-combat setup: appearance, equipment, inventory.</summary>
    Setup,

    /// <summary>Combat is active — entities fight.</summary>
    Combat,

    /// <summary>All enemies or allies are dead — round is over.</summary>
    RoundEnd,
}
