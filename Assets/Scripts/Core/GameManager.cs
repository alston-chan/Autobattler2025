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
using Random = UnityEngine.Random;

public class GameManager : Singleton<GameManager>
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

    // ── Game State ──
    public GameStateMachine StateMachine { get; private set; } = new GameStateMachine();

    /// <summary>Backward-compatible shorthand. True when combat is active.</summary>
    public bool isGameStarted => StateMachine.Current == GameState.Combat;

    void Start()
    {
        StateMachine.OnStateChanged += HandleStateChanged;

        EnsureArenaBounds();
        EnsureUiSortsAboveWorld();
        CreateAvatarUI();
        SetupUnitBars();
        SetupDamageNumbers();
        BuildRoster();

        SetupCharacterInventories();

        // After the roster, because this hangs a badge on each hero's avatar card and there are no
        // heroes to hang them on until BuildRoster has run.
        CreateHeroNoticeBadges();

        // Inspects any unit on the board, company or enemy, so it doesn't depend on the run existing.
        var inspector = gameObject.AddComponent<UnitInspector>();
        inspector.Initialize(canvas != null ? canvas.transform : null);

        // Last, so the company is fully built (gear, spells, inventories) before the first fight is
        // put on the board.
        if (runManager != null)
        {
            runManager.BeginRun(allyCharacters);

            var rewards = gameObject.AddComponent<RewardPanel>();
            rewards.Initialize(runManager, canvas != null ? canvas.transform : null);
        }
    }

    /// <summary>
    /// Guarantee a global <see cref="ArenaBounds"/> so entities stay on-screen. If the scene already
    /// has one (placed to tune the rectangle via its gizmo) it's left alone; otherwise a default one
    /// is spawned so the clamp works with no scene setup.
    /// </summary>
    /// <summary>
    /// Whoever is still standing when a fight ends is stood down. Handled on the transition rather
    /// than inside the win/lose check so every way out of combat is covered.
    /// </summary>
    private void HandleStateChanged(GameState previous, GameState next)
    {
        if (next == GameState.Combat) NotifyResonance(true);

        if (previous != GameState.Combat) return;

        NotifyResonance(false);
        AccrueResonance();

        var all = EntityRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var entity = all[i];
            // The dead are mid death-sequence — putting them back to idle would cancel it.
            if (entity == null || entity.isDead || entity.CombatAI == null) continue;
            entity.CombatAI.StopCombat();
        }
    }

    /// <summary>
    /// Open or close every engraving affecting every unit — those on worn items and those already
    /// banked. Combat start fires after the formation is settled, so an engraving can read who is
    /// standing beside whom; combat end lets it take back anything it granted, which is what stops a
    /// per-fight bonus stacking every encounter.
    /// </summary>
    private void NotifyResonance(bool starting)
    {
        var all = EntityRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var entity = all[i];
            if (entity == null || entity.Resonance == null) continue;
            entity.Resonance.ApplyForCombat(starting);
        }
    }

    /// <summary>
    /// Credit the fight to every resonating item the company is wearing. Only the company accrues:
    /// enemies are spawned per encounter and discarded, so attunement would have nothing to carry.
    /// </summary>
    private void AccrueResonance()
    {
        foreach (var hero in allyCharacters)
            if (hero != null && hero.Resonance != null) hero.Resonance.AccrueAfterCombat();
    }

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
            // An unclaimed reward blocks the next fight. Starting anyway would silently discard the
            // spoils of the fight just won, and the choice is the reason they were offered.
            if (runManager != null && runManager.PendingRewards.Count > 0)
                Debug.Log("[GameManager] Choose your spoils before the next fight.");
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
    /// <summary>
    /// Put an unread dot on each hero's avatar card, so progress is visible from the board without
    /// opening anybody's window.
    ///
    /// The card is the right home for it: it is the one piece of per-hero UI that is always on
    /// screen, and it is already what the player looks at to tell their heroes apart.
    /// </summary>
    private void CreateHeroNoticeBadges()
    {
        foreach (var hero in allyCharacters)
        {
            if (hero == null || hero.Resonance == null) continue;

            var card = hero.Appearance != null ? hero.Appearance.avatar : null;
            var rect = card != null ? card.transform as RectTransform : null;
            if (rect == null) continue;

            var watcher = card.AddComponent<HeroNoticeBadge>();
            watcher.Initialize(hero.Resonance, rect);

            // And over the unit itself, since the card strip is hidden while the board is showing.
            hero.gameObject.AddComponent<HeroNoticeMarker>().Initialize(hero, NoticeBadge.Dot());
        }
    }

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

            // Resonance is otherwise invisible: attunement, tiers and banking all happen silently,
            // and the bank-or-press decision can't be made against numbers the player can't see.
            var resonancePanel = characterInventory.gameObject.AddComponent<ResonancePanel>();
            resonancePanel.Initialize(characterInventory, characterEntity);

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

            // The hero's signature item — where their identity comes from. Added before the random
            // roll is committed so it can't be crowded out of its slot.
            EquipSignatureItem(characterEntity, equippedItems);

            characterInventory.Equipment.Initialize(ref equippedItems);

            // Apply stat modifiers for initially equipped items
            foreach (var item in equippedItems)
            {
                var itemParams = ItemCollection.Active.GetItemParams(item);
                characterEntity.Stats.ApplyItemModifiers(itemParams, item.Id);
            }
            characterInventory.RefreshStatsUI();

            // Unconditionally, unlike SyncSpellSlots below: what a hero is holding decides how they
            // fight whether or not they also carry a spellbook.
            characterInventory.ApplyWeaponLoadout();

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
    /// <summary>
    /// Equip the hero's signature item, replacing whatever the random roll put in the same slot. A
    /// signature is the hero's identity — the piece they are meant to wear and eventually resonate —
    /// so it wins the slot rather than competing with a random drop for it.
    /// </summary>
    private void EquipSignatureItem(Entity characterEntity, List<Item> equippedItems)
    {
        if (characterEntity == null || string.IsNullOrEmpty(characterEntity.signatureItemId)) return;

        string id = characterEntity.signatureItemId;
        var itemParams = ItemCollection.Active.Items.FirstOrDefault(i => i.Id == id);
        if (itemParams == null)
        {
            Debug.LogWarning($"[GameManager] Signature item '{id}' on {characterEntity.name} isn't a " +
                             "known item — skipped.");
            return;
        }

        equippedItems.RemoveAll(i => ItemCollection.Active.GetItemParams(i)?.Type == itemParams.Type);

        var signature = new Item(id);
        equippedItems.Add(signature);

        // Same-type replacement is not enough on its own: a shield and a two-handed weapon occupy
        // different slots but the same pair of hands. Loadout owns that rule, so a signature answers
        // to exactly what the random roll and the equipment window answer to.
        var displaced = Loadout.Normalise(equippedItems, signature);

        // Nothing was displaced, or the signature IS the weapon — either way the hero is still armed.
        if (!signature.IsShield) return;
        if (displaced.Find(i => i.IsWeapon) == null) return;

        // A shield signature can only have displaced the weapon, and leaving the hero empty-handed
        // is worse than the conflict was: swap in a one-hander instead. The roll that produced the
        // two-hander could not have known a shield was coming.
        var oneHanded = ItemCollection.Active.Items
            .Where(i => i.Type == ItemType.Weapon && i.Class != ItemClass.Bow &&
                        i.Class != ItemClass.Firearm && i.Class != ItemClass.Wand &&
                        !i.Tags.Contains(ItemTag.TwoHanded)).ToList();

        if (oneHanded.Count > 0)
            equippedItems.Add(new Item(oneHanded[Random.Range(0, oneHanded.Count)].Id));
        else
            Debug.LogWarning($"[GameManager] {characterEntity.name} lost a two-handed weapon to a " +
                             "shield signature and no one-handed replacement exists.");
    }

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

        CloseCharacterInventories();

        if (currState == false)
        {
            characterInventory.RegisterCallbacks();
            characterInventory.gameObject.SetActive(true);
            PlayerInventory.SetActive(true);
        }
    }

    /// <summary>Whether any hero's equipment window is currently open.</summary>
    public bool AnyCharacterInventoryOpen =>
        characterInventories.Exists(i => i != null && i.isActiveAndEnabled);

    /// <summary>
    /// Shut every equipment window and the shared bag. Safe when none are open, so callers that
    /// dismiss the UI — clicking away, pressing Escape — need not first work out what was showing.
    /// </summary>
    public void CloseCharacterInventories()
    {
        foreach (CharacterInventory i in characterInventories)
        {
            if (i != null) i.gameObject.SetActive(false);
        }
        if (PlayerInventory != null) PlayerInventory.SetActive(false);
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

        if (!alliesAlive) EndRound(false);
        else if (!enemiesAlive) EndRound(true);
    }

    /// <summary>
    /// A fight has been decided. With a run configured this hands off to <see cref="RunManager"/>,
    /// which either sets up the next encounter (back to Setup, press Space to fight) or ends the
    /// run. Without one the behaviour is unchanged: the round simply stops.
    /// </summary>
    private void EndRound(bool won)
    {
        Debug.Log(won ? "[GameManager] Victory — all enemies eliminated."
                      : "[GameManager] Defeat — all allies eliminated.");

        StateMachine.TransitionTo(GameState.RoundEnd);

        if (runManager == null || !runManager.IsRunning) return;

        // The next encounter is spawned now, but combat waits for the player: Setup is where gear and
        // spells get changed between fights, which is the point of the loop.
        if (runManager.ResolveEncounter(won)) StateMachine.TransitionTo(GameState.Setup);
        else StateMachine.TransitionTo(GameState.RunEnd);
    }

    #endregion
}
