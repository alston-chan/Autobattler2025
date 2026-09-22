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

/// <summary>
/// GameManager, the presentation half: what is built for the eye and the hand — arena bounds, the
/// canvas over the world, bars, damage numbers, portraits, badges, the inspector and the formation
/// preview, and the workshop windows opening and closing. Nothing here decides a fight.
/// </summary>
public partial class GameManager
{
    /// <summary>
    /// Bars and damage numbers, both of which provision themselves per unit off entity registration.
    /// Ahead of the company so that dressing a unit — which moves max health through Stats — lands
    /// on a bar that already exists rather than one built later from the result.
    /// </summary>
    private void BuildUnitPresentation()
    {
        SetupUnitBars();
        SetupDamageNumbers();
        SetupCombatAudio();

        // Watches entity registration to record who does what. Here rather than later because it
        // has to be listening before the first unit is dressed, let alone the first blow.
        if (GetComponent<CombatTelemetry>() == null) gameObject.AddComponent<CombatTelemetry>();
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

    /// <summary>
    /// Adds <see cref="CombatAudio"/>, which hears the whole fight through <see cref="CombatEvents"/>
    /// and needs nothing wired. The state machine is handed over so it can also ring the bell and
    /// tell a won fight from a lost one; everything else it learns from the bus.
    /// </summary>
    private void SetupCombatAudio()
    {
        var audio = GetComponent<CombatAudio>();
        if (audio == null) audio = gameObject.AddComponent<CombatAudio>();
        audio.Listen(StateMachine);
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
}
