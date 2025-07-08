using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : Singleton<GameManager>
{
    public GameObject avatarUI;

    // Start is called before the first frame update
    void Start()
    {
        CreateAvatarUI();
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void CreateAvatarUI()
    {
        Entity[] entities = FindObjectsOfType<Entity>();
        foreach (Entity entity in entities)
        {
            if (entity.isTeam)
            {
                entity.Appearance.CreateAvatars();
            }

            entity.Appearance.SetRandomAppearance();
            entity.EquipRandom();
        }
    }
}
