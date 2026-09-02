using UnityEngine;

/// <summary>
/// Everything a fight leaves in the air — arrows, bolts, bullets, bombs, boomerangs, stars, and the
/// particle bursts abilities leave behind — and the sweep that clears it when the round ends.
///
/// A round ends the instant the last enemy dies, but the last enemy's arrow is still flying. It used
/// to land: on a hero already revived and healed for the next fight, killing it after the revive
/// pass had run, so it lay dead through the map screen and started the next fight dead. Damage
/// outside a fight is refused now (see Health), which stops the harm; this stops the litter, so the
/// board between fights is a board and not the tail end of the last one.
/// </summary>
public static class CombatDebris
{
    /// <summary>Destroy every in-flight object and lingering effect. Safe with nothing to clear.</summary>
    public static int Sweep()
    {
        int cleared = 0;
        cleared += Clear<Assets.HeroEditor.Common.Scripts.ExampleScripts.Projectile>();
        cleared += Clear<ThrownBomb>();
        cleared += Clear<ThrownBoomerang>();
        cleared += Clear<ThrownStar>();
        cleared += Clear<CartoonFX.CFXR_Effect>();
        return cleared;
    }

    private static int Clear<T>() where T : Component
    {
        var found = Object.FindObjectsOfType<T>();
        for (int i = 0; i < found.Length; i++)
            if (found[i] != null) Object.Destroy(found[i].gameObject);
        return found.Length;
    }
}
