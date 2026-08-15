using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Elements;
using System.Linq;

public class GameManager : Singleton<GameManager>
{
    public GameObject avatarUI;

    [Tooltip("Handed to UnitBarsManager at startup — bar appearance is tuned there.")]
    public GameObject healthBarsOrganizer;
    public GameObject resourceBarPrefab;

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

    // ── Game State ──
    public GameStateMachine StateMachine { get; private set; } = new GameStateMachine();

    /// <summary>Backward-compatible shorthand. True when combat is active.</summary>
    public bool isGameStarted => StateMachine.Current == GameState.Combat;

    void Start()
    {
        EnsureArenaBounds();
        EnsureUiSortsAboveWorld();
        CreateAvatarUI();
        SetupUnitBars();
        SetupDamageNumbers();
        BuildRoster();

        SetupCharacterInventories();
    }

    /// <summary>
    /// Guarantee a global <see cref="ArenaBounds"/> so entities stay on-screen. If the scene already
    /// has one (placed to tune the rectangle via its gizmo) it's left alone; otherwise a default one
    /// is spawned so the clamp works with no scene setup.
    /// </summary>
    private void EnsureArenaBounds()
    {
        if (ArenaBounds.Instance == null)
            new GameObject("ArenaBounds (auto)").AddComponent<ArenaBounds>();
    }

    /// <summary>
    /// Put the UI canvas on a sorting layer above the world so windows (equipment, inventory) draw
    /// over characters instead of being covered by them. A Screen Space - Camera canvas is sorted
    /// against SpriteRenderers by sorting layer then order; the canvas shipped on 'Default' order 1
    /// while characters reach order ~405 on the same layer, so they won.
    /// </summary>
    private void EnsureUiSortsAboveWorld()
    {
        if (canvas == null) return;

        var c = canvas.GetComponent<Canvas>() ?? canvas.GetComponentInParent<Canvas>();
        if (c == null) return;

        c = c.rootCanvas;   // sorting is a property of the root canvas
        c.sortingLayerName = uiSortingLayer;
        c.sortingOrder = uiSortingOrder;
    }

