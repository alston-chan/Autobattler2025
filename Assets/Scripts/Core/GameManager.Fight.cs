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
/// GameManager, the fight half: the state transitions and what they tell every unit, the stand-down
/// and the revive, resonance at the bell and after it, and the round's verdict.
/// </summary>
public partial class GameManager
{
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

            // Stamp every unit with where it was deployed. Targeting and the engravings read the
            // stamp, never the live position, for the whole fight.
            BoardSnapshot.Freeze(runManager != null ? runManager.Formation : null);
            NotifyResonance(true);

            var startingTelemetry = GetComponent<CombatTelemetry>();
            if (startingTelemetry != null) startingTelemetry.NoteFightStarted();
        }

        // Between fights is the safe point. Nothing on offer and nothing in the air; the run as it
        // stands is the run worth keeping.
        if (next == GameState.Setup && runManager != null) runManager.SaveIfSafe();

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

    private void HandleEntityRegistered(Entity entity)
    {
        if (entity != null) entity.SetFighting(isGameStarted);
    }

    private void BroadcastFighting(bool fighting)
    {
        if (fighting) CombatPhysics.OnFightStart();
        var all = EntityRegistry.All;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (all[i] == null) continue;
            all[i].SetFighting(fighting);
            // At the bell everyone looks across the centre line, whatever the setup screen left them
            // looking at; CombatAI turns them onto their targets from the first tick after this.
            if (fighting) all[i].SetFacing(all[i].isTeam);
        }
        // The fallen have left the registry (they are deactivated), but they are still the company:
        // a hero that died mid-fight must hear the fight end too, or it carries the fight's states
        // and its "fighting" flag into the map screen and the next round.
        foreach (var hero in allyCharacters)
            if (hero != null && !hero.gameObject.activeInHierarchy) hero.SetFighting(fighting);
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
        // And the fallen, who are off the registry: their engravings' combat-end hooks must run, or
        // a hunt, a stat granted for the fight, or a bus subscription outlives the fight.
        foreach (var hero in allyCharacters)
            if (hero != null && !hero.gameObject.activeInHierarchy && hero.Resonance != null) hero.Resonance.ApplyForCombat(starting);
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
