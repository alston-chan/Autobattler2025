using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The pieces a play check is written from: getting the game to the bell, waiting for things,
/// and the questions about a live fight that kept coming up by hand.
///
/// <b>Why this is not NUnit.</b> The obvious build is <c>[UnityTest]</c> with <c>EnterPlayMode</c>,
/// and it does not work here. That capability is only granted to tests in a test assembly defined by
/// an asmdef, and an asmdef assembly cannot reference the predefined <c>Assembly-CSharp</c> — which
/// is where this entire game lives (CLAUDE.md). Written into <c>Assembly-CSharp-Editor</c> where the
/// rest of the suite lives, the framework answers "EditMode test can only yield null, but not
/// &lt;EnterPlayMode&gt;". So the driver is <see cref="PlayTestRunner"/> rather than the test runner,
/// and a check is a coroutine that throws to fail. NUnit's Assert still does the asserting — it is
/// referenced here — so the failures read like every other test's.
/// </summary>
public static class PlayHarness
{
    public const string Scene = "Assets/Scenes/Main.unity";

    /// <summary>
    /// Be in a fight. The checks share one play session — starting a new one costs twenty seconds
    /// each and would need the run's progress carried across a domain reload — so this has to work
    /// whether the game is still in Setup, already fighting, or between fights on the ladder. A
    /// later check simply runs in a later fight, which is more coverage rather than less.
    /// </summary>
    public static IEnumerator ReachTheBell()
    {
        yield return Until(() => GameManager.Instance != null, "the game to wake up");
        if (GameManager.Instance.StateMachine.Current == GameState.Combat) yield break;

        // The bell is what the player presses; the telemetry harness presses it for an unattended
        // run, and takes the spoils that would otherwise block the next fight.
        var telemetry = UnityEngine.Object.FindObjectOfType<CombatTelemetry>();
        if (telemetry != null) telemetry.autoAdvance = true;

        yield return Until(() => GameManager.Instance != null &&
                                 GameManager.Instance.StateMachine.Current == GameState.Combat,
                           "a fight to start", 90f);

        // And then stop, so the ladder does not run on underneath the check.
        telemetry = UnityEngine.Object.FindObjectOfType<CombatTelemetry>();
        if (telemetry != null) telemetry.autoAdvance = false;
    }

    /// <summary>
    /// Wait for something, or throw saying what never happened. A check that hangs tells you nothing
    /// and holds the editor; one that gives up at twenty seconds names the condition it wanted.
    /// </summary>
    public static IEnumerator Until(Func<bool> done, string what, float seconds = 20f)
    {
        double deadline = EditorApplication.timeSinceStartup + seconds;
        while (!done())
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new Exception($"waited {seconds:0}s for {what} and it never happened");
            yield return null;
        }
    }

    /// <summary>Let the fight run, so bodies collide, verbs fire and units fall around each other.</summary>
    public static IEnumerator Fight(float seconds)
    {
        float until = Time.time + seconds;
        yield return Until(() => Time.time >= until, $"{seconds:0}s of fighting", seconds + 20f);
    }

    /// <summary>Every unit on the field that is alive and switched on.</summary>
    public static List<Entity> Living()
    {
        var living = new List<Entity>();
        foreach (var e in EntityRegistry.All)
            if (e != null && !e.isDead && e.gameObject.activeInHierarchy) living.Add(e);
        return living;
    }

    /// <summary>
    /// Why this unit's bar cannot be seen, or null when it can. The check the hand-written probes
    /// converged on: it has to exist, be switched on, be where the unit is, and have something lit
    /// drawing it.
    /// </summary>
    public static string WhyNoBar(Entity unit)
    {
        if (unit == null) return "the unit is gone";
        var bar = unit.healthBar;
        if (bar == null) return "healthBar is null";
        if (!bar.gameObject.activeInHierarchy) return "the bar object is off";

        float away = Vector3.Distance(bar.transform.position, unit.transform.position);
        if (away > 4f) return $"the bar is {away:0.0} units from its unit";
        if (Mathf.Abs(bar.transform.lossyScale.x) < 0.01f) return "the bar is scaled to nothing";

        int renderers = 0;
        foreach (var sr in bar.GetComponentsInChildren<SpriteRenderer>(true))
        {
            renderers++;
            if (sr.enabled && sr.gameObject.activeInHierarchy && sr.color.a > 0.02f) return null;
        }
        return renderers == 0 ? "the bar has no renderers" : "every one of the bar's renderers is off or transparent";
    }

    /// <summary>Whether a unit's bar reads as the enemy red rather than the company green.</summary>
    public static bool BarIsEnemyColoured(Entity unit)
    {
        foreach (var sr in unit.healthBar.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr.color.a < 0.2f) continue;
            if (Mathf.Abs(sr.color.r - sr.color.g) < 0.2f) continue;   // the frame and backing are greys
            return sr.color.r > sr.color.g;
        }
        throw new Exception("no coloured fill found on the bar");
    }
}
