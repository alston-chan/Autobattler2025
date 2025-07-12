using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : Singleton<GameManager>
{
    public GameObject avatarUI;

    public GameObject healthBarsOrganizer;
    public GameObject resourceBarPrefab;

    [Header("Game State")]
    public bool isGameStarted = false;

    void Start()
    {
        CreateAvatarUI();
        InstantiateHealthBars();
    }

    void Update()
    {
        if (!isGameStarted && Input.GetKeyDown(KeyCode.Space))
        {
            isGameStarted = true;
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
        }
    }
}
