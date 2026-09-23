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
/// GameManager, the company half: who fields and what they walk in wearing — the scenario roster,
/// the resumed run's save, each hero's inventory, starting gear and signature item, and the
/// resonance that follows from the gear.
/// </summary>
public partial class GameManager
{
    private void BuildCompany()
    {
        ApplyScenarioRoster();
        CreateAvatarUI();
        BuildRoster();

        // Before the company is dressed: a resumed run dresses it from the save instead of the kit.
        _resume = LoadResumableRun();
        SetupCharacterInventories();
        CreateHeroNoticeBadges();
    }

    /// <summary>
    /// A playtest scenario names who fields: everyone else sits out, and a benched hero it names is
    /// stood up. Before the roster is read and before anyone is dressed, so the rest of the start
    /// sees the scenario's company as the company. Runtime only; the scene is not touched.
    /// </summary>
    private void ApplyScenarioRoster()
    {
        var scenario = Playtest.Scenario;
        if (scenario == null || scenario.FieldsEveryone) return;

        var heroes = new List<Entity>();
        foreach (var e in Resources.FindObjectsOfTypeAll<Entity>())
            if (e != null && e.isTeam && e.isCharacter && e.gameObject.scene.IsValid()) heroes.Add(e);

        foreach (var hero in heroes)
        {
            bool fields = scenario.Includes(hero.name);
            if (hero.gameObject.activeSelf != fields) hero.gameObject.SetActive(fields);
        }
        Debug.Log($"[Playtest] Scenario '{scenario.name}': fielding {string.Join(", ", scenario.heroes.ConvertAll(h => h.heroName))}.");
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

            // Banked weapon verbs, drawn as the weapons they came from, under the Worn panel.
            characterInventory.gameObject.AddComponent<BankedAbilityBar>().Initialize(characterInventory, characterEntity);

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
                characterInventory.InitializePlayerInventory(_resume != null
                    ? RunSave.ToItems(_resume.bag)
                    : BagStock.For(run != null ? run.bag : StartingBag.Workshop));
                initializedPlayerInventory = true;
            }

            // What the hero walks in wearing: what it was saved wearing, else a random roll for the
            // sandbox or an authored kit for a run. A saved list already holds the signature item
            // and the spellbooks, so neither is added again.
            var saved = SavedHeroFor(characterEntity);
            var equippedItems = saved != null ? RunSave.ToItems(saved.equipped) : StartingGearFor(characterEntity);

            var kit = Playtest.Scenario != null ? Playtest.Scenario.KitFor(characterEntity.name) : null;

            // A save from before the weapon rack was removed may still list racked weapons: they go
            // back to the bag rather than vanish.
            if (saved != null && saved.carried != null && saved.carried.Count > 0)
            {
                characterInventory.PlayerInventory.Items.AddRange(RunSave.ToItems(saved.carried));
                saved.carried.Clear();
            }

            // The hero's signature item — where their identity comes from. Added before the random
            // roll is committed so it can't be crowded out of its slot. A playtest kit is the whole
            // outfit, signature included, so it is not added over one: it displaced the kit's weapon.
            if (saved == null && kit == null) EquipSignatureItem(characterEntity, equippedItems);

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

            characterInventory.SyncSpellSlots();

            // Last, once the hero is wearing what it wore: resonance keys by item, and its refresh
            // reads what is equipped, so restoring it earlier would grant against an empty rig.
            if (saved != null && characterEntity.Resonance != null)
                characterEntity.Resonance.RestoreState(saved.resonance);

            // And hold the engravings now, not at the bell. The inventory screen reconciles on
            // every change, but gear that arrives here never passes through it, so until the
            // first fight a hero wore its engravings without holding them: a Bulwark bearer
            // clicked in the first Setup showed nothing on its neighbours, and the bell made
            // the badges appear out of nowhere.
            if (characterEntity.Resonance != null) characterEntity.Resonance.Refresh();

            // That refresh is what grants the weapon's verb; the inventory hears the books change and
            // puts the verb in the slots itself (Resonance.OnGrantsChanged).

            // The scenario's last word: which slot the hero casts and how much health it brings.
            // How it fights follows from the gear the scenario put on it.
            if (kit != null)
            {
                characterInventory.SetActiveSlot(kit.activeSlot);
                float scale = Playtest.Scenario.heroHealthScale;
                if (scale > 0f && !Mathf.Approximately(scale, 1f) && characterEntity.Stats != null && characterEntity.Stats.MaxHealth != null)
                {
                    characterEntity.Stats.MaxHealth.AddModifier(new StatModifier(scale - 1f, StatModType.PercentMult, this));
                    characterEntity.Health.SyncMaxFromStats();
                    characterEntity.Health.HealToFull();
                }
            }
        }
    }

    /// <summary>
    /// The run save to pick up, if there is one for the run this scene plays. A save for a
    /// different run asset is discarded rather than half-applied: its heroes and path belong to
    /// content this scene does not have.
    /// </summary>
    private RunSnapshot LoadResumableRun()
    {
        if (runManager == null || runManager.runData == null) return null;

        var snapshot = RunSave.Read();
        if (snapshot == null) return null;

        // A run that does not persist never resumes. A save left over for it — from before the flag
        // was turned off, say — is removed so it cannot surprise anyone later.
        if (!runManager.runData.persist)
        {
            if (snapshot.runAsset == runManager.runData.name) RunSave.Delete();
            return null;
        }

        if (snapshot.runAsset != runManager.runData.name)
        {
            Debug.Log($"[RunSave] Found a save for '{snapshot.runAsset}' but this scene plays " +
                      $"'{runManager.runData.name}' — discarded.");
            RunSave.Delete();
            return null;
        }

        Debug.Log($"[RunSave] Resuming {snapshot.progress} (saved {snapshot.savedAt}).");
        return snapshot;
    }

    private SavedHero SavedHeroFor(Entity hero)
    {
        if (_resume == null || hero == null) return null;
        return _resume.heroes.Find(h => h != null && h.name == hero.name);
    }

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

        // A playtest scenario dresses its heroes itself, whatever the run would have done.
        var kit = Playtest.Scenario != null ? Playtest.Scenario.KitFor(hero.name) : null;
        List<string> ids = kit != null && kit.itemIds != null && kit.itemIds.Count > 0 ? kit.itemIds : null;

        if (ids == null)
        {
            if (run == null || run.startingGear == StartingGear.Randomized)
                return hero.EquipmentManagement.EquipRandomFromCollection(hero.IsRanged);

            ids = hero.startingItemIds != null && hero.startingItemIds.Count > 0
                ? hero.startingItemIds
                : run.fallbackKitItemIds;
        }

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
                        !i.Tags.Contains(ItemTag.TwoHanded) &&
                        EquipmentManagement.Authored(i)).ToList();   // never a weapon nobody designed

        if (oneHanded.Count > 0)
            equippedItems.Add(new Item(oneHanded[Random.Range(0, oneHanded.Count)].Id));
        else
            Debug.LogWarning($"[GameManager] {characterEntity.name} lost a two-handed weapon to a " +
                             "shield signature and no one-handed replacement exists.");
    }
}
