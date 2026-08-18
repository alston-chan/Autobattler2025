using UnityEngine;

/// <summary>
/// Lets the player rearrange the company by dragging units onto grid cells before a fight.
///
/// Requires two things: the fight has not started — positioning is a decision made while the board is
/// still, and letting a unit be yanked around mid-fight would fight the AI for control of its
/// position — and the grid is on screen, so the cells a unit can be dropped into are actually visible
/// while it is being dragged.
///
/// Dropping onto an occupied cell swaps the two units rather than refusing, so the formation can be
/// reordered without first shuffling someone into an empty tile.
/// </summary>
public class FormationDragger : MonoBehaviour
{
    [Tooltip("Fallback grab distance for units with no body collider, in world units. Units that " +
             "have one are grabbed by that collider instead, which is shaped to the unit.")]
    public float grabRadius = UnitPicking.DefaultRadius;

    [Tooltip("How far above the body collider a grab still counts, in world units. The collider is " +
             "an arrow hitbox that stops at the shoulders; this covers the head.")]
    public float headroom = UnitPicking.DefaultHeadroom;
    [Tooltip("Height above the cursor the held unit floats, so it isn't hidden under the pointer.")]
    public float carryLift = 0.35f;

    private Entity _held;
    private Vector3 _heldOrigin;
    private Camera _camera;
    private BattleGridView _view;

    private void Awake() => _camera = Camera.main;

    private void Update()
    {
        var gm = GameManager.Instance;
        var grid = BattleGrid.Instance;
        if (gm == null || grid == null) return;

        // Combat owns unit positions; only rearrange between fights. Checked separately from the grid
        // below because the grid can be toggled back on mid-fight, and that must not re-open dragging.
        if (gm.isGameStarted || !CanArrange(grid))
        {
            if (_held != null) Drop(cancelled: true);
            return;
        }

        if (Input.GetMouseButtonDown(0)) TryGrab();
        else if (Input.GetMouseButton(0) && _held != null) Carry();
        else if (Input.GetMouseButtonUp(0) && _held != null) Drop(cancelled: false);
    }

    /// <summary>
    /// Units may only be moved while the grid is on screen (Tab). The cells are the only thing that
    /// shows where a unit can legally go, so dragging without them is aiming at invisible targets —
    /// and it makes a click on a unit unambiguous the rest of the time, leaving it to
    /// <see cref="UnitInspector"/>.
    ///
    /// A scene with no <see cref="BattleGridView"/> has no grid to show, so nothing is gated.
    /// </summary>
    private bool CanArrange(BattleGrid grid)
    {
        if (_view == null) _view = grid.GetComponent<BattleGridView>();
        return _view == null || _view.IsVisible;
    }

    private Vector3 MouseWorld()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return Vector3.zero;

        var world = _camera.ScreenToWorldPoint(Input.mousePosition);
        world.z = 0f;
        return world;
    }

    /// <summary>
    /// Pick up the company unit under the cursor — the same hit-test that decides which unit an
    /// inspect click lands on, so a unit can never be draggable at a spot where it isn't clickable.
    /// Only placed units are considered, so a grab can never catch an enemy.
    /// </summary>
    private void TryGrab()
    {
        var runManager = GameManager.Instance.runManager;
        if (runManager == null) return;

        Vector3 mouse = MouseWorld();
        Entity best = null;

        foreach (var pair in runManager.Formation.Placements)
        {
            var unit = pair.Key;
            if (unit == null || unit.isDead || !unit.gameObject.activeInHierarchy) continue;
            if (!UnitPicking.Covers(unit, mouse, headroom, grabRadius)) continue;
            if (UnitPicking.IsInFrontOf(unit, best)) best = unit;
        }

        if (best == null) return;

        _held = best;
        _heldOrigin = best.transform.position;
    }

    private void Carry()
    {
        _held.transform.position = MouseWorld() + Vector3.up * carryLift;
    }

    /// <summary>
    /// Put the unit down. A drop on the company's half snaps to the nearest cell; anything else —
    /// including the enemy half, which the company may never occupy — returns it where it came from.
    /// </summary>
    private void Drop(bool cancelled)
    {
        var grid = BattleGrid.Instance;
        var runManager = GameManager.Instance.runManager;
        Vector3 mouse = MouseWorld();

        if (!cancelled && grid != null && runManager != null && grid.IsOnAllySide(mouse))
        {
            grid.ClosestCell(true, mouse, out int column, out int row);
            runManager.Formation.Place(_held, column, row);
        }
        else
        {
            _held.transform.position = _heldOrigin;
        }

        _held = null;
    }
}
