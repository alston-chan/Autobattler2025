using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Elements;

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
    public List<Entity> allyCharacters = new List<Entity>();
    public List<CharacterInventory> characterInventories = new List<CharacterInventory>();

    // ── Game State ──
    public GameStateMachine StateMachine { get; private set; } = new GameStateMachine();

    /// <summary>Backward-compatible shorthand. True when combat is active.</summary>
    public bool isGameStarted => StateMachine.Current == GameState.Combat;

    void Start()
    {
        CreateAvatarUI();
        SetupUnitBars();
        SetupDamageNumbers();
        BuildRoster();

        SetupCharacterInventories();
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
            characterInventory.Equipment.Initialize(ref equippedItems);

            // Apply stat modifiers for initially equipped items
            foreach (var item in equippedItems)
            {
                var itemParams = ItemCollection.Active.GetItemParams(item);
                characterEntity.Stats.ApplyItemModifiers(itemParams, item.Id);
            }
            characterInventory.RefreshStatsUI();
        }
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
