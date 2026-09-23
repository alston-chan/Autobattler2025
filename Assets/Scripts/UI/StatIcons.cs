using UnityEngine;

/// <summary>
/// The icon for each stat, as a TFT or League stat block shows them: an icon and a number, the name
/// on hover (<see cref="StatGrid"/>). There is no stat icon art in the project, so these are item
/// icons from the same packs — a potion for health, a sword for damage, a shield for armour — which
/// keeps them in the game's style. Loaded from Resources so the grid needs no wiring.
/// </summary>
[CreateAssetMenu(menuName = "Data/Stat Icons", fileName = "StatIcons")]
public class StatIcons : ScriptableObject
{
    public Sprite health;
    [Tooltip("Tinted by manaTint — a white potion made blue.")]
    public Sprite mana;
    public Color manaTint = new Color(0.4f, 0.62f, 1f, 1f);
    public Sprite damage;
    public Sprite attackSpeed;
    public Sprite armor;
    public Sprite magicResist;
    public Sprite moveSpeed;
    public Sprite range;
    public Sprite knockbackResist;

    [Tooltip("The rounded tile behind every icon, so a thin sword and a round shield weigh the same.")]
    public Sprite tile;
    public Color tileColor = new Color(0f, 0f, 0f, 0.45f);

    private static StatIcons _active;

    /// <summary>The project's icon set, or null if the asset is missing (the grid then shows names).</summary>
    public static StatIcons Active
    {
        get
        {
            if (_active == null) _active = Resources.Load<StatIcons>("StatIcons");
            return _active;
        }
    }
}
