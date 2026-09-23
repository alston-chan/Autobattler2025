using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Click any unit on the battlefield to read its stats, the way an autobattler lets you inspect a
/// piece mid-fight.
///
/// Deliberately NOT the equipment window. That window is a workshop — it owns a doll, a bag, a spell
/// row and an engrave button, it only exists for the company, and opening one mid-fight to answer
/// "why is that thing killing me" is far too much furniture. This is a read-only card: no controls,
/// no mutation, works on enemies, and closes the moment you click away.
///
/// Live while it's open, because the interesting questions are all mid-combat ones — how much health
/// is left, is that hero actually attacking faster now — and a card frozen at the moment of clicking
/// would answer none of them.
///
/// Built at runtime like <see cref="ResonancePanel"/>, so no prefab has to be authored for it.
/// </summary>
public class UnitInspector : MonoBehaviour
{
    [Tooltip("Fallback pick distance for units with no body collider, in world units. Units that " +
             "have one are picked by that collider instead, which is shaped to the unit.")]
    public float pickRadius = 1.1f;

    [Tooltip("How far above the body collider a click still counts, in world units. The collider is " +
             "an arrow hitbox that stops at the shoulders; this covers the head, which is a large " +
             "part of what the player is aiming at. A click here only wins if no unit's actual body " +
             "claims the point, so raising it can't steal clicks from the unit behind.")]
    public float headroom = UnitPicking.DefaultHeadroom;

    [Tooltip("Draw every unit's click area in play, to tune it by eye. Requires Gizmos to be " +
             "enabled in the Game view.")]
    public bool drawPickBoxes = false;

    [Tooltip("How far the cursor may travel between press and release and still count as a click " +
             "rather than a drag, in screen pixels. Dragging a unit into formation must not also " +
             "open its card.")]
    public float clickTolerance = 12f;

    [Tooltip("Seconds between text repaints. The bars follow every frame; only the numbers wait.")]
    public float refreshInterval = 0.1f;

    [Tooltip("How long after clicking a unit a second click still counts as a double-click, opening " +
             "that unit's equipment window.")]
    public float doubleClickSeconds = 0.35f;

    [Header("Card")]
    [Tooltip("The card's width. The height is whatever the unit's own card needs (LayoutCard) — this " +
             "y is only what it is built at before the first unit is painted into it.")]
    public Vector2 cardSize = new Vector2(330f, 640f);
    [Tooltip("Inset from the bottom-right corner of the canvas. Bottom-LEFT is taken by the avatar " +
             "strip and the centre by the equipment windows, so the card lives on the right.")]
    public Vector2 cardMargin = new Vector2(-24f, 24f);

    // Opaque. At 0.96 the board showed through as a ghost of whatever stood behind the card —
    // faint, but enough that a dark ability paragraph was being read over a moving character.
    private static readonly Color Backing = new Color(0.07f, 0.07f, 0.09f, 1f);
    private static readonly Color Rule = new Color(1f, 0.82f, 0.28f, 0.35f);

    /// <summary>A card shorter than this reads as a tooltip that failed rather than a small unit.</summary>
    private const float MinCardHeight = 220f;

    /// <summary>Five stat lines at the stat block's font.</summary>
    private const float StatsHeight = 110f;
    private static readonly Color Ally = new Color(1f, 0.82f, 0.28f, 1f);
    private static readonly Color Enemy = new Color(0.95f, 0.42f, 0.36f, 1f);
    private static readonly Color Muted = new Color(0.72f, 0.72f, 0.75f, 1f);
    private static readonly Color Trough = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color HealthAlly = new Color(0.36f, 0.78f, 0.42f, 1f);
    private static readonly Color HealthEnemy = new Color(0.85f, 0.34f, 0.31f, 1f);
    private static readonly Color ManaFill = new Color(0.36f, 0.6f, 0.95f, 1f);

    private Camera _camera;
    private Entity _selected;

    /// <summary>The unit whose card is open, or null.</summary>
    public Entity Selected => _selected;
    private Vector3 _pressPosition;
    private bool _pressed;
    private FormationDragger _dragger;
    private bool _lookedForDragger;
    private Entity _grabSelected;
    private float _nextRefresh;

    private GameObject _card;
    private TextMeshProUGUI _name;
    private TextMeshProUGUI _side;
    private RectTransform _healthFill;
    private TextMeshProUGUI _healthText;
    private GameObject _manaRow;
    private TextMeshProUGUI _manaText;

