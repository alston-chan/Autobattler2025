using UnityEngine;

/// <summary>
/// A reusable arena play-area definition — shape plus the min/max box — shared by every map with the
/// same dimensions. Assign one per map in <see cref="BackgroundCycler"/>; multiple maps can reference
/// the same preset, and editing the preset updates them all. This is the "save the config as a
/// reusable config" layer over <see cref="ArenaBounds"/>.
/// </summary>
[CreateAssetMenu(menuName = "Data/Arena Bounds Preset", fileName = "ArenaBoundsPreset")]
public class ArenaBoundsPreset : ScriptableObject
{
    [Tooltip("Rectangle for flat arenas; Ellipse for round pits (inscribed in the box).")]
    public ArenaShape shape = ArenaShape.Rectangle;
    public float minX = -8.8f;
    public float maxX = 8.8f;
    [Tooltip("Floor — the map's ground line.")]
    public float minY = -4f;
    [Tooltip("Ceiling — the highest a character can be knocked.")]
    public float maxY = 2.2f;

    /// <summary>Push this preset into the global <see cref="ArenaBounds"/>.</summary>
    public void Apply() => ArenaBounds.SetBounds(minX, maxX, minY, maxY, shape);
}
