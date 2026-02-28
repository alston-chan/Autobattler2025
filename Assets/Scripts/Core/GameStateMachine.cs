using System;
using UnityEngine;

/// <summary>
/// Lightweight state machine that tracks the current <see cref="GameState"/>
/// and fires an event whenever it changes. Lives on the <see cref="GameManager"/>.
/// </summary>
public class GameStateMachine
{
    /// <summary>Fired after the state changes. Args: previous state, new state.</summary>
    public event Action<GameState, GameState> OnStateChanged;

    public GameState Current { get; private set; } = GameState.Setup;

    /// <summary>
    /// Transition to a new state. Logs a warning and returns false
    /// if the transition is invalid or a no-op.
    /// </summary>
    public bool TransitionTo(GameState next)
    {
        if (next == Current) return false;

        // Validate transitions
        bool valid = (Current, next) switch
        {
            (GameState.Setup, GameState.Combat) => true,
            (GameState.Combat, GameState.RoundEnd) => true,
            (GameState.RoundEnd, GameState.Setup) => true,
            _ => false
        };

        if (!valid)
        {
            Debug.LogWarning($"[GameStateMachine] Invalid transition {Current} → {next}");
            return false;
        }

        var prev = Current;
        Current = next;
        OnStateChanged?.Invoke(prev, next);
        return true;
    }
}
