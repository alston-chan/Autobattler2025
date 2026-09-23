using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>One named thing to check about a running fight. It fails by throwing.</summary>
public class PlayCheck
{
    public readonly string Name;
    public readonly Func<IEnumerator> Body;
    public PlayCheck(string name, Func<IEnumerator> body) { Name = name; Body = body; }
}

/// <summary>
/// What a live fight has to be true of. Everything here was found by hand this session — a probe, a
/// screenshot and a guess each time — and every one lives where an edit-mode test cannot go: in
/// Awake, in OnEnable, or in the order things happen in during a fight.
///
/// Run them with <see cref="PlayTestRunner"/> (Tools > Tests > Run Play Tests). They are minutes,
/// not seconds, so they are not part of the ordinary suite.
/// </summary>
public static class PlayChecks
{
    public static List<PlayCheck> All() => new List<PlayCheck>
    {
        // First, because it is the only check that needs an actual bell rather than a fight in
        // progress: the others tolerate joining one, and this one is about where units START.
        new PlayCheck("everyone opens on their cell, in rank", EveryoneOpensOnTheirCell),
        new PlayCheck("a whirl cuts the enemy beside it", AWhirlCutsTheEnemyBesideIt),
        new PlayCheck("hold the line covers whoever is close", HoldTheLineCoversWhoeverIsClose),
        new PlayCheck("stand fast turns the row onto its wearer", StandFastTurnsTheRowOntoItsWearer),
        new PlayCheck("the round-end sweep clears the ground", TheRoundEndSweepClearsTheGround),
        new PlayCheck("a weapon banks only when asked, and its verb goes with it", AWeaponBanksOnlyWhenAsked),
        new PlayCheck("a unit at the wall is drawn on screen", AUnitAtTheWallIsDrawnOnScreen),
        new PlayCheck("a won fight opens the shop", AWonFightOpensTheShop),
        new PlayCheck("every living unit can be seen to be alive", EveryLivingUnitHasAVisibleBar),
        new PlayCheck("a decoy is on its owner's side before anything looks at it", DecoyTakesItsOwnersSide),
        new PlayCheck("a bar comes back when its owner is alive again", ABarComesBackFromADeathFade),
        new PlayCheck("nobody fights a body they cannot reach", NobodyWalksPastAFight),
        new PlayCheck("a blow that lands reaches a voice", AHitIsHeard),
        new PlayCheck("a bar is the size of a body, whatever the art", ABarIsTheSizeOfABody),
        new PlayCheck("a body thrown into the wall loses the flat number, as a slam", AWallSlamIsFlatAndSaysSo),
        new PlayCheck("nobody stands a hand's width out of reach", NobodyStandsJustOutOfReach),
    };

    /// <summary>
    /// Whirl, cast for real on a real rig, touches an enemy standing beside the caster.
    ///
    /// Its blades were parented to the caster and inherited the character's 0.5 scale, so the
    /// authored 1.6 circle was 0.8 in the world — inside the 1.1 at which two bodies touch. Every
    /// cast passed between the caster and its enemies: measured, two casts in a fight, zero hits,
    /// with numbers in the asset and the tooltip that all looked right. Only the running game has
    /// the rig's scale, which is why this is a play check.
    /// </summary>
    private static IEnumerator AWhirlCutsTheEnemyBesideIt()
    {
        yield return PlayHarness.ReachTheBell();

        var whirl = UnityEditor.AssetDatabase.LoadAssetAtPath<CompositeSpell>("Assets/Data/Spells/Whirl.asset");
        OrbitEffect orbit = null;
        foreach (var e in whirl.effects) if (e is OrbitEffect o) orbit = o;
        Assert.That(orbit, Is.Not.Null, "Whirl has no blades to test");

        Entity caster = null, foe = null;
        foreach (var unit in PlayHarness.Living())
        {
            if (unit.isTeam && unit.isCharacter && caster == null) caster = unit;
            if (!unit.isTeam && foe == null) foe = unit;
        }
        Assert.That(caster, Is.Not.Null, "no living hero to spin the blades");
        Assert.That(foe, Is.Not.Null, "no living enemy to cut");

        // Where a melee enemy stands to swing: a 1.5 reach stops at 1.35 and swings out to 1.5. The
        // shrunken ring still grazed a body pressed up against the caster, at 1.2 — this check
        // passed on the broken code there — and missed everyone standing where fights happen.
        foe.transform.position = caster.transform.position + Vector3.right * 1.4f;

        var before = new HashSet<OrbitRunner>(UnityEngine.Object.FindObjectsOfType<OrbitRunner>());
        var run = orbit.Run(new SpellContext { caster = caster, target = foe, tier = 1, scale = 1f });
        while (run.MoveNext()) { }
        OrbitRunner runner = null;
        foreach (var r in UnityEngine.Object.FindObjectsOfType<OrbitRunner>()) if (!before.Contains(r)) runner = r;
        Assert.That(runner, Is.Not.Null, "Whirl's effect made no blades");

        // Three blades at 200 degrees a second: one passes any point every 0.6 s.
        int struck = 0;
        float until = Time.time + 2f;
        yield return PlayHarness.Until(() =>
        {
            if (runner != null) struck = runner.Struck;
            return struck > 0 || runner == null || Time.time >= until;
        }, "a whirl to cut or end", 10f);
        if (runner != null) UnityEngine.Object.Destroy(runner.gameObject);

        Assert.That(struck, Is.GreaterThan(0), "Whirl spun on " + DisplayNames.Unit(caster) + " for two seconds and never touched " +
                    DisplayNames.Unit(foe) + ", standing 1.4 away — is the ring being drawn at the rig's scale?");
    }

