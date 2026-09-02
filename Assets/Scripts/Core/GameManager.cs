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
    /// Bars and damage numbers, both of which provision themselves per unit off entity registration.
    /// Ahead of the company so that dressing a unit — which moves max health through Stats — lands
    /// on a bar that already exists rather than one built later from the result.
    /// </summary>
    private void BuildUnitPresentation()
    {
        SetupUnitBars();
        SetupDamageNumbers();

        // Watches entity registration to record who does what. Here rather than later because it
        // has to be listening before the first unit is dressed, let alone the first blow.
        if (GetComponent<CombatTelemetry>() == null) gameObject.AddComponent<CombatTelemetry>();
    }

    private void BuildCompany()
    {
        CreateAvatarUI();
        BuildRoster();
        SetupCharacterInventories();
        CreateHeroNoticeBadges();
    }

    private void BuildPlayerTools()
    {
        // Inspects any unit on the board, company or enemy, so it doesn't depend on the run existing.
        var inspector = gameObject.AddComponent<UnitInspector>();
        inspector.Initialize(canvas != null ? canvas.transform : null);

        // Badges over whoever the company's engravings will touch at the bell, while it is arranged.
        var preview = gameObject.AddComponent<FormationPreview>();
        preview.Initialize(runManager);
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
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void StartRun()
    {
        if (runManager == null) return;

        runManager.BeginRun(allyCharacters);

        var rewards = gameObject.AddComponent<RewardPanel>();
        rewards.Initialize(runManager, canvas != null ? canvas.transform : null);

        // The verdict when the run is over, won or lost, with the way back to a new one.
        var ending = gameObject.AddComponent<RunEndPanel>();
        ending.Initialize(runManager, canvas != null ? canvas.transform : null);

        // The map, for runs that have one. It shows itself only while a path is waiting to be chosen.
        var map = gameObject.AddComponent<MapPanel>();
        map.Initialize(runManager, canvas != null ? canvas.transform : null);
    }

    /// <summary>
    /// Whoever is still standing when a fight ends is stood down. Handled on the transition rather
    /// than inside the win/lose check so every way out of combat is covered.
    /// </summary>
    private void HandleStateChanged(GameState previous, GameState next)
    {
        // Tell every unit whether it is fighting. Entities gate their own Update on this instead of
        // reading the state machine back out of here every frame.
        BroadcastFighting(next == GameState.Combat);

        if (next == GameState.Combat)
        {
            // No hero begins a fight dead. RestoreCompany already runs after a victory; this is the
            // guarantee at the bell itself, for whatever might have happened in between.
            if (runManager != null && runManager.IsRunning) runManager.RestoreCompany();
            NotifyResonance(true);
        }

        if (previous != GameState.Combat) return;

        NotifyResonance(false);
        AccrueResonance();

        var telemetry = GetComponent<CombatTelemetry>();
        if (telemetry != null)
        {
            telemetry.NoteFightEnded();
            Debug.Log(telemetry.BuildReport());
            telemetry.WriteReport();
        }

        RaiseTheFallen();

        // Whatever was still flying when the round ended, before it lands on someone.
        int swept = CombatDebris.Sweep();
        if (swept > 0) Debug.Log($"[GameManager] Cleared {swept} in-flight objects at round end.");

        var all = EntityRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var entity = all[i];
            if (entity == null || entity.isDead || entity.CombatAI == null) continue;
            entity.CombatAI.StopCombat();

            // Turn back to face the enemy. A unit ends a fight looking wherever the last thing it
            // chased happened to be — and an assassin ends it behind the enemy line looking the
            // wrong way entirely — so without this the company stands around backwards between
            // rounds. Done here rather than when the next encounter is staged, because that only
            // happens after a victory, and a fight can end in more ways than winning.
            entity.SetFacing(entity.isTeam);
        }
    }

    /// <summary>
    /// Get the fallen back on their feet now the fighting has stopped.
    ///
    /// The company was already revived between encounters, but only on the way to the NEXT
    /// encounter — which never comes if the fight was lost. A wipe therefore left five corpses
    /// lying on the field for as long as the scene stayed open, and nothing was going to move them.
    /// Doing it on the way out of combat covers every ending rather than the winning one.
    ///
    /// Only the company. Enemies are spawned per encounter and discarded, and a dead one standing
    /// up would be a resurrection rather than a reset.
    /// </summary>
    private void RaiseTheFallen()
    {
        // Collected first: reviving reactivates the object, which re-registers it, and that must
        // not happen while walking the registry.
        var fallen = new List<Entity>();
        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var entity = all[i];
            if (entity != null && entity.isTeam && entity.isDead) fallen.Add(entity);
        }

        foreach (var entity in fallen)
        {
            if (!entity.gameObject.activeSelf) entity.gameObject.SetActive(true);

            entity.Health.Revive();

            // Undoes what the death sequence did to the body — the fade, the collapse, the pose.
            if (entity.DeathFeedback != null) entity.DeathFeedback.RestoreAfterRevive();
        }
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

    private void HandleEntityRegistered(Entity entity)
    {
        if (entity != null) entity.SetFighting(isGameStarted);
    }

    private void BroadcastFighting(bool fighting)
    {
        var all = EntityRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (all[i] != null) all[i].SetFighting(fighting);
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
            // An unclaimed reward blocks the next fight. Starting anyway would silently discard the
            // spoils of the fight just won, and the choice is the reason they were offered.
            if (runManager != null && runManager.PendingRewards.Count > 0)
                Debug.Log("[GameManager] Choose your spoils before the next fight.");
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
        if (allyCharacters.Count == 0)
        {
            Debug.LogError("[GameManager] Notice badges built with an empty roster — no hero will " +
                           "ever show one. This stage must run after BuildRoster.");
            return;
        }

        foreach (var hero in allyCharacters)
        {
            if (hero == null || hero.Resonance == null) continue;

            var card = hero.Appearance != null ? hero.Appearance.avatar : null;
            var rect = card != null ? card.transform as RectTransform : null;
            if (rect == null)
            {
                Debug.LogError($"[GameManager] {hero.name} has no avatar card, so it cannot carry a " +
                               "notice badge. This stage must run after CreateAvatarUI.");
                continue;
            }

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
        if (canvas == null || characterInventoryPrefab == null)
        {
            Debug.LogError("[GameManager] No canvas or character inventory prefab — the company gets " +
                           "no equipment windows, so nobody is dressed, given a signature item, or " +
                           "handed the weapon that decides how they fight.");
            return;
        }

        if (allyCharacters.Count == 0)
        {
            Debug.LogError("[GameManager] Building inventories with an empty roster — the company " +
                           "will fight in whatever it was authored with. This stage must run after " +
                           "BuildRoster.");
            return;
        }

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

            // The shared bag opens once, with whatever the run says. A scene with no run at all is
            // the sandbox, and gets the workshop.
            if (!initializedPlayerInventory)
            {
                var run = runManager != null ? runManager.runData : null;
                characterInventory.InitializePlayerInventory(BagStock.For(run != null ? run.bag : StartingBag.Workshop));
                initializedPlayerInventory = true;
            }

            // What the hero walks in wearing: a random roll for the sandbox, an authored kit for a run.
            var equippedItems = StartingGearFor(characterEntity);

            // Materialize the character's editor-authored spell loadout (Entity.spellSlots) as equipped
            // spellbooks, so the starting spells show in the spell row and drive combat through the
            // SAME equipped-books path as runtime equipping. Equipment.Initialize slots them; the
            // SyncSpellSlots below rebuilds spellSlots from those books (matching what was authored).
            int addedBooks = EquipAuthoredSpellsAsBooks(characterEntity, equippedItems);

            // The hero's signature item — where their identity comes from. Added before the random
            // roll is committed so it can't be crowded out of its slot.
            EquipSignatureItem(characterEntity, equippedItems);

            // A hero with nothing to swing has no basic attack and no damage stat, and stands in the
            // fight doing nothing — quietly, because every stage after this still runs.
            if (equippedItems.Find(i => i.IsWeapon) == null)
                Debug.LogWarning($"[GameManager] {characterEntity.name} starts with no weapon — give " +
                                 "it one in its starting kit or as its signature item.");

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
    /// <summary>
    /// The items a hero starts in. The sandbox rolls them at random so every feature is exercised
    /// against gear nobody chose; a run hands out an authored kit, small on purpose, because the run
    /// is where the rest is meant to be found (Docs/RunSimulation.md: a weapon and one Common piece).
    /// The signature item is added afterwards either way.
    /// </summary>
    private List<Item> StartingGearFor(Entity hero)
    {
        var run = runManager != null ? runManager.runData : null;
        if (run == null || run.startingGear == StartingGear.Randomized)
            return hero.EquipmentManagement.EquipRandomFromCollection(hero.IsRanged);

        var ids = hero.startingItemIds != null && hero.startingItemIds.Count > 0
            ? hero.startingItemIds
            : run.fallbackKitItemIds;

        var items = new List<Item>();
        if (ids == null) return items;

        foreach (var id in ids)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (!ItemCollection.Active.Items.Any(i => i.Id == id))
            {
                Debug.LogWarning($"[GameManager] Starting kit item '{id}' on {hero.name} isn't a known " +
                                 "item — skipped.");
                continue;
            }
            items.Add(new Item(id));
        }
        return items;
    }

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
    public void OnEntityDied(Entity entity) => EvaluateRoundOutcome();

    /// <summary>
    /// Decide whether the fight is over.
    ///
    /// Driven by deaths, because that is when the answer can change — but not ONLY by deaths. A
    /// death is a poor sole trigger for "is anyone left", since a side can be empty without anyone
    /// having died in front of us: a fight entered with nothing to fight, or an encounter that
    /// staged no enemies. Combat then runs forever waiting for a death that cannot happen, with
    /// every unit standing idle and no way out but reloading the scene. Update polls this a few
    /// times a second as well, which costs a loop over a handful of units and removes the whole
    /// category.
    /// </summary>
    private void EvaluateRoundOutcome()
    {
        if (StateMachine.Current != GameState.Combat) return;

        bool alliesAlive = false;
        bool enemiesAlive = false;

        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] == null || all[i].isDead) continue;
            if (all[i].isTeam) alliesAlive = true;
            else enemiesAlive = true;
        }

        if (!alliesAlive) EndRound(false);
        else if (!enemiesAlive) EndRound(true);
    }

    /// <summary>How often the safety check above runs while a fight is on.</summary>
    private const float RoundOutcomeCheckInterval = 0.5f;
    private float _nextRoundOutcomeCheck;

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
