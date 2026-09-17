using TMPro;
using UnityEngine;

/// <summary>
/// The fight clock: seconds since the bell, in the top centre of the screen, and the one thing it
/// does about a fight that goes long. Past <see cref="Settings.speedUpAfter"/> the fight runs at
/// <see cref="Settings.speedUpScale"/> until it resolves, so a stalemate between two kiting
/// archers costs the player seconds rather than a minute. No pressure yet: nothing is hurt, no
/// wall moves; the fight is the same fight, watched faster. Time is put back the moment combat
/// ends, whatever ended it.
/// </summary>
public class FightClock : MonoBehaviour
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("Show the clock during a fight.")]
        public bool showClock = true;
        [Tooltip("Fight seconds after which the fight runs faster. 0 never speeds up.")]
        public float speedUpAfter = 40f;
        [Tooltip("The time scale a long fight runs at.")]
        [Range(1f, 4f)] public float speedUpScale = 2f;
    }

    private static Settings S => CombatFeelSettings.Active.fightClock;

    private TextMeshProUGUI _label;
    private float _bell = -1f;
    private bool _sped;

    /// <summary>Fight seconds since the bell, or 0 outside a fight.</summary>
    public float Elapsed => _bell < 0f ? 0f : Time.time - _bell;

    /// <summary>Whether the fight is currently running at the long-fight speed.</summary>
    public bool SpedUp => _sped;

    public void Initialize(Transform canvas)
    {
        if (canvas == null || GameManager.Instance == null) return;
        Build(canvas);
        GameManager.Instance.StateMachine.OnStateChanged += HandleState;
        _label.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.StateMachine.OnStateChanged -= HandleState;
        RestoreTime();
    }

    private void HandleState(GameState previous, GameState next)
    {
        if (next == GameState.Combat)
        {
            _bell = Time.time;
            _sped = false;
        }
        else
        {
            _bell = -1f;
            RestoreTime();
        }
        if (_label != null) _label.gameObject.SetActive(next == GameState.Combat && S.showClock);
    }

    private void Update()
    {
        if (_bell < 0f) return;
        float t = Elapsed;

        if (!_sped && S.speedUpAfter > 0f && t >= S.speedUpAfter)
        {
            _sped = true;
            Time.timeScale = S.speedUpScale;
        }

        if (_label != null && _label.gameObject.activeSelf)
        {
            int whole = Mathf.FloorToInt(t);
            _label.text = (whole / 60) + ":" + (whole % 60).ToString("00") + (_sped ? "  ×" + S.speedUpScale.ToString("0.#") : "");
            _label.color = _sped ? new Color(1f, 0.82f, 0.28f, 1f) : new Color(0.9f, 0.9f, 0.92f, 0.9f);
        }
    }

    private void RestoreTime()
    {
        if (_sped) Time.timeScale = 1f;
        _sped = false;
    }

    private void Build(Transform canvas)
    {
        var go = new GameObject("FightClock", typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(160f, 34f);
        rect.anchoredPosition = new Vector2(0f, -12f);

        _label = go.AddComponent<TextMeshProUGUI>();
        _label.fontSize = 26f;
        _label.alignment = TextAlignmentOptions.Center;
        _label.raycastTarget = false;
        _label.text = "0:00";
    }
}
