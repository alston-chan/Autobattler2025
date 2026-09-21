using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Which of the game's screen panels draws on top of which — and, first of all, that any of them
/// draws on top of the battlefield.
///
/// The scene's canvas is Screen Space - Camera on the <c>Default</c> sorting layer, and the unit
/// bars are deliberately on <c>UI</c> so they clear the characters' heads. That put every bar, and
/// the taller character sprites with them, in front of panels the player was trying to read: a unit
/// card with an archer standing in the middle of it, reward cards with the battle showing through.
///
/// A nested canvas with <c>overrideSorting</c> is the fix, and it needs nothing changed in the
/// scene. It also needs its own raycaster: overriding sorting takes the subtree out of the parent
/// canvas's raycast, so buttons under it stop answering clicks — which is how a reward panel ends
/// up looking right and being dead.
/// </summary>
public static class UiLayer
{
    /// <summary>The live scoreboard, which the player reads past rather than at.</summary>
    public const int Scoreboard = 300;
    /// <summary>A unit's card: above the board and its bars, below anything asking for a decision.</summary>
    public const int UnitCard = 500;
    /// <summary>The run map.</summary>
    public const int Map = 600;
    /// <summary>The spoils. It blocks the next fight, so nothing may cover it.</summary>
    public const int Reward = 700;
    /// <summary>The end of the run: the last thing anyone needs to see.</summary>
    public const int RunEnd = 800;

    /// <summary>
    /// Raise <paramref name="root"/> above the board at the given order. Idempotent, so a panel
    /// that is rebuilt does not stack components.
    /// </summary>
    public static void Raise(GameObject root, int order)
    {
        if (root == null) return;

        var canvas = root.GetComponent<Canvas>();
        if (canvas == null) canvas = root.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingLayerName = "UI";
        canvas.sortingOrder = order;

        if (root.GetComponent<GraphicRaycaster>() == null) root.AddComponent<GraphicRaycaster>();
    }
}