    /// <summary>
    /// Hold the Line covers an ally who stands close and lets go of one who walks away. It was a
    /// line that held only while its wearer stood still, with a stance to keep it still; now it is
    /// cover that goes where the fight goes, which only the running game can move units through.
    /// </summary>
    private static IEnumerator HoldTheLineCoversWhoeverIsClose()
    {
        yield return PlayHarness.ReachTheBell();

        var line = UnityEditor.AssetDatabase.LoadAssetAtPath<HoldTheLineEngraving>("Assets/Data/Engravings/HoldTheLine.asset");
        Assert.That(line, Is.Not.Null);
        Assert.That(line.heldLine, Is.Not.Null, "Hold the Line has no status to give");

        Entity guard = null, ally = null;
        foreach (var unit in PlayHarness.Living())
        {
            if (!unit.isTeam) continue;
            if (guard == null) guard = unit; else if (ally == null) ally = unit;
        }
        Assert.That(ally, Is.Not.Null, "the company needs two living heroes for this");
        bool hadIt = ally.Statuses.Has(line.heldLine);
        Assert.That(hadIt, Is.False, DisplayNames.Unit(ally) + " already wears Held Line; this check cannot tell who gave it");

        var cover = guard.gameObject.AddComponent<Bodyguard>();
        try
        {
            cover.Begin(guard, line.heldLine, line.radius, 1);
            ally.transform.position = guard.transform.position + Vector3.up * (line.radius * 0.6f);
            yield return PlayHarness.Until(() => ally.Statuses.Has(line.heldLine), DisplayNames.Unit(ally) + " to be covered, standing " + (line.radius * 0.6f).ToString("0.0") + " from its guard", 2f);

            ally.transform.position = guard.transform.position + Vector3.up * (line.radius + 2f);
            yield return PlayHarness.Until(() => !ally.Statuses.Has(line.heldLine), DisplayNames.Unit(ally) + " to lose its cover, " + (line.radius + 2f).ToString("0.0") + " away", 2f);
        }
        finally
        {
            cover.End();
            UnityEngine.Object.Destroy(cover);
        }
    }

    /// <summary>
    /// Stand Fast turns the row a unit faces onto it: every enemy in its lane on the board frozen at
    /// the bell wears Taunted from it, and fights it. The board, the statuses and the targeting are
    /// three systems, and only a running fight has all three.
    /// </summary>
    private static IEnumerator StandFastTurnsTheRowOntoItsWearer()
    {
        yield return PlayHarness.ReachTheBell();

        var stand = UnityEditor.AssetDatabase.LoadAssetAtPath<StandFastEngraving>("Assets/Data/Engravings/StandFast.asset");
        Assert.That(stand, Is.Not.Null);

        // A hero facing a row nobody has taunted yet, so whose taunt it is cannot be in doubt.
        Entity wearer = null; List<Entity> row = null;
        foreach (var unit in PlayHarness.Living())
        {
            if (!unit.isTeam) continue;
            var facing = BoardSnapshot.Facing(unit).FindAll(e => e != null && !e.isDead && e.gameObject.activeInHierarchy);
            if (facing.Count == 0 || facing.Exists(e => e.Statuses.TauntedBy != null)) continue;
            wearer = unit; row = facing; break;
        }
        Assert.That(wearer, Is.Not.Null, "no living hero faces a living, untaunted enemy row on the frozen board");

        stand.OnCombatStart(wearer, 1);
        foreach (var enemy in row)
            Assert.That(enemy.Statuses.TauntedBy, Is.SameAs(wearer), DisplayNames.Unit(enemy) + " stands in " + DisplayNames.Unit(wearer) + "'s row and was not taunted to it");

        var first = row[0];
        yield return PlayHarness.Until(() => first.isDead || first.CombatAI == null || first.CombatAI.CurrentTarget == wearer,
                                       DisplayNames.Unit(first) + " to turn on " + DisplayNames.Unit(wearer), 2f);

        foreach (var enemy in row) if (enemy != null && enemy.Statuses != null) enemy.Statuses.Remove(stand.taunted);   // leave nothing for the next check
    }

    /// <summary>
    /// What a round leaves on the ground does not reach the next one. A tar pool lives five seconds
    /// by the clock, and one cast late in a round was still there at the next bell: every unit in it
    /// walked out of a pool nobody had cast in that fight. The round-end sweep (CombatDebris) was
    /// written before the space verbs and never learned about them, or about decoys.
    /// </summary>
    private static IEnumerator TheRoundEndSweepClearsTheGround()
    {
        yield return PlayHarness.ReachTheBell();

        var owner = PlayHarness.Living().Find(u => !u.isTeam);
        Assert.That(owner, Is.Not.Null, "no living enemy to own a pool");
        var pool = ShapeSprites.OnFloor("SweepCheckPool", owner.transform.position, 1.5f, Color.black, filled: true).gameObject.AddComponent<Zone>();
        pool.Begin(owner, 1.5f, 30f, 0f, null, 0f, Color.white);
        var decoy = Decoy.Spawn(owner, owner.transform.position + Vector3.right, 50f, 30f, null, "SweepCheckDecoy");
        yield return null;
        Assert.That(Zone.All, Has.Member(pool));

        CombatDebris.Sweep();
        yield return null;   // Destroy lands at the end of the frame

        Assert.That(pool == null, Is.True, "a tar pool outlived the round-end sweep");
        Assert.That(decoy == null, Is.True, "a decoy outlived the round-end sweep, and its taunt with it");
        Assert.That(Zone.All.Exists(z => z != null && z.name == "SweepCheckPool"), Is.False);
    }

