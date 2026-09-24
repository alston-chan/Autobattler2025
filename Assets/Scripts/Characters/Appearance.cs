using System.Linq;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using HeroEditor.Common.Enums;
using UnityEngine;


public class Appearance : MonoBehaviour
{
    public CharacterAppearance CharacterAppearance = new CharacterAppearance();
    private Character Character;

    [SerializeField] private GameObject avatarPrefab;
    public GameObject avatar;
    private AvatarSetup avatarSetup;

    public void Awake()
    {
        Character = GetComponent<Character>();
    }

    public void CreateAvatars()
    {
        avatar = Instantiate(avatarPrefab, GameManager.Instance.avatarUI.transform);
        avatarSetup = avatar.GetComponentInChildren<AvatarSetup>();

        Refresh();
    }

    public void Refresh()
    {
        CharacterAppearance.Setup(Character);

        var helmetId = Character.SpriteCollection.Helmet.SingleOrDefault(i => i.Sprite == Character.Helmet)?.Id;

        if (avatarSetup != null)
        {
            avatarSetup.Initialize(CharacterAppearance, helmetId);
        }
    }

    public void SetRandomAppearance()
    {
        // Every part skips the face bans (FaceBans.json), so emoji and undead faces stay off heroes.
        var sc = Character.SpriteCollection;
        var bans = FaceBans.Active;
        CharacterAppearance.Hair = Random.Range(0, 3) == 0 ? null : FaceBans.PickId(sc.Hair, i => i.Id, bans.Hair);
        CharacterAppearance.HairColor = new Color(Random.Range(0, 1f), Random.Range(0, 1f), Random.Range(0, 1f));
        CharacterAppearance.Eyebrows = FaceBans.PickId(sc.Eyebrows, i => i.Id, bans.Eyebrows);
        CharacterAppearance.Eyes = FaceBans.PickId(sc.Eyes, i => i.Id, bans.Eyes);
        CharacterAppearance.EyesColor = new Color(Random.Range(0, 1f), Random.Range(0, 1f), Random.Range(0, 1f));
        CharacterAppearance.Mouth = FaceBans.PickId(sc.Mouth, i => i.Id, bans.Mouth);
        CharacterAppearance.Beard = Random.Range(0, 3) == 0 ? FaceBans.PickId(sc.Beard, i => i.Id, bans.Beard) : null;

        Refresh();
    }

    public void ResetAppearance()
    {
        CharacterAppearance = new CharacterAppearance();
        Refresh();
    }

    public void SetRandomHair()
    {
        var randomIndex = Random.Range(0, Character.SpriteCollection.Hair.Count);
        var randomItem = Character.SpriteCollection.Hair[randomIndex];
        var randomColor = new Color(Random.Range(0, 1f), Random.Range(0, 1f), Random.Range(0, 1f));

        Character.SetBody(randomItem, BodyPart.Hair, randomColor);
    }

    public void SetRandomEyebrows()
    {
        var randomIndex = Random.Range(0, Character.SpriteCollection.Eyebrows.Count);
        var randomItem = Character.SpriteCollection.Eyebrows[randomIndex];

        Character.SetBody(randomItem, BodyPart.Eyebrows);
    }

    public void SetRandomEyes()
    {
        var randomIndex = Random.Range(0, Character.SpriteCollection.Eyes.Count);
        var randomItem = Character.SpriteCollection.Eyes[randomIndex];
        var randomColor = new Color(Random.Range(0, 1f), Random.Range(0, 1f), Random.Range(0, 1f));

        Character.SetBody(randomItem, BodyPart.Eyes, randomColor);
    }

    public void SetRandomMouth()
    {
        var randomIndex = Random.Range(0, Character.SpriteCollection.Mouth.Count);
        var randomItem = Character.SpriteCollection.Mouth[randomIndex];

        Character.SetBody(randomItem, BodyPart.Mouth);
    }
}