    // How the unit fights, as words: its stance, whom it goes for, how long it stays on it. Read-only,
    // because these come from its gear (a tactics item) or its authoring (a kit), never from a click.
    private TextMeshProUGUI _tactics;

    // The other control: which of the hero's spell slots it casts. A book and the weapon's verb
    // both sit in the slots and only one is cast; the workshop can pick a book, but the verb has no
    // item to click, so the switch lives here. Setup only.
    private GameObject _slotRow;
    private readonly Image[] _slotBacks = new Image[Entity.MaxSpellSlots];
    private readonly TextMeshProUGUI[] _slotLabels = new TextMeshProUGUI[Entity.MaxSpellSlots];
    private readonly Button[] _slotButtons = new Button[Entity.MaxSpellSlots];
    private RectTransform _manaFill;
    private TextMeshProUGUI _statKeys;
    private TextMeshProUGUI _statValues;
    private TextMeshProUGUI _kit;

    // The card is re-stacked for every unit it describes, so these are kept rather than positioned
    // once: a unit with no mana, no ability and no engravings is a much shorter card than a hero.
    private RectTransform _rule;
    private RectTransform _healthRow;

    private SpriteRenderer _ring;

    /// <summary>Running Y position while the card is laid out, in canvas units below its top edge.</summary>
    private float _cursor;

    private Entity _lastClicked;
    private float _lastClickTime;

    /// <summary>Build the card under <paramref name="canvas"/>. Starts hidden.</summary>
    public void Initialize(Transform canvas)
    {
        _camera = Camera.main;
        if (canvas == null) return;

        BuildCard(canvas);
        BuildRing();
        Select(null);

        // A unit that dies or despawns takes its card with it — the alternative is a card describing
        // something no longer on the board.
        EntityRegistry.OnUnregistered += HandleUnregistered;
    }

    private void OnDestroy()
    {
        if (_dragger != null) _dragger.OnGrabbed -= Grabbed;
        EntityRegistry.OnUnregistered -= HandleUnregistered;
    }

    private void HandleUnregistered(Entity entity)
    {
        if (entity == _selected) Select(null);
    }

    private void Update()
    {
        if (_card == null) return;

        if (Input.GetKeyDown(KeyCode.Escape)) Dismiss();

        if (!_lookedForDragger)
        {
            _lookedForDragger = true;
            _dragger = FindObjectOfType<FormationDragger>();
            if (_dragger != null) _dragger.OnGrabbed += Grabbed;
        }

        // Press and release are tracked separately so a formation drag doesn't also open a card.
        // A press that STARTS over the UI is ignored outright, which is what lets an item be dragged
        // out of an open window and released over the board without that reading as "click away".
        if (Input.GetMouseButtonDown(0))
        {
            _pressed = !IsPointerOverUI();
            _pressPosition = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(0) && _pressed)
        {
            _pressed = false;
            if (Vector3.Distance(Input.mousePosition, _pressPosition) > clickTolerance)
            {
                _grabSelected = null;        // a real drag: no click follows, so nothing to swallow
                return;
            }

            HandleClick(UnitUnderCursor());
        }
    }

    /// <summary>
    /// One click inspects, two open the equipment window, and a click on empty ground puts
    /// everything away.
    ///
    /// Opening goes through the same toggle the number keys use, which closes whatever else was
    /// showing — so double-clicking a second hero moves straight to them rather than making the
    /// player close one window before opening the next.
    /// </summary>
    private void HandleClick(Entity unit)
    {
        if (unit == null)
        {
            // Clicking the empty board means "I'm done", so it clears everything at once rather than
            // peeling off one layer per click — Escape is the one that steps back gradually. It costs
            // nothing to get wrong either: all of it is a click away from coming back.
            var manager = GameManager.Instance;
            if (manager != null) manager.CloseCharacterInventories();
            if (_selected != null) Select(null);

            _lastClicked = null;
            return;
        }

        // Picking the unit up already selected it (Grabbed). The release of a drag that never moved
        // arrives here as a click, and must not read as "click the selected unit again" — that
        // would put the card away the moment it came out. It still counts as a first click, so a
        // second one opens the equipment. The note is consumed here, never on mouse-down: the
        // dragger's Update may run first in the same frame, and a mouse-down reset then wiped the
        // note the grab had just written.
        bool swallow = unit == _grabSelected;
        _grabSelected = null;
        if (swallow)
        {
            _lastClicked = unit;
            _lastClickTime = Time.unscaledTime;
            return;
        }

        bool again = unit == _lastClicked &&
                     Time.unscaledTime - _lastClickTime <= doubleClickSeconds;

        _lastClicked = unit;
        _lastClickTime = Time.unscaledTime;

        if (again)
        {
            OpenEquipment(unit);
            // Consume the pairing so a third click starts a fresh one rather than toggling madly.
            _lastClicked = null;
            return;
        }

        Select(unit);
    }

