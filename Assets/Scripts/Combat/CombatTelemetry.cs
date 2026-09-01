using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Who actually did what, across a whole run.
///
/// Every number in this game's combat — four weapon identities, seven abilities, the reach on a
/// lance, the crit on an axe — was chosen by judgement and has never been measured. The question
/// "is the axe hero better than the sword hero" had no answer except watching a fight and forming
/// an impression, and an autobattler is exactly the genre where that is not good enough: the
/// player's whole input is who they field and where they stand, so the balance between units IS
/// the game.
///
/// Totals accumulate across fights rather than resetting each round, because one fight is noise.
/// Enemies are pooled under their prefab name for the same reason — five clones of the same unit
/// are five samples of one design, not five designs.
/// </summary>
public class CombatTelemetry : MonoBehaviour
{
    public class Row
    {
        public float DamageDealt, DamageTaken;
        public int Hits, Crits, Kills, Deaths, Fights;

        public float CritRate => Hits > 0 ? (float)Crits / Hits : 0f;
    }

    private static readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();
    private readonly Dictionary<Entity, System.Action<DamageInfo>> _watched =
        new Dictionary<Entity, System.Action<DamageInfo>>();

    private int _fightsRecorded;

    /// <summary>Every unit seen so far, worst-to-best by damage dealt.</summary>
    public static IEnumerable<KeyValuePair<string, Row>> Standings =>
        _rows.OrderByDescending(r => r.Value.DamageDealt);

    private void OnEnable()
    {
        EntityRegistry.OnRegistered += Watch;
        EntityRegistry.OnUnregistered += Unwatch;
        Entity.OnAnyDied += RecordDeath;

        // Units that were already on the field when this was added.
        var all = EntityRegistry.All;
        for (int i = 0; i < all.Count; i++) Watch(all[i]);
    }

    private void OnDisable()
    {
        EntityRegistry.OnRegistered -= Watch;
        EntityRegistry.OnUnregistered -= Unwatch;
        Entity.OnAnyDied -= RecordDeath;

        foreach (var pair in _watched)
            if (pair.Key != null && pair.Key.Health != null) pair.Key.Health.OnDamaged -= pair.Value;
        _watched.Clear();
    }

    private void Watch(Entity entity)
    {
        if (entity == null || entity.Health == null || _watched.ContainsKey(entity)) return;

        // A unit that died and was revived registers again; without this it would be counted twice.
        System.Action<DamageInfo> handler = info => RecordHit(entity, info);
        entity.Health.OnDamaged += handler;
        _watched[entity] = handler;
    }

    private void Unwatch(Entity entity)
    {
        if (entity == null || !_watched.TryGetValue(entity, out var handler)) return;

        if (entity.Health != null) entity.Health.OnDamaged -= handler;
        _watched.Remove(entity);
    }

    private static void RecordHit(Entity victim, DamageInfo info)
    {
        RowFor(victim).DamageTaken += info.amount;

        if (info.source == null) return;      // burn, decay, anything with no author

        var attacker = RowFor(info.source);
        attacker.DamageDealt += info.amount;
        attacker.Hits++;
        if (info.isCrit) attacker.Crits++;
    }

    private static void RecordDeath(Entity victim)
    {
        RowFor(victim).Deaths++;

        // The killer is whoever landed the blow, which the damage event has already attributed;
        // crediting it here would need the last hit's source, so kills are counted where they are
        // knowable — see Health.Die, which passes the killer on.
    }

    /// <summary>Credit a kill. Called from the death path, which knows who struck last.</summary>
    public static void RecordKill(Entity killer)
    {
        if (killer != null) RowFor(killer).Kills++;
    }

    private static Row RowFor(Entity entity)
    {
        string key = NameOf(entity);
        if (!_rows.TryGetValue(key, out var row)) _rows[key] = row = new Row();
        return row;
    }

    /// <summary>Clones are pooled: five copies of one enemy are five samples, not five designs.</summary>
    private static string NameOf(Entity entity)
    {
        string name = entity.name;
        int clone = name.IndexOf("(Clone)", System.StringComparison.Ordinal);
        return clone >= 0 ? name.Substring(0, clone) : name;
    }

    /// <summary>Note that a fight finished, so averages can be per-fight rather than per-run.</summary>
    public void NoteFightEnded()
    {
        _fightsRecorded++;
        foreach (var row in _rows.Values) row.Fights = _fightsRecorded;
    }

    public static void Reset()
    {
        _rows.Clear();
    }

    /// <summary>The standings as a table, for the console.</summary>
    public string BuildReport()
    {
        if (_rows.Count == 0) return "[Telemetry] nothing recorded yet.";

        var sb = new StringBuilder();
        sb.AppendLine($"[Telemetry] after {_fightsRecorded} fight(s)");
        sb.AppendLine($"{"unit",-26}{"dealt",9}{"taken",9}{"hits",7}{"crit%",7}{"kills",7}{"deaths",7}");

        foreach (var pair in Standings)
        {
            var r = pair.Value;
            sb.AppendLine($"{Trim(pair.Key, 25),-26}{r.DamageDealt,9:F0}{r.DamageTaken,9:F0}" +
                          $"{r.Hits,7}{r.CritRate * 100f,6:F0}%{r.Kills,7}{r.Deaths,7}");
        }
        return sb.ToString();
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
