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
        public float DamageDealt, DamageTaken, Blocked;
        public int Hits, Crits, Kills, Deaths, Fights, Ults;

        public float CritRate => Hits > 0 ? (float)Crits / Hits : 0f;

        public Row Copy() => new Row
        {
            DamageDealt = DamageDealt, DamageTaken = DamageTaken, Blocked = Blocked,
            Hits = Hits, Crits = Crits, Kills = Kills, Deaths = Deaths, Fights = Fights, Ults = Ults
        };

        /// <summary>This row less <paramref name="earlier"/> — what happened in between. Null means "nothing before".</summary>
        public Row Since(Row earlier)
        {
            if (earlier == null) return Copy();
            return new Row
            {
                DamageDealt = DamageDealt - earlier.DamageDealt,
                DamageTaken = DamageTaken - earlier.DamageTaken,
                Blocked = Blocked - earlier.Blocked,
                Hits = Hits - earlier.Hits,
                Crits = Crits - earlier.Crits,
                Kills = Kills - earlier.Kills,
                Deaths = Deaths - earlier.Deaths,
                Ults = Ults - earlier.Ults,
                Fights = 1
            };
        }
    }

    private static readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();

    // The last fight on its own. Totals are what balance is measured against, because one fight is
    // noise; but the player reading a scoreboard after a fight wants that fight, since it is the
    // one they can still do something about. Kept as the rows at the fight's start, so the delta
    // needs no second set of hooks.
    private static readonly Dictionary<string, Row> _atFightStart = new Dictionary<string, Row>();
    private static readonly Dictionary<string, Row> _lastFight = new Dictionary<string, Row>();

    /// <summary>What each unit did in the most recent fight. Empty until one has ended.</summary>
    public static IReadOnlyDictionary<string, Row> LastFight => _lastFight;

    /// <summary>The whole run so far, by unit name.</summary>
    public static IReadOnlyDictionary<string, Row> Totals => _rows;

    public static int FightsRecorded { get; private set; }
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
        var victimRow = RowFor(victim);
        victimRow.DamageTaken += info.amount;
        victimRow.Blocked += info.blocked;

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

    /// <summary>Credit a cost ability cast. Called from CombatAI as the mana is spent.</summary>
    public static void RecordUlt(Entity caster)
    {
        if (caster != null) RowFor(caster).Ults++;
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

    /// <summary>Note that a fight is beginning: remember where every row stood, so the fight's own numbers can be read off afterwards.</summary>
    public void NoteFightStarted()
    {
        _atFightStart.Clear();
        foreach (var pair in _rows) _atFightStart[pair.Key] = pair.Value.Copy();
    }

    /// <summary>Note that a fight finished: fix the fight's own numbers, and let averages be per-fight rather than per-run.</summary>
    public void NoteFightEnded()
    {
        _fightsRecorded++;
        FightsRecorded = _fightsRecorded;
        foreach (var row in _rows.Values) row.Fights = _fightsRecorded;

        _lastFight.Clear();
        foreach (var pair in _rows)
        {
            _atFightStart.TryGetValue(pair.Key, out var before);
            _lastFight[pair.Key] = pair.Value.Since(before);
        }
    }

    public static void Reset()
    {
        _rows.Clear();
        _atFightStart.Clear();
        _lastFight.Clear();
        FightsRecorded = 0;
    }

    /// <summary>The standings as a table, for the console.</summary>
    public string BuildReport()
    {
        if (_rows.Count == 0) return "[Telemetry] nothing recorded yet.";

        var sb = new StringBuilder();
        sb.AppendLine($"[Telemetry] after {_fightsRecorded} fight(s)");
        sb.AppendLine($"{"unit",-26}{"dealt",9}{"taken",9}{"blocked",9}{"hits",7}{"crit%",7}{"kills",7}{"deaths",7}{"ults",6}");

        foreach (var pair in Standings)
        {
            var r = pair.Value;
            sb.AppendLine($"{Trim(pair.Key, 25),-26}{r.DamageDealt,9:F0}{r.DamageTaken,9:F0}{r.Blocked,9:F0}" +
                          $"{r.Hits,7}{r.CritRate * 100f,6:F0}%{r.Kills,7}{r.Deaths,7}{r.Ults,6}");
        }
        return sb.ToString();
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

    /// <summary>
    /// Write the standings beside the project, next to the Assets folder rather than inside it, so
    /// Unity does not import a text file every time a fight ends.
    ///
    /// A file rather than only the console because the table is wider and longer than a log line
    /// wants to be, and because the point of measuring across a run is to still have the numbers
    /// afterwards.
    /// </summary>
    public string WriteReport()
    {
        string path = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, "..", "Telemetry.txt"));
        try
        {
            System.IO.File.WriteAllText(path, BuildReport());
            return path;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Telemetry] could not write {path}: {e.Message}");
            return null;
        }
    }

    // ── Harness ──────────────────────────────────────────────────────────────────────────────
    //
    // Measuring balance needs many fights, and many fights need neither a person pressing Space
    // nor real time. These three keys turn a session into a batch run: one to read the table, one
    // to stop waiting between fights, one to stop waiting during them.

    [Tooltip("Start each next fight automatically instead of waiting for Space. Toggled with F2.")]
    public bool autoAdvance;

    [Tooltip("Playback rate while running. Cycled with F3. Physics is not used for combat, so " +
             "speeding time up changes how long a fight takes and nothing about how it resolves.")]
    public float speed = 1f;

    private static readonly float[] Speeds = { 1f, 4f, 8f };
    private int _speedIndex;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.Log(BuildReport());
            string written = WriteReport();
            if (written != null) Debug.Log($"[Telemetry] written to {written}");
        }

        if (Input.GetKeyDown(KeyCode.F2))
        {
            autoAdvance = !autoAdvance;
            Debug.Log($"[Telemetry] auto-advance {(autoAdvance ? "on" : "off")}");
        }

        if (Input.GetKeyDown(KeyCode.F3))
        {
            _speedIndex = (_speedIndex + 1) % Speeds.Length;
            speed = Speeds[_speedIndex];
            Time.timeScale = speed;
            Debug.Log($"[Telemetry] speed {speed}x");
        }

        if (autoAdvance) AdvanceIfWaiting();
    }

    /// <summary>
    /// Start the next fight without being asked.
    ///
    /// An unclaimed reward blocks the next fight, which is right for a player — the choice is the
    /// point — but stops a batch run dead after its first victory. So the harness takes the first
    /// thing offered and carries on. That is a real choice being made arbitrarily, which is worth
    /// remembering when reading the numbers: a run measured this way is a run where nobody drafted
    /// well, and the units are being compared on their own merits rather than on their gear.
    /// </summary>
    private void AdvanceIfWaiting()
    {
        var game = GameManager.Instance;
        if (game == null || game.isGameStarted) return;

        var run = game.runManager;
        if (run != null && run.PendingRewards.Count > 0)
        {
            run.TakeReward(run.PendingRewards[0]);
            return;                            // let the claim settle before starting the fight
        }

        // On a map the harness always takes the first path offered. Like the reward above, that is a
        // real choice made arbitrarily: numbers from such a run describe a company that never routed
        // for anything, around anything.
        if (run != null && run.AwaitingPath)
        {
            var next = run.State.AvailableNext;
            if (next.Count > 0) run.ChoosePath(next[0]);
            return;
        }

        game.StateMachine.TransitionTo(GameState.Combat);
    }
}