    /// <summary>Open a unit's equipment window. Enemies have none, so they simply stay inspected.</summary>
    private void OpenEquipment(Entity unit)
    {
        var manager = GameManager.Instance;
        if (manager == null || unit.characterInventory == null) return;

        manager.ToggleCharacterInventories(unit.characterInventory);
    }

    /// <summary>
    /// Put away one layer of UI: the equipment window first, then the inspector card. Going in that
    /// order means Escape never rips away everything at once — the heavier thing goes first, and the
    /// card the player is reading survives to the second press.
    /// </summary>
    private void Dismiss()
    {
        var manager = GameManager.Instance;
        if (manager != null && manager.AnyCharacterInventoryOpen)
        {
            manager.CloseCharacterInventories();
            return;
        }

        if (_selected != null) Select(null);
    }

    private void LateUpdate()
    {
        if (_selected == null) return;

        // A dead unit's card would go on reporting stats for a corpse.
        if (_selected.isDead)
        {
            Select(null);
            return;
        }

        FollowBars();
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + refreshInterval;
        Repaint();
    }

    private static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    /// <summary>Nearest living unit to the cursor, or null if the click landed on empty ground.</summary>
    private Entity UnitUnderCursor()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null) return null;

        Vector3 mouse = _camera.ScreenToWorldPoint(Input.mousePosition);
        mouse.z = 0f;
        return UnitAt(mouse);
    }

    /// <summary>
    /// The living unit at a world point, front-most first — see <see cref="UnitPicking"/> for how a
    /// click is matched to a unit. Split out from the cursor lookup so the hit-test can be exercised
    /// without synthesising mouse input.
    ///
    /// Any unit on the board may be inspected, enemies included, so this searches the whole registry
    /// rather than the company.
    /// </summary>
    public Entity UnitAt(Vector3 world)
    {
        Entity best = null;
        PickHit bestHit = PickHit.None;

        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var unit = all[i];
            if (unit == null || unit.isDead || !unit.gameObject.activeInHierarchy) continue;

            var hit = UnitPicking.Hit(unit, world, headroom, pickRadius);
            if (hit == PickHit.None) continue;
            if (!UnitPicking.Beats(hit, unit, bestHit, best)) continue;

            best = unit;
            bestHit = hit;
        }

        return best;
    }

    /// <summary>
    /// Draw every unit's click area, so the thing being tuned can be seen rather than inferred from
    /// mis-clicks. Solid where a click lands on the body, faint over the headroom that only counts
    /// when no body claims the point. Gizmos must be enabled in the Game view to see this in play.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!drawPickBoxes || !Application.isPlaying) return;

        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var unit = all[i];
            if (unit == null || unit.isDead || !unit.gameObject.activeInHierarchy) continue;

            if (!UnitPicking.TryGetBoxes(unit, headroom, out var core, out var full))
            {
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
                Gizmos.DrawWireSphere(unit.transform.position + Vector3.up, pickRadius);
                continue;
            }

            Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireCube(core.center, new Vector3(core.size.x, core.size.y, 0f));

            Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.28f);
            Gizmos.DrawWireCube(full.center, new Vector3(full.size.x, full.size.y, 0f));
        }
    }

    /// <summary>Picking a unit up is looking at it: the card and the opener follow the hand.</summary>
    private void Grabbed(Entity unit)
    {
        if (unit == null || unit == _selected) return;
        Select(unit);
        _grabSelected = unit;
    }

    private void Select(Entity unit)
    {
        // Clicking the selected unit again closes the card, so dismissing it never needs a hunt for
        // empty ground.
        if (unit == _selected) unit = null;

        _selected = unit;
        _card.SetActive(unit != null);
        if (_ring != null) _ring.gameObject.SetActive(unit != null);

        if (unit == null) return;

        _nextRefresh = 0f;
        StandClearOf(unit);
        Repaint();
        FollowBars();
    }

    /// <summary>
    /// Put the card on the opposite side of the screen from the unit it is about. Clicking an enemy
    /// to ask what it does, and having the answer land on top of the enemy's own back rank, is the
    /// one thing a read-only card must not do. Bottom corners both, because the avatar strip is
    /// hidden while the board is up and the middle belongs to the equipment windows.
    /// </summary>
    private void StandClearOf(Entity unit)
    {
        if (_card == null) return;
        var rect = _card.GetComponent<RectTransform>();
        if (rect == null) return;

        bool right = true;
        if (_camera != null && unit != null)
        {
            var viewport = _camera.WorldToViewportPoint(unit.transform.position);
            right = viewport.x <= 0.55f;   // a unit left of centre leaves the right corner free
        }

        var corner = new Vector2(right ? 1f : 0f, 0f);
        rect.anchorMin = rect.anchorMax = rect.pivot = corner;
        rect.anchoredPosition = new Vector2(right ? cardMargin.x : -cardMargin.x, cardMargin.y);
    }

    #region Painting

    /// <summary>Per-frame work: the ring tracks a moving unit and the bars track live values.</summary>
    private void FollowBars()
    {
        var health = _selected.Health;
        if (health != null && _healthFill != null)
            _healthFill.anchorMax = new Vector2(Fraction(health.currentHealth, MaxHealthOf(_selected)), 1f);

        var mana = _selected.Mana;
        if (mana != null && _manaFill != null)
        {
            _manaFill.anchorMax = new Vector2(Mathf.Clamp01(mana.Normalized), 1f);
            if (_manaText != null)
                _manaText.text = $"{Mathf.FloorToInt(mana.currentMana)} / {Mathf.CeilToInt(mana.maxMana)}";
        }

        if (_ring != null)
            _ring.transform.position = _selected.transform.position + Vector3.up * 0.06f;
    }

    private static float Fraction(float value, float max) =>
        max <= 0f ? 0f : Mathf.Clamp01(value / max);

    private static float MaxHealthOf(Entity unit) =>
        unit.Stats != null ? unit.Stats.MaxHealth.Value : unit.maxHealth;

    private void Repaint()
    {
        bool ally = _selected.isTeam;
        Color accent = ally ? Ally : Enemy;

        _name.text = DisplayName(_selected);
        _name.color = accent;
        _side.text = ally ? "Company" : "Enemy";

        if (_ring != null) _ring.color = new Color(accent.r, accent.g, accent.b, 0.55f);

        var health = _selected.Health;
        float max = MaxHealthOf(_selected);
        _healthText.text = health != null
            ? $"{Mathf.CeilToInt(health.currentHealth)} / {Mathf.CeilToInt(max)}"
            : "—";
        _healthFill.GetComponent<Image>().color = ally ? HealthAlly : HealthEnemy;

        _manaRow.SetActive(_selected.Mana != null);

        PaintTactics();
        PaintSlots();
        PaintStats();
        PaintKit();
        LayoutCard();
    }

    /// <summary>
    /// Stack the blocks that are actually showing, measure the two that vary, and shrink the card to
    /// what it holds. A fixed-height card is wrong in both directions at once: a hero with a long
    /// verb ran off the bottom edge, and an archer with no ability and no spell slots left a third of
    /// the card empty with a hole in the middle where the cast row would have been.
    /// </summary>
    private void LayoutCard()
    {
        const float pad = 14f, width = 28f;
        float inner = cardSize.x - width;

        _cursor = -pad;
        Stack(_name.rectTransform, 28f, 0f);
        Stack(_side.rectTransform, 18f, 6f);
        Stack(_rule, 2f, 8f);
        Stack(_healthRow, 18f, 4f);
        if (_manaRow.activeSelf) Stack((RectTransform)_manaRow.transform, 16f, 12f);
        Stack(_tactics.rectTransform, Measure(_tactics, inner, 20f), 6f);
        if (_slotRow.activeSelf) Stack((RectTransform)_slotRow.transform, 22f, 10f);

        float statsTop = _cursor;
        Stack(_statKeys.rectTransform, StatsHeight, 0f);
        _cursor = statsTop;
        Stack(_statValues.rectTransform, StatsHeight, 10f);

        Stack(_kit.rectTransform, Measure(_kit, inner, 0f), 0f);

        // _cursor is now the content's bottom edge, measured downward from the card's top.
        var rect = _card.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(cardSize.x, Mathf.Max(MinCardHeight, -_cursor + pad));
    }

    /// <summary>How tall a block of text actually is at the card's width, never less than a minimum.</summary>
    private static float Measure(TextMeshProUGUI text, float width, float minimum)
    {
        if (text == null || string.IsNullOrEmpty(text.text)) return minimum;
        return Mathf.Max(minimum, text.GetPreferredValues(text.text, width, 0f).y);
    }

    /// <summary>
    /// The tactics line: "Holds · the nearest · balanced", and which item says so. Shown for
    /// everyone, since what an enemy will do is worth as much as what a hero will.
    /// </summary>
    private void PaintTactics()
    {
        string line = Tactics.Line(_selected);
        var source = _selected.TacticsSource;
        _tactics.text = source != null ? line + "\n<color=#8A93A6>from " + source.DisplayName + "</color>" : line;
    }

    /// <summary>The cast row: one button per filled slot, the active one lit. Company only.</summary>
    private void PaintSlots()
    {
        bool mine = _selected.isTeam && _selected.isCharacter;
        var slots = _selected.spellSlots;
        int count = mine && slots != null ? Mathf.Min(slots.Count, Entity.MaxSpellSlots) : 0;
        _slotRow.SetActive(count > 0);
        if (count == 0) return;

        var game = GameManager.Instance;
        bool setup = game == null || game.StateMachine.Current == GameState.Setup;
        for (int i = 0; i < Entity.MaxSpellSlots; i++)
        {
            bool filled = i < count && slots[i] != null;
            _slotButtons[i].gameObject.SetActive(filled);
            if (!filled) continue;
            bool lit = i == _selected.activeSpellSlot;
            // The hand weapon's verb is marked; the rest are banked.
            var from = _selected.Resonance != null ? _selected.Resonance.WeaponTeaching(slots[i]) : null;
            bool inHand = from != null && _selected.HandWeapon != null && from.Id == _selected.HandWeapon.Id;
            _slotLabels[i].text = (inHand ? "• " : "") + slots[i].DisplayName;   // a bullet: the font has no sword
            _slotBacks[i].color = lit ? new Color(Ally.r, Ally.g, Ally.b, 0.85f) : Trough;
            _slotLabels[i].color = lit ? Backing : Muted;
            _slotButtons[i].interactable = setup;
        }
    }

    private void SetActiveSlot(int index)
    {
        if (_selected == null || !_selected.isTeam || _selected.spellSlots == null) return;
        if (index < 0 || index >= _selected.spellSlots.Count) return;
        var game = GameManager.Instance;
        if (game != null && game.StateMachine.Current != GameState.Setup) return;
        // The inventory owns the slots: picking through it keeps its highlight and CombatAI in step.
        if (_selected.characterInventory != null) _selected.characterInventory.SetActiveSlot(index);
        else { _selected.activeSpellSlot = index; if (_selected.CombatAI != null) _selected.CombatAI.RefreshSpells(); }
        Repaint();
    }

    private void PaintStats()
    {
        var stats = _selected.Stats;
        if (stats == null)
        {
            _statKeys.text = _statValues.text = "";
            return;
        }

        // Max Health is deliberately absent — the bar above already carries it, in more detail.
        var keys = new StringBuilder();
        var values = new StringBuilder();

        Line(keys, values, "Damage", stats.Damage.Value.ToString("0.##"));
        Line(keys, values, "Attacks / sec", stats.AttacksPerSecond.ToString("0.##"));
        Line(keys, values, "Move Speed", stats.Speed.Value.ToString("0.##"));
        // The rating, and what it means: "Armour 18" tells a player nothing, "15% less" tells them
        // why the knight is still standing. Magic resist only when something grants it.
        Line(keys, values, "Armour", stats.Armor.Value.ToString("0") + "  (" + (Mitigation.Fraction(stats.Armor.Value) * 100f).ToString("0") + "% less physical)");
        if (stats.MagicResist.Value > 0f)
            Line(keys, values, "Magic resist", stats.MagicResist.Value.ToString("0") + "  (" + (Mitigation.Fraction(stats.MagicResist.Value) * 100f).ToString("0") + "% less magical)");

        // Only when something grants it: a line that reads 0% on every unit is noise, and the whole
        // point of the item line is that it is rare.
        float resist = stats.KnockbackResistance != null ? stats.KnockbackResistance.Value : 0f;
        if (resist > 0f) Line(keys, values, "Knockback resist", (resist * 100f).ToString("0") + "%");

        _statKeys.text = keys.ToString();
        _statValues.text = values.ToString();
    }

    private static void Line(StringBuilder keys, StringBuilder values, string key, string value)
    {
        keys.AppendLine(key);
        values.AppendLine(value);
    }

    /// <summary>
    /// What this unit is carrying into the fight: the abilities it can cast and the engravings
    /// acting on it. Engravings are the reason two units with identical stat lines behave
    /// differently, so a card that listed only numbers would hide the most important thing about a
    /// unit.
    ///
    /// Every ability is listed, not just the slotted one. Enemies never fill a spell slot — theirs
    /// is rolled into the innate list at spawn — so reading only the active slot meant an enemy's
    /// card named nothing at all, directly above a mana bar the player could watch filling toward
    /// it. The cost is shown for the same reason: with both, a full bar beside "100 mana" tells the
    /// player what is about to happen while there is still time to answer it.
    ///
    /// The weapon basic attack is deliberately left out. It is the one spell that is not an ability
    /// (<see cref="Spell.IsAbility"/>), the silhouette already says whether a unit swings or shoots,
    /// and listing it would bury the line that varies under one that never does.
    /// </summary>
    private void PaintKit()
    {
        var text = new StringBuilder();

        // How the unit was deployed, in the player's words — the way to learn the words is to click
        // a unit and read them. Past tense once the fight is on: the stamp, not the position.
        var runManager = GameManager.Instance != null ? GameManager.Instance.runManager : null;
        if (runManager != null)
        {
            var board = BoardSnapshot.Capture(runManager.Formation, planned: false);
            string deployed = board.Keywords(_selected);
            if (!string.IsNullOrEmpty(deployed))
                text.Append("<color=#BFC6D4>Deployed</color>  ").Append(deployed).Append('\n');

            // Whom it charges. Before the bell, the prediction the opener line draws; during the
            // charge, the real target — the same answer, so a player can check the promise.
            var game = GameManager.Instance;
            if (game != null && game.StateMachine.Current == GameState.Setup)
            {
                var opener = BoardSnapshot.PredictOpening(board, _selected);
                if (opener != null)
                    text.Append("<color=#BFC6D4>Opens on</color>  ").Append(DisplayName(opener)).Append('\n');
            }
            else if (_selected.OpeningPending && _selected.CombatAI != null && _selected.CombatAI.CurrentTarget != null)
            {
                text.Append("<color=#BFC6D4>Charging</color>  ").Append(DisplayName(_selected.CombatAI.CurrentTarget)).Append('\n');
            }
        }

        foreach (var spell in _selected.CastableSpells())
        {
            if (spell == null || !spell.IsAbility) continue;

            // An ability the current weapon can't satisfy never fires — CombatAI skips it every
            // pass. Listing it unmarked is worse than not listing it at all: the player reads a
            // threat, or a plan, that the unit cannot carry out, and nothing on screen ever
            // contradicts them. Naming the weapon it wants also makes the fix obvious.
            bool inert = !spell.MeetsWeaponRequirement(_selected);

            text.Append("<color=#BFC6D4>Ability</color>  ").Append(spell.DisplayName);
            // A verb is held at the rarity of the weapon that taught it: say it.
            if (_selected.Resonance != null && _selected.Resonance.GrantedVerbs().Contains(spell))
                text.Append(' ').Append(Rarity.Tag(_selected.Resonance.TierOfVerb(spell)));
            if (spell.IsUltimate)
                text.Append("  <color=#5C9AF2>").Append(Mathf.RoundToInt(spell.manaCost))
                    .Append(" mana</color>");
            if (inert)
                text.Append("  <color=#C86A6A>needs ").Append(spell.weaponRequirement)
                    .Append("</color>");
            text.Append('\n');

            // What it does, in numbers, under its name — the ability is the one line on the card the
            // player can act on before the bell, and a name alone says nothing about damage or reach.
            // Decorated here rather than by the block pass below: the prose is wrapped in a muted
            // colour, and the glossary leaves coloured text alone. TMP keeps a colour stack, so a
            // keyword's colour inside the wrapper pops back to muted after it.
            string details = Keywords.Decorate(spell.FullDescriptionFor(_selected));
            if (!string.IsNullOrEmpty(details))
                text.Append("<size=12><color=#BFC6D4>").Append(details.Replace("\n", "  ·  ")).Append("</color></size>\n");
        }

        AppendEngravings(text);

        // The vocabulary coloured last, over the finished block: Keywords leaves our own coloured
        // labels alone, so one call does the ability's prose and the engraving lines together.
        _kit.text = text.Length > 0 ? Keywords.Decorate(text.ToString().TrimEnd('\n')) : "";
    }

    private void AppendEngravings(StringBuilder text)
    {
        var resonance = _selected.Resonance;
        if (resonance == null) return;

        // Worn engravings first — they leave when the item does, which is worth seeing separately
        // from the banked ones, which never leave.
        var inventory = _selected.characterInventory;
        if (inventory != null && inventory.Equipment != null)
        {
            foreach (var item in inventory.Equipment.Items)
            {
                var entry = resonance.EntryFor(item);
                if (entry == null || entry.engraving == null) continue;
                if (AlreadyListed(entry.engraving)) continue;
                Engraving(text, entry.engraving.DisplayName, resonance.TierFor(item), worn: true);
            }
        }

        if (resonance.banked == null) return;
        foreach (var mark in resonance.banked)
        {
            if (mark == null || mark.engraving == null) continue;
            if (AlreadyListed(mark.engraving)) continue;
            Engraving(text, mark.engraving.DisplayName, mark.tier, worn: false);
        }
    }

    /// <summary>
    /// Whether the ability block above already said this. A weapon's engraving IS its verb, so a
    /// hero holding Bull Rush read "Ability  Bull Rush I ..." and then, four lines later, "Bull Rush
    /// I" again — the same fact twice, the second time with nothing added. Only a verb can double
    /// up like this; every other engraving says something the ability list never does.
    /// </summary>
    private bool AlreadyListed(Engraving engraving)
    {
        if (!(engraving is GrantSpellEngraving verb) || verb.spell == null) return false;
        foreach (var spell in _selected.CastableSpells())
            if (spell == verb.spell && spell.IsAbility) return true;
        return false;
    }

    private static void Engraving(StringBuilder text, string name, int tier, bool worn)
    {
        text.Append("<color=#FFD147>").Append(name).Append("</color> ").Append(Rarity.Tag(tier))
            .Append(worn ? "" : "  <color=#8A8F99>banked</color>")
            .Append('\n');
    }

    /// <summary>
    /// The one name a unit is shown under, everywhere. The scoreboard used to trim "Hero_" and
    /// underscores for itself, so the same hero read as "Hero_Wand" on its card and "Wand" on its
    /// bar — two rules for one name, which is wrong whichever is prettier.
    /// </summary>
    public static string DisplayName(Entity unit) => DisplayNames.Unit(unit);

    #endregion

    #region Construction

    private void BuildCard(Transform canvas)
    {
        _card = NewRect("UnitInspectorCard", canvas, new Vector2(1f, 0f), cardSize, cardMargin);
        _card.GetComponent<RectTransform>().pivot = new Vector2(1f, 0f);

        UiLayer.Raise(_card, UiLayer.UnitCard);

        var backing = _card.AddComponent<Image>();
        backing.color = Backing;
        // Swallows clicks so dismissing the card by clicking it isn't possible — and, more usefully,
        // so a click meant for the card never falls through to select whatever unit is behind it.
        backing.raycastTarget = true;

        _cursor = -14f;

        _name = NewText("Name", _card.transform, 23f, Ally, TextAlignmentOptions.Left);
        Stack(_name.rectTransform, 28f, 0f);

        _side = NewText("Side", _card.transform, 14f, Muted, TextAlignmentOptions.Left);
        Stack(_side.rectTransform, 18f, 6f);

        var rule = NewRect("Rule", _card.transform, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        rule.AddComponent<Image>().color = Rule;
        _rule = rule.GetComponent<RectTransform>();
        Stack(_rule, 2f, 8f);

        _healthFill = BuildBar("Health", out _healthText, HealthAlly, 18f, 4f);
        _healthRow = (RectTransform)_healthFill.parent;
        _manaRow = BuildManaRow();
        _tactics = NewText("Tactics", _card.transform, 14f, Color.white, TextAlignmentOptions.Left);
        _tactics.enableWordWrapping = false;
        _tactics.overflowMode = TextOverflowModes.Ellipsis;
        _tactics.alignment = TextAlignmentOptions.TopLeft;
        Stack(_tactics.rectTransform, 36f, 6f);
        _slotRow = BuildSlotRow();

        // Keys and values are two full-width blocks sharing one row, left- and right-aligned, so the
        // numbers line up on the right edge without a layout group.
        float statsTop = _cursor;
        _statKeys = NewText("StatKeys", _card.transform, 16f, Muted, TextAlignmentOptions.TopLeft);
        Stack(_statKeys.rectTransform, StatsHeight, 0f);

        _cursor = statsTop;
        _statValues = NewText("StatValues", _card.transform, 16f, Color.white,
                              TextAlignmentOptions.TopRight);
        Stack(_statValues.rectTransform, StatsHeight, 10f);

        _kit = NewText("Kit", _card.transform, 15f, Color.white, TextAlignmentOptions.TopLeft);
        _kit.enableWordWrapping = true;
        Stack(_kit.rectTransform, 330f, 0f);
    }

    /// <summary>
    /// Place a full-width element with its TOP at the cursor, then move the cursor below it. Rects
    /// here are centre-pivoted, so the half-height offset is what actually puts the top where the
    /// cursor says — without it every block creeps upward into the one above.
    /// </summary>
    private void Stack(RectTransform rect, float height, float gap)
    {
        Anchor(rect, new Vector2(0.5f, 1f), new Vector2(cardSize.x - 28f, height),
               new Vector2(0f, _cursor - height * 0.5f));
        _cursor -= height + gap;
    }

    /// <summary>A labelled trough + fill, stacked below whatever came before it.</summary>
    private RectTransform BuildBar(string name, out TextMeshProUGUI label, Color fillColor,
                                   float height, float gap)
    {
        var trough = NewRect(name + "Bar", _card.transform, new Vector2(0.5f, 1f),
                             new Vector2(cardSize.x - 28f, height),
                             new Vector2(0f, _cursor - height * 0.5f));
        _cursor -= height + gap;
        var troughImage = trough.AddComponent<Image>();
        troughImage.color = Trough;
        troughImage.raycastTarget = false;

        var fill = NewRect(name + "Fill", trough.transform, new Vector2(0f, 0.5f), Vector2.zero,
                           Vector2.zero);
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = fillColor;
        fillImage.raycastTarget = false;

        label = NewText(name + "Text", trough.transform, 13f, Color.white, TextAlignmentOptions.Center);
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

        return fillRect;
    }

    /// <summary>Up to three buttons in one row, one per spell slot; labels are painted per unit.</summary>
    private GameObject BuildSlotRow()
    {
        const float height = 22f, gap = 4f;
        var row = NewRect("Cast", _card.transform, new Vector2(0.5f, 1f), new Vector2(cardSize.x - 28f, height), Vector2.zero);
        Stack(row.GetComponent<RectTransform>(), height, 10f);

        float width = (cardSize.x - 28f - gap * (Entity.MaxSpellSlots - 1)) / Entity.MaxSpellSlots;
        for (int i = 0; i < Entity.MaxSpellSlots; i++)
        {
            int index = i;
            float x = -(cardSize.x - 28f) * 0.5f + width * 0.5f + i * (width + gap);
            var cell = NewRect("Slot" + i, row.transform, new Vector2(0.5f, 0.5f), new Vector2(width, height), new Vector2(x, 0f));
            var back = cell.AddComponent<Image>();
            back.color = Trough;
            var button = cell.AddComponent<Button>();
            button.targetGraphic = back;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SetActiveSlot(index));

            var label = NewText("Label", cell.transform, 12f, Muted, TextAlignmentOptions.Center);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(width - 6f, height), Vector2.zero);

            _slotBacks[i] = back;
            _slotLabels[i] = label;
            _slotButtons[i] = button;
        }
        return row;
    }

    private GameObject BuildManaRow()
    {
        // The number matters now that the pool is the active verb's cost: "38 / 60" says how far
        // the next cast is, and which verb it is charging is on the ability line below.
        var fill = BuildBar("Mana", out _manaText, ManaFill, 16f, 12f);
        _manaText.fontSize = 12f;
        _manaFill = fill;
        return fill.parent.gameObject;
    }

    /// <summary>
    /// A ring on the ground under the selected unit. Without it the card names a unit the player then
    /// has to find again by eye — which in a 5v5 of similarly-dressed enemies is genuinely hard.
    /// Drawn procedurally so this needs no art asset.
    /// </summary>
    private void BuildRing()
    {
        var go = new GameObject("UnitInspectorRing");
        _ring = go.AddComponent<SpriteRenderer>();
        _ring.sprite = RingSprite();
        // Above the background (-1000) and the formation grid (-500), below the units themselves.
        _ring.sortingLayerName = "Default";
        _ring.sortingOrder = -400;
        // Flattened, so it reads as lying on the ground rather than standing up in a side view.
        go.transform.localScale = new Vector3(1.9f, 0.62f, 1f);
        go.SetActive(false);
    }

    public static Sprite RingSprite()
    {
        const int size = 128;
        const float outer = 0.47f;
        const float inner = 0.36f;
        const float feather = 0.02f;

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                // Soft on both edges so the ring doesn't alias into a jagged polygon.
                float alpha = Mathf.Clamp01((outer - distance) / feather) *
                              Mathf.Clamp01((distance - inner) / feather);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static GameObject NewRect(string name, Transform parent, Vector2 anchor, Vector2 size,
                                      Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Anchor(go.GetComponent<RectTransform>(), anchor, size, position);
        return go;
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, float size, Color color,
                                           TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    #endregion
}
