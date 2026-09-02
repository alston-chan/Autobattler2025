using UnityEngine;

/// <summary>
/// A reusable arena play-area definition — shape plus where it sits and how big it is — shared by every map with the
/// same dimensions. Assign one per map in <see cref="BackgroundCycler"/>; multiple maps can reference
/// the same preset, and editing the preset updates them all. This is the "save the config as a
/// reusable config" layer over <see cref="ArenaBounds"/>.
/// </summary>
[CreateAssetMenu(menuName = "Data/Arena Bounds Preset", fileName = "ArenaBoundsPreset")]
public class ArenaBoundsPreset : ScriptableObject
{
    [Tooltip("Rectangle for flat arenas; Ellipse for round pits (inscribed in the box).")]
    public ArenaShape shape = ArenaShape.Rectangle;

    [Tooltip("Middle of this map's play area. Move the arena by moving this.")]
    public Vector2 center = new Vector2(0f, -0.9f);

    [Tooltip("Width and height. The height runs from the map's ground line up to the ceiling a " +
             "knockback can throw someone.")]
    public Vector2 size = new Vector2(17.6f, 6.2f);

    /// <summary>Push this preset into the global <see cref="ArenaBounds"/>.</summary>
    public void Apply() => ArenaBounds.SetBounds(center, size, shape);
}
