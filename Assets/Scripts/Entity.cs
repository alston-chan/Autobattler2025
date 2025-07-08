using System.Collections;
using System.Collections.Generic;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using UnityEngine;

public class Entity : MonoBehaviour
{
    private Character character;

    public Appearance appearance;

    public EquipmentManagement equipmentManagement;

    [SerializeField] private bool isCharacter = true;

    public bool isTeam = true;

    // Start is called before the first frame update
    void Start()
    {
        character = GetComponent<Character>();
        appearance = GetComponent<Appearance>();
        equipmentManagement = GetComponent<EquipmentManagement>();
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void EquipRandom()
    {
        equipmentManagement.EquipRandomArmor();
        equipmentManagement.EquipRandomHelmet();
        equipmentManagement.EquipRandomShield();
        equipmentManagement.EquipRandomWeapon();
    }
}
