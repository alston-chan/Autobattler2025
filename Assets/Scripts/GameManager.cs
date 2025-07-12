using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Assets.HeroEditor.InventorySystem.Scripts.Elements;

public class GameManager : Singleton<GameManager>
{
    public GameObject avatarUI;

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

    [Header("Game State")]
    public bool isGameStarted = false;

    void Start()
    {
        CreateAvatarUI();
        InstantiateHealthBars();

        SetupCharacterInventories();
    }

    void Update()
    {
        if (!isGameStarted && Input.GetKeyDown(KeyCode.Space))
        {
            isGameStarted = true;
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
        Entity[] entities = FindObjectsOfType<Entity>();
        foreach (Entity entity in entities)
        {
            if (entity.isTeam && entity.isCharacter)
            {
                entity.Appearance.CreateAvatars();
            }

            if (entity.isCharacter)
            {
                entity.Appearance.SetRandomAppearance();
                entity.EquipRandom();
            }
        }
    }

    private void InstantiateHealthBars()
    {
        Entity[] entities = FindObjectsOfType<Entity>();
        foreach (Entity entity in entities)
        {
            GameObject healthBarObj = Instantiate(resourceBarPrefab, healthBarsOrganizer.transform);
            ResourceBar healthBar = healthBarObj.GetComponent<ResourceBar>();
            healthBarObj.transform.localScale = entity.transform.localScale;
            entity.healthBar = healthBar;
            entity.healthBar.SetSize(entity.currentHealth / entity.maxHealth);

            if (!entity.isTeam)
            {
                healthBar.SetColor(Color.red);
            }
            healthBar.entity = entity;

            healthBar.entity = entity;
            if (entity.isTeam && entity.isCharacter)
            {
                GameManager.Instance.allyCharacters.Add(entity);
            }
        }
    }

    public void SetupCharacterInventories()
    {
        foreach (Entity characterEntity in allyCharacters)
        {
            CharacterInventory characterInventory = Instantiate(characterInventoryPrefab, canvas.transform).GetComponent<CharacterInventory>();
            characterInventory.Equipment.Preview = characterEntity.character;
            characterInventory.AudioSource = audioSource.GetComponent<AudioSource>();
            characterInventory.PlayerInventory = PlayerInventory.GetComponentInChildren<ScrollInventory>();
            characterInventory.InitializeCharacterInventory();

            characterEntity.characterInventory = characterInventory;
            characterInventory.gameObject.SetActive(false);
            characterInventories.Add(characterInventory);

            // TODO: Refactor this to static 
            if (!initializedPlayerInventory)
            {
                characterInventory.InitializePlayerInventory();
                initializedPlayerInventory = true;
            }
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
}