    void Update()
    {
        // Reload the whole scene for a fresh fight.
        if (Input.GetKeyDown(KeyCode.R))
        {
            // EntityRegistry is static and survives the reload — drop stale entries first.
            EntityRegistry.Clear();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        if (!isGameStarted && Input.GetKeyDown(KeyCode.Space))
        {
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

    /// <summary>
    /// The avatar cards draw their heads with SpriteRenderers (HeroEditor's AvatarSetup) while the
    /// card's own backing and frame are UI Images. Once the canvas sorts above the world
    /// (EnsureUiSortsAboveWorld), that card art paints over the faces and the cards read as empty.
    ///
    /// Sorting cannot fix it — verified at sortingOrder 326, on a sorting layer above the canvas's,
    /// and with the rig reparented out of the canvas at order 9000+; all stayed hidden, while
    /// disabling the canvas's UI Graphics showed the heads rendering perfectly. So each head is
    /// filmed on a private stage and shown as a RawImage, which composites like any other UI
    /// graphic (the same fix <see cref="CharacterPreview"/> uses for the equipment doll).
    /// </summary>
    private void CreateAvatarPortraits()
    {
        if (avatarUI == null) return;

        foreach (Transform card in avatarUI.transform)
        {
            var setup = card.GetComponentInChildren<AvatarSetup>(true);
            var rect = card as RectTransform;
            if (setup == null || rect == null) continue;

            var portrait = card.gameObject.AddComponent<AvatarPortrait>();
            portrait.Initialize(setup, rect, avatarPortraitLayer, avatarPortraitTextureSize,
                                avatarPortraitCameraSize, avatarPortraitCameraOffset,
                                avatarPortraitFill);
        }
    }

    private void CreateAvatarUI()
    {
        var entities = EntityRegistry.All;
        foreach (Entity entity in entities)
        {
            if (entity.isTeam && entity.isCharacter)
            {
                entity.Appearance.CreateAvatars();
            }

            if (entity.isCharacter)
            {
                entity.Appearance.SetRandomAppearance();

                // Enemies use random sprites, allies will be equipped after inventory setup
                if (!entity.isTeam)
                {
                    entity.EquipRandom();
                }
            }
        }

        // Done after every avatar exists, so it catches them all in one pass.
        CreateAvatarPortraits();
    }

    /// <summary>
    /// Hands the bar prefab + parent to <see cref="UnitBarsManager"/>, which then provisions bars
    /// for every entity via EntityRegistry lifecycle events — including anything summoned later.
    /// </summary>
    private void SetupUnitBars()
    {
        var bars = GetComponent<UnitBarsManager>();
        if (bars == null) bars = gameObject.AddComponent<UnitBarsManager>();
        bars.Configure(resourceBarPrefab, healthBarsOrganizer != null ? healthBarsOrganizer.transform : null);
    }

    /// <summary>
    /// Adds the <see cref="DamageNumbersManager"/>, which then hooks every entity's OnDamaged via
    /// EntityRegistry. It builds its own pooled TMP numbers, so there is nothing to wire.
    /// </summary>
    private void SetupDamageNumbers()
    {
        if (GetComponent<DamageNumbersManager>() == null)
            gameObject.AddComponent<DamageNumbersManager>();
    }

    /// <summary>Collect the player-controlled characters for inventory setup.</summary>
    private void BuildRoster()
    {
        allyCharacters.Clear();
        foreach (Entity entity in EntityRegistry.All)
            if (entity.isTeam && entity.isCharacter)
                allyCharacters.Add(entity);
    }

    public void SetupCharacterInventories()
    {
        foreach (Entity characterEntity in allyCharacters)
        {
            CharacterInventory characterInventory = Instantiate(characterInventoryPrefab, canvas.transform).GetComponent<CharacterInventory>();
            characterInventory.Equipment.Preview = characterEntity.character;
            characterInventory.AudioSource = audioSource.GetComponent<AudioSource>();
            characterInventory.PlayerInventory = PlayerInventory.GetComponentInChildren<ScrollInventory>();
            characterInventory.InitializeCharacterInventory(characterEntity);

            characterEntity.characterInventory = characterInventory;

            // A doll of this character inside its own window. Added before the window is deactivated
            // so its OnEnable runs the first time the player opens it with the number keys.
            if (previewBodyPrefab != null)
            {
                var preview = characterInventory.gameObject.AddComponent<CharacterPreview>();
                preview.Initialize(characterInventory.Equipment, previewBodyPrefab,
                                   characterEntity.Appearance);
            }

            characterInventory.gameObject.SetActive(false);
            characterInventories.Add(characterInventory);

            // TODO: Refactor this to static 
            if (!initializedPlayerInventory)
            {
                characterInventory.InitializePlayerInventory();
                initializedPlayerInventory = true;
            }

            // Equip ally with random items from ItemCollection and add to Equipment UI
            var equippedItems = characterEntity.EquipmentManagement.EquipRandomFromCollection(characterEntity.IsRanged);

            // Materialize the character's editor-authored spell loadout (Entity.spellSlots) as equipped
            // spellbooks, so the starting spells show in the spell row and drive combat through the
            // SAME equipped-books path as runtime equipping. Equipment.Initialize slots them; the
            // SyncSpellSlots below rebuilds spellSlots from those books (matching what was authored).
            int addedBooks = EquipAuthoredSpellsAsBooks(characterEntity, equippedItems);

            characterInventory.Equipment.Initialize(ref equippedItems);

            // Apply stat modifiers for initially equipped items
            foreach (var item in equippedItems)
            {
                var itemParams = ItemCollection.Active.GetItemParams(item);
                characterEntity.Stats.ApplyItemModifiers(itemParams, item.Id);
            }
            characterInventory.RefreshStatsUI();

            // Only rebuild spell slots from equipment when we actually materialized authored spells as
            // books. SyncSpellSlots drives spellSlots purely from equipped books, so calling it when a
            // character has none would WIPE ults still sitting in innate 'spells' — leave those alone
            // (CombatAI already picked them up in Awake).
            if (addedBooks > 0)
                characterInventory.SyncSpellSlots();
        }
    }

    /// <summary>
    /// Convert the character's editor-authored <see cref="Entity.spellSlots"/> into equipped spellbook
    /// items (reverse-mapped via <see cref="SpellbookDatabase"/>) appended to <paramref name="equippedItems"/>,
    /// preserving order so the active slot still lines up. Spells with no spellbook entry are skipped
    /// with a warning. Returns how many books were added.
    /// </summary>
    private int EquipAuthoredSpellsAsBooks(Entity characterEntity, List<Item> equippedItems)
    {
        if (characterEntity == null || characterEntity.spellSlots == null) return 0;
        var db = SpellbookDatabase.Active;
        if (db == null) return 0;

        int added = 0;
        foreach (var spell in characterEntity.spellSlots)
        {
            if (spell == null) continue;

            string bookId = db.GetItemId(spell);
            if (string.IsNullOrEmpty(bookId))
            {
                Debug.LogWarning($"[GameManager] {characterEntity.name} has '{spell.name}' in spellSlots " +
                                 "but no spellbook maps to it — add a SpellbookDatabase entry so it can " +
                                 "be equipped. Skipped.");
                continue;
            }
            if (!ItemCollection.Active.Items.Any(i => i.Id == bookId)) continue;

            equippedItems.Add(new Item(bookId));
            added++;
        }
        return added;
    }

    public void ToggleCharacterInventories(CharacterInventory characterInventory)
    {
        bool currState = characterInventory.isActiveAndEnabled;

        foreach (CharacterInventory i in characterInventories)
        {
            i.gameObject.SetActive(false);
        }
        PlayerInventory.SetActive(false);

        if (currState == false)
        {
            characterInventory.RegisterCallbacks();
            characterInventory.gameObject.SetActive(true);
            PlayerInventory.SetActive(true);
        }
    }

    #region Round lifecycle

    /// <summary>
    /// Called by any Entity when it dies. Checks if all allies or all enemies
    /// are dead and transitions to RoundEnd when appropriate.
    /// </summary>
    public void OnEntityDied(Entity entity)
    {
        if (StateMachine.Current != GameState.Combat) return;

        bool alliesAlive = false;
        bool enemiesAlive = false;

        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].isDead) continue;
            if (all[i].isTeam) alliesAlive = true;
            else enemiesAlive = true;
        }

        if (!alliesAlive)
        {
            Debug.Log("[GameManager] Defeat — all allies eliminated.");
            StateMachine.TransitionTo(GameState.RoundEnd);
        }
        else if (!enemiesAlive)
        {
            Debug.Log("[GameManager] Victory — all enemies eliminated.");
            StateMachine.TransitionTo(GameState.RoundEnd);
        }
    }

    #endregion
}