    /// <summary>
    /// The shop loop's rule about banking (Docs/ShopLoop.md), on a weapon: a worn item works at its rarity, its
    /// quest fills while it fights, and a complete quest waits for the player — nothing banks on its
    /// own, and nothing banks mid-fight. Banked, the verb stays on the hero at that rarity and the
    /// weapon is hollowed. And a weapon's verb goes with it: taking the weapon off takes the verb out
    /// of the slots (a rack used to keep it). Needs a hero's real inventory. Leaves the hero with one
    /// banked mark and a hollow weapon — a real outcome, which later checks tolerate.
    /// </summary>
    private static IEnumerator AWeaponBanksOnlyWhenAsked()
    {
        yield return PlayHarness.ReachTheBell();

        Entity hero = null;
        Assets.HeroEditor.InventorySystem.Scripts.Data.Item weapon = null;
        foreach (var unit in PlayHarness.Living())
        {
            if (!unit.isTeam || unit.characterInventory == null || unit.Resonance == null) continue;
            foreach (var worn in unit.characterInventory.Equipment.Items)
                if (worn.IsWeapon && unit.Resonance.HasQuest(worn)) { hero = unit; weapon = worn; break; }
            if (hero != null) break;
        }
        Assert.That(weapon, Is.Not.Null, "no living hero holds a weapon with a quest");

        var resonance = hero.Resonance;
        var entry = resonance.QuestOf(weapon);
        var verb = (entry.engraving as GrantSpellEngraving)?.spell;
        int bankedBefore = resonance.banked.Count;

        // Make it a B, as a shop would have sold it.
        weapon.Modifier = new Assets.HeroEditor.InventorySystem.Scripts.Data.Modifier(
            Assets.HeroEditor.InventorySystem.Scripts.Enums.ItemModifier.Rarity, Rarity.B);
        resonance.Refresh();
        Assert.That(resonance.TierFor(weapon), Is.EqualTo(Rarity.B), "a worn B works at B from the moment it is on");

        resonance.Accrue(entry.requirement, entry.questGoal);
        Assert.That(entry.IsComplete(resonance.AttunementFor(weapon)), Is.True, "the quest should be complete");
        Assert.That(resonance.NoticeFor(weapon), Is.EqualTo(ResonanceNotice.Bankable), "nothing tells the player it is ready");
        Assert.That(resonance.Bank(weapon), Is.False, "banked mid-fight — a hero would lose its weapon in the fight that earned it");

        Time.timeScale = 4f;
        try { yield return PlayHarness.Until(() => GameManager.Instance.StateMachine.Current != GameState.Combat, "the fight to end", 120f); }
        finally { Time.timeScale = 1f; }

        Assert.That(resonance.banked.Count, Is.EqualTo(bankedBefore), "banked on its own when the fight ended; banking is the player's call");
        Assert.That(HollowItems.IsHollow(weapon), Is.False, "the weapon was spent without being asked");

        Assert.That(resonance.Bank(weapon), Is.True, "a complete quest could not be banked between fights");
        var mark = resonance.banked[resonance.banked.Count - 1];
        Assert.That(mark.engraving, Is.SameAs(entry.engraving));
        Assert.That(mark.tier, Is.EqualTo(Rarity.B), "banked at a different grade than the weapon was");
        Assert.That(mark.itemId, Is.EqualTo(weapon.Id), "the Abilities row cannot draw a weapon it was not told");
        Assert.That(HollowItems.IsHollow(weapon), Is.True, "the weapon was not spent");
        Assert.That(resonance.NoticeFor(weapon), Is.EqualTo(ResonanceNotice.None), "the ready mark outlived the banking");
        if (verb != null) Assert.That(hero.spellSlots, Has.Member(verb), "the banked verb is not in the slots");

        // A weapon's verb goes with it: another hero takes its weapon off, then puts it back.
        Entity other = null;
        Assets.HeroEditor.InventorySystem.Scripts.Data.Item held = null;
        foreach (var unit in GameManager.Instance.runManager.Company)
        {
            if (unit == null || unit == hero || unit.characterInventory == null || unit.Resonance == null) continue;
            foreach (var worn in unit.characterInventory.Equipment.Items)
                if (unit.Resonance.QuestOf(worn)?.engraving is GrantSpellEngraving) { other = unit; held = worn; break; }
            if (other != null) break;
        }
        Assert.That(other, Is.Not.Null, "no second hero holds a weapon that teaches a verb");

        var taught = ((GrantSpellEngraving)other.Resonance.QuestOf(held).engraving).spell;
        bool bankedToo = other.Resonance.banked.Exists(m => m != null && m.engraving is GrantSpellEngraving g && g.spell == taught);
        var window = other.characterInventory;
        window.SelectItem(held);
        window.Remove();
        Assert.That(window.Equipment.Items.Exists(i => i.Id == held.Id), Is.False, "the weapon is still worn");
        Assert.That(window.PlayerInventory.Items.Exists(i => i.Id == held.Id), Is.True, "the weapon did not go back to the bag");
        if (!bankedToo) Assert.That(other.spellSlots, Has.No.Member(taught), "the verb stayed after its weapon came off");

        var back = window.PlayerInventory.Items.Find(i => i.Id == held.Id);
        window.SelectItem(back);
        window.Equip();
        Assert.That(other.spellSlots, Has.Member(taught), "putting the weapon back on did not bring its verb back");

        // Three banked verbs fill the Abilities row: a fourth weapon waits.
        var wornAgain = window.Equipment.Items.Find(i => i.Id == held.Id);
        var quest = other.Resonance.QuestOf(wornAgain);
        other.Resonance.Accrue(quest.requirement, quest.questGoal);
        // Fillers teach something else, so the replacement is told apart from them.
        GrantSpellEngraving elsewhere = null;
        string elsewhereId = null;
        foreach (var e in ResonanceDatabase.Active.entries)
            if (e.engraving is GrantSpellEngraving g && g.spell != null && g.spell != taught) { elsewhere = g; elsewhereId = e.itemId; break; }
        Assert.That(elsewhere, Is.Not.Null, "no second verb in the database to fill the row with");
        var fillers = new System.Collections.Generic.List<Resonance.Banked>();
        while (!other.Resonance.AbilitySlotsFull)
        {
            var filler = new Resonance.Banked { engraving = elsewhere, tier = Rarity.C, itemId = elsewhereId };
            other.Resonance.banked.Add(filler);
            fillers.Add(filler);
        }
        try
        {
            Assert.That(other.Resonance.Bank(wornAgain), Is.False, "banked a fourth ability past the row's three slots");
            Assert.That(other.Resonance.MustReplaceToBank(wornAgain), Is.True, "a full row should offer to replace");

            // Replacing the middle slot: the new verb takes that slot, the row stays at three.
            var abilities = other.Resonance.BankedAbilities();
            var replaced = abilities[1];
            int at = other.Resonance.banked.IndexOf(replaced);
            // Through the row when the game is between fights, as the player does it: the first click
            // only arms the slot, the second replaces. After a lost run there is no Setup to click in.
            if (GameManager.Instance.StateMachine.Current == GameState.Setup)
            {
                var row = window.GetComponent<BankedAbilityBar>();
                window.SelectItem(wornAgain);
                Assert.That(row.ClickToReplace(replaced), Is.False, "one click replaced an ability without a confirm");
                Assert.That(other.Resonance.banked, Has.Member(replaced), "the first click threw the ability away");
                Assert.That(row.ClickToReplace(replaced), Is.True, "the confirming click did not replace it");
            }
            else Assert.That(other.Resonance.Bank(wornAgain, replaced), Is.True, "clicking a full slot did not replace it");
            Assert.That(other.Resonance.BankedAbilities().Count, Is.EqualTo(Entity.MaxBankedAbilities), "replacing changed how many are banked");
            Assert.That(other.Resonance.banked, Has.No.Member(replaced), "the replaced ability is still banked");
            Assert.That(other.Resonance.banked[at].itemId, Is.EqualTo(held.Id), "the new ability did not take the replaced one's slot");
            Assert.That(HollowItems.IsHollow(wornAgain), Is.True, "replacing did not spend the weapon");
            Assert.That(other.Resonance.banked[at].engraving, Is.SameAs(quest.engraving), "the slot holds the wrong verb");
            Assert.That(other.spellSlots, Has.Member(taught), "the replacing verb is not in the slots");
        }
        finally
        {
            foreach (var filler in fillers) other.Resonance.banked.Remove(filler);
            other.Resonance.Refresh();
        }
    }

