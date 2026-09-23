using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Elements;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using System.Linq;
using Kryz.CharacterStats;
using Random = UnityEngine.Random;

public partial class GameManager : Singleton<GameManager>
{
    public GameObject avatarUI;

    [Tooltip("Handed to UnitBarsManager at startup — bar appearance is tuned there.")]
    public GameObject healthBarsOrganizer;
    public GameObject resourceBarPrefab;

    [Header("Run")]
    [Tooltip("Drives the sequence of fights. Leave null to keep the scene's hand-placed units and " +
             "play a single round, as before.")]
    public RunManager runManager;

    [Header("Inventory")]
    public GameObject canvas;
    public GameObject audioSource;
    public GameObject PlayerInventory;
    public bool initializedPlayerInventory = false;
    public GameObject characterInventoryPrefab;
    [Tooltip("Plain HeroEditor character prefab (no gameplay scripts) used as the cosmetic body for " +
             "the equipment window's preview doll. Leave null to disable the preview.")]
    public GameObject previewBodyPrefab;

    [Header("UI sorting")]
    [Tooltip("Sorting layer for the main canvas. Characters sort on 'Default' up to ~405, so the " +
             "canvas must sit on a HIGHER sorting layer ('UI') to draw over them — raising the order " +
             "within 'Default' would just start another arms race.")]
    public string uiSortingLayer = "UI";
    [Tooltip("Sorting order within the layer above. Health/mana bars also live on 'UI' at low orders, " +
             "so keep this well above them for windows to cover the bars.")]
    public int uiSortingOrder = 100;

    [Header("Avatar portraits")]
    [Tooltip("Layer for the off-screen portrait stage. Excluded from the main camera automatically.")]
    public int avatarPortraitLayer = 8;
    public int avatarPortraitTextureSize = 256;
    [Tooltip("Orthographic size of each portrait camera — smaller crops tighter on the face.")]
    public float avatarPortraitCameraSize = 1.4f;
    [Tooltip("Camera centre relative to the head rig's origin.")]
    public Vector2 avatarPortraitCameraOffset = new Vector2(0f, 0f);
    [Tooltip("Portrait size as a fraction of the card's width.")]
    public float avatarPortraitFill = 0.9f;
    public List<Entity> allyCharacters = new List<Entity>();
    public List<CharacterInventory> characterInventories = new List<CharacterInventory>();

    /// <summary>The saved run being resumed this session, or null for a fresh one. Read once, before the company is dressed.</summary>
    private RunSnapshot _resume;

    // ── Game State ──
    public GameStateMachine StateMachine { get; private set; } = new GameStateMachine();

    /// <summary>Backward-compatible shorthand. True when combat is active.</summary>
    public bool isGameStarted => StateMachine.Current == GameState.Combat;

    /// <summary>
    /// Bring the game up in the one order that works.
    ///
    /// This was a flat list of nine calls whose order carried real constraints and said so only in
    /// comments attached to individual lines — "after the roster, because…", "last, so the company
    /// is fully built…". The constraints are named as stages here instead, because the failure mode
    /// when one is broken is silent: a stage that runs too early finds nothing to work on and does
    /// nothing at all. Hero notice badges were once built before the roster and simply produced
    /// none — no error, no badges, nothing to grep for.
    ///
    /// Each stage needs the one above it:
    ///
    /// <list type="number">
    /// <item><b>Listen</b> — before anything exists that could fire an event.</item>
    /// <item><b>World</b> — the arena units are clamped into, and the canvas everything is drawn
    /// on top of.</item>
    /// <item><b>Unit presentation</b> — bars and damage numbers hook entity registration, so they
    /// have to exist before units are dressed and long before any encounter is spawned.</item>
    /// <item><b>Company</b> — avatar cards, then the roster, then inventories and gear, then the
    /// badges that hang off a card belonging to a hero on the roster. Each reads what the step
    /// before it wrote; this is the run of the sequence that actually cannot be reordered.</item>
    /// <item><b>Player tools</b> — the inspector reads whatever is on the board, so it needs only
    /// the canvas and not the run.</item>
    /// <item><b>Run</b> — last, because the first encounter is staged against a finished company.</item>
    /// </list>
    ///
    /// The stages that can quietly do nothing now say so out loud instead.
    /// </summary>
    void Start()
    {
        ListenForGameEvents();
        BuildWorld();
        BuildUnitPresentation();
        BuildCompany();
        BuildPlayerTools();
        StartRun();
    }

