using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the <see cref="BattleGrid"/> in the game view as translucent tiles, toggled with a key.
///
/// The grid's Scene-view gizmo only exists in the editor, so while actually playing the cells are
/// invisible and dragging a unit is guesswork. Tiles are built once as sprites and shown or hidden,
/// rather than drawn every frame, and they sort between the backdrop and the units so the grid reads
/// as markings on the ground rather than a pane in front of the fight.
/// </summary>
[RequireComponent(typeof(BattleGrid))]
public class BattleGridView : MonoBehaviour
{
    [Header("Toggle")]
    public KeyCode toggleKey = KeyCode.Tab;
    public bool visibleOnStart = false;

    [Header("Look")]
    public Color allyColor = new Color(0.35f, 0.75f, 1f, 0.20f);
    public Color enemyColor = new Color(1f, 0.42f, 0.36f, 0.16f);
    [Tooltip("Tile size as a fraction of the cell, leaving a gutter so cells read as separate tiles.")]
    [Range(0.5f, 1f)] public float tileFill = 0.9f;

    [Header("Sorting")]
    [Tooltip("Below the units (which start at 0) but above the backdrop (forced to -1000), so the " +
             "grid looks painted on the floor.")]
    public string sortingLayerName = "Default";
    public int sortingOrder = -500;

    private BattleGrid _grid;
    private GameObject _root;
    private readonly List<SpriteRenderer> _tiles = new List<SpriteRenderer>();
    private bool _visible;

    private static Sprite _tileSprite;

    private void Awake()
    {
        _grid = GetComponent<BattleGrid>();
        Build();
        SetVisible(visibleOnStart);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)) SetVisible(!_visible);
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_root != null) _root.SetActive(visible);
    }

    /// <summary>(Re)create one tile per cell. Call after changing the grid's shape.</summary>
    [ContextMenu("Rebuild Tiles")]
    public void Build()
    {
        if (_root != null) DestroyImmediate(_root);
        _tiles.Clear();

        if (_grid == null) _grid = GetComponent<BattleGrid>();
        if (_grid == null) return;

        _root = new GameObject("GridTiles");
        _root.transform.SetParent(transform, false);

        BuildSide(true, allyColor);
        BuildSide(false, enemyColor);
    }

    private void BuildSide(bool allySide, Color color)
    {
        for (int c = 0; c < _grid.columns; c++)
        {
            for (int r = 0; r < _grid.rows; r++)
            {
                var go = new GameObject($"Cell_{(allySide ? "A" : "E")}{c}{r}");
                go.transform.SetParent(_root.transform, false);
                go.transform.position = _grid.CellToWorld(allySide, c, r);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = TileSprite();
                sr.color = color;
                sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = sortingOrder;

                // The unit sprite for a cell stands on its centre, so the tile is drawn around it.
                go.transform.localScale = new Vector3(_grid.cellSize.x * tileFill,
                                                      _grid.cellSize.y * tileFill, 1f);

                _tiles.Add(sr);
            }
        }
    }

    /// <summary>A 1x1 white sprite, scaled per tile — avoids shipping an art asset for a flat quad.</summary>
    private static Sprite TileSprite()
    {
        if (_tileSprite != null) return _tileSprite;

        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        _tileSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        _tileSprite.name = "GridTile";
        return _tileSprite;
    }
}
