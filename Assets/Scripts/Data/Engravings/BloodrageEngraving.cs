using System.Collections;
using UnityEngine;

/// <summary>
/// "Every few seconds of the fight, the bearer hits harder and faster." The Ogre Warchief's rule
/// (Docs/Enemies.md, the Bloodrager): a damage race. A company that brings burst single-target damage
/// kills it before the stacks matter; one that spreads its damage, or stalls behind a tank, meets the
/// fifth stack. Stacks count from the bell and are all gone when the fight ends.
///
/// Each stack is announced over the bearer, so the rising threat is seen rather than inferred from
/// the health bars going down faster.
/// </summary>
[CreateAssetMenu(menuName = "Data/Engraving/Bloodrage", fileName = "Bloodrage")]
public class BloodrageEngraving : Engraving
{
    [Tooltip("Seconds of fighting between stacks.")]
    public float interval = 5f;
    [Tooltip("Damage per stack, as a fraction of base — 0.15 is +15%. Scaled by tier.")]
    public float damagePerStack = 0.15f;
    [Tooltip("Attack speed per stack, as a fraction — 0.1 is +10%. Scaled by tier.")]
    public float attackSpeedPerStack = 0.1f;
    [Tooltip("Stacks stop here. 0 is no limit.")]
    public int maxStacks = 0;

    // One bearer's state: Resonance hands every bearer its own copy.
    private Coroutine _loop;
    private int _stacks;

    private void Reset()
    {
        engravingName = "Bloodrage";
        description = "Hits harder and faster the longer the fight goes on.";
    }

    public override string DescribeTier(int tier)
    {
        int t = Mathf.Max(1, tier);
        return $"Every {interval:0.#} s: +{damagePerStack * t * 100f:0}% damage and +{attackSpeedPerStack * t * 100f:0}% " +
               "attack speed" + (maxStacks > 0 ? $", up to {maxStacks} times." : ", without limit.");
    }

    public override void OnCombatStart(Entity owner, int tier)
    {
        if (owner == null || owner.Stats == null) return;
        OnCombatEnd(owner, tier);   // never two loops
        _stacks = 0;
        _loop = owner.StartCoroutine(Rage(owner, Mathf.Max(1, tier)));
    }

    public override void OnCombatEnd(Entity owner, int tier)
    {
        if (owner == null) return;
        if (_loop != null) owner.StopCoroutine(_loop);
        _loop = null;
        _stacks = 0;
        if (owner.Stats == null) return;
        owner.Stats.Damage.RemoveAllModifiersFromSource(this);
        owner.Stats.AttackSpeed.RemoveAllModifiersFromSource(this);
    }

    private IEnumerator Rage(Entity owner, int tier)
    {
        while (owner != null && !owner.isDead)
        {
            yield return new WaitForSeconds(interval);
            if (owner == null || owner.isDead || owner.Stats == null) yield break;
            if (maxStacks > 0 && _stacks >= maxStacks) yield break;

            _stacks++;
            owner.Stats.Damage.AddModifier(new Kryz.CharacterStats.StatModifier(
                damagePerStack * tier, Kryz.CharacterStats.StatModType.PercentAdd, this));
            owner.Stats.AttackSpeed.AddModifier(new Kryz.CharacterStats.StatModifier(
                attackSpeedPerStack * tier, Kryz.CharacterStats.StatModType.PercentAdd, this));
            AbilityFeedback.AnnounceEngraving(owner, this, _stacks == 1 ? "Bloodrage" : "Bloodrage ×" + _stacks);
        }
    }
}