    private void ListenForGameEvents()
    {
        StateMachine.OnStateChanged += HandleStateChanged;

        // Units that appear once a fight is under way — an encounter's enemies, anything summoned —
        // missed the transition that told everyone else, so they are told on arrival instead.
        EntityRegistry.OnRegistered += HandleEntityRegistered;

        // The win/lose check listens for deaths rather than being called by the dying unit.
        Entity.OnAnyDied += OnEntityDied;
    }

    private void BuildWorld()
    {
        EnsureArenaBounds();
        EnsureUiSortsAboveWorld();
    }

    /// <summary>
    /// Start over: a fresh scene, a fresh map, a fresh company. The one exit from a finished run,
    /// and the developer's reload key, so both leave the game in the same state.
    /// </summary>
    public void RestartRun()
    {
        // Statics survive a scene reload. The registry would otherwise hold stale entries, and the
        // telemetry would go on counting the last run's fights into the next one's table.
        EntityRegistry.Clear();
        CombatTelemetry.Reset();
        RunSave.Delete();                             // a new run, not the old one again
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void StartRun()
    {
        if (runManager == null) return;

        runManager.BeginRun(allyCharacters, _resume);
        runManager.SaveIfSafe();                     // the first safe point: a run exists

        var shop = gameObject.AddComponent<ShopPanel>();
        shop.Initialize(runManager, canvas != null ? canvas.transform : null);

        // The verdict when the run is over, won or lost, with the way back to a new one.
        var ending = gameObject.AddComponent<RunEndPanel>();
        ending.Initialize(runManager, canvas != null ? canvas.transform : null);

        // Who did what in the fight just fought, while there is still something to do about it.
        var scoreboard = gameObject.AddComponent<FightScoreboard>();
        scoreboard.Initialize(canvas != null ? canvas.transform : null);

        // The clock over the fight, and the faster pace a long one runs at.
        var clock = gameObject.AddComponent<FightClock>();
        clock.Initialize(canvas != null ? canvas.transform : null);

        // The map, for runs that have one. It shows itself only while a path is waiting to be chosen.
        var map = gameObject.AddComponent<MapPanel>();
        map.Initialize(runManager, canvas != null ? canvas.transform : null);
    }

    /// <summary>
    /// Both events above are static, so a subscription outlives this object — and a stale one would
    /// fire into a destroyed manager on the next play session. Hand them back.
    /// </summary>
    private void OnDestroy()
    {
        EntityRegistry.OnRegistered -= HandleEntityRegistered;
        Entity.OnAnyDied -= OnEntityDied;
        StateMachine.OnStateChanged -= HandleStateChanged;
    }

    void Update()
    {
        // A fight can become unwinnable-to-observe without a death — see EvaluateRoundOutcome.
        if (StateMachine.Current == GameState.Combat && Time.time >= _nextRoundOutcomeCheck)
        {
            _nextRoundOutcomeCheck = Time.time + RoundOutcomeCheckInterval;
            EvaluateRoundOutcome();
        }

        // Reload the whole scene for a fresh run.
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartRun();
            return;
        }

        if (!isGameStarted && Input.GetKeyDown(KeyCode.Space))
        {
            // An open shop blocks the next fight: it is left with its own button, so a Space meant
            // for something else cannot walk past it.
            if (runManager != null && runManager.ShopOpen)
                Debug.Log("[GameManager] Leave the shop before the next fight.");
            else if (runManager != null && runManager.AwaitingPath)
                // With no destination there are no enemies staged, and a fight with nobody in it
                // would resolve as an instant victory.
                Debug.Log("[GameManager] Choose a path on the map before the next fight.");
            else
                StateMachine.TransitionTo(GameState.Combat);
        }

        // Toggle character inventories with number keys 1,2,3,...
        if (characterInventories.Count > 0)
        {
            for (int i = 0; i < characterInventories.Count && i < 9; i++)
            {
                // KeyCode.Alpha1 is 1, Alpha2 is 2, etc.
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    ToggleCharacterInventories(characterInventories[i]);
                }
            }
        }
    }

}