    /// <summary>
    /// A unit pinned against a side wall keeps its body on screen. The map presets put the walls at
    /// ±8.6 while a 16:9 camera shows ±8.9, so a unit knocked to a wall was drawn off the edge. Every
    /// living unit is put against each wall in turn, facing INTO the arena (a unit faces its target,
    /// and its target is always inside the walls), measured over 120 frames of the fight so every
    /// pose of the animations is sampled, and put back.
    ///
    /// Weapons are left out, on purpose: a swing reaches up to 2.2 past its bearer, and covering it
    /// would put the walls inside the back column's cells. A weapon crossing the edge for part of a
    /// swing is the accepted cost; a head, a body or a cape is not.
    /// </summary>
    private static IEnumerator AUnitAtTheWallIsDrawnOnScreen()
    {
        yield return PlayHarness.ReachTheBell();

        var cam = Camera.main;
        var arena = ArenaBounds.Instance;
        Assert.That(cam, Is.Not.Null); Assert.That(arena, Is.Not.Null);
        float left = cam.transform.position.x - cam.orthographicSize * cam.aspect;
        float right = cam.transform.position.x + cam.orthographicSize * cam.aspect;

        string worst = null; float worstOver = 0f;
        for (int frame = 0; frame < 120; frame++)
        {
        foreach (var unit in PlayHarness.Living())
        {
            Vector3 home = unit.transform.position;
            foreach (bool rightWall in new[] { true, false })
            {
                unit.transform.position = ArenaBounds.ClampToArena(new Vector3(rightWall ? 100f : -100f, home.y, home.z));
                unit.SetFacing(!rightWall);   // facing into the arena, toward whatever it is fighting
                foreach (var r in unit.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!r.enabled || r.sprite == null || !r.gameObject.activeInHierarchy) continue;
                    if (IsWeaponArt(r.name)) continue;
                    float over = rightWall ? r.bounds.max.x - right : left - r.bounds.min.x;
                    if (over > worstOver) { worstOver = over; worst = DisplayNames.Unit(unit) + "'s " + r.name + (rightWall ? " at the right wall" : " at the left wall"); }
                }
            }
            unit.transform.position = home;
        }
        yield return null;
        }
        Assert.That(worstOver, Is.LessThanOrEqualTo(0.05f), worst + " is drawn " + worstOver.ToString("0.00") + " past the edge of the screen");
    }

    /// <summary>
    /// A won fight opens the shop with that fight's gold, and the shop sells into the bag, rerolls
    /// and closes (Docs/ShopLoop.md). End to end: a real fight is fought (at speed) and won, since the
    /// shop is opened by the run's own handling of a victory. A lost fight, or a won last fight (which
    /// ends the run and has nothing to shop for), is tried again, three times at most.
    /// </summary>
    private static IEnumerator AWonFightOpensTheShop()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            yield return PlayHarness.ReachTheBell();
            var run = GameManager.Instance.runManager;
            int before = run.Gold;
            var rules = run.runData.shop;

            Time.timeScale = 4f;
            try { yield return PlayHarness.Until(() => GameManager.Instance.StateMachine.Current != GameState.Combat, "the fight to end", 120f); }
            finally { Time.timeScale = 1f; }
            yield return PlayHarness.Until(() => run.ShopOpen || GameManager.Instance.StateMachine.Current == GameState.RunEnd, "the shop, or the run's end", 10f);
            if (!run.ShopOpen) continue;

            Assert.That(run.Gold, Is.EqualTo(before + rules.goldPerFight + rules.winBonus), "a won fight pays its gold");
            Assert.That(run.ShopOffers.Count, Is.EqualTo(rules.slots), "the shop has a full row");

            var bag = run.Company[0].characterInventory.PlayerInventory.Items;
            int cheapest = -1;
            for (int i = 0; i < run.ShopOffers.Count; i++)
                if (cheapest < 0 || run.PriceOf(run.ShopOffers[i]) < run.PriceOf(run.ShopOffers[cheapest])) cheapest = i;
            var offer = run.ShopOffers[cheapest];
            int price = run.PriceOf(offer), gold = run.Gold;
            Assert.That(run.Buy(cheapest), Is.True, "could not buy a " + price + "-gold item with " + gold);
            Assert.That(run.Gold, Is.EqualTo(gold - price));
            Assert.That(run.ShopOffers[cheapest], Is.Null, "a bought offer is still on the shelf");
            Assert.That(bag.Exists(i => i.Id == offer.Id && Rarity.Of(i) == Rarity.Of(offer)), Is.True,
                        "the bag did not get " + offer.Id + " at " + Rarity.Letter(Rarity.Of(offer)));
            Assert.That(run.Buy(cheapest), Is.False, "the same slot sold twice");

            if (run.Gold >= run.RerollCost)
            {
                gold = run.Gold;
                Assert.That(run.Reroll(), Is.True);
                Assert.That(run.Gold, Is.EqualTo(gold - run.RerollCost));
                Assert.That(run.ShopOffers.TrueForAll(o => o != null), Is.True, "a reroll refills the sold slots too");
            }

            // Freeze: what is left on the shelf opens the next shop, at the same rarity; a freeze
            // holds for one shop.
            Assert.That(run.ToggleFreeze(), Is.True);
            Assert.That(run.ShopFrozen, Is.True);
            if (run.Gold >= run.RerollCost)
            {
                run.Reroll();
                Assert.That(run.ShopFrozen, Is.False, "a reroll should let a frozen shelf go");
                run.ToggleFreeze();
            }
            Assert.That(run.Buy(0) || run.ShopOffers[0] == null || run.Gold < run.PriceOf(run.ShopOffers[0]), Is.True);
            var kept = run.ShopOffers.FindAll(o => o != null);
            run.LeaveShop();
            Assert.That(run.ShopOpen, Is.False);
            Assert.That(run.ShopOffers, Is.Empty, "the shelf is still up after leaving");

            run.OpenShop(null, 0);
            Assert.That(run.ShopOffers.Count, Is.EqualTo(rules.slots), "a frozen shop's sold slots are not refilled");
            for (int i = 0; i < kept.Count; i++)
            {
                Assert.That(run.ShopOffers[i].Id, Is.EqualTo(kept[i].Id), "frozen offer " + i + " did not carry over");
                Assert.That(Rarity.Of(run.ShopOffers[i]), Is.EqualTo(Rarity.Of(kept[i])), "frozen offer " + i + " changed rarity");
            }
            Assert.That(run.ShopFrozen, Is.False, "a freeze holds for one shop");

            run.LeaveShop();
            Assert.That(run.ShopOffers, Is.Empty, "what was not bought is gone");
            yield break;
        }
        Assert.Fail("three fights without a won fight that had another after it");
    }

    private static bool IsWeaponArt(string rendererName) =>
        rendererName.Contains("Weapon") || rendererName.Contains("Bow") || rendererName.Contains("Firearm") ||
        rendererName.Contains("Shield") || rendererName.Contains("Arrow");

    /// <summary>
    /// The deadlock: a unit whose target is just past its reach, standing still, not attacking.
    /// CombatAI's walk-in band once began at reach + 0.15 while attacking needs distance <= reach,
    /// so a unit in between did neither — and two melee units facing each other 1.55 apart, both
    /// with 1.5 reach, stood for the rest of the fight. Reported as "units standing still, not
    /// attacking anyone and not moving to a target"; measured at 12% of unit-frames with a target.
    ///
    /// Movement is judged over a window of frames, not one: the editor ticks at about 160 Hz and a
    /// walking unit moves under 0.02 a frame, which a per-frame test called standing still.
    /// </summary>
    private static IEnumerator NobodyStandsJustOutOfReach()
    {
        yield return PlayHarness.ReachTheBell();

        var trail = new Dictionary<Entity, Queue<Vector3>>();
        int withTarget = 0, justOutOfReach = 0;
        Entity worst = null; int worstFrames = 0; var perUnit = new Dictionary<Entity, int>();

        for (int sweep = 0; sweep < 240; sweep++)
        {
            foreach (var unit in PlayHarness.Living())
            {
                var ai = unit.CombatAI;
                if (ai == null || ai.CurrentTarget == null) continue;
                Vector3 p = unit.transform.position;
                if (!trail.TryGetValue(unit, out var q)) trail[unit] = q = new Queue<Vector3>();
                q.Enqueue(p); if (q.Count > 12) q.Dequeue();
                bool moved = q.Count < 12 || (q.Peek() - p).sqrMagnitude > 0.01f;

                // Only a unit that has every reason to act: free to move, not held by a taunt, a
                // stun, a throw or a root, and not mid-swing.
                if (unit.Knockback == null || !unit.Knockback.Steerable || unit.Knockback.IsStunned) continue;
                if (unit.Statuses != null && (unit.Statuses.Rooted || unit.Statuses.TauntedBy != null)) continue;
                if (ai.IsAttacking) continue;
                withTarget++;

                float d = Vector3.Distance(p, ai.CurrentTarget.transform.position);
                if (moved || d <= ai.AttackRange || d > ai.AttackRange + 0.2f) continue;
                justOutOfReach++;
                perUnit[unit] = (perUnit.TryGetValue(unit, out int n) ? n : 0) + 1;
                if (perUnit[unit] > worstFrames) { worstFrames = perUnit[unit]; worst = unit; }
            }
            yield return null;
        }

        if (withTarget < 200) yield break;   // too quiet a fight to say anything
        float share = justOutOfReach * 100f / withTarget;
        Debug.Log($"[PlayChecks] stood just out of reach in {share:0.0}% of frames ({justOutOfReach}/{withTarget})");
        // Ten percent, not one: with the bug this reads 60%; healthy fights read 0 to 3%, the 3
        // being a unit's few frames of noticing its target stepped away, which a 1% bar called a
        // failure. The bar is for the deadlock, not for reaction time.
        Assert.That(share, Is.LessThan(10f),
                    $"units stand a hand's width out of reach, neither walking nor attacking, in {share:0.0}% of frames" +
                    (worst != null ? " — worst: " + DisplayNames.Unit(worst) + " for " + worstFrames + " frames facing " + DisplayNames.Unit(worst.CombatAI.CurrentTarget) : ""));
    }

    /// <summary>
    /// The physics rule end to end: a real throw reaches a real wall, the hurt that arrives is
    /// exactly the flat wall number, and it is tagged as a slam so the damage number can say SLAM.
    /// The arithmetic is unit-tested on its own; this is the proof that the number the rule states
    /// is the number a unit actually loses, with nothing in between quietly scaling it.
    /// </summary>
    private static IEnumerator AWallSlamIsFlatAndSaysSo()
    {
        yield return PlayHarness.ReachTheBell();

        var s = CombatPhysics.Active;
        if (s == null || !s.enableImpacts || !CombatFeelSettings.Active.enableKnockback) yield break;
        Assert.That(ArenaBounds.Instance, Is.Not.Null, "no arena to have a wall");

        // A free-standing character: not a rooted decoy (it does not move), not a body already
        // flying (its own throw would hide ours), and not thrown between fights, when nothing is
        // hurt at all. This check failed once with "never happened" and no way to tell which.
        // The LOWEST such character: a body thrown down from the top of the field hit an ally on
        // the way, passed its momentum on, and never reached the floor. Nothing stands below the
        // lowest unit, so its path to the floor is clear by definition.
        Entity unit = null;
        foreach (var u in PlayHarness.Living())
        {
            if (u.Health == null || u.Knockback == null || !u.isCharacter) continue;
            if (!u.Knockback.Steerable) continue;
            if (u.Statuses != null && u.Statuses.Rooted) continue;
            // Not a unit with knockback resistance: this is a check about the wall, and the Wall
            // Keeper's 40% turned the throw into a shove that stopped short of it.
            if (u.Stats != null && u.Stats.KnockbackResistance != null && u.Stats.KnockbackResistance.Value > 0f) continue;
            if (unit == null || u.transform.position.y < unit.transform.position.y) unit = u;
        }
        Assert.That(unit, Is.Not.Null, "no free-standing, unresisting character to throw");
        Assert.That(GameManager.Instance.StateMachine.Current, Is.EqualTo(GameState.Combat), "nothing is hurt outside a fight");
        unit.Health.HealToFull();
        Vector3 before = unit.transform.position;

        DamageInfo? slam = null;
        Action<DamageInfo> watch = info => { if (info.kind == DamageKind.Slam && slam == null) slam = info; };
        unit.Health.OnDamaged += watch;
        try
        {
            // Straight down: the floor is the nearest wall from anywhere on the field, so a throw
            // this hard cannot fail to reach it, and it reaches it inside a second.
            unit.ApplyKnockback(Vector3.down, 20f, null);
            float deadline = Time.time + 6f;
            while (slam == null && Time.time < deadline) yield return null;
        }
        finally { unit.Health.OnDamaged -= watch; }

        Assert.That(slam, Is.Not.Null,
                    DisplayNames.Unit(unit) + " was thrown down from " + before + " and is at " + unit.transform.position +
                    " (state " + GameManager.Instance.StateMachine.Current + ", dead=" + unit.isDead +
                    ", steerable=" + unit.Knockback.Steerable + ") — no slam arrived");

        // What the rule sent, before blocking and shields took their share: amount + blocked is the
        // number TakeDamage was handed, and that is the one the rule promises.
        float sent = slam.Value.amount + slam.Value.blocked;
        float promised = s.wallSlamPercent * unit.Health.maxHealth;
        Assert.That(sent, Is.EqualTo(promised).Within(0.5f),
                    DisplayNames.Unit(unit) + " hit the wall for " + sent.ToString("0") + " and the rule says " +
                    promised.ToString("0") + " (" + (s.wallSlamPercent * 100f).ToString("0") + "% of " +
                    unit.Health.maxHealth.ToString("0") + ")");

        // And armour took its share: a slam is physical, and this is the damage pipeline running on
        // a real unit wearing real items. A shield may have taken more on top, never less.
        // The share from the curve's closed form, NOT from Mitigation.Reduce: an expectation computed
        // through the code under test agreed with it perfectly when that code was broken to do
        // nothing — "expected 0, got 0". A check has to know the answer on its own.
        float armour = unit.Stats != null && unit.Stats.Armor != null ? unit.Stats.Armor.Value : 0f;
        float armoursShare = sent * armour / (Mitigation.Constant + armour);
        Assert.That(slam.Value.blocked, Is.GreaterThanOrEqualTo(armoursShare - 0.5f),
                    DisplayNames.Unit(unit) + " has " + armour.ToString("0") + " armour, which should take " +
                    armoursShare.ToString("0.0") + " off a " + sent.ToString("0") + " slam; mitigation took " +
                    slam.Value.blocked.ToString("0.0"));
    }

    /// <summary>
    /// Where units stand when the bell rings: on the centres of their cells, ranged behind melee.
    /// </summary>
    private static IEnumerator EveryoneOpensOnTheirCell()
    {
        yield return PlayHarness.Until(() => GameManager.Instance != null, "the game to wake up");
        yield return PlayHarness.PastARunEnd();

        // A real bell, not "a fight is happening". Mid-fight a unit near the edge has usually been
        // thrown there, which is allowed; this check is about where units are SEATED, and
        // sampling a fight in progress made it fail at random on units that had every right to be
        // in the corner. So watch for the transition itself and sample on the frame it happens.
        // The seats are read inside the state change itself: by the check's next tick a diver may
        // already have blinked.
        bool rang = false;
        var seats = new Dictionary<Entity, Vector3>();
        Action<GameState, GameState> watch = (from, to) =>
        {
            if (to != GameState.Combat || rang) return;
            rang = true;
            foreach (var unit in PlayHarness.Living()) seats[unit] = unit.transform.position;
        };
        var states = GameManager.Instance.StateMachine;
        states.OnStateChanged += watch;

        var telemetry = UnityEngine.Object.FindObjectOfType<CombatTelemetry>();
        if (telemetry != null) telemetry.autoAdvance = true;

        try { yield return PlayHarness.Until(() => rang, "a fight to begin", 120f); }
        finally
        {
            states.OnStateChanged -= watch;
            telemetry = UnityEngine.Object.FindObjectOfType<CombatTelemetry>();
            if (telemetry != null) telemetry.autoAdvance = false;
        }

        // Everyone stands on the centre of a cell. Units were once moved off their cells to keep them
        // clear of a soft wall (since removed), and a unit standing beside its tile looked wrong
        // enough to be reported. Enemies are seated by the spawner and the company by the
        // formation, and both go through CellToWorld.
        Assert.That(seats, Is.Not.Empty, "no living units at the bell");
        if (BattleGrid.Instance != null)
        {
            var grid = BattleGrid.Instance;
            foreach (var seat in seats)
            {
                var unit = seat.Key; Vector3 at = seat.Value;
                grid.ClosestCell(unit.isTeam, at, out int column, out int row);
                float off = Vector2.Distance(at, grid.CellToWorld(unit.isTeam, column, row));
                Assert.That(off, Is.LessThan(0.1f), DisplayNames.Unit(unit) + " opens the fight " + off.ToString("0.00") +
                            " off the centre of its cell (" + column + "," + row + "), and " +
                            Vector2.Distance(at, unit.transform.position).ToString("0.00") + " from there a moment later");
            }
        }

        // And the company is in rank: whoever fights at range opens behind whoever fights up close.
        // GridFormation.AutoPlace used to fill the front rank in list order, and in the four-verb
        // playtest that seated both archers a lancer's reach from the enemy with the daggers behind
        // them. Melee steps up from the first frame, so this can only get truer after the bell — a
        // failure here is a seating failure, not a timing one.
        if (BattleGrid.Instance != null)
        {
            float centre = BattleGrid.Instance.CentreLine;
            float nearestRanged = float.MaxValue, furthestMelee = float.MinValue;
            Entity rangedInFront = null, meleeBehind = null;
            foreach (var unit in PlayHarness.Living())
            {
                if (!unit.isTeam) continue;
                float depth = Mathf.Abs(unit.transform.position.x - centre);
                if (unit.FightsAtRange) { if (depth < nearestRanged) { nearestRanged = depth; rangedInFront = unit; } }
                else if (depth > furthestMelee) { furthestMelee = depth; meleeBehind = unit; }
            }
            if (rangedInFront != null && meleeBehind != null)
                Assert.That(nearestRanged, Is.GreaterThanOrEqualTo(furthestMelee),
                            DisplayNames.Unit(rangedInFront) + " (ranged) opens " + nearestRanged.ToString("0.0") +
                            " from the centre line, in front of " + DisplayNames.Unit(meleeBehind) + " (melee) at " +
                            furthestMelee.ToString("0.0") + " — the company was seated in list order, not by role");
        }
    }

    /// <summary>
    /// A bar belongs to a body, not to a piece of art. UnitBarsManager copies the entity's localScale
    /// onto the bar, and a decoy scales its ROOT to make an arbitrary sprite stand a body high — up
    /// to six times — so a decoy made from a small sprite wore a health bar six times everyone
    /// else's. Reported as "large health bars spawning".
    /// </summary>
    private static IEnumerator ABarIsTheSizeOfABody()
    {
        yield return PlayHarness.ReachTheBell();

        var owner = PlayHarness.Living()[0];
        float bodyBar = owner.healthBar != null ? Mathf.Abs(owner.healthBar.transform.lossyScale.x) : 0f;
        Assert.That(bodyBar, Is.GreaterThan(0f), "the unit to compare against has no bar");

        // A deliberately tiny sprite: this is exactly the input that made the root scale up.
        var texture = new Texture2D(4, 4);
        var tiny = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);

        var decoy = Decoy.Spawn(owner, owner.transform.position, 50f, 4f, null, tiny, "BarSizeCheckDecoy");
        Assert.That(decoy, Is.Not.Null, "no decoy was made");

        yield return null;   // a frame, so the bars manager has provisioned it

        Assert.That(PlayHarness.WhyNoBar(decoy), Is.Null, "the decoy has no visible bar to measure");
        float decoyBar = Mathf.Abs(decoy.healthBar.transform.lossyScale.x);

        Assert.That(decoyBar, Is.LessThan(bodyBar * 2f),
                    "a decoy's bar is " + (decoyBar / bodyBar).ToString("0.0") +
                    "x a real unit's — its art size has leaked into the bar");

        UnityEngine.Object.Destroy(decoy.gameObject);   // leave nothing for the next check to chase
    }

    /// <summary>
    /// The audio path, end to end: the bus announces a hit, <see cref="CombatAudio"/> is listening,
    /// and a clip ends up on a voice. Every part of that lives in OnEnable and in a static event —
    /// the two places an edit-mode test cannot see. The failure this guards against is silent by
    /// definition: a subscription that was never made sounds exactly like a library with no clips
    /// in it yet, which is what the library will legitimately be for weeks.
    /// </summary>
    private static IEnumerator AHitIsHeard()
    {
        yield return PlayHarness.ReachTheBell();

        var audio = UnityEngine.Object.FindObjectOfType<CombatAudio>();
        Assert.That(audio, Is.Not.Null, "nothing in the scene is listening to the fight");

        var voices = audio.GetComponentsInChildren<AudioSource>(true);
        Assert.That(voices.Length, Is.GreaterThan(0), "CombatAudio built no voices to play through");

        // A second of silence, so this says nothing about how anything sounds — only about routing.
        var probe = AudioClip.Create("SfxProbe", 8000, 1, 8000, false);

        // The bank this particular unit is heard through — a bow and a hammer deliberately do not
        // share one, so seeding a fixed bank would only be testing whoever happened to be first.
        var unit = PlayHarness.Living()[0];
        var bank = audio.BankFor(CombatAudio.Flavour(unit));
        var hadClips = bank.clips;
        bank.clips = new[] { probe };

        try
        {
            CombatEvents.RaiseHit(new HitInfo(unit, unit, 1f, false, false));

            bool onAVoice = false;
            foreach (var voice in voices) if (voice.clip == probe) onAVoice = true;
            Assert.That(onAVoice, Is.True,
                        "a hit was announced and no voice picked it up — CombatAudio is not subscribed");
        }
        finally
        {
            bank.clips = hadClips;   // the library is a real asset; leave it exactly as found
        }

        // And the other route, which is the one every authored clip actually uses: a spell named in
        // the library is heard when it is cast. This one needs no seeding — it asserts against the
        // real wiring, so it fails if a clip is unassigned or the row is pointed at the wrong spell.
        var sword = UnityEditor.AssetDatabase.LoadAssetAtPath<Spell>("Assets/Data/Spells/DefaultMeleeAttack.asset");
        Assert.That(sword, Is.Not.Null, "DefaultMeleeAttack has moved — this check is out of date");

        var swordBank = SfxLibrary.Active.For(sword);
        Assert.That(swordBank, Is.Not.Null, "the library has no row for the sword attack");
        Assert.That(swordBank.HasClips, Is.True, "the sword attack's row has no clip in it");

        var swung = PlayHarness.Living()[0];
        CombatEvents.RaiseCast(swung, sword);

        bool heard = false;
        foreach (var voice in voices)
            foreach (var authored in swordBank.clips)
                if (voice.clip == authored) heard = true;

        Assert.That(heard, Is.True, "a sword attack was cast and none of its clips reached a voice");

        // And the mix reaches the speaker: an ability's voice is louder than the swing's. The rule
        // is unit-tested on its own; this is the proof that the number the rule produces is the
        // number the AudioSource is given, with the row's trim and the master fader on top.
        AudioSource swordVoice = null;
        foreach (var voice in voices)
            foreach (var authored in swordBank.clips)
                if (voice.clip == authored) swordVoice = voice;

        var whirl = UnityEditor.AssetDatabase.LoadAssetAtPath<Spell>("Assets/Data/Spells/Whirl.asset");
        var whirlBank = whirl != null ? SfxLibrary.Active.For(whirl) : null;
        Assert.That(whirlBank, Is.Not.Null, "the library has no row for Whirl — pick another ability for this check");

        var abilityProbe = AudioClip.Create("AbilityProbe", 8000, 1, 8000, false);
        var whirlHad = whirlBank.clips;
        whirlBank.clips = new[] { abilityProbe };
        try
        {
            CombatEvents.RaiseCast(swung, whirl);
            AudioSource abilityVoice = null;
            foreach (var voice in voices) if (voice.clip == abilityProbe) abilityVoice = voice;
            Assert.That(abilityVoice, Is.Not.Null, "an ability was cast and no voice picked it up");
            Assert.That(abilityVoice.volume, Is.GreaterThan(swordVoice.volume),
                        "an ability plays at " + abilityVoice.volume.ToString("0.00") + " and a sword swing at " +
                        swordVoice.volume.ToString("0.00") + " — the bed is not under the abilities");
        }
        finally
        {
            whirlBank.clips = whirlHad;
        }
    }

    /// <summary>
    /// Bars, at the bell and right through a fight. Reported twice as disappearing, and never
    /// reproducible by hand; this is the check that would have settled it either way.
    /// </summary>
    private static IEnumerator EveryLivingUnitHasAVisibleBar()
    {
        yield return PlayHarness.ReachTheBell();

        var atBell = PlayHarness.Living();
        Assert.That(atBell.Count, Is.GreaterThanOrEqualTo(6), "a five-a-side should field ten units");
        foreach (var unit in atBell)
            Assert.That(PlayHarness.WhyNoBar(unit), Is.Null, DisplayNames.Unit(unit) + " at the bell");

        for (int sweep = 0; sweep < 14; sweep++)
        {
            yield return PlayHarness.Fight(1.5f);
            var living = PlayHarness.Living();
            foreach (var unit in living)
            {
                string why = PlayHarness.WhyNoBar(unit);
                Assert.That(why, Is.Null, DisplayNames.Unit(unit) + " lost its bar mid-fight: " + why);
            }
            if (living.Count < 3) break;   // the fight is over
        }
    }

    /// <summary>
    /// A decoy's team, at the moment it registers rather than afterwards. The bug was one line wide
    /// and invisible once the frame ended: AddComponent runs Awake and OnEnable there and then, so
    /// the decoy registered — and had its bar coloured — before its side was set.
    /// </summary>
    private static IEnumerator DecoyTakesItsOwnersSide()
    {
        yield return PlayHarness.ReachTheBell();
        yield return PlayHarness.Fight(2f);

        Entity ally = null, foe = null;
        foreach (var unit in PlayHarness.Living())
        {
            if (unit.isTeam && ally == null) ally = unit;
            if (!unit.isTeam && foe == null) foe = unit;
        }
        Assert.That(ally, Is.Not.Null, "no living ally to leave a decoy");
        Assert.That(foe, Is.Not.Null, "no living enemy to leave a decoy");

        bool? sideAtRegistration = null;
        Action<Entity> watch = e =>
        {
            if (e != null && sideAtRegistration == null && e.name.Contains("Decoy")) sideAtRegistration = e.isTeam;
        };

        EntityRegistry.OnRegistered += watch;
        Entity decoy;
        try { decoy = Decoy.Spawn(foe, foe.transform.position, 100f, 6f, null, null, "EnemyCheckDecoy"); }
        finally { EntityRegistry.OnRegistered -= watch; }

        Assert.That(decoy, Is.Not.Null, "no decoy was made");
        Assert.That(sideAtRegistration, Is.EqualTo(false),
                    "an enemy's decoy registered as one of the company — whatever reads isTeam at that " +
                    "moment, the health bar included, gets it wrong");

        yield return null;   // a frame, so the bars manager has provisioned and coloured it

        Assert.That(PlayHarness.WhyNoBar(decoy), Is.Null, "the enemy decoy has no visible bar");
        Assert.That(PlayHarness.BarIsEnemyColoured(decoy), Is.True, "an enemy's decoy wears the company's colour");

        // The other way round, so this is a check about sides rather than about red.
        var friendly = Decoy.Spawn(ally, ally.transform.position, 100f, 6f, null, null, "AllyCheckDecoy");
        yield return null;
        Assert.That(friendly.isTeam, Is.True);
        Assert.That(PlayHarness.BarIsEnemyColoured(friendly), Is.False, "an ally's decoy wears the enemy's colour");

        // Leave nothing behind. A decoy taunts and lives six seconds, and the checks after this one
        // run inside those seconds: the pacing check counted units walking to these as walking past
        // a fight, and failed at random for an afternoon.
        UnityEngine.Object.Destroy(decoy.gameObject);
        UnityEngine.Object.Destroy(friendly.gameObject);
    }

    /// <summary>
    /// A bar dimmed the way a death dims it, on a unit that is alive. Nothing used to put it back,
    /// so a bar that outlived its owner's death stayed at whatever alpha it reached — at worst
    /// invisible, for the rest of the run, with nothing in the logs.
    /// </summary>
    private static IEnumerator ABarComesBackFromADeathFade()
    {
        yield return PlayHarness.ReachTheBell();

        var unit = PlayHarness.Living()[0];
        var bar = unit.healthBar;
        Assert.That(bar, Is.Not.Null, DisplayNames.Unit(unit) + " has no bar to begin with");

        var fade = typeof(ResourceBar).GetField("_deathFade",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var apply = typeof(ResourceBar).GetMethod("ApplyDeathFade",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.That(fade, Is.Not.Null, "ResourceBar no longer has _deathFade — this check is out of date");

        fade.SetValue(bar, 0f);
        if (apply != null) apply.Invoke(bar, null);
        Assert.That(PlayHarness.WhyNoBar(unit), Is.Not.Null, "the bar was supposed to be invisible at this point");

        yield return null;
        yield return null;

        Assert.That(unit.isDead, Is.False, "the unit under check died; the result would mean nothing");
        Assert.That(PlayHarness.WhyNoBar(unit), Is.Null,
                    "a living unit's bar stayed faded out — nothing puts the fade back");
    }

    /// <summary>
    /// The pacing, as a number. A melee unit walking to a target it cannot reach, while an enemy
    /// stands inside its reach the whole time, is the thing that read as a unit refusing to commit:
    /// measured at 55 to 83% of walking frames before the rule that takes the nearer fight.
    /// </summary>
    private static IEnumerator NobodyWalksPastAFight()
    {
        yield return PlayHarness.ReachTheBell();

        int walking = 0, walkingPastSomeone = 0;
        var offenders = new Dictionary<string, int>();
        for (int sweep = 0; sweep < 120; sweep++)
        {
            foreach (var unit in PlayHarness.Living())
            {
                var ai = unit.CombatAI;
                if (ai == null || ai.CurrentTarget == null) continue;

                // The same exclusions as the rule this measures (CombatAI, "take the fight that is
                // already here"): only a unit that picks the nearest takes it — one whose gear picked
                // the weakest, the farthest or its attacker goes on by design — a thrown body is not
                // walking, a taunted one walks past everyone by design, and a unit mid-swing at a
                // target that just stepped out of reach is finishing its swing.
                if (unit.EffectiveTarget != TargetMode.Nearest) continue;
                if (unit.Knockback != null && !unit.Knockback.Steerable) continue;
                if (unit.Statuses != null && unit.Statuses.TauntedBy != null) continue;
                if (ai.IsAttacking) continue;

                float reach = ai.AttackRange;
                if (Vector3.Distance(unit.transform.position, ai.CurrentTarget.transform.position) <= reach) continue;

                walking++;
                foreach (var other in PlayHarness.Living())
                {
                    if (other == unit || other == ai.CurrentTarget || other.isTeam == unit.isTeam) continue;
                    if (Vector3.Distance(unit.transform.position, other.transform.position) > reach * 0.8f) continue;
                    walkingPastSomeone++;
                    string who = DisplayNames.Unit(unit) + " -> " +
                                 DisplayNames.Unit(ai.CurrentTarget) + " past " + DisplayNames.Unit(other);
                    offenders[who] = offenders.TryGetValue(who, out int n) ? n + 1 : 1;
                    break;
                }
            }
            yield return null;
        }

        if (walking < 20) yield break;   // too quiet a fight to say anything

        // A failure names its frames. This check failed at random for an afternoon before it did,
        // and every guess about why was wrong until the frames were listed.
        var worst = new List<KeyValuePair<string, int>>(offenders);
        worst.Sort((a, b) => b.Value.CompareTo(a.Value));
        var detail = new System.Text.StringBuilder();
        for (int i = 0; i < worst.Count && i < 4; i++) detail.Append(" | ").Append(worst[i].Value).Append(" frames: ").Append(worst[i].Key);

        float share = walkingPastSomeone * 100f / walking;
        Debug.Log($"[PlayChecks] walked past a reachable enemy in {share:0}% of walking frames ({walkingPastSomeone}/{walking})");
        Assert.That(share, Is.LessThan(35f),
                    $"units walk past a fight they could be having in {share:0}% of the frames they spend walking" + detail);
    }
}
